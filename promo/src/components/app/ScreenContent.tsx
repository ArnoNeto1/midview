import React from "react";
import { random } from "remotion";
import { clamp01 } from "../../lib/motion";

/** Telas simuladas das miniaturas, desenhadas num canvas virtual de 320×200 (16:10). */
export const SCREEN = { w: 320, h: 200 } as const;

export type ScreenKind = "code" | "browser" | "slides" | "sheet" | "blocks" | "doc" | "chart" | "terminal" | "paint";

type ScreenProps = { frame: number; variant: number };

type BoxProps = { x: number; y: number; w: number; h: number; c: string; r?: number; o?: number; style?: React.CSSProperties };

/** Retângulo posicionado: o "pixel" das telas simuladas. */
export const Box: React.FC<BoxProps> = ({ x, y, w, h, c, r = 2, o = 1, style }) => (
  <div
    style={{
      position: "absolute",
      left: x,
      top: y,
      width: Math.max(0, w),
      height: h,
      background: c,
      borderRadius: r,
      opacity: o,
      ...style,
    }}
  />
);

const WALLPAPERS = [
  "linear-gradient(140deg, #1E1B4B 0%, #0F172A 55%, #083344 100%)",
  "linear-gradient(140deg, #082F49 0%, #0F172A 50%, #312E81 100%)",
  "linear-gradient(140deg, #164E63 0%, #0B1222 60%, #1E1B4B 100%)",
];

/** Janela de app dentro da tela do aluno (área útil 304×171). */
const Win: React.FC<{ bg: string; bar: string; light?: boolean; children: React.ReactNode }> = ({
  bg,
  bar,
  light = false,
  children,
}) => {
  const ink = light ? "rgba(15,23,42,0.35)" : "rgba(255,255,255,0.35)";
  return (
    <div
      style={{
        position: "absolute",
        left: 8,
        top: 8,
        width: 304,
        height: 184,
        borderRadius: 7,
        overflow: "hidden",
        background: bg,
        boxShadow: "0 8px 24px rgba(0,0,0,0.5)",
      }}
    >
      <div style={{ position: "absolute", left: 0, top: 0, right: 0, height: 13, background: bar }}>
        <Box x={7} y={4.5} w={34} h={4} c={ink} />
        {[0, 1, 2].map((i) => (
          <Box key={i} x={290 - i * 11} y={4.5} w={6} h={4} r={1} c={ink} />
        ))}
      </div>
      <div style={{ position: "absolute", left: 0, top: 13, right: 0, bottom: 0, overflow: "hidden" }}>{children}</div>
    </div>
  );
};

const blink = (frame: number) => Math.floor(frame / 15) % 2 === 0;

const CodeScreen: React.FC<ScreenProps> = ({ frame, variant }) => {
  const accent = variant ? "#F472B6" : "#38BDF8";
  const palette = ["#C084FC", accent, "#FBBF24", "#34D399", "#94A3B8"];
  const indents = [0, 8, 8, 16, 16, 8, 0, 8, 16, 8];
  const typing = ((frame + variant * 37) % 150) / 150;
  return (
    <Win bg="#0B1222" bar="#0A101D">
      <Box x={0} y={0} w={14} h={171} c="#080D18" r={0} />
      {[0, 1, 2, 3].map((i) => (
        <Box key={i} x={4} y={8 + i * 14} w={6} h={6} r={1.5} c={i === 0 ? accent : "#334155"} />
      ))}
      <Box x={14} y={0} w={62} h={171} c="#0D1526" r={0} />
      <Box x={16} y={42} w={58} h={11} c="rgba(56,189,248,0.14)" />
      {Array.from({ length: 10 }, (_, i) => (
        <Box
          key={i}
          x={i % 3 === 0 ? 20 : 26}
          y={9 + i * 12}
          w={16 + random(`tree-${variant}-${i}`) * 28}
          h={4}
          c={i === 3 ? "#E2E8F0" : "#475569"}
        />
      ))}
      <Box x={76} y={0} w={228} h={13} c="#080D18" r={0} />
      <Box x={76} y={0} w={64} h={13} c="#0B1222" r={0} />
      <Box x={83} y={4.5} w={40} h={4} c="#CBD5E1" />
      <Box x={148} y={4.5} w={36} h={4} c="#475569" />
      {indents.map((indent, i) => {
        const y = 22 + i * 13.5;
        const last = i === indents.length - 1;
        let x = 98 + indent;
        const segments = last ? [typing * 120] : [0, 1, 2].slice(0, 1 + (i % 3)).map((s) => 12 + random(`seg-${variant}-${i}-${s}`) * 36);
        return (
          <React.Fragment key={i}>
            <Box x={82} y={y} w={7} h={4} c="#334155" />
            {segments.map((w, s) => {
              const box = <Box key={s} x={x} y={y} w={w} h={4} c={palette[(i + s * 2 + variant) % palette.length]} />;
              x += w + 4;
              return box;
            })}
            {last && blink(frame) ? <Box x={98 + indent + typing * 120 + 2} y={y - 2} w={1.5} h={8} c="#E2E8F0" r={0} /> : null}
          </React.Fragment>
        );
      })}
    </Win>
  );
};

const BrowserScreen: React.FC<ScreenProps> = ({ frame, variant }) => {
  const [c1, c2] = variant ? ["#F97316", "#FACC15"] : ["#6366F1", "#22D3EE"];
  const scroll = (frame * 0.32 + variant * 25) % 64;
  return (
    <Win bg="#F8FAFC" bar="#E2E8F0" light>
      <Box x={0} y={0} w={304} h={15} c="#EEF2F7" r={0} />
      {[0, 1, 2].map((i) => (
        <Box key={i} x={8 + i * 9} y={5.5} w={5} h={4} r={1} c="#94A3B8" />
      ))}
      <Box x={40} y={3.5} w={196} h={8} r={4} c="#FFFFFF" style={{ boxShadow: "inset 0 0 0 0.6px #CBD5E1" }} />
      <Box x={46} y={6} w={4} h={3} r={1} c="#22C55E" />
      <Box x={54} y={6} w={74} h={3} c="#94A3B8" />
      <div style={{ position: "absolute", left: 0, top: 15, right: 0, bottom: 0, overflow: "hidden" }}>
        <div style={{ position: "absolute", left: 0, top: -scroll, width: 304, height: 260 }}>
          <Box x={12} y={8} w={30} h={6} c={c1} />
          {[0, 1, 2].map((i) => (
            <Box key={i} x={200 + i * 30} y={9} w={20} h={4} c="#94A3B8" />
          ))}
          <Box x={10} y={22} w={284} h={64} r={6} c={`linear-gradient(120deg, ${c1}, ${c2})`} />
          <Box x={22} y={36} w={120} h={8} c="rgba(255,255,255,0.95)" />
          <Box x={22} y={50} w={150} h={4} c="rgba(255,255,255,0.7)" />
          <Box x={22} y={58} w={110} h={4} c="rgba(255,255,255,0.7)" />
          <Box x={22} y={68} w={40} h={9} r={4} c="#FFFFFF" />
          {[0, 1, 2].map((i) => (
            <React.Fragment key={i}>
              <Box x={10 + i * 96} y={94} w={88} h={70} r={5} c="#FFFFFF" style={{ boxShadow: "0 1px 3px rgba(15,23,42,0.15)" }} />
              <Box x={10 + i * 96} y={94} w={88} h={36} r={5} c={[c1, c2, "#A5B4FC"][i]} o={0.35} />
              <Box x={16 + i * 96} y={136} w={60} h={4} c="#334155" />
              <Box x={16 + i * 96} y={144} w={44} h={3.5} c="#94A3B8" />
              <Box x={16 + i * 96} y={151} w={52} h={3.5} c="#94A3B8" />
            </React.Fragment>
          ))}
          {Array.from({ length: 8 }, (_, i) => (
            <Box key={i} x={12} y={174 + i * 9} w={170 + random(`text-${variant}-${i}`) * 110} h={4} c="#CBD5E1" />
          ))}
        </div>
      </div>
    </Win>
  );
};

const SlidesScreen: React.FC<ScreenProps> = ({ frame }) => {
  const pulse = 1 + Math.sin(frame * 0.08) * 0.05;
  return (
    <Win bg="#1E293B" bar="#C2410C">
      {[0, 1, 2, 3].map((i) => (
        <Box
          key={i}
          x={6}
          y={8 + i * 38}
          w={46}
          h={30}
          r={2}
          c="#F8FAFC"
          o={i === 1 ? 1 : 0.5}
          style={i === 1 ? { boxShadow: "0 0 0 1.5px #F97316" } : undefined}
        />
      ))}
      <Box x={62} y={8} w={234} h={150} r={3} c="#FFFFFF" />
      <Box x={78} y={24} w={112} h={10} c="#0F172A" />
      <Box x={78} y={40} w={80} h={4} c="#64748B" />
      {[0, 1, 2].map((i) => (
        <React.Fragment key={i}>
          <Box x={80} y={62 + i * 16} w={5} h={5} r={3} c="#F97316" />
          <Box x={90} y={62.5 + i * 16} w={72 - i * 10} h={4} c="#94A3B8" />
        </React.Fragment>
      ))}
      <div
        style={{
          position: "absolute",
          left: 200,
          top: 52,
          width: 72,
          height: 72,
          borderRadius: "50%",
          background: "conic-gradient(#F97316 0 35%, #FACC15 0 62%, #6366F1 0 100%)",
          transform: `rotate(${frame * 0.6}deg) scale(${pulse})`,
        }}
      />
      <div
        style={{
          position: "absolute",
          left: 218,
          top: 70,
          width: 36,
          height: 36,
          borderRadius: "50%",
          background: "#FFFFFF",
          transform: `scale(${pulse})`,
        }}
      />
    </Win>
  );
};

const SheetScreen: React.FC<ScreenProps> = ({ frame, variant }) => {
  const cols = 7;
  const rows = 10;
  const cw = 40;
  const ch = 13.5;
  const ox = 16;
  const oy = 26;
  const step = Math.floor((frame + variant * 23) / 16) % 24;
  const selR = 1 + (step % 8);
  const selC = 1 + (Math.floor(step / 8) % 5);
  return (
    <Win bg="#FFFFFF" bar="#15803D">
      <Box x={0} y={0} w={304} h={12} r={0} c="#F0FDF4" />
      {[0, 1, 2, 3, 4, 5].map((i) => (
        <Box key={i} x={6 + i * 12} y={3} w={7} h={6} r={1.5} c={i === 2 ? "#16A34A" : "#86EFAC"} />
      ))}
      <Box x={6} y={15.5} w={14} h={5} c="#94A3B8" />
      <Box x={26} y={15.5} w={90} h={5} c="#CBD5E1" />
      <Box x={0} y={oy} w={ox} h={rows * ch} r={0} c="#F1F5F9" />
      <Box x={ox} y={oy} w={cols * cw} h={ch} r={0} c="#F1F5F9" />
      {Array.from({ length: cols + 1 }, (_, c) => (
        <Box key={`v${c}`} x={ox + c * cw} y={oy} w={0.6} h={rows * ch} r={0} c="#E2E8F0" />
      ))}
      {Array.from({ length: rows + 1 }, (_, r) => (
        <Box key={`h${r}`} x={0} y={oy + r * ch} w={304} h={0.6} r={0} c="#E2E8F0" />
      ))}
      {Array.from({ length: (rows - 1) * cols }, (_, k) => {
        const r = 1 + Math.floor(k / cols);
        const c = k % cols;
        const v = random(`cell-${variant}-${k}`);
        if (v < 0.22) return null;
        const w = c === 0 ? 18 + v * 12 : 5 + v * 18;
        return (
          <Box
            key={k}
            x={c === 0 ? ox + 4 : ox + (c + 1) * cw - 5 - w}
            y={oy + r * ch + 4.75}
            w={w}
            h={4}
            c={c === 0 ? "#475569" : "#64748B"}
            o={0.85}
          />
        );
      })}
      <Box
        x={ox + selC * cw}
        y={oy + selR * ch}
        w={cw}
        h={ch}
        r={0}
        c="rgba(22,163,74,0.08)"
        style={{ boxShadow: "inset 0 0 0 1.5px #16A34A" }}
      />
    </Win>
  );
};

const BlocksScreen: React.FC<ScreenProps> = ({ frame, variant }) => {
  const spriteX = 248 + Math.sin(frame * 0.07 + variant) * 28;
  const hop = -Math.abs(Math.sin(frame * 0.14)) * 6;
  const block = (key: string, x: number, y: number, w: number, c: string) => (
    <React.Fragment key={key}>
      <Box x={x} y={y} w={w} h={12} r={3} c={c} style={{ boxShadow: "inset 0 -2px 0 rgba(0,0,0,0.15)" }} />
      <Box x={x + 6} y={y + 4} w={w * 0.45} h={3.5} c="rgba(255,255,255,0.85)" />
    </React.Fragment>
  );
  return (
    <Win bg="#F8FAFC" bar="#7C3AED">
      <Box x={0} y={0} w={22} h={171} r={0} c="#EEF2FF" />
      {["#3B82F6", "#8B5CF6", "#EC4899", "#F59E0B", "#22C55E", "#F97316"].map((c, i) => (
        <Box key={c} x={7} y={8 + i * 16} w={8} h={8} r={4} c={c} />
      ))}
      <Box x={22} y={0} w={54} h={171} r={0} c="#FFFFFF" style={{ boxShadow: "inset -0.6px 0 0 #E2E8F0" }} />
      {[0, 1, 2, 3, 4, 5].map((i) => block(`p${i}`, 28, 10 + i * 18, 40, "#3B82F6"))}
      <Box x={88} y={12} w={76} h={18} r={9} c="#F59E0B" />
      <Box x={96} y={19} w={40} h={3.5} c="rgba(255,255,255,0.85)" />
      {block("s1", 88, 30, 98, "#3B82F6")}
      {block("s2", 88, 42, 82, "#8B5CF6")}
      <Box x={88} y={54} w={104} h={12} r={3} c="#F59E0B" />
      <Box x={88} y={66} w={10} h={26} r={0} c="#F59E0B" />
      {block("s3", 98, 66, 74, "#3B82F6")}
      {block("s4", 98, 78, 86, "#EC4899")}
      <Box x={88} y={90} w={64} h={8} r={3} c="#F59E0B" />
      {block("s5", 88, 98, 78, "#22C55E")}
      <Box x={202} y={8} w={94} h={74} r={4} c="#FFFFFF" style={{ boxShadow: "0 0 0 0.6px #CBD5E1" }} />
      <Box x={202} y={66} w={94} h={16} r={0} c="#DCFCE7" />
      <div
        style={{
          position: "absolute",
          left: spriteX - 9,
          top: 46 + hop,
          width: 18,
          height: 18,
          borderRadius: "50%",
          background: "#FB923C",
          boxShadow: "inset -2px -2px 0 rgba(0,0,0,0.12)",
        }}
      >
        <Box x={4} y={6} w={3} h={3} r={2} c="#0F172A" />
        <Box x={11} y={6} w={3} h={3} r={2} c="#0F172A" />
      </div>
    </Win>
  );
};

const DocScreen: React.FC<ScreenProps> = ({ frame, variant }) => {
  const typing = ((frame + variant * 31) % 150) / 150;
  return (
    <Win bg="#E2E8F0" bar="#1D4ED8">
      <Box x={0} y={0} w={304} h={14} r={0} c="#FFFFFF" />
      {Array.from({ length: 9 }, (_, i) => (
        <Box key={i} x={8 + i * 14} y={4} w={9} h={6} r={1.5} c={i === 3 ? "#2563EB" : "#CBD5E1"} />
      ))}
      <Box x={66} y={22} w={172} h={170} r={2} c="#FFFFFF" style={{ boxShadow: "0 2px 8px rgba(15,23,42,0.15)" }} />
      <Box x={80} y={34} w={96} h={8} c="#0F172A" />
      <Box x={80} y={48} w={60} h={4} c="#2563EB" o={0.8} />
      {Array.from({ length: 6 }, (_, i) => (
        <Box key={i} x={80} y={60 + i * 9} w={118 + random(`line-${variant}-${i}`) * 26} h={4} c="#94A3B8" />
      ))}
      <Box x={80} y={116} w={144} h={30} r={3} c="#DBEAFE" />
      <Box x={80} y={152} w={typing * 130} h={4} c="#475569" />
      {blink(frame) ? <Box x={80 + typing * 130 + 2} y={149} w={1.2} h={9} r={0} c="#1D4ED8" /> : null}
    </Win>
  );
};

const ChartScreen: React.FC<ScreenProps> = ({ frame, variant }) => {
  const [c1, c2] = variant ? ["#F472B6", "#A78BFA"] : ["#22D3EE", "#818CF8"];
  const line = Array.from({ length: 12 }, (_, k) => `${10 + k * 24},${62 - 22 * Math.sin(frame * 0.05 + k * 0.7 + variant)}`).join(" ");
  return (
    <Win bg="#0B1222" bar="#0A101D">
      {[0, 1, 2].map((i) => (
        <React.Fragment key={i}>
          <Box x={10 + i * 98} y={10} w={90} h={34} r={5} c="#111A2E" />
          <Box x={18 + i * 98} y={17} w={34} h={4} c="#64748B" />
          <Box x={18 + i * 98} y={27} w={44 + i * 6} h={8} c={[c1, c2, "#34D399"][i]} />
        </React.Fragment>
      ))}
      <Box x={10} y={52} w={284} h={110} r={6} c="#111A2E" />
      {Array.from({ length: 10 }, (_, i) => {
        const v = 0.35 + 0.5 * (0.5 + 0.5 * Math.sin(frame * 0.07 + i * 0.9 + variant * 2));
        const h = v * 78;
        return <Box key={i} x={24 + i * 26} y={152 - h} w={14} h={h} r={3} c={`linear-gradient(180deg, ${c1}, ${c2})`} o={0.9} />;
      })}
      <svg style={{ position: "absolute", left: 10, top: 52 }} width={284} height={110}>
        <polyline fill="none" stroke="#F8FAFC" strokeOpacity={0.55} strokeWidth={1.4} points={line} />
      </svg>
    </Win>
  );
};

const TerminalScreen: React.FC<ScreenProps> = ({ frame, variant }) => {
  const step = 7;
  const lineHeight = 11;
  const first = Math.floor(frame / step);
  const offset = (frame % step) / step;
  return (
    <Win bg="#05070D" bar="#111827">
      {Array.from({ length: 15 }, (_, k) => {
        const n = first + k;
        const y = 6 + k * lineHeight - offset * lineHeight;
        const kind = Math.floor(random(`kind-${variant}-${n}`) * 4);
        const w1 = 20 + random(`w1-${variant}-${n}`) * 120;
        const w2 = random(`w2-${variant}-${n}`) * 80;
        const x = kind === 0 ? 18 : 8;
        return (
          <React.Fragment key={n}>
            {kind === 0 ? <Box x={8} y={y} w={6} h={4} c="#4ADE80" /> : null}
            <Box x={x} y={y} w={w1} h={4} c={kind === 0 ? "#E2E8F0" : kind === 1 ? "#4ADE80" : "#64748B"} o={0.9} />
            {w2 > 30 ? <Box x={x + w1 + 5} y={y} w={w2} h={4} c="#38BDF8" o={0.8} /> : null}
          </React.Fragment>
        );
      })}
      <Box x={0} y={150} w={304} h={21} r={0} c="#05070D" />
      <Box x={8} y={156} w={6} h={4} c="#4ADE80" />
      {Math.floor(frame / 12) % 2 === 0 ? <Box x={18} y={154} w={6} h={8} r={1} c="#E2E8F0" /> : null}
    </Win>
  );
};

const PaintScreen: React.FC<ScreenProps> = ({ frame, variant }) => {
  // O desenho avança no tempo absoluto do vídeo e fica pronto.
  const stroke1 = clamp01((frame - 170 - variant * 20) / 80);
  const stroke2 = clamp01((frame - 230 - variant * 20) / 70);
  return (
    <Win bg="#F8FAFC" bar="#DB2777">
      <Box x={0} y={0} w={20} h={171} r={0} c="#FDF2F8" />
      {[0, 1, 2, 3, 4, 5].map((i) => (
        <Box key={i} x={5} y={8 + i * 15} w={10} h={10} r={2.5} c={i === 1 ? "#DB2777" : "#F9A8D4"} o={i === 1 ? 1 : 0.6} />
      ))}
      {["#0F172A", "#EF4444", "#F59E0B", "#22C55E", "#3B82F6", "#A855F7"].map((c, i) => (
        <Box key={c} x={30 + i * 14} y={5} w={9} h={9} r={5} c={c} />
      ))}
      <Box x={28} y={20} w={268} h={144} r={3} c="#FFFFFF" style={{ boxShadow: "0 1px 4px rgba(15,23,42,0.15)" }} />
      <svg style={{ position: "absolute", left: 28, top: 20 }} width={268} height={144} viewBox="0 0 268 144">
        <circle cx={214} cy={38} r={18} fill="#FCD34D" />
        <path d="M0 118 Q 60 70 120 104 T 268 96 V144 H0 Z" fill="#86EFAC" />
        <path
          d="M20 60 C 60 20, 90 100, 130 50 S 190 30, 210 80"
          fill="none"
          stroke="#EC4899"
          strokeWidth={4}
          strokeLinecap="round"
          pathLength={1}
          strokeDasharray="1 1"
          strokeDashoffset={1 - stroke1}
        />
        <path
          d="M40 130 C 80 110, 120 140, 170 118"
          fill="none"
          stroke="#3B82F6"
          strokeWidth={3.5}
          strokeLinecap="round"
          pathLength={1}
          strokeDasharray="1 1"
          strokeDashoffset={1 - stroke2}
        />
      </svg>
    </Win>
  );
};

const SCREENS: Record<ScreenKind, React.FC<ScreenProps>> = {
  code: CodeScreen,
  browser: BrowserScreen,
  slides: SlidesScreen,
  sheet: SheetScreen,
  blocks: BlocksScreen,
  doc: DocScreen,
  chart: ChartScreen,
  terminal: TerminalScreen,
  paint: PaintScreen,
};

type ScreenContentProps = {
  kind: ScreenKind;
  frame: number;
  variant: number;
  wallpaper: number;
  /** Opacidade do programa aberto; 0 = só a área de trabalho. */
  apps?: number;
  /** Realce da pasta "Arquivos do professor" (arquivo recém-chegado). */
  inbox?: number;
};

/** As duas pastas que o Midview cria na Área de Trabalho do aluno. */
const DesktopFolders: React.FC<{ inbox: number }> = ({ inbox }) => (
  <>
    {[
      { label: "Arquivos do professor", y: 18, glow: inbox },
      { label: "Entregar ao professor", y: 66, glow: 0 },
    ].map(({ label, y, glow }) => (
      <div key={label} style={{ position: "absolute", left: 10, top: y, width: 54, textAlign: "center" }}>
        <div
          style={{
            margin: "0 auto",
            width: 26,
            height: 20,
            borderRadius: 3,
            background: "linear-gradient(180deg, #FCD34D, #F59E0B)",
            boxShadow: glow > 0 ? `0 0 ${10 * glow}px ${4 * glow}px rgba(252,211,77,${0.8 * glow})` : undefined,
          }}
        />
        <div style={{ marginTop: 3, fontSize: 7.5, lineHeight: 1.15, color: "#F8FAFC", textShadow: "0 1px 2px rgba(0,0,0,0.9)" }}>{label}</div>
      </div>
    ))}
  </>
);

/** Área de trabalho do aluno: papel de parede, pastas e o programa aberto, animados pelo frame absoluto. */
export const ScreenContent: React.FC<ScreenContentProps> = ({ kind, frame, variant, wallpaper, apps = 1, inbox = 0 }) => {
  const Screen = SCREENS[kind];
  return (
    <div style={{ position: "absolute", inset: 0, background: WALLPAPERS[wallpaper % WALLPAPERS.length] }}>
      <DesktopFolders inbox={inbox} />
      {apps > 0.01 ? (
        <div style={{ position: "absolute", inset: 0, opacity: apps }}>
          <Screen frame={frame} variant={variant} />
        </div>
      ) : null}
      <Box x={0} y={176} w={320} h={14} r={0} c="rgba(15,23,42,0.92)" />
      <Box x={6} y={179} w={10} h={8} r={2} c="#38BDF8" o={0.9} />
      {[0, 1, 2].map((i) => (
        <Box key={i} x={24 + i * 12} y={180} w={8} h={6} r={1.5} c="#94A3B8" o={0.7} />
      ))}
    </div>
  );
};
