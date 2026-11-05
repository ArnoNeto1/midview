import React from "react";
import { interpolate, spring } from "remotion";
import { ClickRipple, Cursor } from "../components/Cursor";
import { AppWindow } from "../components/app/AppWindow";
import { WIN, type Box, type ButtonId } from "../components/app/geometry";
import type { ButtonVisual } from "../components/app/Toolbar";
import { CLAMP, SPRING, mix, ramp } from "../lib/motion";
import { FPS } from "../timing";
import type { Timeline } from "./engine";
import { worldBottom, worldButtons, worldSelected, worldTiles, worldTitle } from "./world";

/** Quanto o destaque do grupo do trecho está aceso: entra no primeiro trecho do grupo e sai no último. */
const groupSpot = (tl: Timeline, frame: number) => {
  const slot = tl.slotAt(frame);
  const group = slot.beat.group;
  if (!group) return 0;
  let first = slot.index;
  let last = slot.index;
  while (first > 0 && tl.SLOTS[first - 1].beat.group === group) first--;
  while (last < tl.SLOTS.length - 1 && tl.SLOTS[last + 1].beat.group === group) last++;
  const from = tl.SLOTS[first].from;
  const to = tl.SLOTS[last].from + tl.SLOTS[last].beat.dur;
  return ramp(frame, from, from + 12) * (1 - ramp(frame, to - 12, to));
};

const mergeButtons = (a: Partial<Record<ButtonId, ButtonVisual>>, b: Partial<Record<ButtonId, ButtonVisual>> = {}) => {
  const out = { ...a };
  for (const [id, v] of Object.entries(b) as [ButtonId, ButtonVisual][]) out[id] = { ...(out[id] ?? {}), ...v };
  return out;
};

type StageProps = {
  tl: Timeline;
  /** Frame da linha do tempo do roteiro. */
  frame: number;
  /** Área da tela onde a câmera enquadra a janela. */
  viewport: Box;
  /** Frame em que a janela entra (em perspectiva); sem ele, já está no lugar. */
  enterAt?: number;
  maxScale?: number;
};

/** O app no visual Painel em uso: câmera, janela, diálogos, cursor e cliques, num frame do roteiro. */
export const PainelStage: React.FC<StageProps> = ({ tl, frame, viewport, enterAt, maxScale = 1.9 }) => {
  const slot = tl.slotAt(frame);
  const t = frame - slot.from;
  const out = slot.beat.draw(t, frame);
  const ev = tl.EVENTS;
  const selected = out.selected ?? worldSelected(frame, ev);
  const tiles = worldTiles(frame, ev).map((it) => {
    const state = { ...it.state, selected: selected(it.index) };
    return { ...it, state: { ...state, ...(out.tiles ? out.tiles(it.index, state) : {}) } };
  });
  const buttons = mergeButtons(worldButtons(frame, ev), out.buttons);
  const spot = groupSpot(tl, frame) * (out.spot ?? 1);
  const toolbar = { buttons, spotlight: slot.beat.group && spot > 0.01 ? { group: slot.beat.group, amount: spot } : null };
  const bottom = { ...worldBottom(frame, ev), ...out.bottom };
  const title = { ...worldTitle(frame, ev, tiles), ...out.title };

  const rect = tl.cameraAt(frame);
  const scale = Math.min(maxScale, viewport.w / rect.w, viewport.h / rect.h);
  const tx = viewport.x + viewport.w / 2 - (rect.x + rect.w / 2) * scale;
  const ty = viewport.y + viewport.h / 2 - (rect.y + rect.h / 2) * scale;
  const enter = enterAt === undefined ? 1 : spring({ frame: frame - enterAt, fps: FPS, config: SPRING.soft, durationInFrames: 50 });
  const cursor = tl.cursorAt(frame);

  return (
    <div style={{ position: "absolute", left: 0, top: 0, width: WIN.w, height: WIN.h, transformOrigin: "0 0", transform: `translate(${tx}px, ${ty}px) scale(${scale})` }}>
      <div
        style={{
          width: WIN.w,
          height: WIN.h,
          opacity: interpolate(enter, [0, 0.3], [0, 1], CLAMP),
          transformOrigin: "50% 80%",
          transform: `perspective(2400px) translateY(${(1 - enter) * 120}px) rotateX(${(1 - enter) * 24}deg) rotateY(${(1 - enter) * -16}deg) scale(${mix(0.86, 1, enter)})`,
        }}
      >
        <AppWindow frame={frame} tiles={tiles} toolbar={toolbar} bottom={bottom} title={title}>
          {out.overlay}
          {tl.CLICKS.map((c) => {
            if (frame < c || frame > c + 16) return null;
            const p = tl.cursorAt(c);
            return <ClickRipple key={c} x={p.x} y={p.y} progress={ramp(frame, c, c + 16)} />;
          })}
          <Cursor x={cursor.x} y={cursor.y} press={tl.pressAt(frame)} opacity={1} />
        </AppWindow>
      </div>
    </div>
  );
};
