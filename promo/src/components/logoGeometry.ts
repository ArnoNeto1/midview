/**
 * Símbolo do midview em vetor (viewBox 128×128), redesenhado a partir de
 * assets/midview-logo.jpg: monitor central com sinal de transmissão, ligado em rede
 * a quatro monitores nos cantos. Metade esquerda cinza, metade direita ciano.
 *
 * A mesma geometria desenha o SVG (MidviewLogo) e gera os alvos das partículas da Cena 1.
 */
export type Phase = readonly [start: number, end: number];

export type LogoPrimitive =
  | { kind: "rect"; x: number; y: number; w: number; h: number; r: number; phase: Phase }
  | { kind: "line"; x1: number; y1: number; x2: number; y2: number; phase: Phase }
  | { kind: "arc"; cx: number; cy: number; r: number; from: number; to: number; phase: Phase }
  | { kind: "dot"; cx: number; cy: number; r: number; phase: Phase };

export const LOGO_VIEWBOX = 128;
export const LOGO_CENTER = 64;
export const LOGO_STROKE = 3.4;

const SCREEN = { w: 28, h: 20, r: 4 };
const CORNERS: Phase = [0.62, 1];
const LINKS: Phase = [0.42, 0.82];

const cornerMonitor = (x: number, y: number): LogoPrimitive[] => {
  const cx = x + SCREEN.w / 2;
  const bottom = y + SCREEN.h;
  return [
    { kind: "rect", x, y, w: SCREEN.w, h: SCREEN.h, r: SCREEN.r, phase: CORNERS },
    { kind: "line", x1: cx, y1: bottom, x2: cx, y2: bottom + 5, phase: CORNERS },
    { kind: "line", x1: cx - 6, y1: bottom + 5, x2: cx + 6, y2: bottom + 5, phase: CORNERS },
  ];
};

/** Ordem = ordem de desenho: monitor central, sinal, rede, monitores dos cantos. */
export const LOGO_PRIMITIVES: LogoPrimitive[] = [
  { kind: "rect", x: 36, y: 38, w: 56, h: 40, r: 6, phase: [0, 0.36] },
  { kind: "line", x1: 64, y1: 78, x2: 64, y2: 86, phase: [0.24, 0.44] },
  { kind: "line", x1: 53, y1: 86, x2: 75, y2: 86, phase: [0.3, 0.5] },
  { kind: "dot", cx: 64, cy: 58, r: 3.4, phase: [0.28, 0.42] },
  { kind: "arc", cx: 64, cy: 58, r: 8.5, from: 135, to: 225, phase: [0.32, 0.52] },
  { kind: "arc", cx: 64, cy: 58, r: 8.5, from: -45, to: 45, phase: [0.32, 0.52] },
  { kind: "arc", cx: 64, cy: 58, r: 15, from: 135, to: 225, phase: [0.4, 0.62] },
  { kind: "arc", cx: 64, cy: 58, r: 15, from: -45, to: 45, phase: [0.4, 0.62] },
  { kind: "line", x1: 34, y1: 18, x2: 94, y2: 18, phase: LINKS },
  { kind: "line", x1: 64, y1: 18, x2: 64, y2: 38, phase: LINKS },
  { kind: "line", x1: 34, y1: 102, x2: 94, y2: 102, phase: LINKS },
  { kind: "line", x1: 64, y1: 86, x2: 64, y2: 102, phase: LINKS },
  { kind: "line", x1: 20, y1: 33, x2: 20, y2: 92, phase: LINKS },
  { kind: "line", x1: 20, y1: 58, x2: 36, y2: 58, phase: LINKS },
  { kind: "line", x1: 108, y1: 33, x2: 108, y2: 92, phase: LINKS },
  { kind: "line", x1: 108, y1: 58, x2: 92, y2: 58, phase: LINKS },
  ...cornerMonitor(6, 8),
  ...cornerMonitor(94, 8),
  ...cornerMonitor(6, 92),
  ...cornerMonitor(94, 92),
];

type Point = { x: number; y: number };

const polar = (cx: number, cy: number, r: number, deg: number): Point => ({
  x: cx + r * Math.cos((deg * Math.PI) / 180),
  y: cy + r * Math.sin((deg * Math.PI) / 180),
});

/** Path SVG de uma primitiva (pontos não têm path: viram <circle>). */
export const primitivePath = (p: Exclude<LogoPrimitive, { kind: "dot" }>): string => {
  switch (p.kind) {
    case "rect": {
      const { x, y, w, h, r } = p;
      return `M${x + r} ${y}H${x + w - r}A${r} ${r} 0 0 1 ${x + w} ${y + r}V${y + h - r}A${r} ${r} 0 0 1 ${x + w - r} ${y + h}H${x + r}A${r} ${r} 0 0 1 ${x} ${y + h - r}V${y + r}A${r} ${r} 0 0 1 ${x + r} ${y}Z`;
    }
    case "line":
      return `M${p.x1} ${p.y1}L${p.x2} ${p.y2}`;
    case "arc": {
      const a = polar(p.cx, p.cy, p.r, p.from);
      const b = polar(p.cx, p.cy, p.r, p.to);
      const large = Math.abs(p.to - p.from) > 180 ? 1 : 0;
      return `M${a.x} ${a.y}A${p.r} ${p.r} 0 ${large} 1 ${b.x} ${b.y}`;
    }
  }
};

const rectPoint = (p: Extract<LogoPrimitive, { kind: "rect" }>, d: number): Point => {
  if (d < p.w) return { x: p.x + d, y: p.y };
  if (d < p.w + p.h) return { x: p.x + p.w, y: p.y + d - p.w };
  if (d < 2 * p.w + p.h) return { x: p.x + p.w - (d - p.w - p.h), y: p.y + p.h };
  return { x: p.x, y: p.y + p.h - (d - 2 * p.w - p.h) };
};

/** Pontos distribuídos ao longo dos traços do símbolo, a cada ~`spacing` unidades. */
export const sampleLogo = (spacing: number): Point[] => {
  const points: Point[] = [];
  for (const p of LOGO_PRIMITIVES) {
    switch (p.kind) {
      case "rect": {
        const perimeter = 2 * (p.w + p.h);
        const n = Math.max(4, Math.round(perimeter / spacing));
        for (let i = 0; i < n; i++) points.push(rectPoint(p, (i / n) * perimeter));
        break;
      }
      case "line": {
        const n = Math.max(1, Math.round(Math.hypot(p.x2 - p.x1, p.y2 - p.y1) / spacing));
        for (let i = 0; i <= n; i++) points.push({ x: p.x1 + ((p.x2 - p.x1) * i) / n, y: p.y1 + ((p.y2 - p.y1) * i) / n });
        break;
      }
      case "arc": {
        const length = (Math.abs(p.to - p.from) * Math.PI * p.r) / 180;
        const n = Math.max(1, Math.round(length / spacing));
        for (let i = 0; i <= n; i++) points.push(polar(p.cx, p.cy, p.r, p.from + ((p.to - p.from) * i) / n));
        break;
      }
      case "dot":
        points.push({ x: p.cx, y: p.cy });
        break;
    }
  }
  return points;
};
