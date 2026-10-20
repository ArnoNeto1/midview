import type { CSSProperties } from "react";

/** Paleta do vídeo: dark mode com acentos ciano e índigo. */
export const COLORS = {
  bg: "#090D16",
  cyan: "#06B6D4",
  cyanLight: "#22D3EE",
  cyanPale: "#67E8F9",
  indigo: "#6366F1",
  indigoLight: "#818CF8",
  indigoPale: "#A5B4FC",
  green: "#22C55E",
  greenLight: "#4ADE80",
  emerald: "#34D399",
  white: "#F8FAFC",
  slate200: "#E2E8F0",
  slate300: "#CBD5E1",
  slate400: "#94A3B8",
  slate500: "#64748B",
  slate600: "#475569",
  /** Cinza da metade esquerda da logo original. */
  logoGray: "#D3D9E2",
} as const;

export const GRADIENTS = {
  brand: `linear-gradient(135deg, ${COLORS.cyanLight} 0%, ${COLORS.indigo} 100%)`,
  text: `linear-gradient(92deg, ${COLORS.cyanPale} 0%, ${COLORS.cyanLight} 38%, ${COLORS.indigoLight} 100%)`,
} as const;

/** Texto preenchido com gradiente. */
export const gradientText = (gradient: string = GRADIENTS.text): CSSProperties => ({
  backgroundImage: gradient,
  WebkitBackgroundClip: "text",
  backgroundClip: "text",
  color: "transparent",
  WebkitTextFillColor: "transparent",
});
