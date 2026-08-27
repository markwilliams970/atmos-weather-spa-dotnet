// Shared app state — ported from the reference app's single global `state`
// object (weather-server.ts:1278-1287). ES modules give every importer the
// same live object reference, which is what a plain `var state` gave the
// original's inline script for free.
export const state = {
  data: null,
  units: 'imperial',
  lat: null,
  lon: null,
  debounce: null,
  suggIdx: -1,
  elevation: null,
  elevationWarning: null,
};

export const $ = (id) => document.getElementById(id);

const statusEl = () => $('status');

export function setStatus(msg, isErr, loading) {
  const el = statusEl();
  if (!el) return;
  el.innerHTML = loading ? '<span class="spin"></span>' + msg : msg;
  el.className = 'show' + (isErr ? ' err' : '');
}

export function clearStatus() {
  const el = statusEl();
  if (el) el.className = '';
}

export function closeSuggestions() {
  clearTimeout(state.debounce);
  const box = $('suggestions');
  const wrap = $('search-wrap');
  if (box) box.style.display = 'none';
  if (wrap) wrap.classList.remove('open');
  state.suggIdx = -1;
}
