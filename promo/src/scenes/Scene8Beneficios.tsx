import { GraduationCap, Lock, Monitor, Network, SquareTerminal, Zap, type LucideIcon } from "lucide-react";
import React from "react";
import { AbsoluteFill, interpolateColors, spring, useCurrentFrame, useVideoConfig } from "remotion";
import { ClickRipple, Cursor } from "../components/Cursor";
import { Eyebrow } from "../components/Eyebrow";
import { FONTS } from "../fonts";
import { EASE, SPRING, clamp01, mix, pulse, ramp, springAt } from "../lib/motion";
import { COLORS } from "../theme";

type VizProps = { frame: number; start: number };

const CARD = { w: 500, h: 560, gap: 36 } as const;
const VIZ_W = 420;

/** Leve e Rápido: uso da rede local bem abaixo da capacidade. */
const NetworkUsageViz: React.FC<VizProps> = ({ frame, start }) => {
  const draw = ramp(frame, start, start + 36, EASE.inOut);
  const h = 118;
  const points = Array.from({ length: 29 }, (_, i) => ({
    x: (i / 28) * VIZ_W,
    y: h - 24 - 9 * Math.sin(i * 0.75 + frame * 0.09) - 6 * Math.sin(i * 0.31 + 1.7),
  }));
  const line = points.map((p, i) => `${i ? "L" : "M"}${p.x.toFixed(1)} ${p.y.toFixed(1)}`).join(" ");
  const head = points[Math.min(points.length - 1, Math.round(draw * (points.length - 1)))];
  return (
    <div>
      <div className="mb-3 flex items-center justify-between">
        <span className="text-[17px] text-slate-400">Uso da rede local</span>
        <span className="rounded-full border border-emerald-400/30 bg-emerald-400/10 px-3 py-1 font-mono text-[13px] font-semibold text-emerald-300">
          baixo
        </span>
      </div>
      <svg width={VIZ_W} height={h} viewBox={`0 0 ${VIZ_W} ${h}`} style={{ overflow: "visible" }}>
        <defs>
          <linearGradient id="usage-fill" x1="0" y1="0" x2="0" y2="1">
            <stop offset="0" stopColor="#22D3EE" stopOpacity="0.35" />
            <stop offset="1" stopColor="#22D3EE" stopOpacity="0" />
          </linearGradient>
          <clipPath id="usage-clip">
            <rect x={-4} y={-10} width={VIZ_W * draw + 4} height={h + 20} />
          </clipPath>
        </defs>
        <line x1={0} y1={16} x2={VIZ_W} y2={16} stroke="rgba(148,163,184,0.35)" strokeDasharray="4 6" />
        <text x={VIZ_W} y={8} textAnchor="end" fill={COLORS.slate500} fontSize={13} fontFamily={FONTS.mono}>
          capacidade
        </text>
        <g clipPath="url(#usage-clip)">
          <path d={`${line} L${VIZ_W} ${h} L0 ${h} Z`} fill="url(#usage-fill)" />
          <path d={line} fill="none" stroke={COLORS.cyanPale} strokeWidth={2.5} strokeLinejoin="round" />
        </g>
        {draw > 0.01 ? (
          <>
            <circle cx={head.x} cy={head.y} r={9} fill="rgba(34,211,238,0.25)" />
            <circle cx={head.x} cy={head.y} r={4} fill={COLORS.white} />
          </>
        ) : null}
      </svg>
    </div>
  );
};

/** Gestão Centralizada: um clique bloqueia os 12 PCs. */
const OneClickViz: React.FC<VizProps> = ({ frame, start }) => {
  const { fps } = useVideoConfig();
  const click = start + 22;
  const move = spring({ frame: frame - start, fps, config: { damping: 18, stiffness: 120, mass: 0.85 } });
  const press = pulse(frame, click, 3, 8);
  const sweep = ramp(frame, click + 4, click + 30, EASE.linear);
  const cursor = { x: mix(400, 150, move), y: mix(160, 36, move) };
  return (
    <div className="relative" style={{ width: VIZ_W, height: 170 }}>
      <div className="flex items-center gap-4">
        <div
          className="flex h-[54px] items-center gap-3 rounded-2xl px-5 text-[19px] font-semibold text-white"
          style={{
            background: "linear-gradient(180deg, rgba(99,102,241,0.38), rgba(99,102,241,0.18))",
            boxShadow: `inset 0 0 0 1px rgba(165,180,252,0.45), 0 0 ${20 + press * 30}px -6px rgba(99,102,241,0.9)`,
            transform: `scale(${1 - 0.05 * press})`,
          }}
        >
          <Lock size={19} strokeWidth={2.2} />
          Bloquear todas
        </div>
        <span className="font-mono text-[15px] text-slate-500">1 clique</span>
      </div>
      <div className="mt-6 grid grid-cols-6 gap-2.5">
        {Array.from({ length: 12 }, (_, i) => {
          const on = clamp01(sweep * 13 - i);
          return (
            <div
              key={i}
              className="flex h-[36px] items-center justify-center rounded-lg"
              style={{
                background: `rgba(99,102,241,${0.08 + 0.24 * on})`,
                boxShadow: `inset 0 0 0 1px rgba(165,180,252,${0.12 + 0.5 * on}), 0 0 ${16 * on}px -4px rgba(129,140,248,0.9)`,
                color: interpolateColors(on, [0, 1], [COLORS.slate600, "#C7D2FE"]),
              }}
            >
              {on > 0.5 ? <Lock size={15} strokeWidth={2.2} /> : <Monitor size={15} strokeWidth={2} />}
            </div>
          );
        })}
      </div>
      <ClickRipple x={150} y={36} progress={ramp(frame, click, click + 16)} />
      <Cursor x={cursor.x} y={cursor.y} press={press} opacity={ramp(frame, start, start + 6)} />
    </div>
  );
};

const STUDENTS = Array.from({ length: 6 }, (_, k) => ({ x: 35 + k * 70, y: 140 }));
const TEACHER = { x: VIZ_W / 2, y: 30 };

const bezier = (p0: number, p1: number, p2: number, p3: number, t: number) => {
  const u = 1 - t;
  return u * u * u * p0 + 3 * u * u * t * p1 + 3 * u * t * t * p2 + t * t * t * p3;
};

/** Feito para Educação: a aula flui do professor para cada aluno. */
const ClassroomViz: React.FC<VizProps> = ({ frame, start }) => (
  <div className="relative" style={{ width: VIZ_W, height: 170 }}>
    <svg width={VIZ_W} height={170} style={{ position: "absolute", inset: 0, overflow: "visible" }}>
      <defs>
        <linearGradient id="class-link" gradientUnits="userSpaceOnUse" x1="0" y1={TEACHER.y} x2="0" y2={STUDENTS[0].y}>
          <stop offset="0" stopColor="#67E8F9" stopOpacity="0.95" />
          <stop offset="1" stopColor="#818CF8" stopOpacity="0.5" />
        </linearGradient>
      </defs>
      {STUDENTS.map((s, k) => {
        const draw = ramp(frame, start + k * 3, start + 18 + k * 3, EASE.inOut);
        const d = `M${TEACHER.x} ${TEACHER.y + 26} C ${TEACHER.x} ${TEACHER.y + 80}, ${s.x} ${s.y - 80}, ${s.x} ${s.y - 20}`;
        return (
          <path
            key={k}
            d={d}
            fill="none"
            stroke="url(#class-link)"
            strokeWidth={2}
            pathLength={1}
            strokeDasharray="1 1"
            strokeDashoffset={1 - draw}
          />
        );
      })}
      {STUDENTS.map((s, k) => {
        const begin = start + 24 + k * 5;
        if (frame < begin) return null;
        const t = ((frame - begin) % 34) / 34;
        const x = bezier(TEACHER.x, TEACHER.x, s.x, s.x, t);
        const y = bezier(TEACHER.y + 26, TEACHER.y + 80, s.y - 80, s.y - 20, t);
        return <circle key={k} cx={x} cy={y} r={3.4} fill="#A5F3FC" opacity={Math.sin(Math.PI * t)} />;
      })}
    </svg>
    <Node x={TEACHER.x} y={TEACHER.y} size={56} Icon={GraduationCap} active appear={ramp(frame, start - 4, start + 8)} />
    {STUDENTS.map((s, k) => (
      <Node key={k} x={s.x} y={s.y} size={40} Icon={Monitor} appear={ramp(frame, start + 10 + k * 3, start + 22 + k * 3)} />
    ))}
  </div>
);

const Node: React.FC<{ x: number; y: number; size: number; Icon: LucideIcon; active?: boolean; appear: number }> = ({
  x,
  y,
  size,
  Icon,
  active = false,
  appear,
}) => (
  <div
    className="absolute flex items-center justify-center rounded-full"
    style={{
      left: x - size / 2,
      top: y - size / 2,
      width: size,
      height: size,
      background: active ? "linear-gradient(135deg, #22D3EE, #6366F1)" : "#111A2E",
      boxShadow: active ? "0 0 34px -4px rgba(34,211,238,0.85)" : "inset 0 0 0 1px rgba(148,163,184,0.3)",
      color: active ? COLORS.white : COLORS.slate400,
      opacity: appear,
      transform: `scale(${mix(0.6, 1, appear)})`,
    }}
  >
    <Icon size={size * 0.46} strokeWidth={2} />
  </div>
);

type Benefit = {
  title: string;
  description: string;
  Icon: LucideIcon;
  iconColor: string;
  glow: string;
  Viz: React.FC<VizProps>;
};

const BENEFITS: Benefit[] = [
  { title: "Leve e Rápido", description: "Baixo consumo na rede local.", Icon: Zap, iconColor: COLORS.cyanPale, glow: "#22D3EE", Viz: NetworkUsageViz },
  { title: "Gestão Centralizada", description: "Controle em 1 clique.", Icon: SquareTerminal, iconColor: COLORS.indigoPale, glow: "#6366F1", Viz: OneClickViz },
  { title: "Feito para Educação", description: "Focado na dinâmica de sala de aula.", Icon: Network, iconColor: "#7DD3FC", glow: "#38BDF8", Viz: ClassroomViz },
];

const BenefitCard: React.FC<Benefit & { index: number; frame: number }> = ({ title, description, Icon, iconColor, glow, Viz, index, frame }) => {
  const { fps } = useVideoConfig();
  const at = 8 + index * 6;
  const enter = springAt(frame, fps, at, { damping: 16, stiffness: 110, mass: 0.9 });
  const icon = springAt(frame, fps, at + 10, SPRING.bouncy);
  const angle = frame * 2.2 + index * 120;
  return (
    <div
      style={{
        width: CARD.w,
        height: CARD.h,
        opacity: clamp01(enter * 1.4),
        transformOrigin: "50% 100%",
        transform: `perspective(1800px) translateY(${(1 - enter) * 170}px) rotateX(${(1 - enter) * 26}deg) scale(${mix(0.9, 1, enter)})`,
      }}
    >
      {/* Borda com brilho girando (conic-gradient). */}
      <div
        style={{
          height: "100%",
          padding: 1.5,
          borderRadius: 30,
          background: `conic-gradient(from ${angle}deg at 50% 50%, rgba(255,255,255,0.07) 0deg, ${glow} 40deg, rgba(255,255,255,0.07) 95deg, rgba(255,255,255,0.07) 190deg, rgba(129,140,248,0.75) 235deg, rgba(255,255,255,0.07) 290deg, rgba(255,255,255,0.07) 360deg)`,
          boxShadow: `0 40px 90px -40px rgba(0,0,0,0.9), 0 0 60px -30px ${glow}`,
        }}
      >
        <div
          className="relative h-full overflow-hidden"
          style={{ borderRadius: 28.5, padding: 40, background: "linear-gradient(180deg, #121A2C 0%, #0B1120 100%)" }}
        >
          <div
            className="absolute rounded-full"
            style={{ top: -130, right: -130, width: 330, height: 330, background: `radial-gradient(circle, ${glow}38, transparent 70%)` }}
          />
          <div
            className="relative flex h-[78px] w-[78px] items-center justify-center rounded-[22px]"
            style={{
              background: `linear-gradient(140deg, ${glow}3D, rgba(99,102,241,0.16))`,
              boxShadow: `inset 0 0 0 1px ${glow}70, 0 0 46px -8px ${glow}B0`,
              transform: `scale(${mix(0.5, 1, icon)})`,
              opacity: clamp01(icon * 1.5),
            }}
          >
            <Icon size={36} strokeWidth={1.9} color={iconColor} />
          </div>
          <h3 className="relative mt-9 font-display text-[42px] leading-tight font-bold tracking-[-0.035em] text-white">{title}</h3>
          <p className="relative mt-3 text-[23px] leading-snug text-slate-400">{description}</p>
          <div className="absolute inset-x-10 bottom-10" style={{ height: 170 }}>
            <Viz frame={frame} start={at + 14} />
          </div>
        </div>
      </div>
    </div>
  );
};

/** Cena 8 (74–82 s): três benefícios entram em cascata. */
export const Scene8Beneficios: React.FC = () => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const eyebrow = springAt(frame, fps, 4, SPRING.soft);
  return (
    <AbsoluteFill className="items-center justify-center">
      <div
        className="absolute inset-x-0 flex justify-center"
        style={{ top: 158, opacity: eyebrow, transform: `translateY(${(1 - eyebrow) * 16}px)` }}
      >
        <Eyebrow>Por que midview</Eyebrow>
      </div>
      <div className="flex" style={{ gap: CARD.gap, marginTop: 64 }}>
        {BENEFITS.map((benefit, index) => (
          <BenefitCard key={benefit.title} {...benefit} index={index} frame={frame} />
        ))}
      </div>
    </AbsoluteFill>
  );
};
