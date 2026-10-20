import { ArrowRight } from "lucide-react";
import React from "react";
import { AbsoluteFill, interpolate, useCurrentFrame, useVideoConfig } from "remotion";
import { DashboardWindow } from "../components/dashboard/DashboardWindow";
import { STAGE, WINDOW, WINDOW_HEIGHT } from "../components/dashboard/layout";
import { LIVE_CARD } from "../components/dashboard/PcCard";
import { PCS } from "../components/dashboard/pcs";
import { KineticText } from "../components/KineticText";
import { MidviewLogo } from "../components/MidviewLogo";
import { Wordmark } from "../components/Wordmark";
import { CLAMP, SPRING, clamp01, mix, ramp, springAt } from "../lib/motion";
import { SCENES } from "../timing";

const LIVE_CARDS = PCS.map(() => LIVE_CARD);
const LOGO = { size: 160, x: 960, y: 290 };

/** Marcos da cena, em frames locais. */
const T = {
  recede: 4,
  logo: 18,
  draw: 22,
  burst: 44,
  wordmark: 52,
  tagline: 55,
  cta: 88,
} as const;

/** Cena 5 (25–30 s): o dashboard recua em perspectiva 3D e a logo volta ao centro com o CTA. */
export const Scene5CTA: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const abs = SCENES.cta.from + frame;

  const recede = springAt(frame, fps, T.recede, SPRING.smooth, 64);
  const logoIn = springAt(frame, fps, T.logo, SPRING.soft);
  const draw = springAt(frame, fps, T.draw, SPRING.smooth, 32);
  const glow = interpolate(frame, [T.burst - 14, T.burst + 2, T.burst + 46], [0, 1, 0.55], CLAMP);
  const ring = ramp(frame, T.burst, T.burst + 36);
  const ringRadius = mix(110, 640, ring);
  const wordmark = springAt(frame, fps, T.wordmark, SPRING.snappy);
  const cta = springAt(frame, fps, T.cta, { damping: 14, stiffness: 140, mass: 0.8 });
  const mask = `linear-gradient(180deg, #000 ${mix(100, 42, recede)}%, transparent ${mix(130, 96, recede)}%)`;

  return (
    <AbsoluteFill>
      <AbsoluteFill style={{ maskImage: mask, WebkitMaskImage: mask }}>
        <div
          style={{
            position: "absolute",
            left: (1920 - WINDOW.width) / 2,
            top: STAGE.y,
            width: WINDOW.width,
            height: WINDOW_HEIGHT,
            opacity: mix(1, 0.6, recede),
            transformOrigin: "50% 100%",
            transform: `perspective(1600px) translateY(${mix(0, 330, recede)}px) rotateX(${mix(0, 34, recede)}deg) scale(${mix(0.94, 0.8, recede)})`,
          }}
        >
          <DashboardWindow frame={abs} cards={LIVE_CARDS} onlineCount={12} />
        </div>
      </AbsoluteFill>

      {/* Escurece atrás do texto para manter contraste sobre o dashboard. */}
      <div
        className="absolute"
        style={{
          left: 960 - 760,
          top: 0,
          width: 1520,
          height: 820,
          background: "radial-gradient(50% 50% at 50% 50%, rgba(9,13,22,0.9) 0%, rgba(9,13,22,0.55) 55%, transparent 80%)",
          opacity: recede,
        }}
      />

      <div
        className="absolute rounded-full"
        style={{
          left: LOGO.x - 420,
          top: LOGO.y - 420,
          width: 840,
          height: 840,
          background: "radial-gradient(circle, rgba(34,211,238,0.38) 0%, rgba(99,102,241,0.18) 35%, transparent 68%)",
          opacity: interpolate(ring, [0, 0.15, 1], [0, 0.85, 0.25], CLAMP),
          transform: `scale(${mix(0.4, 1.3, ring)})`,
        }}
      />
      {ring > 0 && ring < 1 ? (
        <div
          className="absolute rounded-full"
          style={{
            left: LOGO.x - ringRadius,
            top: LOGO.y - ringRadius,
            width: ringRadius * 2,
            height: ringRadius * 2,
            border: "1.5px solid rgba(165,243,252,0.85)",
            boxShadow: "0 0 40px rgba(34,211,238,0.35)",
            opacity: (1 - ring) * 0.75,
          }}
        />
      ) : null}

      <div
        style={{
          position: "absolute",
          left: LOGO.x - LOGO.size / 2,
          top: LOGO.y - LOGO.size / 2,
          opacity: clamp01(logoIn * 2),
          transform: `scale(${mix(0.6, 1, logoIn)})`,
        }}
      >
        <MidviewLogo size={LOGO.size} progress={draw} glow={glow} />
      </div>

      <div className="absolute inset-x-0 flex items-baseline justify-center" style={{ top: 432, gap: 24 }}>
        <span
          style={{
            display: "inline-block",
            opacity: clamp01(wordmark * 1.5),
            transform: `translateY(${(1 - wordmark) * 30}px) scale(${mix(0.85, 1, wordmark)})`,
          }}
        >
          <Wordmark size={80} />
        </span>
        <KineticText
          text="— O futuro da gestão de laboratórios."
          frame={frame}
          start={T.tagline}
          stagger={2}
          className="font-display text-[58px] font-bold tracking-[-0.035em] text-white"
        />
      </div>

      <div className="absolute inset-x-0 flex justify-center" style={{ top: 586 }}>
        <CtaButton frame={frame} start={T.cta} progress={cta} />
      </div>
    </AbsoluteFill>
  );
};

/** Botão brilhante: brilho pulsando, reflexo varrendo e anel se expandindo. */
const CtaButton: React.FC<{ frame: number; start: number; progress: number }> = ({ frame, start, progress }) => {
  const t = frame - start;
  const breathe = 0.5 + 0.5 * Math.sin(t * 0.12);
  const shine = ((t - 16) % 54) / 30;
  const ring = t > 8 ? ((t - 8) % 40) / 40 : 0;
  return (
    <div className="relative" style={{ opacity: clamp01(progress * 1.6), transform: `scale(${mix(0.6, 1, progress)})` }}>
      {ring > 0 ? (
        <div
          className="absolute rounded-full"
          style={{ inset: -ring * 34, border: "2px solid rgba(103,232,249,0.7)", opacity: (1 - ring) * 0.6 }}
        />
      ) : null}
      <div
        className="relative flex items-center gap-4 overflow-hidden rounded-full px-12 py-6 text-[32px] font-semibold tracking-[-0.01em] text-white"
        style={{
          background: "linear-gradient(135deg, #06B6D4 0%, #4F46E5 100%)",
          boxShadow: `0 0 ${50 + 40 * breathe}px rgba(34,211,238,${0.35 + 0.25 * breathe}), 0 24px 60px -20px rgba(99,102,241,0.9), inset 0 1px 0 rgba(255,255,255,0.4), inset 0 -2px 0 rgba(0,0,0,0.15)`,
        }}
      >
        {shine >= 0 && shine <= 1 ? (
          <div
            className="absolute inset-y-0"
            style={{
              left: `${mix(-35, 110, shine)}%`,
              width: "28%",
              background: "linear-gradient(100deg, transparent, rgba(255,255,255,0.55), transparent)",
              transform: "skewX(-18deg)",
            }}
          />
        ) : null}
        <span className="relative">Transforme sua sala de aula</span>
        <ArrowRight className="relative" size={32} strokeWidth={2.4} />
      </div>
    </div>
  );
};
