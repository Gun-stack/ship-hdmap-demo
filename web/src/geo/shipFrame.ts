/** Ship Frame: x forward from AP, y port (+), z up, metres. Heading psi CCW from +x (deg).
 *  Georef.heading_deg is a different quantity: bow bearing clockwise from true north (deg). */
export type Georef = { ap_lat: number; ap_lon: number; heading_deg: number };

export const METRES_PER_DEG_LAT = 111_320;

/** (-180, 180] */
export function wrapDeg(a: number): number {
  a %= 360;
  if (a <= -180) a += 360; else if (a > 180) a -= 360;
  return a;
}

/** (-pi, pi] */
export function wrapRad(a: number): number {
  a %= 2 * Math.PI;
  if (a <= -Math.PI) a += 2 * Math.PI; else if (a > Math.PI) a -= 2 * Math.PI;
  return a;
}

/** Same as C#/Java: (x, y, z) -> (x, z, -y). `+ 0` normalizes -0 (e.g. y=0) to 0. */
export function toUnity(x: number, y: number, z: number): [number, number, number] { return [x, z, -y + 0]; }

export function unityYawDeg(psiDeg: number): number { return psiDeg; }

export function toWgs84(g: Georef, x: number, y: number): { lat: number; lon: number } {
  const h = (g.heading_deg * Math.PI) / 180;
  const e = x * Math.sin(h) - y * Math.cos(h);
  const n = x * Math.cos(h) + y * Math.sin(h);
  return { lat: g.ap_lat + n / METRES_PER_DEG_LAT, lon: g.ap_lon + e / (METRES_PER_DEG_LAT * Math.cos((g.ap_lat * Math.PI) / 180)) };
}
