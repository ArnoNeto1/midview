import React from "react";
import { spring } from "remotion";
import { clamp01 } from "../../lib/motion";
import type { Point } from "./layout";

/** Ponteiro do mouse; (x, y) é a ponta da seta. */
export const Cursor: React.FC<{ x: number; y: number; press: number; opacity: number }> = ({ x, y, press, opacity }) =>
  opacity > 0.01 ? (
    <div
      style={{
        position: "absolute",
        left: x - 3,
        top: y - 2,
        opacity,
        transform: `scale(${1 - 0.16 * press})`,
        transformOrigin: "3px 2px",
        filter: "drop-shadow(0 8px 14px rgba(0,0,0,0.55))",
      }}
    >
      <svg width={30} height={36} viewBox="0 0 30 36">
        <path
          d="M3 2 L3 27.5 L9.6 21.6 L14 31.8 L18.6 29.9 L14.3 19.9 L23 19.9 Z"
          fill="#FFFFFF"
          stroke="#0B1020"
          strokeWidth={1.6}
          strokeLinejoin="round"
        />
      </svg>
    </div>
  ) : null;

/** Onda de clique que se expande a partir do ponto clicado. */
export const ClickRipple: React.FC<{ x: number; y: number; progress: number }> = ({ x, y, progress }) => {
  if (progress <= 0 || progress >= 1) return null;
  const r = 8 + progress * 42;
  return (
    <div
      style={{
        position: "absolute",
        left: x - r,
        top: y - r,
        width: r * 2,
        height: r * 2,
        borderRadius: "50%",
        border: "2px solid rgba(165,243,252,0.9)",
        boxShadow: "0 0 18px rgba(34,211,238,0.5)",
        opacity: (1 - progress) * 0.9,
      }}
    />
  );
};

/** Um movimento do cursor: começa em `at` e vai até `to`, com `arc` px de curvatura. */
export type CursorMove = { at: number; to: Point; arc?: number };

/** Cada movimento é uma mola até o alvo, com um leve arco, como o gesto de uma mão. */
export const cursorPosition = (frame: number, fps: number, start: Point, moves: CursorMove[]): Point => {
  let from = start;
  let position = start;
  for (const move of moves) {
    if (frame < move.at) break;
    const p = spring({ frame: frame - move.at, fps, config: { damping: 18, stiffness: 120, mass: 0.85 } });
    const dx = move.to.x - from.x;
    const dy = move.to.y - from.y;
    const length = Math.hypot(dx, dy) || 1;
    const bow = Math.sin(Math.PI * clamp01(p)) * (move.arc ?? 0);
    position = { x: from.x + dx * p - (dy / length) * bow, y: from.y + dy * p + (dx / length) * bow };
    from = move.to;
  }
  return position;
};
