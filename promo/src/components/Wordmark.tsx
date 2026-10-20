import React from "react";
import { FONTS } from "../fonts";
import { COLORS } from "../theme";

/** "mid" cinza + "view" ciano, como na logo original. */
export const Wordmark: React.FC<{ size: number; style?: React.CSSProperties }> = ({ size, style }) => (
  <span
    style={{
      fontFamily: FONTS.brand,
      fontWeight: 700,
      fontSize: size,
      lineHeight: 1,
      letterSpacing: "-0.015em",
      whiteSpace: "nowrap",
      ...style,
    }}
  >
    <span style={{ color: COLORS.logoGray }}>mid</span>
    <span style={{ color: COLORS.cyanLight }}>view</span>
  </span>
);
