// Sky theme engine — ported from weather-server.ts:1377-1478. Preserves the
// exact rnd() ranges and per-theme parameters from the reference app.
import { $ } from './state.js';

const THEMES = {
  hot:        { bg: 'linear-gradient(170deg,#020c1e 0%,#08204a 55%,#0e3470 100%)', glow: 'radial-gradient(ellipse 80% 45% at 50% -5%,rgba(255,200,50,.4) 0%,transparent 70%)', orb: 'orb-sun-hot', top: '-90px', stars: false, rain: false, snow: false, clouds: false, fog: false, storm: false, accent: '#ffd54f', adim: '#c09030' },
  sunny:      { bg: 'linear-gradient(170deg,#030e20 0%,#081c3c 55%,#0d2c5c 100%)', glow: 'radial-gradient(ellipse 70% 40% at 50% -5%,rgba(232,184,75,.3) 0%,transparent 70%)', orb: 'orb-sun', top: '-70px', stars: false, rain: false, snow: false, clouds: false, fog: false, storm: false, accent: '#e8b84b', adim: '#a07a28' },
  cool:       { bg: 'linear-gradient(170deg,#020810 0%,#060e1e 55%,#0a1830 100%)', glow: 'radial-gradient(ellipse 65% 35% at 50% -5%,rgba(160,200,240,.2) 0%,transparent 70%)', orb: 'orb-sun-cool', top: '-55px', stars: false, rain: false, snow: false, clouds: false, fog: false, storm: false, accent: '#90b8d8', adim: '#6090b0' },
  partly:     { bg: 'linear-gradient(170deg,#030a14 0%,#071424 55%,#0b1e38 100%)', glow: 'radial-gradient(ellipse 60% 30% at 50% -5%,rgba(200,160,50,.15) 0%,transparent 70%)', orb: 'orb-sun', top: '-70px', stars: false, rain: false, snow: false, clouds: true, fog: false, storm: false, accent: '#c8a040', adim: '#806820' },
  overcast:   { bg: 'linear-gradient(170deg,#040608 0%,#080c10 55%,#0c1018 100%)', glow: 'radial-gradient(ellipse 80% 40% at 50% -5%,rgba(80,100,130,.15) 0%,transparent 70%)', orb: null, stars: false, rain: false, snow: false, clouds: true, fog: false, storm: false, accent: '#788ca0', adim: '#4a5e70' },
  night:      { bg: 'linear-gradient(170deg,#010306 0%,#040618 55%,#080c2a 100%)', glow: 'radial-gradient(ellipse 60% 40% at 50% 0%,rgba(80,60,160,.2) 0%,transparent 70%)', orb: 'orb-moon', top: '-45px', stars: true, rain: false, snow: false, clouds: false, fog: false, storm: false, accent: '#9080c8', adim: '#605090' },
  coldnight:  { bg: 'linear-gradient(170deg,#010205 0%,#030410 55%,#050620 100%)', glow: 'radial-gradient(ellipse 55% 35% at 50% 0%,rgba(60,80,160,.18) 0%,transparent 70%)', orb: 'orb-moon-cold', top: '-40px', stars: true, rain: false, snow: false, clouds: false, fog: false, storm: false, accent: '#7090c8', adim: '#406080' },
  cloudnight: { bg: 'linear-gradient(170deg,#010206 0%,#03050e 55%,#050818 100%)', glow: 'radial-gradient(ellipse 60% 30% at 50% 0%,rgba(40,50,80,.15) 0%,transparent 70%)', orb: 'orb-moon', top: '-45px', stars: false, rain: false, snow: false, clouds: true, fog: false, storm: false, accent: '#6878a0', adim: '#405070' },
  rain:       { bg: 'linear-gradient(170deg,#020408 0%,#04080e 55%,#060c14 100%)', glow: 'radial-gradient(ellipse 80% 40% at 50% -5%,rgba(50,80,120,.15) 0%,transparent 70%)', orb: null, stars: false, rain: true, snow: false, clouds: true, fog: false, storm: false, accent: '#6090b8', adim: '#3a6080' },
  storm:      { bg: 'linear-gradient(170deg,#010104 0%,#020208 55%,#03030e 100%)', glow: 'radial-gradient(ellipse 90% 50% at 50% -5%,rgba(30,20,80,.2) 0%,transparent 70%)', orb: null, stars: false, rain: true, snow: false, clouds: true, fog: false, storm: true, accent: '#6060a0', adim: '#404070' },
  snow:       { bg: 'linear-gradient(170deg,#030610 0%,#060b1c 55%,#0a1028 100%)', glow: 'radial-gradient(ellipse 70% 40% at 50% -5%,rgba(140,180,230,.12) 0%,transparent 70%)', orb: null, stars: false, rain: false, snow: true, clouds: true, fog: false, storm: false, accent: '#90b0d8', adim: '#6080a0' },
  fog:        { bg: 'linear-gradient(170deg,#050608 0%,#08090c 55%,#0c0d12 100%)', glow: 'radial-gradient(ellipse 90% 50% at 50% 0%,rgba(80,90,110,.1) 0%,transparent 70%)', orb: null, stars: false, rain: false, snow: false, clouds: false, fog: true, storm: false, accent: '#7888a0', adim: '#4a5868' },
};

export function getThemeKey(d) {
  const c = d.condition.toLowerCase(), n = !d.isDay, t = d.tempF;
  if (c.includes('thunder') || c.includes('storm')) return 'storm';
  if (c.includes('snow') || c.includes('grains')) return 'snow';
  if (c.includes('fog')) return 'fog';
  if (c.includes('drizzle') || c.includes('rain') || c.includes('shower')) return 'rain';
  if (c.includes('overcast')) return n ? 'cloudnight' : 'overcast';
  if (c.includes('partly') || c.includes('cloud')) return n ? 'cloudnight' : 'partly';
  if (n) return t < 32 ? 'coldnight' : 'night';
  if (t >= 85) return 'hot';
  if (t < 45) return 'cool';
  return 'sunny';
}

function rnd(a, b) { return a + Math.random() * (b - a); }

function hexToRgb(hex) {
  const r = parseInt(hex.slice(1, 3), 16), g = parseInt(hex.slice(3, 5), 16), b = parseInt(hex.slice(5, 7), 16);
  return `${r},${g},${b}`;
}

function makeStars(el) {
  for (let i = 0; i < 90; i++) {
    const s = document.createElement('div');
    s.className = 'star';
    const sz = Math.random() < .15 ? 2 : 1;
    s.style.cssText = `width:${sz}px;height:${sz}px;top:${rnd(2, 75).toFixed(1)}%;left:${rnd(0, 100).toFixed(1)}%;--d:${rnd(2, 5).toFixed(1)}s;--dl:-${rnd(0, 4).toFixed(1)}s;--lo:${rnd(.1, .4).toFixed(2)}`;
    el.appendChild(s);
  }
}
function makeRain(el, heavy) {
  for (let i = 0; i < (heavy ? 90 : 55); i++) {
    const d = document.createElement('div');
    d.className = 'drop';
    d.style.cssText = `left:${rnd(0, 100).toFixed(1)}%;height:${rnd(10, 22)}px;--s:${rnd(.45, .85).toFixed(2)}s;--dl:-${rnd(0, 2).toFixed(2)}s`;
    el.appendChild(d);
  }
}
function makeSnow(el) {
  const chars = ['❄', '❅', '❆', '•'];
  for (let i = 0; i < 40; i++) {
    const f = document.createElement('div');
    f.className = 'flake';
    f.textContent = chars[i % 4];
    const dx = (Math.random() > .5 ? 1 : -1) * rnd(20, 70);
    f.style.cssText = `left:${rnd(0, 100).toFixed(1)}%;font-size:${rnd(.5, 1.1).toFixed(1)}rem;--s:${rnd(5, 10).toFixed(1)}s;--dl:-${rnd(0, 9).toFixed(1)}s;--dx:${dx.toFixed(0)}px;--rot:${Math.random() > .5 ? 360 : -360}deg`;
    el.appendChild(f);
  }
}
function makeClouds(el) {
  [{ w: 220, h: 85, top: '1%', left: '8%', op: .28, dur: 70, dx: 25 },
   { w: 290, h: 110, top: '4%', left: '48%', op: .22, dur: 95, dx: 35 },
   { w: 190, h: 75, top: '0%', left: '73%', op: .26, dur: 58, dx: 20 },
   { w: 230, h: 90, top: '7%', left: '28%', op: .18, dur: 82, dx: 30 }].forEach((c) => {
    const p = document.createElement('div');
    p.className = 'cloud-puff';
    p.style.cssText = `width:${c.w}px;height:${c.h}px;top:${c.top};left:${c.left};background:rgba(50,60,80,${c.op});--d:${c.dur}s;--dx:${c.dx}px`;
    el.appendChild(p);
  });
}
function makeFog(el) {
  for (let i = 0; i < 5; i++) {
    const f = document.createElement('div');
    f.className = 'fog-layer';
    const dx = (Math.random() > .5 ? 1 : -1) * rnd(25, 55);
    f.style.cssText = `top:${12 + i * 16}%;height:${rnd(40, 70).toFixed(0)}px;--d:${rnd(22, 40).toFixed(0)}s;--dx:${dx.toFixed(0)}px;opacity:${rnd(.4, .8).toFixed(2)}`;
    el.appendChild(f);
  }
}

export function applyTheme(d) {
  const t = THEMES[getThemeKey(d)] || THEMES.sunny;
  $('sky-bg').style.background = t.bg;
  $('sky-glow').style.background = t.glow;
  const orb = $('sky-orb');
  if (t.orb) {
    orb.style.cssText = `position:absolute;top:${t.top};left:50%;transform:translateX(-50%);border-radius:50%;display:block`;
    orb.className = t.orb;
  } else {
    orb.style.display = 'none';
  }
  ['sky-stars', 'sky-rain', 'sky-snow', 'sky-clouds', 'sky-fog'].forEach((id) => { $(id).innerHTML = ''; });
  const li = $('sky-lightning');
  li.style.animation = '';
  li.style.display = 'none';
  if (t.stars) makeStars($('sky-stars'));
  if (t.rain) makeRain($('sky-rain'), t.storm);
  if (t.snow) makeSnow($('sky-snow'));
  if (t.clouds) makeClouds($('sky-clouds'));
  if (t.fog) makeFog($('sky-fog'));
  if (t.storm) { li.style.display = 'block'; li.style.animation = `lightning ${rnd(4, 7).toFixed(0)}s ease-in-out infinite`; }
  document.documentElement.style.setProperty('--accent', t.accent);
  document.documentElement.style.setProperty('--accent-dim', t.adim);
  document.documentElement.style.setProperty('--accent-glow', `rgba(${hexToRgb(t.accent)},.18)`);
}
