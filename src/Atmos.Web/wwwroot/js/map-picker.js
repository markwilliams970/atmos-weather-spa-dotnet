// Interactive location picker — ported as directly as possible from
// weather-server.ts:2002-2176 (flagged High risk for fidelity in Phase A
// §18/Phase B §19 — no "improvements" made during this port, only a faithful
// translation of the pan/zoom/drag/click handling).
import { $, state } from './state.js';
import { xyFromLatLon, lonLatFromXY } from './geo.js';
import { getWeather } from './weather.js';

function fmtCoordLabel(lat, lon) {
  return Math.abs(lat).toFixed(2) + '°' + (lat >= 0 ? 'N' : 'S') + ', ' +
         Math.abs(lon).toFixed(2) + '°' + (lon >= 0 ? 'E' : 'W');
}

const pickerState = {
  centerLat: 20, centerLon: 0, zoom: 2,
  pinLat: null, pinLon: null,
  dragging: false, dragMoved: false,
  dragStartX: 0, dragStartY: 0, dragStartCenter: null,
};

const pickerOverlay = $('map-picker-overlay');
const pickerVp = $('map-picker-viewport');
const pickerPin = $('map-picker-pin');
const pickerCoordsEl = $('map-picker-coords');
const pickerUseBtn = $('map-picker-use');

function positionPickerPin() {
  if (pickerState.pinLat === null) { pickerPin.style.display = 'none'; return; }
  const W = pickerVp.offsetWidth > 0 ? pickerVp.offsetWidth : 500;
  const H = pickerVp.offsetHeight > 0 ? pickerVp.offsetHeight : 380;
  const pos = xyFromLatLon(pickerState.centerLat, pickerState.centerLon, pickerState.zoom);
  const pinPos = xyFromLatLon(pickerState.pinLat, pickerState.pinLon, pickerState.zoom);
  const offX = (pinPos.tx + pinPos.fx) - (pos.tx + pos.fx);
  const offY = (pinPos.ty + pinPos.fy) - (pos.ty + pos.fy);
  pickerPin.style.left = Math.round(W / 2 + offX * 256) + 'px';
  pickerPin.style.top = Math.round(H / 2 + offY * 256) + 'px';
  pickerPin.style.display = 'block';
}

function renderPickerTiles() {
  if (!pickerVp) return;
  const ZOOM = pickerState.zoom, TILE = 256;
  const W = pickerVp.offsetWidth > 0 ? pickerVp.offsetWidth : 500;
  const H = pickerVp.offsetHeight > 0 ? pickerVp.offsetHeight : 380;
  const pos = xyFromLatLon(pickerState.centerLat, pickerState.centerLon, ZOOM);
  const maxT = Math.pow(2, ZOOM) - 1;
  const rx = Math.ceil(W / TILE / 2) + 1;
  const ry = Math.ceil(H / TILE / 2) + 1;
  const subs = ['a', 'b', 'c', 'd'];

  pickerVp.querySelectorAll('.picker-tile').forEach((el) => el.remove());

  for (let dy = -ry; dy <= ry; dy++) {
    for (let dx = -rx; dx <= rx; dx++) {
      const tx = pos.tx + dx;
      const ty = pos.ty + dy;
      if (tx < 0 || tx > maxT || ty < 0 || ty > maxT) continue;
      const px = Math.round(W / 2 - pos.fx * TILE + dx * TILE);
      const py = Math.round(H / 2 - pos.fy * TILE + dy * TILE);
      const sub = subs[Math.abs(tx + ty) % 4];
      const img = document.createElement('img');
      img.className = 'picker-tile';
      img.crossOrigin = 'anonymous';
      img.src = `https://${sub}.basemaps.cartocdn.com/dark_all/${ZOOM}/${tx}/${ty}.png`;
      img.style.cssText = `left:${px}px;top:${py}px;width:${TILE}px;height:${TILE}px;`;
      pickerVp.insertBefore(img, pickerVp.firstChild);
    }
  }
  positionPickerPin();
}

function handlePickerClick(e) {
  const rect = pickerVp.getBoundingClientRect();
  const px = e.clientX - rect.left, py = e.clientY - rect.top;
  const pos = xyFromLatLon(pickerState.centerLat, pickerState.centerLon, pickerState.zoom);
  const cx = pos.tx + pos.fx, cy = pos.ty + pos.fy;
  const ll = lonLatFromXY(cx + (px - rect.width / 2) / 256, cy + (py - rect.height / 2) / 256, pickerState.zoom);
  pickerState.pinLat = ll.lat; pickerState.pinLon = ll.lon;
  pickerUseBtn.disabled = false;
  pickerCoordsEl.textContent = fmtCoordLabel(ll.lat, ll.lon);
  positionPickerPin();
}

if (pickerVp) {
  pickerVp.addEventListener('pointerdown', (e) => {
    if (e.target !== pickerVp && e.target.closest && e.target.closest('.map-picker-zoom')) return;
    pickerState.dragging = true;
    pickerState.dragMoved = false;
    pickerState.dragStartX = e.clientX;
    pickerState.dragStartY = e.clientY;
    pickerState.dragStartCenter = { lat: pickerState.centerLat, lon: pickerState.centerLon };
    pickerVp.setPointerCapture(e.pointerId);
    pickerVp.classList.add('dragging');
  });
  pickerVp.addEventListener('pointermove', (e) => {
    if (!pickerState.dragging) return;
    const dxPix = e.clientX - pickerState.dragStartX;
    const dyPix = e.clientY - pickerState.dragStartY;
    if (Math.abs(dxPix) > 4 || Math.abs(dyPix) > 4) pickerState.dragMoved = true;
    const start = xyFromLatLon(pickerState.dragStartCenter.lat, pickerState.dragStartCenter.lon, pickerState.zoom);
    const cx = start.tx + start.fx, cy = start.ty + start.fy;
    const ll = lonLatFromXY(cx - dxPix / 256, cy - dyPix / 256, pickerState.zoom);
    pickerState.centerLat = ll.lat; pickerState.centerLon = ll.lon;
    renderPickerTiles();
  });
  pickerVp.addEventListener('pointerup', (e) => {
    if (!pickerState.dragging) return;
    pickerState.dragging = false;
    pickerVp.classList.remove('dragging');
    try { pickerVp.releasePointerCapture(e.pointerId); } catch (err) { /* already released */ }
    if (!pickerState.dragMoved) handlePickerClick(e);
  });
  pickerVp.addEventListener('wheel', (e) => {
    e.preventDefault();
    pickerState.zoom = Math.max(2, Math.min(18, pickerState.zoom + (e.deltaY < 0 ? 1 : -1)));
    renderPickerTiles();
  }, { passive: false });
}

const pickerZoomInBtn = $('map-picker-zoom-in');
const pickerZoomOutBtn = $('map-picker-zoom-out');
if (pickerZoomInBtn) pickerZoomInBtn.addEventListener('click', () => { pickerState.zoom = Math.min(18, pickerState.zoom + 1); renderPickerTiles(); });
if (pickerZoomOutBtn) pickerZoomOutBtn.addEventListener('click', () => { pickerState.zoom = Math.max(2, pickerState.zoom - 1); renderPickerTiles(); });

export function openMapPicker() {
  pickerState.centerLat = state.lat !== null ? state.lat : 20;
  pickerState.centerLon = state.lon !== null ? state.lon : 0;
  pickerState.zoom = state.lat !== null ? 9 : 2;
  pickerState.pinLat = null; pickerState.pinLon = null;
  pickerPin.style.display = 'none';
  pickerUseBtn.disabled = true;
  pickerCoordsEl.textContent = 'Click the map to drop a pin';
  pickerOverlay.classList.add('open');
  renderPickerTiles();
}

export function closeMapPicker() {
  pickerOverlay.classList.remove('open');
}

const pickerCloseBtn = $('map-picker-close');
if (pickerCloseBtn) pickerCloseBtn.addEventListener('click', closeMapPicker);
pickerOverlay.addEventListener('mousedown', (e) => { if (e.target === pickerOverlay) closeMapPicker(); });
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape' && pickerOverlay.classList.contains('open')) closeMapPicker();
});

pickerUseBtn.addEventListener('click', async () => {
  if (pickerState.pinLat === null) return;
  const lat = pickerState.pinLat, lon = pickerState.pinLon;
  closeMapPicker();
  const baseLabel = fmtCoordLabel(lat, lon);
  $('search-input').value = baseLabel;
  state.lat = lat; state.lon = lon;
  state.elevation = null; state.elevationWarning = null;
  state.locationType = 'map';

  try {
    const er = await fetch(`/api/elevation?lat=${lat}&lon=${lon}`);
    const ed = await er.json();
    if (er.ok && typeof ed.elevation === 'number') {
      state.elevation = ed.elevation;
    } else {
      state.elevationWarning = 'Could not determine elevation for this point — showing forecast without elevation correction.';
    }
  } catch (e) {
    state.elevationWarning = 'Could not determine elevation for this point — showing forecast without elevation correction.';
  }

  try {
    const pr = await fetch(`/api/nearby-place?lat=${lat}&lon=${lon}`);
    const pd = await pr.json();
    if (pr.ok && pd.name) {
      $('search-input').value = baseLabel + ' Near ' + pd.name;
    }
  } catch (e) { /* nearby-place is cosmetic — omit the "Near ..." suffix on failure */ }

  getWeather();
});
