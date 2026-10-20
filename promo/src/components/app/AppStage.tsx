import React from "react";
import { interpolate, spring, useVideoConfig } from "remotion";
import { CLAMP, SPRING, clamp01, mix, ramp } from "../../lib/motion";
import { ClickRipple, Cursor, cursorPosition } from "../Cursor";
import { useAppTheme } from "./appTheme";
import { AppWindow } from "./AppWindow";
import { Dialogs } from "./Dialogs";
import { WIN } from "./geometry";
import {
  BOTTOM_SPOT_BOX,
  CLICKS,
  CURSOR_MOVES,
  CURSOR_START,
  E,
  VIEW_PC,
  bottomSpot,
  bottomState,
  cameraRect,
  cursorOpacity,
  cursorPress,
  dialogsState,
  toolbarState,
  tourTiles,
} from "./tour";

/** Área da tela onde a câmera enquadra a janela (abaixo da legenda). */
const VIEWPORT = { x: 40, y: 196, w: 1840, h: 864 } as const;

/** O app em uso, no frame absoluto `abs`: câmera, janela, diálogos e cursor. */
export const AppStage: React.FC<{ abs: number }> = ({ abs }) => {
  const { fps } = useVideoConfig();
  const theme = useAppTheme();
  const rect = cameraRect(abs);
  const scale = Math.min(1.9, VIEWPORT.w / rect.w, VIEWPORT.h / rect.h);
  const tx = VIEWPORT.x + VIEWPORT.w / 2 - (rect.x + rect.w / 2) * scale;
  const ty = VIEWPORT.y + VIEWPORT.h / 2 - (rect.y + rect.h / 2) * scale;
  const enter = spring({ frame: abs - E.enter, fps, config: SPRING.soft, durationInFrames: 50 });
  const cursor = cursorPosition(abs, fps, CURSOR_START, CURSOR_MOVES);
  const spot = bottomSpot(abs);

  return (
    <div
      style={{
        position: "absolute",
        left: 0,
        top: 0,
        width: WIN.w,
        height: WIN.h,
        transformOrigin: "0 0",
        transform: `translate(${tx}px, ${ty}px) scale(${scale})`,
      }}
    >
      <div
        style={{
          width: WIN.w,
          height: WIN.h,
          opacity: interpolate(enter, [0, 0.3], [0, 1], CLAMP),
          transformOrigin: "50% 80%",
          transform: `perspective(2400px) translateY(${(1 - enter) * 120}px) rotateX(${(1 - enter) * 24}deg) rotateY(${(1 - enter) * -16}deg) scale(${mix(0.86, 1, enter)})`,
        }}
      >
        <AppWindow frame={abs} tiles={tourTiles(abs)} toolbar={toolbarState(abs)} bottom={bottomState(abs)}>
          {spot > 0.01 ? (
            <div
              style={{
                position: "absolute",
                left: BOTTOM_SPOT_BOX.x,
                top: BOTTOM_SPOT_BOX.y,
                width: BOTTOM_SPOT_BOX.w,
                height: BOTTOM_SPOT_BOX.h,
                borderRadius: 8,
                border: `2px solid ${theme.spotlight}`,
                boxShadow: `0 0 22px ${theme.spotlight}88`,
                opacity: clamp01(spot),
              }}
            />
          ) : null}
          <Dialogs s={dialogsState(abs)} viewPc={VIEW_PC} frame={abs} />
          {CLICKS.map((at) => {
            if (abs < at || abs > at + 16) return null;
            const p = cursorPosition(at, fps, CURSOR_START, CURSOR_MOVES);
            return <ClickRipple key={at} x={p.x} y={p.y} progress={ramp(abs, at, at + 16)} />;
          })}
          <Cursor x={cursor.x} y={cursor.y} press={cursorPress(abs)} opacity={cursorOpacity(abs)} />
        </AppWindow>
      </div>
    </div>
  );
};
