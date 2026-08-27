// Hand-built SVG gauges/charts — ported from weather-server.ts:1479-1723,
// 2189-2208. No charting library, exactly as in the reference app.
import { $ } from './state.js';

export function drawHumidity(pct) {
  const svg = $('hum-svg');
  if (!svg) return;
  const r = 24, cx = 30, cy = 30, circ = 2 * Math.PI * r;
  const filled = (pct / 100) * circ;
  svg.innerHTML =
    '<defs><linearGradient id="hg" x1="0" y1="0" x2="1" y2="1">' +
    '<stop offset="0%" stop-color="#80d8ff"/><stop offset="100%" stop-color="#1060a0"/></linearGradient></defs>' +
    `<circle cx="${cx}" cy="${cy}" r="${r}" fill="none" stroke="rgba(255,255,255,.08)" stroke-width="5"/>` +
    `<circle cx="${cx}" cy="${cy}" r="${r}" fill="none" stroke="url(#hg)" stroke-width="5" ` +
    `stroke-dasharray="${filled.toFixed(1)} ${circ.toFixed(1)}" stroke-linecap="round" transform="rotate(-90 ${cx} ${cy})"/>` +
    `<text x="${cx}" y="${cy + 4}" text-anchor="middle" class="g-val">${pct}%</text>`;
}

export function drawUV(uv) {
  const svg = $('uv-svg');
  if (!svg) return;
  const cx = 35, cy = 36, r = 28;
  const halfCirc = Math.PI * r;
  const pct = Math.min(uv / 11, 1);
  const filled = pct * halfCirc;
  const angle = Math.PI * (1 - pct);
  const nx = cx + r * Math.cos(angle);
  const ny = cy - r * Math.sin(angle);
  const uvLabel = uv <= 2 ? 'Low' : uv <= 5 ? 'Moderate' : uv <= 7 ? 'High' : uv <= 10 ? 'Very High' : 'Extreme';
  svg.innerHTML =
    '<defs><linearGradient id="ug" x1="0" y1="0" x2="1" y2="0">' +
    '<stop offset="0%" stop-color="#60e060"/><stop offset="40%" stop-color="#f0d000"/>' +
    '<stop offset="72%" stop-color="#f04000"/><stop offset="100%" stop-color="#c000c0"/></linearGradient></defs>' +
    `<path d="M ${cx - r} ${cy} A ${r} ${r} 0 0 1 ${cx + r} ${cy}" fill="none" stroke="rgba(255,255,255,.08)" stroke-width="6" stroke-linecap="round"/>` +
    `<path d="M ${cx - r} ${cy} A ${r} ${r} 0 0 1 ${cx + r} ${cy}" fill="none" stroke="url(#ug)" stroke-width="6" stroke-linecap="round" ` +
    `stroke-dasharray="${filled.toFixed(1)} ${halfCirc.toFixed(1)}"/>` +
    `<circle cx="${nx.toFixed(1)}" cy="${ny.toFixed(1)}" r="4" fill="#fff" opacity=".9"/>` +
    `<text x="${cx}" y="${cy + 14}" text-anchor="middle" class="g-sub">${uvLabel}</text>`;
}

export function drawWind(deg) {
  const svg = $('wind-svg');
  if (!svg) return;
  const cx = 30, cy = 30, r = 24;
  const arrowDeg = (deg + 180) % 360;
  const rad = (arrowDeg - 90) * Math.PI / 180;
  const tipX = cx + r * .55 * Math.cos(rad);
  const tipY = cy + r * .55 * Math.sin(rad);
  const tailX = cx - r * .3 * Math.cos(rad);
  const tailY = cy - r * .3 * Math.sin(rad);
  const pRad = rad + Math.PI / 2;
  const hw = 4, hl = 7;
  const hbx = tipX - hl * Math.cos(rad), hby = tipY - hl * Math.sin(rad);
  const l1x = hbx + hw * Math.cos(pRad), l1y = hby + hw * Math.sin(pRad);
  const l2x = hbx - hw * Math.cos(pRad), l2y = hby - hw * Math.sin(pRad);
  const cards = [['N', cx, cy - r + 7, 'middle'], ['E', cx + r - 5, cy + 4, 'start'], ['S', cx, cy + r - 3, 'middle'], ['W', cx - r + 5, cy + 4, 'end']];
  const cardSvg = cards.map((c) => `<text x="${c[1]}" y="${c[2]}" text-anchor="${c[3]}" class="g-card">${c[0]}</text>`).join('');
  svg.innerHTML =
    `<circle cx="${cx}" cy="${cy}" r="${r}" fill="rgba(255,255,255,.03)" stroke="rgba(255,255,255,.1)" stroke-width="1"/>` +
    cardSvg +
    `<line x1="${tailX.toFixed(1)}" y1="${tailY.toFixed(1)}" x2="${tipX.toFixed(1)}" y2="${tipY.toFixed(1)}" stroke="var(--accent)" stroke-width="2" stroke-linecap="round"/>` +
    `<polygon points="${tipX.toFixed(1)},${tipY.toFixed(1)} ${l1x.toFixed(1)},${l1y.toFixed(1)} ${l2x.toFixed(1)},${l2y.toFixed(1)}" fill="var(--accent)"/>`;
}

export function drawSunArc(d) {
  const svg = $('sun-arc-svg');
  if (!svg) return;
  const W = 360, H = 100, cx = 180, cy = 95, r = 82;
  const now = new Date();
  const nowMin = now.getHours() * 60 + now.getMinutes();
  let t = (nowMin - d.sunriseMin) / (d.sunsetMin - d.sunriseMin);
  t = Math.max(0.01, Math.min(0.99, t));
  const sunAngle = Math.PI * (1 - t);
  const sx = cx + r * Math.cos(sunAngle);
  const sy = cy - r * Math.sin(sunAngle);
  const x1 = cx - r, x2 = cx + r;
  const dLen = d.sunsetMin - d.sunriseMin;
  const hrs = Math.floor(dLen / 60), mins = dLen % 60;
  const glowR = d.isDay ? 10 : 0;
  svg.setAttribute('viewBox', `0 0 ${W} ${H}`);
  svg.innerHTML =
    '<defs>' +
    '<linearGradient id="sg" x1="0" y1="0" x2="1" y2="0">' +
    '<stop offset="0%" stop-color="#ff9040"/><stop offset="50%" stop-color="#ffd54f"/>' +
    '<stop offset="100%" stop-color="#ff7020"/></linearGradient>' +
    '<radialGradient id="orbG" cx="50%" cy="50%" r="50%">' +
    '<stop offset="0%" stop-color="#ffd54f" stop-opacity=".4"/>' +
    '<stop offset="100%" stop-color="#ffd54f" stop-opacity="0"/></radialGradient>' +
    '</defs>' +
    `<line x1="${x1 - 8}" y1="${cy}" x2="${x2 + 8}" y2="${cy}" stroke="rgba(255,255,255,.07)" stroke-width="1"/>` +
    `<path d="M ${x1} ${cy} A ${r} ${r} 0 0 1 ${x2} ${cy}" fill="none" stroke="rgba(255,255,255,.1)" stroke-width="2"/>` +
    `<path d="M ${x1} ${cy} A ${r} ${r} 0 0 1 ${sx.toFixed(1)} ${sy.toFixed(1)}" fill="none" stroke="url(#sg)" stroke-width="2.5"/>` +
    (glowR ? `<circle cx="${sx.toFixed(1)}" cy="${sy.toFixed(1)}" r="18" fill="url(#orbG)"/>` : '') +
    `<circle cx="${sx.toFixed(1)}" cy="${sy.toFixed(1)}" r="5.5" fill="#ffd54f"/>` +
    `<text x="${x1}" y="${cy + 14}" text-anchor="middle" class="g-sub">${d.sunrise}</text>` +
    `<text x="${x2}" y="${cy + 14}" text-anchor="middle" class="g-sub">${d.sunset}</text>` +
    `<text x="${cx}" y="${cy - r - 8}" text-anchor="middle" class="g-sub">${hrs}h ${mins}m daylight</text>`;
}

function smoothPath(pts) {
  if (pts.length < 2) return '';
  let d = `M ${pts[0][0].toFixed(1)} ${pts[0][1].toFixed(1)}`;
  for (let i = 0; i < pts.length - 1; i++) {
    const p0 = pts[Math.max(0, i - 1)], p1 = pts[i], p2 = pts[i + 1], p3 = pts[Math.min(pts.length - 1, i + 2)];
    const f = 0.38;
    const cp1x = p1[0] + (p2[0] - p0[0]) * f / 2, cp1y = p1[1] + (p2[1] - p0[1]) * f / 2;
    const cp2x = p2[0] - (p3[0] - p1[0]) * f / 2, cp2y = p2[1] - (p3[1] - p1[1]) * f / 2;
    d += ` C ${cp1x.toFixed(1)} ${cp1y.toFixed(1)},${cp2x.toFixed(1)} ${cp2y.toFixed(1)},${p2[0].toFixed(1)} ${p2[1].toFixed(1)}`;
  }
  return d;
}

export function drawTempChart(hourly, metric) {
  const svg = $('temp-chart');
  if (!svg || !hourly.length) return;
  const W = svg.parentElement.offsetWidth || 600;
  const H = 140;
  const PAD = { t: 24, b: 32, l: 8, r: 8 };
  const cW = W - PAD.l - PAD.r;
  const cH = H - PAD.t - PAD.b;
  svg.setAttribute('width', W);
  svg.setAttribute('height', H);
  svg.setAttribute('viewBox', `0 0 ${W} ${H}`);

  const temps = hourly.map((h) => (metric ? h.tempC : h.tempF));
  const precips = hourly.map((h) => h.precipProb);
  const tMin = Math.min(...temps);
  const tMax = Math.max(...temps);
  const tRange = Math.max(tMax - tMin, 4);

  const xOf = (i) => PAD.l + (i / (hourly.length - 1)) * cW;
  const yOf = (t) => PAD.t + cH * (1 - (t - tMin + 1.5) / (tRange + 3));

  const pts = temps.map((t, i) => [xOf(i), yOf(t)]);
  const linePath = smoothPath(pts);
  const lastPt = pts[pts.length - 1];
  const fillPath = `${linePath} L ${lastPt[0].toFixed(1)} ${H - PAD.b} L ${xOf(0).toFixed(1)} ${H - PAD.b} Z`;

  const barW = Math.max(2, (cW / hourly.length) * 0.55);
  let bars = '';
  precips.forEach((p, i) => {
    if (!p) return;
    const bh = (p / 100) * 20;
    const bx = xOf(i) - barW / 2;
    bars += `<rect x="${bx.toFixed(1)}" y="${(H - PAD.b - bh).toFixed(1)}" width="${barW.toFixed(1)}" height="${bh.toFixed(1)}" fill="rgba(100,170,255,.3)" rx="1.5"/>`;
  });

  let labels = '';
  hourly.forEach((h, i) => {
    if (i % 3 === 0 || h.isCurrent) {
      const x = xOf(i);
      const lbl = h.isCurrent ? 'Now' : h.timeLabel;
      labels += `<text x="${x.toFixed(1)}" y="${H - 8}" text-anchor="middle" font-family="DM Mono,monospace" font-size="9" fill="rgba(139,167,196,.7)">${lbl}</text>`;
    }
  });

  let tempLabels = '';
  hourly.forEach((h, i) => {
    if (i % 3 === 0) {
      const x = xOf(i), y = yOf(temps[i]) - 7;
      const t = metric ? h.tempC : h.tempF;
      tempLabels += `<text x="${x.toFixed(1)}" y="${y.toFixed(1)}" text-anchor="middle" font-family="DM Mono,monospace" font-size="9" fill="rgba(232,184,75,.8)">${t}°</text>`;
    }
  });

  const curIdx = hourly.findIndex((h) => h.isCurrent);
  let curLine = '';
  if (curIdx >= 0) {
    const cx = xOf(curIdx);
    curLine = `<line x1="${cx.toFixed(1)}" y1="${PAD.t}" x2="${cx.toFixed(1)}" y2="${H - PAD.b}" stroke="rgba(255,255,255,.15)" stroke-width="1" stroke-dasharray="3,3"/>`;
  }

  const gid = 'tcg' + Date.now();
  svg.innerHTML =
    `<defs><linearGradient id="${gid}" x1="0" y1="0" x2="0" y2="1">` +
    '<stop offset="0%" stop-color="var(--accent)" stop-opacity=".35"/>' +
    '<stop offset="100%" stop-color="var(--accent)" stop-opacity=".02"/>' +
    '</linearGradient></defs>' +
    bars +
    `<path d="${fillPath}" fill="url(#${gid})"/>` +
    `<path d="${linePath}" fill="none" stroke="var(--accent)" stroke-width="2" stroke-linejoin="round"/>` +
    curLine + tempLabels + labels;
}

export function renderHourly(hourly, metric) {
  const el = $('hour-scroll');
  if (!el) return;
  el.innerHTML = '';
  hourly.forEach((h) => {
    const d = document.createElement('div');
    d.className = 'hour-card' + (h.isCurrent ? ' now' : '');
    const temp = metric ? h.tempC + '°C' : h.tempF + '°F';
    d.innerHTML =
      `<div class="hour-time">${h.timeLabel}</div>` +
      `<div class="hour-emoji">${h.emoji}</div>` +
      `<div class="hour-temp">${temp}</div>` +
      (h.precipProb > 5 ? `<div class="hour-precip">${h.precipProb}%</div>` : '<div class="hour-precip" style="opacity:0">·</div>');
    el.appendChild(d);
  });
  const nowCard = el.querySelector('.now');
  if (nowCard) nowCard.scrollIntoView({ inline: 'center', block: 'nearest' });
}

export function renderDaily(daily, metric) {
  const el = $('daily-list');
  if (!el) return;
  el.innerHTML = '';

  const allHi = daily.map((d) => (metric ? d.highC : d.highF));
  const allLo = daily.map((d) => (metric ? d.lowC : d.lowF));
  const wkMax = Math.max(...allHi);
  const wkMin = Math.min(...allLo);
  const wkRange = Math.max(wkMax - wkMin, 2);

  daily.forEach((row) => {
    const hi = metric ? row.highC : row.highF;
    const lo = metric ? row.lowC : row.lowF;
    const loOffset = (((lo - wkMin) / wkRange) * 100).toFixed(1);
    const barW = (((hi - lo) / wkRange) * 100).toFixed(1);
    const d = document.createElement('div');
    d.className = 'day-row';
    d.innerHTML =
      `<div class="day-name${row.dayName === 'Today' ? ' today' : ''}">${row.dayName}</div>` +
      `<div class="day-emoji">${row.emoji}</div>` +
      '<div class="day-bar-area">' +
      `<span class="day-lo">${lo}°</span>` +
      `<div class="day-bar-track"><div class="day-bar-fill" style="margin-left:${loOffset}%;width:${barW}%"></div></div>` +
      `<span class="day-hi">${hi}°</span>` +
      '</div>' +
      `<div class="day-precip">${row.precipProbMax > 5 ? row.precipProbMax + '%' : ''}</div>`;
    el.appendChild(d);
  });
}

// color comes from the server's AirQuality response — the client renders it
// rather than recomputing AQI category/color itself (closes the reference
// app's own duplication between fetchAirQuality and its aqCat() helper).
export function drawAqiBar(aqi, color) {
  const svg = $('aqi-bar-svg');
  if (!svg) return;
  const W = 300, H = 12, r = 3;
  const pct = Math.min(aqi / 500, 1);
  const filled = pct * (W - 8);
  svg.innerHTML =
    '<defs><linearGradient id="aqig" x1="0" y1="0" x2="1" y2="0">' +
    '<stop offset="0%" stop-color="#22c55e"/>' +
    '<stop offset="33%" stop-color="#eab308"/>' +
    '<stop offset="55%" stop-color="#f97316"/>' +
    '<stop offset="70%" stop-color="#ef4444"/>' +
    '<stop offset="85%" stop-color="#a855f7"/>' +
    '<stop offset="100%" stop-color="#7e0023"/>' +
    '</linearGradient></defs>' +
    `<rect x="4" y="1" width="${W - 8}" height="${H - 2}" rx="${r}" fill="rgba(255,255,255,.08)"/>` +
    `<rect x="4" y="1" width="${Math.max(0, filled).toFixed(1)}" height="${H - 2}" rx="${r}" fill="url(#aqig)"/>` +
    `<circle cx="${(4 + filled).toFixed(1)}" cy="${H / 2}" r="4.5" fill="${color}" stroke="rgba(0,0,0,.3)" stroke-width="1"/>`;
}
