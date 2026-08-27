// Core weather orchestration — ported from weather-server.ts:1725-1920,
// 2178-2234, plus the History-API navigation model approved in Phase B §4
// (bookmarkable /weather?... URLs, no full-page reload during search).
import { $, state, setStatus, clearStatus, closeSuggestions } from './state.js';
import { drawHumidity, drawUV, drawWind, drawSunArc, drawTempChart, renderHourly, renderDaily, drawAqiBar } from './charts.js';
import { applyTheme } from './themes.js';
import { renderRadarMap } from './radar.js';

const input = () => $('search-input');
const panels = () => $('wx-panels');

export function applyUnits() {
  const d = state.data;
  if (!d) return;
  const m = state.units === 'metric';
  $('temp-val').textContent = m ? d.tempC : d.tempF;
  $('temp-unit').textContent = m ? '°C' : '°F';
  $('feels-like').textContent = (m ? d.feelsLikeC : d.feelsLikeF) + (m ? '°C' : '°F');
  $('today-hi').textContent = (m ? d.todayHighC : d.todayHighF) + (m ? '°C' : '°F');
  $('today-lo').textContent = (m ? d.todayLowC : d.todayLowF) + (m ? '°C' : '°F');
  $('wind-val').textContent = (m ? d.windKmh + ' km/h' : d.windMph + ' mph');
  $('wind-sub').textContent = d.windDir;
  $('precip-val').textContent = (m ? d.precipMm + ' mm' : d.precipIn + '"');
  $('unit-f').classList.toggle('active', !m);
  $('unit-c').classList.toggle('active', m);
  $('loc-coords').textContent = d.latitude.toFixed(4) + ', ' + d.longitude.toFixed(4) +
    (typeof d.elevationMeters === 'number'
      ? '  ·  ⛰ ' + (m ? Math.round(d.elevationMeters) + ' m' : Math.round(d.elevationMeters * 3.28084) + ' ft')
      : '');
  drawTempChart(d.hourly, m);
  renderHourly(d.hourly, m);
  renderDaily(d.daily, m);
}

export function populate(d) {
  state.data = d;
  $('loc-name').textContent = d.location;
  $('loc-zip').textContent = d.zip ? 'ZIP ' + d.zip : '';
  $('cond-emoji').textContent = d.conditionEmoji;
  $('cond-label').textContent = d.condition;
  $('humidity-val').textContent = d.humidity + '%';
  $('humidity-sub').textContent = d.humidity > 80 ? 'High' : d.humidity > 50 ? 'Moderate' : 'Low';
  $('uv-val').textContent = d.uvIndex;
  $('uv-sub').textContent = '';
  $('map-link').href = `https://www.openstreetmap.org/?mlat=${d.latitude}&mlon=${d.longitude}#map=13/${d.latitude}/${d.longitude}`;

  const detailsEl = $('details-row');
  detailsEl.innerHTML =
    `<div class="detail-pill">🌅 Sunrise <span>${d.sunrise}</span></div>` +
    `<div class="detail-pill">🌇 Sunset <span>${d.sunset}</span></div>` +
    `<div class="detail-pill">💧 Humidity <span>${d.humidity}%</span></div>`;

  drawHumidity(d.humidity);
  drawUV(d.uvIndex);
  drawWind(d.windDeg);
  drawSunArc(d);
  applyUnits();
  applyTheme(d);
  renderRadarMap(d.latitude, d.longitude);
  fetchAndRenderAQ(d.latitude, d.longitude);

  panels().classList.add('show');
}

function buildWeatherUrl() {
  const params = new URLSearchParams();
  if (state.lat !== null && state.lon !== null) {
    params.set('lat', state.lat);
    params.set('lon', state.lon);
    params.set('label', input().value.trim());
    if (state.locationType) params.set('locationType', state.locationType);
    if (typeof state.elevation === 'number') params.set('elevation', state.elevation);
  } else {
    params.set('zip', input().value.trim());
  }
  params.set('units', state.units);
  return params;
}

export async function getWeather() {
  closeSuggestions();
  const q = input().value.trim();
  if (!q) { setStatus('Enter a city name or ZIP code.', true, false); return; }
  setStatus('Fetching weather…', false, true);
  try {
    if (state.lat === null && !/^\d{5}$/.test(q)) {
      // Free-text, non-ZIP input with no suggestion selected yet — resolve
      // it via geocoding first, exactly as pressing Enter without picking a
      // dropdown suggestion does in the reference app.
      const gr = await fetch('/api/geocode?q=' + encodeURIComponent(q) + '&count=1');
      const gd = await gr.json();
      if (!gd.results || !gd.results.length) throw new Error(`No location found for "${q}"`);
      const r = gd.results[0];
      input().value = formatCityLabel(r);
      state.lat = r.latitude; state.lon = r.longitude;
      state.elevation = null; state.elevationWarning = null; state.locationType = 'city';
    }

    const apiParams = buildWeatherUrl();

    // /about and /map don't render the weather-panels markup at all (they
    // hide it via ViewData["HideWeatherPanels"]) — there's nothing to
    // populate in place, so send the browser to a page that actually has
    // that DOM instead of fetching into a void.
    if (!panels()) {
      location.href = '/weather?' + apiParams.toString();
      return;
    }

    panels().classList.remove('show');
    const res = await fetch('/api/weather?' + apiParams.toString());
    const data = await res.json();
    if (!res.ok) throw new Error(data.error || 'Request failed');
    clearStatus();
    populate(data);
    if (state.elevationWarning) { setStatus(state.elevationWarning, true, false); state.elevationWarning = null; }
    history.pushState(null, '', '/weather?' + apiParams.toString());
    loadRecent();
  } catch (err) {
    setStatus(err.message || 'Something went wrong.', true, false);
  }
}

export function formatCityLabel(r) {
  const p = [r.name];
  if (r.admin1) p.push(r.admin1);
  if (r.countryCode) p.push(r.countryCode);
  return p.join(', ');
}

/// Reads the current URL's query string and, if it describes a location,
/// loads that forecast — used both on a cold /weather?... page load and on
/// browser back/forward (popstate), per Phase B §4's navigation model.
export function loadFromLocation() {
  const params = new URLSearchParams(location.search);
  const zip = params.get('zip');
  const lat = params.get('lat');
  const lon = params.get('lon');
  const label = params.get('label');
  const units = params.get('units');

  if (units === 'metric' || units === 'imperial') state.units = units;

  if (zip) {
    input().value = zip;
    state.lat = null; state.lon = null;
    getWeather();
  } else if (lat && lon && label) {
    input().value = label;
    state.lat = parseFloat(lat);
    state.lon = parseFloat(lon);
    state.locationType = params.get('locationType') || 'city';
    const elevation = params.get('elevation');
    state.elevation = elevation !== null ? parseFloat(elevation) : null;
    getWeather();
  }
}

export function setUnits(u) {
  if (u === state.units || !state.data) return;
  state.units = u;
  applyUnits();
  if (state.data.location) {
    fetch('/api/recent/units', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ label: state.data.location, units: state.units }),
    }).catch(() => {});
  }
}

export function switchTab(name) {
  document.querySelectorAll('.tab-btn').forEach((b) => b.classList.toggle('active', b.dataset.tab === name));
  document.querySelectorAll('.tab-panel').forEach((p) => p.classList.toggle('active', p.id === 'tab-' + name));
  if (name === 'hourly' && state.data) drawTempChart(state.data.hourly, state.units === 'metric');
  document.querySelectorAll('.bnav-btn').forEach((b) => b.classList.toggle('active', b.dataset.bnav === name));
}

function renderRecent(searches) {
  const list = $('recent-list');
  const drawer = $('drawer-list');
  if (!searches || !searches.length) {
    list.innerHTML = '<li class="recent-empty">No recent searches</li>';
    if (drawer) drawer.innerHTML = '<div style="color:var(--text-3);font-size:.8rem">No recent searches</div>';
    return;
  }

  list.innerHTML = '';
  searches.forEach((s) => {
    const li = document.createElement('li');
    li.className = 'recent-item';
    li.textContent = s.label;
    li.addEventListener('click', () => selectRecent(s));
    list.appendChild(li);
  });

  if (drawer) {
    drawer.innerHTML = '';
    searches.forEach((s) => {
      const d = document.createElement('div');
      d.className = 'drawer-item';
      d.textContent = s.label;
      d.addEventListener('click', () => {
        selectRecent(s);
        $('recents-drawer').classList.remove('open');
      });
      drawer.appendChild(d);
    });
  }
}

function selectRecent(s) {
  input().value = s.label;
  state.lat = s.latitude;
  state.lon = s.longitude;
  state.units = s.units || 'imperial';
  // Recents don't reliably retain elevation across a fresh fetch — a
  // re-selected map pick will only keep its correction if the server sent
  // one back with this row (Phase A's known gap is closed at the schema
  // level, but re-deriving here would need another lookup round trip).
  state.elevation = s.elevationMeters ?? null;
  state.elevationWarning = null;
  state.locationType = null;
  getWeather();
}

export async function loadRecent() {
  try {
    const res = await fetch('/api/recent');
    if (res.ok) renderRecent(await res.json());
  } catch (e) { /* Recent list is non-essential; fail silently as in the reference app */ }
}

async function fetchAndRenderAQ(lat, lon) {
  const card = $('aq-card');
  if (!card) return;
  try {
    const res = await fetch(`/api/air-quality?lat=${lat}&lon=${lon}`);
    if (!res.ok) throw new Error('AQ unavailable');
    const d = await res.json();
    $('aqi-val').textContent = d.usAqi;
    $('aqi-val').style.color = d.color;
    $('aqi-cat').textContent = d.category;
    $('aqi-cat').style.color = d.color;
    $('aq-pm25').textContent = d.pm25 + ' μg/m³';
    $('aq-pm10').textContent = d.pm10 + ' μg/m³';
    $('aq-ozone').textContent = d.ozone + ' μg/m³';
    $('aq-no2').textContent = d.no2 + ' μg/m³';
    drawAqiBar(d.usAqi, d.color);
    card.style.opacity = '1';
  } catch (e) {
    $('aqi-val').textContent = '—';
    $('aqi-cat').textContent = 'Unavailable';
    card.style.opacity = '0.5';
  }
}
