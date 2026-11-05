import React from "react";
import { clamp01 } from "../../../lib/motion";
import type { BottomBarProps } from "../BottomBar";
import { WIN, type Box } from "../geometry";
import { PAINEL, P_BOTTOM, pSliderKnob } from "./geometry";
import { P, PFONT, PG, PICONS } from "./palette";

/** Botão da faixa (FilterToggleButton): ligado, contorno e texto turquesa. */
const ToggleBox: React.FC<{ box: Box; hover?: number; press?: number; on?: number; children: React.ReactNode }> = ({
  box,
  hover = 0,
  press = 0,
  on = 0,
  children,
}) => {
  const lit = on > 0.5;
  return (
    <div
      style={{
        position: "absolute",
        left: box.x,
        top: box.y - P_BOTTOM.y,
        width: box.w,
        height: box.h,
        borderRadius: 4,
        border: `1px solid ${lit ? P.accent : hover > 0.5 ? P.lineHover : P.line}`,
        background: P.card,
        overflow: "hidden",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        fontSize: 12,
        fontWeight: lit ? 600 : 400,
        color: lit ? P.accentText : P.text,
        whiteSpace: "nowrap",
        transform: `scale(${1 - 0.04 * clamp01(press)})`,
      }}
    >
      <div style={{ position: "absolute", inset: 0, background: P.line, opacity: clamp01(hover) * (1 - clamp01(on)) }} />
      <div style={{ position: "absolute", inset: 0, background: P.accentSoft, opacity: clamp01(on) }} />
      <span style={{ position: "relative" }}>{children}</span>
    </div>
  );
};

/** A faixa de baixo do Painel: estado à esquerda; "Organizar sala", "Visualizar todos", "Só online" e o tamanho das telas. */
export const PainelBottomBar: React.FC<BottomBarProps> = ({
  status,
  onlineOnly,
  slider,
  fitHover,
  fitPress,
  onlineHover,
  sliderGrab,
  fitOn = 0,
  organize = 0,
  organizeHover = 0,
  alignHover = 0,
  alignPress = 0,
}) => {
  const knob = pSliderKnob(slider);
  const s = P_BOTTOM.slider;
  const top = (y: number) => y - P_BOTTOM.y;
  return (
    <div
      style={{
        position: "absolute",
        left: 0,
        top: P_BOTTOM.y,
        width: WIN.w,
        height: PAINEL.bottomBar,
        background: P.surface,
        borderTop: `1px solid ${P.line}`,
        fontFamily: PFONT,
      }}
    >
      <div
        style={{
          position: "absolute",
          left: 14,
          top: 0,
          bottom: 0,
          width: (organize > 0.01 ? P_BOTTOM.align.x : P_BOTTOM.organize.x) - 40,
          display: "flex",
          alignItems: "center",
          fontSize: 13,
          color: P.muted,
          whiteSpace: "nowrap",
          overflow: "hidden",
        }}
      >
        {status}
      </div>
      {organize > 0.01 ? (
        <div style={{ opacity: clamp01(organize * 1.5) }}>
          <ToggleBox box={P_BOTTOM.align} hover={alignHover} press={alignPress}>
            Alinhar
          </ToggleBox>
        </div>
      ) : null}
      <ToggleBox box={P_BOTTOM.organize} hover={organizeHover} on={organize}>
        Organizar sala
      </ToggleBox>
      <ToggleBox box={P_BOTTOM.fitAll} hover={fitHover} press={fitPress} on={fitOn}>
        Visualizar todos
      </ToggleBox>
      <ToggleBox box={P_BOTTOM.onlineOnly} hover={onlineHover} on={onlineOnly}>
        Só online
      </ToggleBox>
      <ToggleBox box={P_BOTTOM.smaller}>
        <span style={{ fontFamily: PICONS, fontSize: 12 }}>{PG.zoomOut}</span>
      </ToggleBox>
      <ToggleBox box={P_BOTTOM.larger}>
        <span style={{ fontFamily: PICONS, fontSize: 12 }}>{PG.zoomIn}</span>
      </ToggleBox>
      <div style={{ position: "absolute", left: s.x + 2, top: top(P_BOTTOM.cy) - 2, width: s.w - 4, height: 4, borderRadius: 2, background: P.line }} />
      <div style={{ position: "absolute", left: s.x + 2, top: top(P_BOTTOM.cy) - 2, width: knob.x - s.x - 2, height: 4, borderRadius: 2, background: P.accent }} />
      <div
        style={{
          position: "absolute",
          left: knob.x - 8,
          top: top(P_BOTTOM.cy) - 8,
          width: 16,
          height: 16,
          borderRadius: 8,
          background: P.text,
          border: `2px solid ${P.accent}`,
          boxSizing: "border-box",
          boxShadow: `0 0 ${4 + 6 * clamp01(sliderGrab)}px rgba(45,212,191,${0.25 + 0.4 * clamp01(sliderGrab)})`,
        }}
      />
    </div>
  );
};
