import React from "react";
import { AbsoluteFill, useCurrentFrame, useVideoConfig } from "remotion";
import { DashboardStage, dashboardCamera } from "../components/dashboard/camera";
import { DashboardWindow } from "../components/dashboard/DashboardWindow";
import { CAPTION, CARD } from "../components/dashboard/layout";
import { LIVE_CARD, type CardState } from "../components/dashboard/PcCard";
import { PCS } from "../components/dashboard/pcs";
import { Eyebrow } from "../components/Eyebrow";
import { KineticText } from "../components/KineticText";
import { SPRING, springAt } from "../lib/motion";
import { COLORS, gradientText } from "../theme";
import { SCENES } from "../timing";

/** Cards da Cena 2: entram em cascata na diagonal e ficam "Online". `frame` = frame local da cena. */
export const monitoringCards = (frame: number, fps: number): CardState[] =>
  PCS.map((_, i) => {
    const col = i % CARD.cols;
    const row = Math.floor(i / CARD.cols);
    const at = 22 + (row * 3 + col) * 3;
    return {
      ...LIVE_CARD,
      appear: springAt(frame, fps, at, SPRING.snappy),
      online: springAt(frame, fps, at + 10, SPRING.smooth, 14),
    };
  });

export const countOnline = (cards: CardState[]) => cards.filter((c) => c.online > 0.5).length;

type MonitoringCaptionProps = {
  /** Frame contado a partir do início da Cena 2. */
  frame: number;
  online: number;
  /** 0→1: saída (usada pela Cena 3). */
  exit?: number;
};

/** Legenda lateral: "Monitoramento em tempo real de todas as máquinas". */
export const MonitoringCaption: React.FC<MonitoringCaptionProps> = ({ frame, online, exit = 0 }) => {
  const { fps } = useVideoConfig();
  if (exit >= 1) return null;
  const eyebrow = springAt(frame, fps, 26, SPRING.soft);
  const counter = springAt(frame, fps, 46, SPRING.soft);
  return (
    <div
      className="absolute flex flex-col justify-center"
      style={{
        left: CAPTION.x,
        width: CAPTION.width,
        top: 0,
        bottom: 0,
        opacity: 1 - exit,
        transform: `translateY(${-exit * 36}px)`,
      }}
    >
      <div style={{ opacity: eyebrow, transform: `translateY(${(1 - eyebrow) * 16}px)` }}>
        <Eyebrow dot={COLORS.greenLight} frame={frame}>
          Ao vivo
        </Eyebrow>
      </div>
      <KineticText
        text="Monitoramento em tempo real de todas as máquinas"
        frame={frame}
        start={32}
        stagger={3}
        className="mt-7 font-display text-[60px] leading-[1.04] font-extrabold tracking-[-0.04em] text-white"
        wordStyle={(word) => (word === "tempo" || word === "real" ? gradientText() : undefined)}
      />
      <div className="mt-10" style={{ opacity: counter, transform: `translateY(${(1 - counter) * 14}px)` }}>
        <OnlineCounter value={online} />
      </div>
    </div>
  );
};

const OnlineCounter: React.FC<{ value: number }> = ({ value }) => (
  <div className="flex items-center gap-5">
    <div className="font-mono text-[46px] leading-none font-semibold tabular-nums text-white">
      {String(value).padStart(2, "0")}
      <span className="text-slate-600">/12</span>
    </div>
    <div>
      <div className="text-[18px] text-slate-300">computadores online</div>
      <div className="mt-2.5 flex gap-1.5">
        {PCS.map((pc, i) => (
          <span
            key={pc.id}
            className="h-1.5 w-4 rounded-full"
            style={{
              background: i < value ? COLORS.greenLight : "rgba(255,255,255,0.1)",
              boxShadow: i < value ? "0 0 8px rgba(74,222,128,0.6)" : undefined,
            }}
          />
        ))}
      </div>
    </div>
  </div>
);

/** Cena 2 (5–12 s): a janela do midview entra em perspectiva e a grade de PCs ganha vida. */
export const Scene2Dashboard: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const abs = SCENES.dashboard.from + frame;
  const cards = monitoringCards(frame, fps);
  const online = countOnline(cards);
  return (
    <AbsoluteFill>
      <MonitoringCaption frame={frame} online={online} />
      <DashboardStage camera={dashboardCamera(abs, fps)}>
        <DashboardWindow frame={abs} cards={cards} onlineCount={online} />
      </DashboardStage>
    </AbsoluteFill>
  );
};
