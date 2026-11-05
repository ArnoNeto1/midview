import React from "react";
import { clamp01, mix } from "../../../lib/motion";
import type { Box } from "../geometry";
import type { TileState } from "../MachineTile";
import type { Pc } from "../pcs";
import { ScreenContent } from "../ScreenContent";
import { StudentOverlays } from "../StudentOverlays";
import { P_BADGE_LINE, P_NAME_LINE, pScreenH } from "./geometry";
import { GROUP_COLOR, P, PFONT, PG, PICONS } from "./palette";

/**
 * Uma máquina na grade do Painel (MachineTileButton em Painel.Main.xaml): um
 * cartão em volta da tela, com a linha da máquina embaixo (bolinha de estado,
 * nome e o estado curto à direita) e os avisos depois. Seleção: borda
 * turquesa com um brilho suave.
 */
export const PainelTile: React.FC<{ pc: Pc; index: number; box: Box; state: TileState; frame: number }> = ({ pc, index, box, state, frame }) => {
  if (state.appear <= 0.01) return null;
  const screenW = box.w - 10;
  const screenH = pScreenH(screenW);
  const k = screenW / 320;
  const online = clamp01(state.online);
  const selected = clamp01(state.selected);
  const locked = clamp01(state.lockBadge);
  const block = clamp01(state.blockBadge);
  const dot = locked > 0.5 ? GROUP_COLOR.energia : online > 0.5 ? P.online : P.offline;
  const shortStatus = online < 0.5 && state.status === "Sem resposta" ? state.status : "";
  return (
    <div
      style={{
        position: "absolute",
        left: box.x,
        top: box.y,
        width: box.w,
        height: box.h + block * P_BADGE_LINE,
        opacity: clamp01(state.appear * 1.5),
        transform: `scale(${mix(0.92, 1, clamp01(state.appear))})`,
        fontFamily: PFONT,
      }}
    >
      {selected > 0.01 ? (
        <>
          <div style={{ position: "absolute", inset: -4, borderRadius: 16, border: `2px solid ${P.accent}`, opacity: 0.1 * selected }} />
          <div style={{ position: "absolute", inset: -2, borderRadius: 14, border: `2px solid ${P.accent}`, opacity: 0.22 * selected }} />
        </>
      ) : null}
      <div
        style={{
          position: "absolute",
          inset: 0,
          borderRadius: 12,
          background: selected > 0.5 ? P.tileSelected : P.card,
          border: `1px solid ${selected > 0.5 ? P.accent : P.line}`,
        }}
      />
      <div style={{ position: "absolute", left: 5, top: 5, width: screenW, height: screenH, borderRadius: 8, overflow: "hidden", background: P.bg }}>
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
            <ScreenContent
              kind={pc.kind}
              frame={frame}
              variant={pc.variant}
              wallpaper={index}
              apps={state.apps}
              inbox={state.inbox}
              classFolder={state.classFolder}
            />
            <StudentOverlays view={state} />
          </div>
        ) : null}
        {online < 0.99 ? (
          <div
            style={{
              position: "absolute",
              inset: 10,
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              textAlign: "center",
              fontSize: 13,
              color: P.text,
              opacity: 1 - online,
            }}
          >
            {state.status}
          </div>
        ) : null}
        {locked > 0.01 ? (
          <div
            style={{
              position: "absolute",
              left: 6,
              top: 6,
              height: 18,
              padding: "0 8px",
              borderRadius: 10,
              background: "rgba(52,28,38,0.9)",
              border: "1px solid rgba(255,107,107,0.6)",
              display: "flex",
              alignItems: "center",
              gap: 4,
              fontSize: 10.5,
              fontWeight: 600,
              color: "#FFB4B4",
              opacity: locked,
              transform: `scale(${mix(0.7, 1, locked)})`,
              transformOrigin: "0 50%",
            }}
          >
            <span style={{ fontFamily: PICONS, fontSize: 10 }}>{PG.lock}</span>
            Bloqueada
          </div>
        ) : null}
      </div>
      <div
        style={{
          position: "absolute",
          left: 9,
          right: 9,
          top: 5 + screenH + 5,
          height: P_NAME_LINE - 6,
          display: "flex",
          alignItems: "center",
          whiteSpace: "nowrap",
        }}
      >
        <span style={{ flex: "0 0 auto", width: 8, height: 8, borderRadius: 4, background: dot, marginRight: 6 }} />
        <span style={{ fontSize: 12, fontWeight: 500, color: P.text }}>{pc.name}</span>
        <span style={{ marginLeft: "auto", fontSize: 11, color: P.muted }}>{shortStatus}</span>
      </div>
      {block > 0.01 ? (
        <div
          style={{
            position: "absolute",
            left: 0,
            right: 0,
            top: box.h - 4,
            textAlign: "center",
            fontSize: 11,
            color: P.error,
            opacity: block,
          }}
        >
          <span style={{ fontFamily: PICONS, fontSize: 10, marginRight: 4 }}>{PG.lock}</span>
          {state.blockText ?? "3 bloqueados"}
        </div>
      ) : null}
    </div>
  );
};
