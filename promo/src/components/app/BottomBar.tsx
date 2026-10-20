import React from "react";
import { clamp01, mix } from "../../lib/motion";
import { useAppTheme } from "./appTheme";
import { BOTTOM, WIN, sliderKnob, type Box } from "./geometry";
import { GLYPH, Glyph } from "./Glyph";

export type BottomBarProps = {
  status: string;
  /** "Só online" ligado (0→1). */
  onlineOnly: number;
  /** Posição do controle de tamanho (0 = 160 px, 1 = 640 px). */
  slider: number;
  fitHover: number;
  fitPress: number;
  onlineHover: number;
  sliderGrab: number;
};

const ButtonBox: React.FC<{ box: Box; hover?: number; press?: number; on?: number; children: React.ReactNode }> = ({
  box,
  hover = 0,
  press = 0,
  on = 0,
  children,
}) => {
  const theme = useAppTheme();
  const b = theme.button;
  return (
    <div
      style={{
        position: "absolute",
        left: box.x,
        top: box.y - BOTTOM.y,
        width: box.w,
        height: box.h,
        borderRadius: 4,
        border: `1px solid ${on > 0.5 ? theme.toggleOn.line : b.line}`,
        background: b.bg,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        fontFamily: theme.font,
        fontSize: 12,
        fontWeight: on > 0.5 ? 600 : 400,
        color: b.text,
        overflow: "hidden",
      }}
    >
      <div style={{ position: "absolute", inset: 0, background: b.hover, opacity: clamp01(hover) }} />
      <div style={{ position: "absolute", inset: 0, background: theme.toggleOn.bg, opacity: clamp01(on) }} />
      <div style={{ position: "absolute", inset: 0, background: b.pressed, opacity: clamp01(press) }} />
      <span style={{ position: "relative" }}>{children}</span>
    </div>
  );
};

/** A barra de baixo: status à esquerda; "Visualizar todos", "Só online" e o tamanho das telas à direita. */
export const BottomBar: React.FC<BottomBarProps> = ({ status, onlineOnly, slider, fitHover, fitPress, onlineHover, sliderGrab }) => {
  const theme = useAppTheme();
  const knob = sliderKnob(slider);
  const s = BOTTOM.slider;
  return (
    <div
      style={{
        position: "absolute",
        left: 0,
        top: BOTTOM.y,
        width: WIN.w,
        height: WIN.bottomBar,
        background: theme.bar.bg,
        borderTop: `1px solid ${theme.bar.line}`,
      }}
    >
      <div
        style={{
          position: "absolute",
          left: 14,
          top: 0,
          bottom: 0,
          width: BOTTOM.fitAll.x - 40,
          display: "flex",
          alignItems: "center",
          fontFamily: theme.font,
          fontSize: 13,
          color: theme.inkMuted,
          whiteSpace: "nowrap",
          overflow: "hidden",
        }}
      >
        {status}
      </div>
      <ButtonBox box={BOTTOM.fitAll} hover={fitHover} press={fitPress}>
        Visualizar todos
      </ButtonBox>
      <ButtonBox box={BOTTOM.onlineOnly} hover={onlineHover} on={onlineOnly}>
        Só online
      </ButtonBox>
      <ButtonBox box={BOTTOM.smaller}>
        <Glyph code={GLYPH.smaller} size={12} />
      </ButtonBox>
      <ButtonBox box={BOTTOM.larger}>
        <Glyph code={GLYPH.larger} size={12} />
      </ButtonBox>
      <div
        style={{
          position: "absolute",
          left: s.x,
          top: BOTTOM.cy - BOTTOM.y - 2,
          width: s.w,
          height: 4,
          borderRadius: 2,
          background: theme.slider.track,
        }}
      />
      <div
        style={{
          position: "absolute",
          left: knob.x - 5,
          top: BOTTOM.cy - BOTTOM.y - 9,
          width: 10,
          height: 18,
          borderRadius: 3,
          background: theme.slider.thumb,
          border: `1px solid ${theme.slider.thumbLine}`,
          boxShadow: `0 1px 3px rgba(0,0,0,${mix(0.15, 0.4, clamp01(sliderGrab))})`,
        }}
      />
    </div>
  );
};
