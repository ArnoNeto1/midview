import { Cast, ShieldCheck } from "lucide-react";
import React from "react";
import { FONTS } from "../../fonts";
import { clamp01, mix } from "../../lib/motion";
import { COLORS } from "../../theme";
import type { Point, Rect } from "./layout";
import { LessonScreen } from "./LessonScreen";
import { Padlock } from "./Padlock";
import type { Pc } from "./pcs";
import { SCREEN, ScreenContent } from "./ScreenContent";

/** Estado visual de uma estação. Todos os campos vão de 0 a 1 (molas podem passar de 1). */
export type CardState = {
  appear: number;
  online: number;
  /** Overlay "Tela bloqueada pelo professor". */
  lock: number;
  /** Fechamento do cadeado. */
  close: number;
  /** Badge de escudo do filtro web. */
  shield: number;
  /** Varredura do filtro (0→1). */
  scan: number;
  /** Tela do professor transmitida. */
  cast: number;
};

export const LIVE_CARD: CardState = { appear: 1, online: 1, lock: 0, close: 0, shield: 0, scan: 0, cast: 0 };

type PcCardProps = {
  pc: Pc;
  index: number;
  rect: Rect;
  state: CardState;
  /** Frame absoluto do vídeo (conteúdo ao vivo). */
  frame: number;
  /** Origem da onda de transmissão, em coordenadas da janela. */
  castOrigin?: Point;
};

export const PcCard: React.FC<PcCardProps> = ({ pc, index, rect, state, frame, castOrigin }) => {
  const { appear, online, lock, close, shield, scan, cast } = state;
  const k = rect.w / SCREEN.w;
  const shieldFlash = Math.sin(Math.PI * clamp01(scan)) * 0.9;
  return (
    <div
      style={{
        position: "absolute",
        left: rect.x,
        top: rect.y,
        width: rect.w,
        height: rect.h,
        borderRadius: 12,
        overflow: "hidden",
        background: "#0A0F1C",
        opacity: clamp01(appear * 1.6),
        transform: `translateY(${(1 - appear) * 28}px) scale(${mix(0.9, 1, appear)})`,
        boxShadow: "0 14px 30px -16px rgba(0,0,0,0.8)",
      }}
    >
      <Scaled k={k} style={{ opacity: clamp01(online) }}>
        <ScreenContent kind={pc.kind} frame={frame} variant={pc.variant} wallpaper={index} />
      </Scaled>
      {online < 0.999 ? <Connecting frame={frame} opacity={1 - clamp01(online)} /> : null}
      {lock > 0.001 ? <LockOverlay lock={clamp01(lock)} close={close} /> : null}
      {cast > 0.001 ? (
        <div style={{ position: "absolute", inset: 0, clipPath: castClip(rect, cast, castOrigin) }}>
          <Scaled k={k}>
            <LessonScreen frame={frame} />
          </Scaled>
        </div>
      ) : null}
      <NameStrip pc={pc} index={index} frame={frame} online={clamp01(online)} cast={clamp01(cast)} />
      {shield > 0.001 ? <ShieldBadge progress={shield} /> : null}
      {scan > 0.001 && scan < 0.999 ? <ScanLine progress={scan} /> : null}
      <div
        style={{
          position: "absolute",
          inset: 0,
          borderRadius: 12,
          boxShadow: [
            "inset 0 0 0 1px rgba(255,255,255,0.09)",
            `inset 0 0 0 1.5px rgba(52,211,153,${shieldFlash})`,
            `inset 0 0 0 1.5px rgba(129,140,248,${clamp01(cast) * 0.75})`,
          ].join(", "),
        }}
      />
    </div>
  );
};

/** Desenha no canvas virtual 320×200 e escala para o tamanho do card. */
const Scaled: React.FC<{ k: number; style?: React.CSSProperties; children: React.ReactNode }> = ({ k, style, children }) => (
  <div
    style={{
      position: "absolute",
      left: 0,
      top: 0,
      width: SCREEN.w,
      height: SCREEN.h,
      transform: `scale(${k})`,
      transformOrigin: "0 0",
      ...style,
    }}
  >
    {children}
  </div>
);

/** A tela do professor "varre" o card a partir do lado de onde vem a onda. */
const castClip = (rect: Rect, cast: number, origin?: Point) => {
  const ox = (origin?.x ?? rect.cx) - rect.x;
  const oy = (origin?.y ?? rect.cy) - rect.y;
  const near = Math.hypot(ox - Math.min(Math.max(ox, 0), rect.w), oy - Math.min(Math.max(oy, 0), rect.h));
  const far = Math.max(
    Math.hypot(ox, oy),
    Math.hypot(ox - rect.w, oy),
    Math.hypot(ox, oy - rect.h),
    Math.hypot(ox - rect.w, oy - rect.h),
  );
  return `circle(${mix(near, far, clamp01(cast))}px at ${ox}px ${oy}px)`;
};

const Connecting: React.FC<{ frame: number; opacity: number }> = ({ frame, opacity }) => (
  <div
    style={{
      position: "absolute",
      inset: 0,
      display: "flex",
      flexDirection: "column",
      alignItems: "center",
      justifyContent: "center",
      gap: 8,
      paddingBottom: 26,
      opacity,
    }}
  >
    <svg width={22} height={22} viewBox="0 0 22 22" style={{ transform: `rotate(${frame * 12}deg)` }}>
      <circle cx={11} cy={11} r={8} fill="none" stroke="rgba(148,163,184,0.25)" strokeWidth={2.4} />
      <path d="M11 3 a8 8 0 0 1 8 8" fill="none" stroke={COLORS.cyanLight} strokeWidth={2.4} strokeLinecap="round" />
    </svg>
    <span style={{ fontSize: 12, color: COLORS.slate400 }}>Conectando…</span>
  </div>
);

const LockOverlay: React.FC<{ lock: number; close: number }> = ({ lock, close }) => (
  <div
    style={{
      position: "absolute",
      inset: 0,
      opacity: lock,
      background: "radial-gradient(110% 90% at 50% 38%, rgba(67,56,202,0.5) 0%, rgba(9,13,22,0.94) 70%)",
      backdropFilter: `blur(${lock * 6}px)`,
      display: "flex",
      flexDirection: "column",
      alignItems: "center",
      justifyContent: "center",
      paddingBottom: 28,
    }}
  >
    <Padlock size={40} close={close} />
    <div style={{ marginTop: 6, fontSize: 14, fontWeight: 600, color: COLORS.white, letterSpacing: "-0.01em" }}>
      Tela bloqueada
    </div>
    <div style={{ fontSize: 12, color: COLORS.slate400 }}>pelo professor</div>
  </div>
);

const NameStrip: React.FC<{ pc: Pc; index: number; frame: number; online: number; cast: number }> = ({
  pc,
  index,
  frame,
  online,
  cast,
}) => {
  const ping = ((frame + index * 7) % 45) / 45;
  const live = online * (1 - cast);
  return (
    <div
      style={{
        position: "absolute",
        left: 0,
        right: 0,
        bottom: 0,
        height: 34,
        padding: "0 10px",
        display: "flex",
        alignItems: "center",
        justifyContent: "space-between",
        background: "linear-gradient(180deg, rgba(2,6,23,0) 0%, rgba(2,6,23,0.7) 50%, rgba(2,6,23,0.86) 100%)",
      }}
    >
      <div style={{ display: "flex", alignItems: "baseline", gap: 7 }}>
        <span style={{ fontFamily: FONTS.mono, fontSize: 13, fontWeight: 600, color: COLORS.white }}>{pc.id}</span>
        <span style={{ fontSize: 13, color: COLORS.slate300 }}>{pc.student}</span>
      </div>
      <div style={{ position: "relative", width: 112, height: 18 }}>
        <Status opacity={live} color={COLORS.greenLight}>
          <span style={{ position: "relative", width: 7, height: 7 }}>
            <span
              style={{
                position: "absolute",
                inset: 0,
                borderRadius: "50%",
                background: COLORS.green,
                transform: `scale(${1 + ping * 1.8})`,
                opacity: 0.7 * (1 - ping),
              }}
            />
            <span style={{ position: "absolute", inset: 0, borderRadius: "50%", background: COLORS.greenLight, boxShadow: "0 0 8px #22C55E" }} />
          </span>
          Online
        </Status>
        <Status opacity={cast} color={COLORS.indigoPale}>
          <Cast size={13} strokeWidth={2.4} />
          Transmitindo
        </Status>
        <Status opacity={1 - online} color={COLORS.slate500}>
          <span style={{ width: 7, height: 7, borderRadius: "50%", background: COLORS.slate600 }} />
          Offline
        </Status>
      </div>
    </div>
  );
};

const Status: React.FC<{ opacity: number; color: string; children: React.ReactNode }> = ({ opacity, color, children }) =>
  opacity > 0.01 ? (
    <div
      style={{
        position: "absolute",
        right: 0,
        top: 0,
        bottom: 0,
        display: "flex",
        alignItems: "center",
        gap: 6,
        fontSize: 12.5,
        fontWeight: 600,
        color,
        opacity,
      }}
    >
      {children}
    </div>
  ) : null;

const ShieldBadge: React.FC<{ progress: number }> = ({ progress }) => (
  <div
    style={{
      position: "absolute",
      top: 8,
      right: 8,
      display: "flex",
      alignItems: "center",
      gap: 5,
      padding: "4px 9px 4px 7px",
      borderRadius: 999,
      background: "rgba(6,78,59,0.78)",
      border: "1px solid rgba(52,211,153,0.5)",
      color: "#6EE7B7",
      fontSize: 12,
      fontWeight: 600,
      boxShadow: "0 0 18px -2px rgba(52,211,153,0.55)",
      transformOrigin: "100% 0%",
      transform: `scale(${mix(0.4, 1, progress)})`,
      opacity: clamp01(progress * 1.8),
    }}
  >
    <ShieldCheck size={13} strokeWidth={2.4} />
    Protegido
  </div>
);

const ScanLine: React.FC<{ progress: number }> = ({ progress }) => (
  <div
    style={{
      position: "absolute",
      left: 0,
      right: 0,
      top: `${mix(-30, 110, progress)}%`,
      height: "30%",
      background: "linear-gradient(180deg, rgba(52,211,153,0) 0%, rgba(52,211,153,0.2) 70%, rgba(110,231,183,0.75) 100%)",
      opacity: Math.sin(Math.PI * progress),
    }}
  />
);
