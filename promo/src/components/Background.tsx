import React from "react";
import { AbsoluteFill, useCurrentFrame } from "remotion";
import { COLORS } from "../theme";

const NOISE_SVG = `<svg xmlns='http://www.w3.org/2000/svg' width='220' height='220'><filter id='n'><feTurbulence type='fractalNoise' baseFrequency='0.85' numOctaves='3' stitchTiles='stitch'/><feColorMatrix type='saturate' values='0'/></filter><rect width='100%' height='100%' filter='url(#n)'/></svg>`;
const NOISE = `url("data:image/svg+xml;utf8,${encodeURIComponent(NOISE_SVG)}")`;

const GRID_MASK = "radial-gradient(ellipse 66% 62% at 50% 46%, #000 12%, transparent 78%)";

/** Fundo contínuo do vídeo: base #090D16, brilhos radiais ciano/índigo, grade e grão. */
export const Background: React.FC = () => {
  const frame = useCurrentFrame();
  const t = frame / 30;
  const cyanX = 18 + Math.sin(t * 0.35) * 7;
  const cyanY = 16 + Math.cos(t * 0.29) * 6;
  const indigoX = 84 + Math.cos(t * 0.31) * 7;
  const indigoY = 82 + Math.sin(t * 0.27) * 6;

  return (
    <AbsoluteFill style={{ backgroundColor: COLORS.bg }}>
      <AbsoluteFill
        style={{
          background: `radial-gradient(48% 56% at ${cyanX}% ${cyanY}%, rgba(6,182,212,0.18) 0%, rgba(6,182,212,0.05) 45%, transparent 75%)`,
        }}
      />
      <AbsoluteFill
        style={{
          background: `radial-gradient(52% 58% at ${indigoX}% ${indigoY}%, rgba(99,102,241,0.22) 0%, rgba(99,102,241,0.06) 45%, transparent 75%)`,
        }}
      />
      <AbsoluteFill
        style={{
          backgroundImage:
            "linear-gradient(rgba(255,255,255,0.045) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,0.045) 1px, transparent 1px)",
          backgroundSize: "72px 72px",
          backgroundPosition: "center center",
          maskImage: GRID_MASK,
          WebkitMaskImage: GRID_MASK,
        }}
      />
      <AbsoluteFill style={{ background: "radial-gradient(42% 32% at 50% -6%, rgba(255,255,255,0.07), transparent 70%)" }} />
      <AbsoluteFill style={{ background: "radial-gradient(ellipse 86% 78% at 50% 50%, transparent 55%, rgba(2,4,10,0.62) 100%)" }} />
      <AbsoluteFill style={{ backgroundImage: NOISE, backgroundSize: "220px 220px", opacity: 0.05, mixBlendMode: "overlay" }} />
    </AbsoluteFill>
  );
};
