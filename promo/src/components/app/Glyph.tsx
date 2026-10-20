import React from "react";
import { useAppTheme } from "./appTheme";

/** Ícone do Segoe Fluent Icons, a fonte de ícones do app (instalada no Windows). */
export const Glyph: React.FC<{ code: string; size: number; color?: string; style?: React.CSSProperties }> = ({
  code,
  size,
  color,
  style,
}) => {
  const theme = useAppTheme();
  return (
    <span style={{ fontFamily: theme.iconFont, fontSize: size, lineHeight: 1, color, display: "inline-block", ...style }}>
      {code}
    </span>
  );
};

/** Glifos usados fora da barra de ferramentas. */
export const GLYPH = {
  close: "",
  minimize: "",
  maximize: "",
  lock: "",
  smaller: "",
  larger: "",
  chevronRight: "",
  options: "",
  check: "",
  folder: "",
  fullScreen: "",
  disconnect: "",
} as const;
