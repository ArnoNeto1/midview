import React from "react";
import { AbsoluteFill, interpolate, useCurrentFrame, useVideoConfig } from "remotion";
import { AppThemeProvider } from "../components/app/appTheme";
import { WIN } from "../components/app/geometry";
import { Background } from "../components/Background";
import { Eyebrow } from "../components/Eyebrow";
import { KineticText } from "../components/KineticText";
import { MidviewLogo } from "../components/MidviewLogo";
import { Wordmark } from "../components/Wordmark";
import { CLAMP, SPRING, clamp01, mix, ramp, springAt } from "../lib/motion";
import { COLORS } from "../theme";
import { makeTimeline } from "../painel/engine";
import { eyebrowOf } from "../painel/PainelCaption";
import { PainelStage } from "../painel/PainelStage";
import { REELS_BEATS, REELS_START } from "./beats";

export const REELS_W = 1080;
export const REELS_H = 1920;

/**
 * Área segura do Reels: o Instagram cobre uns 250 px no alto (nome, áudio) e
 * uns 350 px embaixo (legenda, botões). Os textos ficam entre as duas.
 */
const SAFE = { top: 250, bottom: REELS_H - 350 };
/** Onde a câmera enquadra a janela do app. */
const VIEWPORT = { x: 40, y: 640, w: 1000, h: 880 };

const TOUR = makeTimeline(REELS_BEATS, { cursor: { x: WIN.w + 80, y: 400 }, cam: REELS_START });
const END = 190;
export const REELS_TOTAL = TOUR.TOTAL + END;

const HEADLINE = "font-display text-[76px] leading-[1.08] font-extrabold tracking-[-0.035em] text-white";

/** Legenda grande: o grupo da barra e a frase do trecho, dentro da área segura. */
const ReelsCaption: React.FC<{ frame: number }> = ({ frame }) => {
  const slot = TOUR.slotAt(frame);
  const beat = slot.beat;
  const t = frame - slot.from;
  const prev = slot.index > 0 ? TOUR.SLOTS[slot.index - 1].beat : null;
  const swap = prev ? ramp(t, 0, 8) : 1;
  const eb = eyebrowOf(beat);
  return (
    <div style={{ position: "absolute", left: 60, right: 60, top: SAFE.top + 30, textAlign: "center" }}>
      <div className="flex justify-center" style={{ opacity: prev ? ramp(t, 0, 8) : 1, transform: `translateY(${prev ? (1 - ramp(t, 0, 8)) * 10 : 0}px)` }}>
        <Eyebrow dot={eb.color} frame={frame} style={{ fontSize: 22, padding: "10px 22px" }}>
          {eb.text}
        </Eyebrow>
      </div>
      <div style={{ position: "relative", marginTop: 26 }}>
        {prev && swap < 1 ? (
          <div className="absolute inset-x-0" style={{ opacity: 1 - swap, transform: `translateY(${-swap * 30}px)` }}>
            <div className={HEADLINE}>{prev.caption}</div>
          </div>
        ) : null}
        {/* No primeiro trecho a frase já está inteira no quadro 0 (o gancho dos 2 primeiros segundos). */}
        <KineticText key={beat.id} text={beat.caption ?? ""} frame={t} start={prev ? 3 : -20} stagger={2} config={SPRING.snappy} className={HEADLINE} />
      </div>
    </div>
  );
};

/** O fim: a logo, o nome, para quem é e o convite para baixar. */
const EndCard: React.FC<{ t: number }> = ({ t }) => {
  const { fps } = useVideoConfig();
  const logo = springAt(t, fps, 4, SPRING.soft);
  const draw = springAt(t, fps, 8, SPRING.smooth, 30);
  const glow = interpolate(t, [24, 36, 80], [0, 1, 0.5], CLAMP);
  const word = springAt(t, fps, 30, SPRING.snappy);
  const line = springAt(t, fps, 44, SPRING.soft);
  const cta = springAt(t, fps, 64, { damping: 14, stiffness: 140, mass: 0.8 });
  const breathe = 0.5 + 0.5 * Math.sin(t * 0.12);
  return (
    <AbsoluteFill>
      <div
        className="absolute rounded-full"
        style={{
          left: REELS_W / 2 - 420,
          top: 620 - 420,
          width: 840,
          height: 840,
          background: "radial-gradient(circle, rgba(45,212,191,0.32) 0%, rgba(99,102,241,0.16) 38%, transparent 68%)",
          opacity: clamp01(logo),
        }}
      />
      <div style={{ position: "absolute", left: REELS_W / 2 - 150, top: 470, opacity: clamp01(logo * 2), transform: `scale(${mix(0.6, 1, logo)})` }}>
        <MidviewLogo size={300} progress={draw} glow={glow} />
      </div>
      <div className="absolute inset-x-0 flex justify-center" style={{ top: 830, opacity: clamp01(word * 1.5), transform: `translateY(${(1 - word) * 30}px)` }}>
        <Wordmark size={150} />
      </div>
      <div className="absolute inset-x-0 text-center" style={{ top: 1030, padding: "0 70px", opacity: clamp01(line * 1.4), transform: `translateY(${(1 - line) * 24}px)` }}>
        <div className="font-display text-[60px] leading-[1.12] font-bold tracking-[-0.03em] text-white">O laboratório inteiro na sua tela.</div>
        <div className="mt-6 text-[36px] font-semibold text-slate-300">Gratuito para escolas.</div>
      </div>
      <div className="absolute inset-x-0 flex justify-center" style={{ top: 1330, opacity: clamp01(cta * 1.6), transform: `scale(${mix(0.6, 1, cta)})` }}>
        <div
          className="rounded-full px-14 py-7 text-[42px] font-bold tracking-[-0.01em]"
          style={{
            color: "#04221D",
            background: "linear-gradient(135deg, #5EEAD4 0%, #2DD4BF 55%, #22D3EE 100%)",
            boxShadow: `0 0 ${40 + 40 * breathe}px rgba(45,212,191,${0.35 + 0.25 * breathe}), inset 0 1px 0 rgba(255,255,255,0.5)`,
          }}
        >
          Baixe pelo link na bio
        </div>
      </div>
    </AbsoluteFill>
  );
};

/** Reels do Midview no visual Painel: 1080×1920, sem narração, com legendas grandes e o convite no fim. */
export const MidviewReels: React.FC = () => {
  const frame = useCurrentFrame();
  const inTour = frame < TOUR.TOTAL;
  const endT = frame - TOUR.TOTAL;
  const out = ramp(frame, TOUR.TOTAL - 14, TOUR.TOTAL + 10);
  return (
    <AppThemeProvider name="painel">
      <AbsoluteFill className="font-sans text-white" style={{ backgroundColor: COLORS.bg }}>
        <Background />
        {out < 1 ? (
          <AbsoluteFill style={{ opacity: 1 - out, transform: `scale(${mix(1, 0.9, out)})`, filter: out > 0.01 ? `blur(${out * 8}px)` : undefined }}>
            <div style={{ position: "absolute", left: 0, top: 0, width: REELS_W, height: REELS_H, overflow: "hidden" }}>
              <PainelStage tl={TOUR} frame={Math.min(frame, TOUR.TOTAL - 1)} viewport={VIEWPORT} maxScale={2.2} />
            </div>
            {/* Escurece em cima e embaixo, onde o Instagram põe os botões e a legenda. */}
            <div
              style={{
                position: "absolute",
                inset: 0,
                background: `linear-gradient(180deg, rgba(9,13,22,0.96) 0px, rgba(9,13,22,0.9) ${VIEWPORT.y - 70}px, rgba(9,13,22,0) ${VIEWPORT.y - 10}px, rgba(9,13,22,0) ${VIEWPORT.y + VIEWPORT.h - 60}px, rgba(9,13,22,0.95) ${SAFE.bottom + 40}px)`,
              }}
            />
            {inTour ? <ReelsCaption frame={frame} /> : null}
          </AbsoluteFill>
        ) : null}
        {endT >= -10 ? <EndCard t={endT + 10} /> : null}
      </AbsoluteFill>
    </AppThemeProvider>
  );
};
