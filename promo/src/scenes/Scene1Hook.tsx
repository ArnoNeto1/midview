import { ArrowRight } from "lucide-react";
import React from "react";
import { AbsoluteFill, interpolate, useCurrentFrame, useVideoConfig } from "remotion";
import { KineticText } from "../components/KineticText";
import { LogoParticles } from "../components/LogoParticles";
import { MidviewLogo } from "../components/MidviewLogo";
import { Wordmark } from "../components/Wordmark";
import { CLAMP, SPRING, mix, ramp, springAt } from "../lib/motion";
import { gradientText } from "../theme";

const CENTER = { x: 960, y: 540 };
const LOGO_SIZE = 300;

/** Marcos da cena, em frames locais. */
const T = {
  converge: 6,
  draw: 42,
  burst: 54,
  particlesOut: 56,
  lift: 74,
  line1: 82,
  line2: 102,
  subtitle: 118,
} as const;

/** Cena 1 (0–5 s): a rede de partículas converge e forma a logo; entra o título cinético. */
export const Scene1Hook: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();

  const draw = springAt(frame, fps, T.draw, SPRING.smooth, 30);
  const lift = springAt(frame, fps, T.lift, SPRING.soft, 32);
  const glow = interpolate(frame, [T.burst - 8, T.burst + 4, T.burst + 40], [0, 1, 0.4], CLAMP);
  const burst = ramp(frame, T.burst - 4, T.burst + 34);
  const ring = ramp(frame, T.burst, T.burst + 36);
  const ringRadius = mix(160, 760, ring);
  const subtitle = springAt(frame, fps, T.subtitle, SPRING.soft);

  return (
    <AbsoluteFill>
      <div
        style={{
          position: "absolute",
          left: CENTER.x - 450,
          top: CENTER.y - 450,
          width: 900,
          height: 900,
          borderRadius: "50%",
          background: "radial-gradient(circle, rgba(34,211,238,0.42) 0%, rgba(99,102,241,0.2) 35%, transparent 68%)",
          opacity: interpolate(burst, [0, 0.18, 1], [0, 0.9, 0], CLAMP),
          transform: `scale(${mix(0.35, 1.5, burst)})`,
        }}
      />
      {ring > 0 && ring < 1 ? (
        <div
          style={{
            position: "absolute",
            left: CENTER.x - ringRadius,
            top: CENTER.y - ringRadius,
            width: ringRadius * 2,
            height: ringRadius * 2,
            borderRadius: "50%",
            border: "1.5px solid rgba(165,243,252,0.85)",
            boxShadow: "0 0 40px rgba(34,211,238,0.35), inset 0 0 40px rgba(99,102,241,0.25)",
            opacity: (1 - ring) * 0.8,
          }}
        />
      ) : null}

      <LogoParticles
        frame={frame}
        cx={CENTER.x}
        cy={CENTER.y}
        size={LOGO_SIZE}
        convergeAt={T.converge}
        fadeOutAt={T.particlesOut}
      />

      <div
        style={{
          position: "absolute",
          left: CENTER.x - LOGO_SIZE / 2,
          top: CENTER.y - LOGO_SIZE / 2,
          transform: `translateY(${mix(0, -205, lift)}px) scale(${mix(1, 0.5, lift)})`,
        }}
      >
        <MidviewLogo size={LOGO_SIZE} progress={draw} glow={glow} />
      </div>

      <div className="absolute inset-x-0 text-center" style={{ top: 438 }}>
        <KineticText
          text="Controle total do seu laboratório."
          frame={frame}
          start={T.line1}
          stagger={4}
          className="font-display text-[80px] leading-[1.1] font-extrabold tracking-[-0.04em] text-white"
        />
        <KineticText
          text="Sem complicação."
          frame={frame}
          start={T.line2}
          stagger={5}
          className="font-display text-[80px] leading-[1.1] font-extrabold tracking-[-0.04em]"
          wordStyle={() => gradientText()}
        />
      </div>

      <div
        className="absolute inset-x-0 flex justify-center"
        style={{ top: 690, opacity: subtitle, transform: `translateY(${(1 - subtitle) * 22}px)` }}
      >
        <div className="flex items-center gap-3 rounded-full border border-white/10 bg-white/[0.04] py-3 pr-6 pl-7 text-[28px] font-medium text-slate-300">
          <span>Conheça o</span>
          <Wordmark size={33} />
          <ArrowRight size={26} strokeWidth={2.2} className="text-cyan-300" />
        </div>
      </div>
    </AbsoluteFill>
  );
};
