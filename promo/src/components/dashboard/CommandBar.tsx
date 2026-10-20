import { Cast, Ellipsis, Lock, MessageSquare, ShieldCheck, type LucideIcon } from "lucide-react";
import React from "react";
import { interpolateColors } from "remotion";
import { clamp01 } from "../../lib/motion";
import { COLORS } from "../../theme";
import { COMMAND_BAR, type CommandId } from "./layout";

export type ButtonState = { active: number; press: number; hover: number };

/** Quanto a barra cresce no foco (o cursor mira já considerando essa escala). */
export const COMMAND_BAR_LIFT = 0.06;

const BUTTONS: Record<CommandId, { label: string; Icon: LucideIcon }> = {
  lock: { label: "Bloquear Telas", Icon: Lock },
  filter: { label: "Filtro Web", Icon: ShieldCheck },
  cast: { label: "Transmitir Tela", Icon: Cast },
};

type CommandBarProps = {
  /** Entrada da barra (sobe do rodapé). */
  appear: number;
  /** Foco: a barra cresce e ganha brilho. */
  lift: number;
  states: Record<CommandId, ButtonState>;
};

/** Barra de ações rápidas flutuante, em coordenadas da janela. */
export const CommandBar: React.FC<CommandBarProps> = ({ appear, lift, states }) => {
  const { left, top, width, height, pad, gap, iconW, order, widths } = COMMAND_BAR;
  const visible = clamp01(appear);
  return (
    <>
      <div
        style={{
          position: "absolute",
          left: left - 140,
          top: top - 60,
          width: width + 280,
          height: height + 150,
          background: "radial-gradient(50% 50% at 50% 55%, rgba(34,211,238,0.24), rgba(99,102,241,0.12) 45%, transparent 75%)",
          opacity: clamp01(lift) * visible,
        }}
      />
      <div
        className="absolute flex items-center rounded-2xl border border-white/[0.12]"
        style={{
          left,
          top,
          width,
          height,
          padding: pad,
          gap,
          background: "rgba(16,22,38,0.88)",
          backdropFilter: "blur(20px)",
          boxShadow: `0 ${18 + 16 * lift}px ${48 + 30 * lift}px -16px rgba(0,0,0,0.9), inset 0 1px 0 rgba(255,255,255,0.09)`,
          opacity: visible,
          transform: `translateY(${(1 - appear) * 46}px) scale(${1 + COMMAND_BAR_LIFT * lift})`,
        }}
      >
        {order.map((id) => (
          <CommandButton key={id} width={widths[id]} {...BUTTONS[id]} state={states[id]} />
        ))}
        <div className="h-7 w-px bg-white/10" />
        {[MessageSquare, Ellipsis].map((Icon, i) => (
          <div key={i} className="flex h-[46px] items-center justify-center rounded-xl text-slate-400" style={{ width: iconW }}>
            <Icon size={18} strokeWidth={2} />
          </div>
        ))}
      </div>
    </>
  );
};

const CommandButton: React.FC<{ width: number; label: string; Icon: LucideIcon; state: ButtonState }> = ({
  width,
  label,
  Icon,
  state,
}) => {
  const active = clamp01(state.active);
  return (
    <div
      className="relative flex h-[46px] items-center justify-center gap-2.5 rounded-xl text-[16px] font-semibold"
      style={{
        width,
        transform: `scale(${1 - 0.06 * state.press})`,
        color: interpolateColors(active, [0, 1], [COLORS.slate300, COLORS.white]),
      }}
    >
      <div className="absolute inset-0 rounded-xl bg-white/[0.07]" style={{ opacity: clamp01(state.hover) * (1 - active) }} />
      <div
        className="absolute inset-0 rounded-xl"
        style={{
          opacity: active,
          background: "linear-gradient(180deg, rgba(34,211,238,0.26), rgba(99,102,241,0.22))",
          boxShadow: "inset 0 0 0 1px rgba(103,232,249,0.55), 0 0 26px -4px rgba(34,211,238,0.6)",
        }}
      />
      <Icon className="relative" size={18} strokeWidth={2.2} color={interpolateColors(active, [0, 1], [COLORS.slate400, COLORS.cyanPale])} />
      <span className="relative">{label}</span>
    </div>
  );
};
