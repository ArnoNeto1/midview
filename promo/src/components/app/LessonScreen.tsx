import React from "react";
import { FONTS } from "../../fonts";
import { Box } from "./ScreenContent";

const NODES = [
  [236, 60],
  [286, 92],
  [272, 148],
  [206, 154],
  [184, 104],
] as const;

/** Tela do professor transmitida para os alunos (canvas virtual 320×200). */
export const LessonScreen: React.FC<{ frame: number }> = ({ frame }) => {
  const pulse = 0.5 + 0.5 * Math.sin(frame * 0.12);
  return (
    <div
      style={{
        position: "absolute",
        inset: 0,
        overflow: "hidden",
        background: "linear-gradient(135deg, #1E1B4B 0%, #312E81 40%, #155E75 100%)",
      }}
    >
      <div style={{ position: "absolute", inset: 0, background: "radial-gradient(60% 70% at 80% 30%, rgba(34,211,238,0.28), transparent 70%)" }} />
      <div
        style={{
          position: "absolute",
          left: 18,
          top: 22,
          fontFamily: FONTS.mono,
          fontSize: 10,
          fontWeight: 600,
          letterSpacing: "0.14em",
          color: "#A5F3FC",
        }}
      >
        AULA 07
      </div>
      <div
        style={{
          position: "absolute",
          left: 18,
          top: 40,
          fontFamily: FONTS.display,
          fontSize: 25,
          lineHeight: 1.04,
          fontWeight: 800,
          letterSpacing: "-0.02em",
          color: "#FFFFFF",
        }}
      >
        Redes de
        <br />
        Computadores
      </div>
      <Box x={18} y={104} w={96} h={5} c="rgba(255,255,255,0.55)" />
      <Box x={18} y={115} w={74} h={5} c="rgba(255,255,255,0.35)" />
      <svg style={{ position: "absolute", left: 0, top: 0 }} width={320} height={200}>
        {NODES.map(([x, y], i) => (
          <line key={`l${i}`} x1={236} y1={108} x2={x} y2={y} stroke="#A5F3FC" strokeOpacity={0.55} strokeWidth={1.5} />
        ))}
        {NODES.map(([x, y], i) => (
          <circle key={`n${i}`} cx={x} cy={y} r={7} fill="#0F172A" stroke="#67E8F9" strokeWidth={2} />
        ))}
        <circle cx={236} cy={108} r={14 + pulse * 3} fill="rgba(34,211,238,0.2)" />
        <circle cx={236} cy={108} r={11} fill="#FFFFFF" />
      </svg>
    </div>
  );
};
