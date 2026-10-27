import React from "react";
import { useAppTheme } from "../../components/app/appTheme";
import { PCS } from "../../components/app/pcs";
import { ScreenContent } from "../../components/app/ScreenContent";
import { Frame } from "../../components/app/ui";
import { clamp01 } from "../../lib/motion";
import { centered } from "../kit";

/** "Midview - vendo PC-03" / "controlando" (src/Midview.Master/ViewControlWindow.xaml). */
export const VIEW = centered(1040, 660);
const STRIP = 44;
export const VIEW_CTRL = {
  control: { x: 12, y: 32 + 7, w: 200, h: 30 },
  full: { x: 220, y: 32 + 7, w: 100, h: 30 },
  disconnect: { x: 328, y: 32 + 7, w: 110, h: 30 },
};
export const VIEW_SCREEN = { x: 0, y: 32 + STRIP, w: 1040, h: 660 - 32 - STRIP };

export type ViewState = {
  appear: number;
  /** Outra caixa (Tela Cheia ocupa a janela inteira). */
  box?: { x: number; y: number; w: number; h: number };
  pc: number;
  controlling: boolean;
  full?: boolean;
  controlHover?: number;
  controlPress?: number;
  fullHover?: number;
  fullPress?: number;
  disconnectHover?: number;
  disconnectPress?: number;
  /** Aviso vermelho na tela do aluno (aparece também aqui, porque é a tela dele). */
  notice?: number;
  frame: number;
};

const StripButton: React.FC<{ box: { x: number; y: number; w: number; h: number }; hover?: number; press?: number; children: React.ReactNode }> = ({
  box,
  hover = 0,
  press = 0,
  children,
}) => (
  <div
    style={{
      position: "absolute",
      left: box.x,
      top: box.y,
      width: box.w,
      height: box.h,
      borderRadius: 4,
      border: "1px solid rgba(255,255,255,0.25)",
      background: `rgba(255,255,255,${0.08 + 0.12 * clamp01(hover) + 0.12 * clamp01(press)})`,
      color: "#FFFFFF",
      fontSize: 12.5,
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      transform: `scale(${1 - 0.04 * clamp01(press)})`,
    }}
  >
    {children}
  </div>
);

export const ViewWindow: React.FC<{ s: ViewState }> = ({ s }) => {
  const theme = useAppTheme();
  const pc = PCS[s.pc];
  const box = s.box ?? VIEW;
  const screen = { x: 0, y: 32 + STRIP, w: box.w, h: box.h - 32 - STRIP };
  const k = Math.min(screen.h / 180, screen.w / 320);
  const w = 320 * k;
  return (
    <Frame title={`Midview - ${s.controlling ? "controlando" : "vendo"} ${pc.name}`} box={box} appear={s.appear}>
      <div style={{ position: "absolute", left: 0, top: 32, width: box.w, height: STRIP, background: "#1F2A2E", fontFamily: theme.font }}>
        <StripButton box={{ ...VIEW_CTRL.control, y: 7 }} hover={s.controlHover} press={s.controlPress}>
          {s.controlling ? "Parar de controlar (só ver)" : "Controlar"}
        </StripButton>
        <StripButton box={{ ...VIEW_CTRL.full, y: 7 }} hover={s.fullHover} press={s.fullPress}>
          {s.full ? "Restaurar" : "Tela Cheia"}
        </StripButton>
        <StripButton box={{ ...VIEW_CTRL.disconnect, y: 7 }} hover={s.disconnectHover} press={s.disconnectPress}>
          Desconectar
        </StripButton>
        <div style={{ position: "absolute", left: 452, top: 0, height: STRIP, display: "flex", alignItems: "center", color: "#FFFFFF", fontSize: 12.5 }}>
          {s.controlling
            ? "Controlando: o teclado e o mouse do aluno estão travados."
            : "Só vendo: o aluno continua usando o computador normalmente."}
        </div>
      </div>
      <div style={{ position: "absolute", left: screen.x, top: screen.y, width: screen.w, height: screen.h, background: "#000", overflow: "hidden" }}>
        <div style={{ position: "absolute", left: (screen.w - w) / 2, top: 0, width: 320, height: 200, transformOrigin: "0 0", transform: `scale(${k}) translateY(-10px)` }}>
          <ScreenContent kind={pc.kind} frame={s.frame} variant={pc.variant} wallpaper={s.pc} />
          {(s.notice ?? 0) > 0.01 ? (
            <div
              style={{
                position: "absolute",
                left: 70,
                top: 13,
                width: 180,
                padding: "2.5px 0",
                background: "rgba(196,30,30,0.9)",
                color: "#FFFFFF",
                fontFamily: '"Segoe UI", sans-serif',
                fontSize: 6,
                fontWeight: 600,
                textAlign: "center",
                opacity: clamp01((s.notice ?? 0) * 1.5),
              }}
            >
              Este computador está sendo controlado pelo professor
            </div>
          ) : null}
        </div>
      </div>
    </Frame>
  );
};
