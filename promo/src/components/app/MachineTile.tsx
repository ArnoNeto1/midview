import React from "react";
import { clamp01, mix } from "../../lib/motion";
import { useAppTheme } from "./appTheme";
import { BADGE_H, screenHeight, type Box } from "./geometry";
import { GLYPH, Glyph } from "./Glyph";
import type { Pc } from "./pcs";
import { ScreenContent } from "./ScreenContent";
import { StudentOverlays, type StudentView } from "./StudentOverlays";

/** Estado de um card. Valores de 0 a 1 (molas podem passar de 1). */
export type TileState = StudentView & {
  appear: number;
  /** 1 = tela do aluno chegando; 0 = só o status ("Conectando...", "Tela indisponível"). */
  online: number;
  status: string;
  selected: number;
  lockBadge: number;
  blockBadge: number;
  /** Programa aberto pelo aluno; 0 = programas fechados. */
  apps: number;
  inbox: number;
};

export const IDLE_TILE: TileState = {
  appear: 1,
  online: 1,
  status: "",
  selected: 0,
  lockBadge: 0,
  blockBadge: 0,
  apps: 1,
  inbox: 0,
  lock: 0,
  control: 0,
  message: 0,
  chat: 0,
  toast: 0,
  toastText: "",
  website: 0,
  blocked: 0,
  countdown: 0,
  countdownClock: "",
  countdownText: "",
};

type MachineTileProps = { pc: Pc; index: number; box: Box; state: TileState; frame: number };

/**
 * Um card da grade como em MainWindow.xaml: a tela do aluno ocupa o card, o nome
 * fica numa faixa clara sobre a borda de baixo e os avisos ficam embaixo do card.
 */
export const MachineTile: React.FC<MachineTileProps> = ({ pc, index, box, state, frame }) => {
  const theme = useAppTheme();
  const screenH = screenHeight(box.w);
  const badge = clamp01(Math.max(state.lockBadge, state.blockBadge));
  const k = (box.w - 2) / 320;
  const online = clamp01(state.online);
  const selected = clamp01(state.selected);
  if (state.appear <= 0.01) return null;
  return (
    <div
      style={{
        position: "absolute",
        left: box.x,
        top: box.y,
        width: box.w,
        height: screenH + 2 + badge * BADGE_H,
        opacity: clamp01(state.appear * 1.5),
        transform: `scale(${mix(0.92, 1, clamp01(state.appear))})`,
      }}
    >
      <div
        style={{
          position: "absolute",
          inset: 0,
          borderRadius: 8,
          background: theme.card.bg,
          border: `1px solid ${theme.card.line}`,
        }}
      />
      {selected > 0.01 ? (
        <div style={{ position: "absolute", inset: 0, borderRadius: 8, background: theme.selected.fill, opacity: selected }} />
      ) : null}
      <div
        style={{
          position: "absolute",
          left: 1,
          top: 1,
          width: box.w - 2,
          height: screenH,
          borderRadius: 7,
          overflow: "hidden",
          background: theme.card.screen,
        }}
      >
        {online > 0.01 ? (
          <div
            style={{
              position: "absolute",
              left: 0,
              top: 0,
              width: 320,
              height: 200,
              opacity: online,
              transformOrigin: "0 0",
              transform: `scale(${k}) translateY(-10px)`,
            }}
          >
            <ScreenContent kind={pc.kind} frame={frame} variant={pc.variant} wallpaper={index} apps={state.apps} inbox={state.inbox} />
            <StudentOverlays view={state} />
          </div>
        ) : null}
        {online < 0.99 ? (
          <div
            style={{
              position: "absolute",
              inset: "10px 10px 22px",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              textAlign: "center",
              fontFamily: theme.font,
              fontSize: 13,
              color: theme.chalk,
              opacity: 1 - online,
            }}
          >
            {state.status}
          </div>
        ) : null}
        <div
          style={{
            position: "absolute",
            left: 0,
            right: 0,
            bottom: 0,
            padding: "2px 6px 3px",
            background: "rgba(17,26,29,0.4)",
            textAlign: "center",
            fontFamily: theme.font,
            fontSize: 11,
            fontWeight: 600,
            color: "#FFFFFF",
            textShadow: "0 0 3px rgba(0,0,0,0.9)",
          }}
        >
          {pc.name}
        </div>
      </div>
      {badge > 0.01 ? (
        <div
          style={{
            position: "absolute",
            left: 0,
            right: 0,
            top: screenH + 4,
            textAlign: "center",
            fontFamily: theme.font,
            fontSize: 11,
            opacity: badge,
          }}
        >
          {state.lockBadge > 0.01 ? (
            <span style={{ color: theme.inkMuted, opacity: clamp01(state.lockBadge) }}>
              <Glyph code={GLYPH.lock} size={10} style={{ marginRight: 4 }} />
              Tela bloqueada
            </span>
          ) : (
            <span style={{ color: theme.alert, opacity: clamp01(state.blockBadge) }}>
              <Glyph code={GLYPH.lock} size={10} style={{ marginRight: 4 }} />3 bloqueados
            </span>
          )}
        </div>
      ) : null}
      {selected > 0.01 ? (
        <div
          style={{
            position: "absolute",
            inset: 0,
            borderRadius: 8,
            border: `3px solid ${theme.selected.line}`,
            opacity: selected,
          }}
        />
      ) : null}
    </div>
  );
};
