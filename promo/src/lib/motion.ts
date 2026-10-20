import { Easing, interpolate, spring, type SpringConfig } from "remotion";

/** Molas usadas no vídeo: física elástica suave, sem travamentos. */
export const SPRING = {
  smooth: { damping: 200 },
  soft: { damping: 26, stiffness: 90, mass: 1 },
  snappy: { damping: 18, stiffness: 170, mass: 0.7 },
  bouncy: { damping: 10, stiffness: 150, mass: 0.6 },
} satisfies Record<string, Partial<SpringConfig>>;

export const EASE = {
  out: Easing.bezier(0.16, 1, 0.3, 1),
  inOut: Easing.bezier(0.65, 0, 0.35, 1),
  linear: (t: number) => t,
};

export const CLAMP = { extrapolateLeft: "clamp", extrapolateRight: "clamp" } as const;

/** spring() que começa no frame `at`. */
export const springAt = (
  frame: number,
  fps: number,
  at: number,
  config: Partial<SpringConfig> = SPRING.smooth,
  durationInFrames?: number,
) => spring({ frame: frame - at, fps, config, durationInFrames });

/** 0→1 entre dois frames, com clamp e easing. */
export const ramp = (frame: number, from: number, to: number, easing: (t: number) => number = EASE.out) =>
  interpolate(frame, [from, to], [0, 1], { ...CLAMP, easing });

/** Pulso 0→1→0, para cliques e flashes. */
export const pulse = (frame: number, at: number, rise: number, fall: number) =>
  interpolate(frame, [at, at + rise, at + rise + fall], [0, 1, 0], CLAMP);

export const mix = (a: number, b: number, t: number) => a + (b - a) * t;

export const clamp01 = (value: number) => Math.min(1, Math.max(0, value));
