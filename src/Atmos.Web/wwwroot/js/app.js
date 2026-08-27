// Bootstrap — ported from weather-server.ts:1819-1863, 2236-2253, plus the
// History-API router approved in Phase B §4. Importing search.js and
// map-picker.js wires their event listeners as a side effect (they attach
// directly to DOM elements present in the shared layout on every page).
import { $, state } from './state.js';
import { switchTab, setUnits, loadRecent, loadFromLocation } from './weather.js';
import { drawTempChart, drawSunArc } from './charts.js';
import './search.js';
import './map-picker.js';

document.querySelectorAll('.tab-btn').forEach((btn) => {
  btn.addEventListener('click', () => switchTab(btn.dataset.tab));
});

document.querySelectorAll('.bnav-btn').forEach((btn) => {
  btn.addEventListener('click', () => {
    const name = btn.dataset.bnav;
    if (name === 'recents') {
      const drawer = $('recents-drawer');
      drawer.classList.toggle('open');
      document.querySelectorAll('.bnav-btn').forEach((b) => b.classList.toggle('active', b.dataset.bnav === 'recents' && drawer.classList.contains('open')));
    } else {
      $('recents-drawer').classList.remove('open');
      if (state.data) switchTab(name);
      document.querySelectorAll('.bnav-btn').forEach((b) => b.classList.toggle('active', b.dataset.bnav === name));
    }
  });
});

const unitF = $('unit-f');
const unitC = $('unit-c');
if (unitF) unitF.addEventListener('click', () => setUnits('imperial'));
if (unitC) unitC.addEventListener('click', () => setUnits('metric'));

let resizeTimer;
window.addEventListener('resize', () => {
  clearTimeout(resizeTimer);
  resizeTimer = setTimeout(() => {
    if (state.data) {
      drawTempChart(state.data.hourly, state.units === 'metric');
      drawSunArc(state.data);
    }
  }, 150);
});

window.addEventListener('popstate', loadFromLocation);

// ── Init ──────────────────────────────────────────────────────────────────
loadRecent();
loadFromLocation();
