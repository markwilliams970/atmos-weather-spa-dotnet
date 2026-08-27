// Search input + autocomplete dropdown — ported from weather-server.ts:1304-1374.
import { $, state, closeSuggestions } from './state.js';
import { getWeather, formatCityLabel } from './weather.js';
import { openMapPicker } from './map-picker.js';

const input = $('search-input');
const searchWrap = $('search-wrap');
const suggBox = $('suggestions');

function renderSugg(results) {
  suggBox.innerHTML = '';
  (results || []).forEach((r) => {
    const label = formatCityLabel(r);
    const d = document.createElement('div');
    d.className = 'sugg-item';
    d.textContent = label;
    d.addEventListener('mousedown', (e) => {
      e.preventDefault();
      input.value = label;
      state.lat = r.latitude; state.lon = r.longitude;
      state.elevation = null; state.elevationWarning = null; state.locationType = 'city';
      closeSuggestions();
      getWeather();
    });
    suggBox.appendChild(d);
  });

  const mapItem = document.createElement('div');
  mapItem.className = 'sugg-item sugg-map-item';
  mapItem.textContent = '📍 Select from map…';
  mapItem.addEventListener('mousedown', (e) => {
    e.preventDefault();
    closeSuggestions();
    openMapPicker();
  });
  suggBox.appendChild(mapItem);

  suggBox.style.display = 'block';
  searchWrap.classList.add('open');
  state.suggIdx = -1;
}

async function fetchSugg(q) {
  if (q.length < 2 || /^\d+$/.test(q)) { renderSugg([]); return; }
  try {
    const res = await fetch('/api/geocode?q=' + encodeURIComponent(q));
    if (res.ok) renderSugg((await res.json()).results);
    else renderSugg([]);
  } catch (e) { renderSugg([]); }
}

input.addEventListener('input', () => {
  state.lat = null; state.lon = null;
  clearTimeout(state.debounce);
  const v = input.value.trim();
  if (v.length < 2) { renderSugg([]); return; }
  state.debounce = setTimeout(() => fetchSugg(v), 280);
});

input.addEventListener('keydown', (e) => {
  const items = suggBox.querySelectorAll('.sugg-item');
  if (suggBox.style.display === 'block' && items.length) {
    if (e.key === 'ArrowDown') { e.preventDefault(); state.suggIdx = Math.min(state.suggIdx + 1, items.length - 1); items.forEach((el, i) => el.classList.toggle('active', i === state.suggIdx)); return; }
    if (e.key === 'ArrowUp') { e.preventDefault(); state.suggIdx = Math.max(state.suggIdx - 1, -1); items.forEach((el, i) => el.classList.toggle('active', i === state.suggIdx)); return; }
    if (e.key === 'Enter' && state.suggIdx >= 0) { e.preventDefault(); items[state.suggIdx].dispatchEvent(new MouseEvent('mousedown')); return; }
    if (e.key === 'Escape') { closeSuggestions(); return; }
  }
  if (e.key === 'Enter') getWeather();
});

input.addEventListener('blur', () => setTimeout(closeSuggestions, 150));
input.addEventListener('focus', () => {
  const v = input.value.trim();
  if (v.length >= 2) fetchSugg(v); else renderSugg([]);
});

$('search-btn').addEventListener('click', getWeather);
