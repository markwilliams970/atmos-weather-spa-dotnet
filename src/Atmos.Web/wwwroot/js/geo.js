// Web Mercator tile math — the one piece of logic genuinely shared between
// radar.js and map-picker.js. The two tile-rendering loops themselves stay
// separate on purpose (see map-picker.js) — only this coordinate math is
// shared, exactly as in the reference app (Claude.md's own documented
// rationale for that split).

export function xyFromLatLon(lat, lon, z) {
  const n = Math.pow(2, z);
  const x = (lon + 180) / 360 * n;
  const lr = lat * Math.PI / 180;
  const y = (1 - Math.log(Math.tan(lr) + 1 / Math.cos(lr)) / Math.PI) / 2 * n;
  return { tx: Math.floor(x), ty: Math.floor(y), fx: x % 1, fy: y % 1 };
}

export function lonLatFromXY(x, y, z) {
  const n = Math.pow(2, z);
  const lon = x / n * 360 - 180;
  const latRad = Math.atan(Math.sinh(Math.PI * (1 - 2 * y / n)));
  const lat = latRad * 180 / Math.PI;
  return { lat, lon };
}
