import { getStaticFiles } from "@remotion/studio";
import React, { useId } from "react";
import { Img, interpolate } from "remotion";
import { clamp01 } from "../lib/motion";
import { COLORS } from "../theme";
import { LOGO_PRIMITIVES, LOGO_STROKE, LOGO_VIEWBOX, primitivePath, type Phase } from "./logoGeometry";

/**
 * Logo oficial: salve o SVG em `public/` com um destes nomes e ela substitui
 * o símbolo vetorial padrão em todo o vídeo (o primeiro encontrado vence).
 */
export const CUSTOM_LOGO_FILES = ["logo.svg", "midview-logo.svg", "midview.svg"];

/** URL da logo personalizada em public/, ou null para usar o fallback vetorial. */
export const findCustomLogo = (): string | null => {
  const files = getStaticFiles();
  for (const name of CUSTOM_LOGO_FILES) {
    const file = files.find((f) => f.name.toLowerCase() === name);
    if (file) return file.src;
  }
  return null;
};

type MidviewLogoProps = {
  /** Lado do símbolo, em px. */
  size: number;
  /** 0→1: desenho dos traços na entrada (na logo personalizada: fade + escala). */
  progress?: number;
  /** 0→1: intensidade do brilho. */
  glow?: number;
  /** Força um arquivo, ex.: staticFile("minha-logo.svg"). */
  src?: string;
  style?: React.CSSProperties;
};

const glowFilter = (glow: number) =>
  glow > 0.001
    ? `drop-shadow(0 0 ${6 + glow * 20}px rgba(34,211,238,${0.2 + glow * 0.45})) drop-shadow(0 0 ${2 + glow * 4}px rgba(99,102,241,${glow * 0.55}))`
    : undefined;

export const MidviewLogo: React.FC<MidviewLogoProps> = ({ size, progress = 1, glow = 0, src, style }) => {
  const custom = src ?? findCustomLogo();
  if (custom) {
    const p = clamp01(progress);
    return (
      <div style={{ width: size, height: size, ...style }}>
        <Img
          src={custom}
          style={{
            width: "100%",
            height: "100%",
            objectFit: "contain",
            opacity: p,
            transform: `scale(${interpolate(p, [0, 1], [0.86, 1])})`,
            filter: glowFilter(glow),
          }}
        />
      </div>
    );
  }
  return <FallbackMark size={size} progress={progress} glow={glow} style={style} />;
};

const local = (progress: number, [start, end]: Phase) => clamp01((progress - start) / (end - start));

const FallbackMark: React.FC<Omit<MidviewLogoProps, "src">> = ({ size, progress = 1, glow = 0, style }) => {
  const id = `mv${useId().replace(/[^a-zA-Z0-9]/g, "")}`;
  // Em tamanhos pequenos (barra de título) o traço engrossa para continuar legível.
  const stroke = Math.max(LOGO_STROKE, (1.15 * LOGO_VIEWBOX) / size);
  const paint = `url(#${id})`;
  return (
    <svg
      width={size}
      height={size}
      viewBox={`0 0 ${LOGO_VIEWBOX} ${LOGO_VIEWBOX}`}
      style={{ overflow: "visible", filter: glowFilter(glow), ...style }}
    >
      <defs>
        <linearGradient id={id} gradientUnits="userSpaceOnUse" x1="0" y1="0" x2={LOGO_VIEWBOX} y2="0">
          <stop offset="0" stopColor="#EEF2F7" />
          <stop offset="0.5" stopColor={COLORS.logoGray} />
          <stop offset="0.5" stopColor={COLORS.cyanLight} />
          <stop offset="1" stopColor={COLORS.cyan} />
        </linearGradient>
      </defs>
      <g fill="none" stroke={paint} strokeWidth={stroke} strokeLinecap="round" strokeLinejoin="round">
        {LOGO_PRIMITIVES.map((p, i) => {
          const t = local(progress, p.phase);
          if (t <= 0) return null;
          if (p.kind === "dot") return <circle key={i} cx={p.cx} cy={p.cy} r={p.r * t} fill={paint} stroke="none" />;
          return <path key={i} d={primitivePath(p)} pathLength={1} strokeDasharray="1 1" strokeDashoffset={1 - t} />;
        })}
      </g>
    </svg>
  );
};
