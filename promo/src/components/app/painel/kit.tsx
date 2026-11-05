import React from "react";
import { clamp01, mix } from "../../../lib/motion";
import type { Box, GroupId } from "../geometry";
import { P_CAPTION, P_TOTAL, PAINEL_PCS } from "./geometry";
import { GROUP_COLOR, P, PFONT, PG, PICONS } from "./palette";
import { MidviewIcon } from "./TitleBar";

/**
 * Peças das janelas de diálogo do Painel (Themes/Painel.Dialogs.xaml e
 * Painel.Controls.xaml): a janela com a barra de título escura do Windows, o
 * cabeçalho com o ícone no quadrado da cor do grupo, cartões, botões (normal,
 * principal e perigo), opções, campos e a lista de resultado. As caixas são
 * relativas ao conteúdo da janela, abaixo da barra de título, e foram medidas
 * nas fotos do programa.
 */

export const icon = (size: number, color?: string): React.CSSProperties => ({ fontFamily: PICONS, fontSize: size, lineHeight: 1, color });

/** "Na turma toda (15 máquinas)", como o app escreve. */
export const TURMA_TODA = `Na turma toda (${P_TOTAL} máquinas)`;

type DialogProps = {
  /** Título na barra do Windows. */
  title: string;
  box: Box;
  appear: number;
  /** Cabeçalho: ícone do botão que abriu a janela, na cor do grupo. */
  icon?: string;
  group?: GroupId;
  heading?: string;
  hint?: React.ReactNode;
  closeHover?: number;
  children?: React.ReactNode;
};

/** A janela: barra de título nativa pintada de escuro, fundo #0F1828 e o cabeçalho. */
export const PDialog: React.FC<DialogProps> = ({ title, box, appear, icon: glyph, group, heading, hint, closeHover = 0, children }) => {
  if (appear <= 0.01) return null;
  const accent = group ? GROUP_COLOR[group] : P.accent;
  return (
    <div
      style={{
        position: "absolute",
        left: box.x,
        top: box.y,
        width: box.w,
        height: box.h,
        borderRadius: 8,
        overflow: "hidden",
        background: P.surface,
        border: "1px solid #2B3954",
        boxShadow: "0 30px 80px -12px rgba(0,0,0,0.75), 0 0 0 1px rgba(0,0,0,0.4)",
        opacity: clamp01(appear * 1.4),
        transform: `scale(${mix(0.95, 1, clamp01(appear))})`,
        fontFamily: PFONT,
        color: P.text,
      }}
    >
      <div
        style={{
          position: "relative",
          height: P_CAPTION,
          display: "flex",
          alignItems: "center",
          gap: 8,
          paddingLeft: 10,
          fontFamily: '"Segoe UI", system-ui, sans-serif',
          fontSize: 12,
          color: "#E6EAF0",
        }}
      >
        <MidviewIcon size={16} />
        {title}
        <div
          style={{
            position: "absolute",
            right: 0,
            top: 0,
            width: 46,
            height: P_CAPTION,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            background: `rgba(196,43,28,${clamp01(closeHover)})`,
          }}
        >
          <span style={icon(10, "#E6EAF0")}>{PG.close}</span>
        </div>
      </div>
      <div style={{ position: "absolute", left: 0, top: P_CAPTION, width: box.w, height: box.h - P_CAPTION }}>
        {glyph ? (
          <div style={{ position: "absolute", left: 24, top: 22, width: 40, height: 40 }}>
            <div style={{ position: "absolute", inset: 0, borderRadius: 12, background: accent, opacity: 0.18 }} />
            <div style={{ position: "absolute", inset: 0, display: "flex", alignItems: "center", justifyContent: "center" }}>
              <span style={icon(19, accent)}>{glyph}</span>
            </div>
          </div>
        ) : null}
        {heading ? (
          <div style={{ position: "absolute", left: glyph ? 78 : 24, top: 28, fontSize: 20, fontWeight: 600, lineHeight: "28px", whiteSpace: "nowrap" }}>
            {heading}
          </div>
        ) : null}
        {hint ? (
          <div style={{ position: "absolute", left: glyph ? 78 : 24, top: 66, right: 24, fontSize: 12, color: P.muted, lineHeight: "16px" }}>{hint}</div>
        ) : null}
        {children}
      </div>
    </div>
  );
};

/** Cartão #141F33 com borda e cantos de 10. */
export const Card: React.FC<{ box: Box; children?: React.ReactNode; style?: React.CSSProperties }> = ({ box, children, style }) => (
  <div
    style={{
      position: "absolute",
      left: box.x,
      top: box.y,
      width: box.w,
      height: box.h,
      borderRadius: 10,
      background: P.card,
      border: `1px solid ${P.line}`,
      boxSizing: "border-box",
      overflow: "hidden",
      ...style,
    }}
  >
    {children}
  </div>
);

/** Texto posicionado (12,5 px; `muted` = dica cinza, `title` = título de cartão). */
export const T: React.FC<{
  x: number;
  y: number;
  w?: number;
  size?: number;
  weight?: number;
  color?: string;
  muted?: boolean;
  title?: boolean;
  nowrap?: boolean;
  lh?: number;
  children: React.ReactNode;
}> = ({ x, y, w, size, weight, color, muted, title, nowrap, lh, children }) => (
  <div
    style={{
      position: "absolute",
      left: x,
      top: y,
      width: w,
      fontSize: size ?? (title ? 14 : muted ? 12 : 12.5),
      fontWeight: weight ?? (title ? 600 : 400),
      lineHeight: lh ? `${lh}px` : 1.25,
      color: color ?? (muted ? P.muted : P.text),
      whiteSpace: nowrap ? "nowrap" : "normal",
    }}
  >
    {children}
  </div>
);

export type BtnKind = "normal" | "primary" | "danger";

/** Botão: normal (cartão), principal (turquesa, um por janela) ou perigo (contorno vermelho). */
export const PBtn: React.FC<{
  box: Box;
  kind?: BtnKind;
  hover?: number;
  press?: number;
  disabled?: boolean;
  children: React.ReactNode;
}> = ({ box, kind = "normal", hover = 0, press = 0, disabled = false, children }) => {
  const h = disabled ? 0 : clamp01(hover);
  const base =
    kind === "primary"
      ? { bg: P.accent, line: P.accent, text: P.onAccent, hoverBg: P.accentText, weight: 600 }
      : kind === "danger"
        ? { bg: "transparent", line: P.error, text: P.error, hoverBg: P.dangerSoft, weight: 400 }
        : { bg: P.card, line: P.line, text: P.text, hoverBg: P.line, weight: 400 };
  const off = disabled
    ? { bg: kind === "primary" ? P.line : P.surface, line: P.line, text: P.disabled }
    : null;
  return (
    <div
      style={{
        position: "absolute",
        left: box.x,
        top: box.y,
        width: box.w,
        height: box.h,
        borderRadius: 4,
        border: `1px solid ${off ? off.line : kind === "normal" && h > 0.5 ? P.lineHover : base.line}`,
        background: off ? off.bg : base.bg,
        boxSizing: "border-box",
        overflow: "hidden",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        fontSize: 12,
        fontWeight: off ? 400 : base.weight,
        color: off ? off.text : base.text,
        whiteSpace: "nowrap",
        transform: `scale(${1 - 0.05 * clamp01(press)})`,
        opacity: 1 - 0.2 * clamp01(press),
      }}
    >
      <div style={{ position: "absolute", inset: 0, background: base.hoverBg, opacity: h }} />
      <span style={{ position: "relative" }}>{children}</span>
    </div>
  );
};

/** Opção (RadioButton): círculo com a bolinha turquesa. */
export const PRadio: React.FC<{ x: number; y: number; on: number; w?: number; color?: string; children: React.ReactNode }> = ({
  x,
  y,
  on,
  w,
  color,
  children,
}) => {
  const c = clamp01(on);
  return (
    <div style={{ position: "absolute", left: x, top: y, width: w, display: "flex", gap: 8, fontSize: 12.5, lineHeight: "16px", color: color ?? P.text }}>
      <span
        style={{
          flex: "0 0 auto",
          width: 16,
          height: 16,
          borderRadius: 8,
          border: `1.5px solid ${c > 0.5 ? P.accent : P.text}`,
          background: P.bg,
          boxSizing: "border-box",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
        }}
      >
        <span style={{ width: 8, height: 8, borderRadius: 4, background: P.accent, opacity: c, transform: `scale(${mix(0.3, 1, c)})` }} />
      </span>
      <span>{children}</span>
    </div>
  );
};

/** Caixa de seleção: escura; marcada, turquesa com o visto escuro. */
export const PCheck: React.FC<{ x: number; y: number; on: number; w?: number; bold?: boolean; children: React.ReactNode }> = ({
  x,
  y,
  on,
  w,
  bold,
  children,
}) => {
  const c = clamp01(on);
  return (
    <div style={{ position: "absolute", left: x, top: y, width: w, display: "flex", gap: 8, fontSize: 12.5, lineHeight: "16px", fontWeight: bold ? 600 : 400 }}>
      <span
        style={{
          position: "relative",
          flex: "0 0 auto",
          width: 16,
          height: 16,
          borderRadius: 3,
          border: `1.5px solid ${P.text}`,
          background: P.bg,
          boxSizing: "border-box",
        }}
      >
        <span
          style={{
            position: "absolute",
            inset: -1.5,
            borderRadius: 3,
            background: P.accent,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            opacity: c,
          }}
        >
          <span style={{ ...icon(11, P.onAccent), fontWeight: 700 }}>{PG.check}</span>
        </span>
      </span>
      <span>{children}</span>
    </div>
  );
};

/** Campo de texto: fundo #0A111E; com foco, borda turquesa. */
export const PField: React.FC<{ box: Box; text?: string; caret?: boolean; focus?: boolean; multiline?: boolean; size?: number }> = ({
  box,
  text = "",
  caret = false,
  focus = false,
  multiline = false,
  size = 12.5,
}) => (
  <div
    style={{
      position: "absolute",
      left: box.x,
      top: box.y,
      width: box.w,
      height: box.h,
      borderRadius: 4,
      border: `1px solid ${focus || caret ? P.accent : P.line}`,
      background: P.bg,
      boxSizing: "border-box",
      padding: multiline ? "6px 7px" : "0 7px",
      display: "flex",
      alignItems: multiline ? "flex-start" : "center",
      fontSize: size,
      lineHeight: 1.3,
      color: P.text,
      whiteSpace: multiline ? "pre-wrap" : "nowrap",
      overflow: "hidden",
    }}
  >
    <span>
      {text}
      {caret ? <span style={{ borderRight: `1.5px solid ${P.text}`, marginLeft: 1 }}>&#8203;</span> : null}
    </span>
  </div>
);

/** Chave de liga/desliga (só no Agendar). */
export const PSwitch: React.FC<{ x: number; y: number; on: number }> = ({ x, y, on }) => {
  const c = clamp01(on);
  return (
    <div
      style={{
        position: "absolute",
        left: x,
        top: y,
        width: 36,
        height: 20,
        borderRadius: 10,
        background: c > 0.5 ? P.accent : P.line,
        border: `1px solid ${c > 0.5 ? P.accent : P.lineHover}`,
        boxSizing: "border-box",
      }}
    >
      <div
        style={{
          position: "absolute",
          top: 2,
          left: mix(2, 18, c),
          width: 14,
          height: 14,
          borderRadius: 7,
          background: c > 0.5 ? P.onAccent : P.muted,
        }}
      />
    </div>
  );
};

/** Dia da semana do Agendar: aceso, turquesa com texto escuro. */
export const DayPill: React.FC<{ x: number; y: number; on: number; children: React.ReactNode }> = ({ x, y, on, children }) => {
  const c = clamp01(on);
  return (
    <div
      style={{
        position: "absolute",
        left: x,
        top: y,
        width: 46,
        height: 24,
        borderRadius: 12,
        background: P.card,
        border: `1px solid ${c > 0.5 ? P.accent : P.line}`,
        boxSizing: "border-box",
        overflow: "hidden",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        fontSize: 12,
        fontWeight: c > 0.5 ? 600 : 400,
        color: c > 0.5 ? P.onAccent : P.muted,
      }}
    >
      <div style={{ position: "absolute", inset: 0, background: P.accent, opacity: c }} />
      <span style={{ position: "relative" }}>{children}</span>
    </div>
  );
};

/** Número de um passo (selo redondo turquesa-claro). */
export const StepBadge: React.FC<{ x: number; y: number; n: number }> = ({ x, y, n }) => (
  <div
    style={{
      position: "absolute",
      left: x,
      top: y,
      width: 22,
      height: 22,
      borderRadius: 11,
      background: P.accentSoft,
      color: P.accentText,
      fontSize: 12,
      fontWeight: 700,
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
    }}
  >
    {n}
  </div>
);

/** Barra de rolagem fina, à direita de um cartão. */
export const ScrollThumb: React.FC<{ x: number; y: number; h: number }> = ({ x, y, h }) => (
  <div style={{ position: "absolute", left: x, top: y, width: 6, height: h, borderRadius: 3, background: "#3A4D70" }} />
);

/**
 * Cartão de andamento e resultado: "Enviando para 15 máquinas..." e uma linha
 * por máquina ("✓ ALUNO-01: ok"), chegando conforme `progress` (0→1).
 */
export const ResultCard: React.FC<{
  box: Box;
  progress: number;
  head?: string;
  /** Texto de cada máquina; null = ainda não respondeu. */
  line?: (name: string, i: number) => { text: string; ok: boolean } | null;
  /** Máquinas que aparecem (as desligadas não entram). */
  machines?: number[];
}> = ({ box, progress, head, line, machines }) => {
  if (progress <= 0.001) return <Card box={box} />;
  const list = machines ?? PAINEL_PCS.map((_, i) => i);
  const shown = Math.floor(clamp01(progress) * list.length + 0.0001);
  const rowH = 14.5;
  const room = Math.floor((box.h - 30 - (head ? rowH : 0)) / rowH);
  return (
    <Card box={box}>
      <div style={{ position: "absolute", left: 12, top: 14, right: 20, fontSize: 12, lineHeight: `${rowH}px`, fontWeight: 500 }}>
        {head ? <div>{head}</div> : null}
        {list.slice(0, Math.min(shown, room)).map((m, i) => {
          const r = line ? line(PAINEL_PCS[m].name, m) : { text: `${PAINEL_PCS[m].name}: ok`, ok: true };
          if (!r) return null;
          return (
            <div key={m} style={{ color: r.ok ? P.text : P.error, opacity: clamp01((progress * list.length - i) * 2) }}>
              {r.ok ? "✓" : "✗"} {r.text}
            </div>
          );
        })}
      </div>
      {shown > room ? <ScrollThumb x={box.w - 22} y={30} h={Math.max(40, (box.h - 60) * (room / list.length))} /> : null}
    </Card>
  );
};

/** Texto digitado até a fração `p`. */
export const typed = (text: string, p: number) => text.slice(0, Math.round(clamp01(p) * text.length));
