// Read-only radar tile renderer — ported from weather-server.ts:1932-2000.
// Frame metadata now comes from our own /api/radar/frame (Phase B decision
// #3) instead of a direct browser->RainViewer call; tile URL construction
// (host + path, never a raw timestamp) is otherwise unchanged.
import { $ } from './state.js';
import { xyFromLatLon } from './geo.js';

export async function renderRadarMap(lat, lon) {
  const container = $('radar-map');
  if (!container) return;
  container.innerHTML = '<div class="radar-placeholder"><span class="spin"></span>Loading radar…</div>';

  let frameTime = null;
  let frameUrl = null;
  try {
    const res = await fetch('/api/radar/frame');
    if (res.ok) {
      const frame = await res.json();
      if (frame) {
        frameTime = frame.frameTimeUtc;
        frameUrl = frame.host + frame.path;
      }
    }
  } catch (e) { /* radar overlay is cosmetic — fall back to basemap-only tiles */ }

  const tsEl = $('radar-ts');
  if (tsEl) {
    tsEl.textContent = frameTime
      ? new Date(frameTime).toLocaleTimeString('en-US', { hour: 'numeric', minute: '2-digit' })
      : '';
  }

  const ZOOM = 7, TILE = 256;
  const W = container.offsetWidth > 0 ? container.offsetWidth : 340;
  const H = 220;
  container.style.height = H + 'px';

  const pos = xyFromLatLon(lat, lon, ZOOM);
  const maxT = Math.pow(2, ZOOM) - 1;
  const rx = Math.ceil(W / TILE / 2) + 1;
  const ry = Math.ceil(H / TILE / 2) + 1;
  const subs = ['a', 'b', 'c', 'd'];

  container.innerHTML = '';

  for (let dy = -ry; dy <= ry; dy++) {
    for (let dx = -rx; dx <= rx; dx++) {
      const tx = pos.tx + dx;
      const ty = pos.ty + dy;
      if (tx < 0 || tx > maxT || ty < 0 || ty > maxT) continue;
      const px = Math.round(W / 2 - pos.fx * TILE + dx * TILE);
      const py = Math.round(H / 2 - pos.fy * TILE + dy * TILE);
      const st = `position:absolute;left:${px}px;top:${py}px;width:${TILE}px;height:${TILE}px;`;
      const sub = subs[Math.abs(tx + ty) % 4];
      const base = document.createElement('img');
      base.crossOrigin = 'anonymous';
      base.src = `https://${sub}.basemaps.cartocdn.com/dark_all/${ZOOM}/${tx}/${ty}.png`;
      base.style.cssText = st;
      container.appendChild(base);
      if (frameUrl) {
        const rov = document.createElement('img');
        rov.crossOrigin = 'anonymous';
        rov.src = `${frameUrl}/256/${ZOOM}/${tx}/${ty}/4/0_0.png`;
        rov.style.cssText = st + 'opacity:0.8;mix-blend-mode:screen;';
        container.appendChild(rov);
      }
    }
  }

  const dot = document.createElement('div');
  dot.style.cssText = `position:absolute;left:${Math.round(W / 2)}px;top:${Math.round(H / 2)}px;width:10px;height:10px;background:var(--accent);border-radius:50%;transform:translate(-50%,-50%);box-shadow:0 0 6px var(--accent);z-index:5;pointer-events:none;`;
  container.appendChild(dot);
  const ring = document.createElement('div');
  ring.style.cssText = `position:absolute;left:${Math.round(W / 2)}px;top:${Math.round(H / 2)}px;width:22px;height:22px;border:1.5px solid var(--accent);border-radius:50%;transform:translate(-50%,-50%);z-index:5;pointer-events:none;animation:radarPulse 2s ease-out infinite;`;
  container.appendChild(ring);
}
