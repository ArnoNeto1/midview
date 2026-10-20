import { Cast, Lock, ShieldCheck, type LucideIcon } from "lucide-react";
import React from "react";
import { AbsoluteFill, interpolateColors, useCurrentFrame, useVideoConfig } from "remotion";
import { DashboardStage, dashboardCamera } from "../components/dashboard/camera";
import { COMMAND_BAR_LIFT, CommandBar, type ButtonState } from "../components/dashboard/CommandBar";
import { ClickRipple, Cursor, cursorPosition, type CursorMove } from "../components/dashboard/Cursor";
import { DashboardWindow } from "../components/dashboard/DashboardWindow";
import {
  CAPTION,
  COMMAND_BAR,
  GRID,
  POPOVER,
  WINDOW,
  WINDOW_HEIGHT,
  cardRect,
  type CommandId,
  type Point,
} from "../components/dashboard/layout";
import { LessonScreen } from "../components/dashboard/LessonScreen";
import { LIVE_CARD, type CardState } from "../components/dashboard/PcCard";
import { PCS } from "../components/dashboard/pcs";
import { Eyebrow } from "../components/Eyebrow";
import { tightPunctuation } from "../components/KineticText";
import { EASE, SPRING, clamp01, mix, pulse, ramp, springAt } from "../lib/motion";
import { COLORS } from "../theme";
import { SCENES } from "../timing";
import { MonitoringCaption } from "./Scene2Dashboard";

/** Roteiro da cena (frames locais): bloquear → filtro web → transmitir. */
const T = {
  bar: 2,
  toLock: 8,
  clickLock: 34,
  toFilter: 78,
  clickFilter: 100,
  toCast: 136,
  clickCast: 158,
  wave: 170,
  popoverOut: 206,
  cursorOut: 212,
} as const;

/** Velocidade da onda de transmissão, em px por frame. */
const WAVE_SPEED = 30;

/** Alvo do cursor num botão, já com a barra crescida pelo foco. */
const aim = (id: CommandId, dx: number): Point => {
  const c = COMMAND_BAR.center[id];
  return { x: COMMAND_BAR.cx + (c.x - COMMAND_BAR.cx) * (1 + COMMAND_BAR_LIFT) + dx, y: c.y + 6 };
};

const CURSOR_START: Point = { x: WINDOW.width + 150, y: WINDOW_HEIGHT + 120 };
const AT_LOCK = aim("lock", -30);
const AT_FILTER = aim("filter", -22);
const AT_CAST = aim("cast", -36);
const CURSOR_MOVES: CursorMove[] = [
  { at: T.toLock, to: AT_LOCK, arc: 70 },
  { at: T.toFilter, to: AT_FILTER, arc: -22 },
  { at: T.toCast, to: AT_CAST, arc: -22 },
  { at: T.cursorOut, to: { x: AT_CAST.x + 90, y: AT_CAST.y + 110 } },
];
const CLICKS = [
  { at: T.clickLock, point: AT_LOCK },
  { at: T.clickFilter, point: AT_FILTER },
  { at: T.clickCast, point: AT_CAST },
];

const ACTIONS: { Icon: LucideIcon; text: string; at: number }[] = [
  { Icon: Lock, text: "Bloqueio instantâneo.", at: T.clickLock + 3 },
  { Icon: ShieldCheck, text: "Controle de rede.", at: T.clickFilter + 3 },
  { Icon: Cast, text: "Foco na aula.", at: T.clickCast + 5 },
];

/** Cena 3 (12–20 s): foco na barra de comandos e três micro-interações com o cursor. */
export const Scene3Actions: React.FC = () => {
  const f = useCurrentFrame();
  const { fps } = useVideoConfig();
  const abs = SCENES.actions.from + f;

  const barIn = springAt(f, fps, T.bar, SPRING.soft);
  const lift = springAt(f, fps, T.bar + 10, SPRING.smooth, 30);
  const lockOn = springAt(f, fps, T.clickLock + 2, SPRING.smooth, 12);
  const lockClose = springAt(f, fps, T.clickLock + 8, SPRING.bouncy);
  const filterOn = springAt(f, fps, T.clickFilter + 2, SPRING.smooth, 12);
  const castOn = springAt(f, fps, T.clickCast + 2, SPRING.smooth, 12);

  const cards: CardState[] = PCS.map((_, i) => {
    const r = cardRect(i);
    const reach = T.wave + Math.hypot(r.cx - POPOVER.center.x, r.cy - POPOVER.center.y) / WAVE_SPEED;
    return {
      ...LIVE_CARD,
      // Todas as telas bloqueiam ao mesmo tempo.
      lock: lockOn,
      close: lockClose,
      shield: springAt(f, fps, T.clickFilter + 4 + i * 1.6, SPRING.bouncy),
      scan: ramp(f, T.clickFilter + 2 + i * 1.6, T.clickFilter + 22 + i * 1.6, EASE.linear),
      // A transmissão chega a cada PC quando a onda passa por ele.
      cast: springAt(f, fps, reach, SPRING.snappy),
    };
  });

  const hover = (from: number, to: number) => ramp(f, from, from + 6) * (1 - ramp(f, to, to + 6));
  const states: Record<CommandId, ButtonState> = {
    lock: { active: lockOn * (1 - castOn), press: pulse(f, T.clickLock, 3, 8), hover: hover(T.toLock + 18, T.toFilter + 4) },
    filter: { active: filterOn, press: pulse(f, T.clickFilter, 3, 8), hover: hover(T.toFilter + 14, T.toCast + 4) },
    cast: { active: castOn, press: pulse(f, T.clickCast, 3, 8), hover: hover(T.toCast + 14, T.cursorOut + 4) },
  };

  const cursor = cursorPosition(f, fps, CURSOR_START, CURSOR_MOVES);
  const cursorOpacity = ramp(f, T.toLock, T.toLock + 6) * (1 - ramp(f, T.cursorOut + 4, T.cursorOut + 18));
  const press = Math.max(...CLICKS.map((c) => pulse(f, c.at, 3, 7)));

  return (
    <AbsoluteFill>
      <MonitoringCaption frame={abs - SCENES.dashboard.from} online={12} exit={ramp(f, 0, 16)} />
      <ActionsCaption frame={f} />
      <DashboardStage camera={dashboardCamera(abs, fps)}>
        <DashboardWindow
          frame={abs}
          cards={cards}
          onlineCount={12}
          footer={1 - clamp01(barIn)}
          dim={lift}
          castOrigin={POPOVER.center}
        >
          <Wave frame={f} />
          <CommandBar appear={barIn} lift={lift} states={states} />
          <ScreenPopover frame={f} abs={abs} />
          {CLICKS.map((c) => (
            <ClickRipple key={c.at} x={c.point.x} y={c.point.y} progress={ramp(f, c.at, c.at + 16, EASE.out)} />
          ))}
          <Cursor x={cursor.x} y={cursor.y} press={press} opacity={cursorOpacity} />
        </DashboardWindow>
      </DashboardStage>
    </AbsoluteFill>
  );
};

/** Texto de apoio: cada frase acende junto com o clique correspondente. */
const ActionsCaption: React.FC<{ frame: number }> = ({ frame }) => {
  const { fps } = useVideoConfig();
  const eyebrow = springAt(frame, fps, 12, SPRING.soft);
  const allLit = ramp(frame, T.wave + 38, T.wave + 52);
  return (
    <div className="absolute flex flex-col justify-center" style={{ left: CAPTION.x, width: CAPTION.width, top: 0, bottom: 0 }}>
      <div style={{ opacity: eyebrow, transform: `translateY(${(1 - eyebrow) * 16}px)` }}>
        <Eyebrow>Ações rápidas</Eyebrow>
      </div>
      <div className="mt-9 flex flex-col gap-6">
        {ACTIONS.map(({ Icon, text, at }, k) => {
          const appear = springAt(frame, fps, at, SPRING.soft);
          const next = k + 1 < ACTIONS.length ? ACTIONS[k + 1].at : null;
          const current = ramp(frame, at, at + 8) * (next === null ? 1 : 1 - ramp(frame, next, next + 8));
          const active = Math.max(current, allLit);
          return (
            <div
              key={text}
              className="flex items-center gap-5"
              style={{ opacity: clamp01(appear * 1.4), transform: `translateX(${(1 - appear) * -40}px)` }}
            >
              <div
                className="flex h-[54px] w-[54px] shrink-0 items-center justify-center rounded-2xl"
                style={{
                  background: `linear-gradient(140deg, rgba(34,211,238,${0.08 + 0.22 * active}), rgba(99,102,241,${0.06 + 0.2 * active}))`,
                  boxShadow: `inset 0 0 0 1px rgba(103,232,249,${0.12 + 0.45 * active}), 0 0 ${30 * active}px -6px rgba(34,211,238,0.8)`,
                }}
              >
                <Icon size={25} strokeWidth={2.1} color={interpolateColors(active, [0, 1], [COLORS.slate500, COLORS.cyanPale])} />
              </div>
              <span
                className="font-display text-[38px] font-bold tracking-[-0.03em] whitespace-nowrap"
                style={{ color: interpolateColors(active, [0, 1], [COLORS.slate600, COLORS.white]) }}
              >
                {tightPunctuation(text)}
              </span>
            </div>
          );
        })}
      </div>
    </div>
  );
};

/** Anéis que partem da tela do professor e varrem a grade. */
const Wave: React.FC<{ frame: number }> = ({ frame }) => {
  const box = { left: GRID.left - 12, top: GRID.top - 12, width: GRID.width + 24, height: GRID.height + 24 };
  const cx = POPOVER.center.x - box.left;
  const cy = POPOVER.center.y - box.top;
  return (
    <div className="absolute overflow-hidden rounded-2xl" style={box}>
      {[0, 9].map((delay, i) => {
        const r = (frame - T.wave - delay) * WAVE_SPEED;
        const alpha = clamp01(1 - r / 900) * (i === 0 ? 0.95 : 0.6);
        if (r <= 0 || alpha <= 0) return null;
        return (
          <div
            key={delay}
            style={{
              position: "absolute",
              left: cx - r,
              top: cy - r,
              width: r * 2,
              height: r * 2,
              borderRadius: "50%",
              border: `2px solid rgba(165,180,252,${alpha})`,
              boxShadow: `0 0 50px 8px rgba(99,102,241,${alpha * 0.45}), inset 0 0 50px 8px rgba(34,211,238,${alpha * 0.3})`,
            }}
          />
        );
      })}
    </div>
  );
};

/** Prévia "Sua tela": a tela principal que será sincronizada com os PCs. */
const ScreenPopover: React.FC<{ frame: number; abs: number }> = ({ frame, abs }) => {
  const { fps } = useVideoConfig();
  const popIn = springAt(frame, fps, T.clickCast + 3, SPRING.snappy);
  const popOut = springAt(frame, fps, T.popoverOut, SPRING.smooth, 14);
  const visible = clamp01(popIn) * (1 - popOut);
  if (visible <= 0.01) return null;
  const inner = POPOVER.w - 16;
  return (
    <div
      className="absolute overflow-hidden rounded-2xl border border-white/15 p-2"
      style={{
        left: POPOVER.left,
        top: POPOVER.top,
        width: POPOVER.w,
        height: POPOVER.h,
        background: "rgba(14,20,36,0.96)",
        boxShadow: "0 30px 60px -20px rgba(0,0,0,0.9), 0 0 40px -10px rgba(99,102,241,0.6)",
        opacity: visible,
        transformOrigin: "50% 100%",
        transform: `translateY(${(1 - popIn) * 18 + popOut * 30}px) scale(${mix(0.88, 1, popIn) * mix(1, 0.6, popOut)})`,
      }}
    >
      <div className="flex items-center justify-between px-1 pb-2 text-[12.5px]">
        <span className="font-semibold text-white">Sua tela</span>
        <span className="flex items-center gap-1.5 font-semibold text-indigo-300">
          <Cast size={13} strokeWidth={2.4} />
          Para 12 PCs
        </span>
      </div>
      <div className="relative overflow-hidden rounded-lg" style={{ width: inner, height: (inner * 200) / 320 }}>
        <div style={{ width: 320, height: 200, transform: `scale(${inner / 320})`, transformOrigin: "0 0" }}>
          <LessonScreen frame={abs} />
        </div>
      </div>
    </div>
  );
};
