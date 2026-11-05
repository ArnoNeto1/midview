import React from "react";
import { IDLE_TILE } from "../../MachineTile";
import type { Pc } from "../../pcs";
import { ScreenContent } from "../../ScreenContent";
import { StudentOverlays } from "../../StudentOverlays";
import type { Point } from "../../geometry";
import { pDialog } from "../geometry";
import { PBtn, PDialog } from "../kit";
import { P } from "../palette";

/* ---------- Ver a tela / Controlar (ViewControlWindow.xaml, fotos 28 e 29) ---------- */

export const VIEW = pDialog(1100, 619);
const BAR = 37;
export const VIEW_CTRL = {
  toggle: { x: 8, y: 6, w: 73, h: 24 },
  toggleOn: { x: 8, y: 6, w: 166, h: 24 },
  disconnect: (controlling: boolean) => ({ x: controlling ? 266 : 173, y: 6, w: 90, h: 24 }),
  /** A tela do aluno, 16:9, centrada na área preta. */
  screen: (() => {
    const h = 619 - BAR;
    const w = Math.round((h * 16) / 9);
    return { x: Math.round((1100 - w) / 2), y: BAR, w, h };
  })(),
};

export type ViewState = {
  appear: number;
  controlling: boolean;
  toggleHover?: number;
  disconnectHover?: number;
  disconnectPress?: number;
  /** Cursor do professor dentro da tela do aluno (coordenadas da tela). */
  remote?: Point | null;
};

/** A janela de ver e controlar: a barra escura de cima e a tela do aluno em preto. */
export const ViewWindow: React.FC<{ s: ViewState; pc: Pc; index: number; frame: number }> = ({ s, pc, index, frame }) => {
  const scr = VIEW_CTRL.screen;
  const k = scr.w / 320;
  const c = s.controlling;
  return (
    <PDialog title={`Midview - ${c ? "controlando" : "vendo"} ${pc.name}`} box={VIEW} appear={s.appear}>
      <div style={{ position: "absolute", left: 0, top: 0, width: VIEW.w, height: BAR, background: P.surface, borderBottom: `1px solid ${P.line}` }} />
      <PBtn box={c ? VIEW_CTRL.toggleOn : VIEW_CTRL.toggle} hover={s.toggleHover}>
        {c ? "Parar de controlar (só ver)" : "Controlar"}
      </PBtn>
      <PBtn box={{ x: c ? 182 : 89, y: 6, w: 77, h: 24 }}>Tela Cheia</PBtn>
      <PBtn box={VIEW_CTRL.disconnect(c)} hover={s.disconnectHover} press={s.disconnectPress}>
        Desconectar
      </PBtn>
      <div
        style={{
          position: "absolute",
          left: c ? 372 : 279,
          top: 6,
          height: 24,
          padding: "0 12px",
          borderRadius: 12,
          display: "flex",
          alignItems: "center",
          fontSize: 11,
          fontWeight: 600,
          whiteSpace: "nowrap",
          background: c ? "#123A40" : P.card,
          border: `1px solid ${c ? "#1C5A5C" : P.line}`,
          color: c ? P.accentText : P.muted,
        }}
      >
        {c ? "Controlando: o teclado e o mouse do aluno estão travados." : "Só vendo: o aluno continua usando o computador normalmente."}
      </div>
      <div style={{ position: "absolute", left: 0, top: BAR, width: VIEW.w, height: VIEW.h - BAR, background: "#000" }} />
      <div style={{ position: "absolute", left: scr.x, top: scr.y, width: scr.w, height: scr.h, overflow: "hidden", background: "#000" }}>
        <div style={{ position: "absolute", left: 0, top: 0, width: 320, height: 200, transformOrigin: "0 0", transform: `scale(${k}) translateY(-10px)` }}>
          <ScreenContent kind={pc.kind} frame={frame} variant={pc.variant} wallpaper={index} />
          <StudentOverlays view={{ ...IDLE_TILE, control: c ? 1 : 0 }} />
        </div>
        {s.remote ? (
          <svg width={24} height={30} viewBox="0 0 30 36" style={{ position: "absolute", left: s.remote.x - 3, top: s.remote.y - 2 }}>
            <path d="M3 2 L3 27.5 L9.6 21.6 L14 31.8 L18.6 29.9 L14.3 19.9 L23 19.9 Z" fill="#111827" stroke="#FFFFFF" strokeWidth={1.6} />
          </svg>
        ) : null}
      </div>
    </PDialog>
  );
};
