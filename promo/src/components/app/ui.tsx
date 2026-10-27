import React from "react";
import { clamp01, mix } from "../../lib/motion";
import { MidviewLogo } from "../MidviewLogo";
import { useAppTheme } from "./appTheme";
import type { Box } from "./geometry";
import { GLYPH, Glyph } from "./Glyph";

/** Peças das janelas do Midview (WPF): moldura com título, texto, botão, campo e caixas de seleção. */

/* ---------- peças comuns ---------- */

export const Frame: React.FC<{ title: string; box: Box; appear: number; closeHover?: number; children: React.ReactNode }> = ({
  title,
  box,
  appear,
  closeHover = 0,
  children,
}) => {
  const theme = useAppTheme();
  const d = theme.dialog;
  if (appear <= 0.01) return null;
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
        background: d.bg,
        border: `1px solid ${d.line}`,
        boxShadow: "0 28px 70px -14px rgba(0,0,0,0.6), 0 0 0 1px rgba(0,0,0,0.05)",
        opacity: clamp01(appear * 1.4),
        transform: `scale(${mix(0.95, 1, clamp01(appear))})`,
        fontFamily: theme.font,
        color: d.text,
      }}
    >
      <div
        style={{
          position: "relative",
          height: 32,
          background: d.title,
          borderBottom: `1px solid ${d.line}`,
          display: "flex",
          alignItems: "center",
          gap: 8,
          paddingLeft: 12,
          fontSize: 12,
        }}
      >
        <MidviewLogo size={15} />
        {title}
        <div
          style={{
            position: "absolute",
            right: 0,
            top: 0,
            width: 46,
            height: 32,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            background: `rgba(196,43,28,${clamp01(closeHover)})`,
            color: closeHover > 0.5 ? "#FFFFFF" : d.text,
          }}
        >
          <Glyph code={GLYPH.close} size={10} />
        </div>
      </div>
      {children}
    </div>
  );
};

export const Text: React.FC<{ x: number; y: number; w?: number; size?: number; weight?: number; muted?: boolean; children: React.ReactNode }> = ({
  x,
  y,
  w,
  size = 12.5,
  weight = 400,
  muted = false,
  children,
}) => {
  const theme = useAppTheme();
  return (
    <div
      style={{
        position: "absolute",
        left: x,
        top: y,
        width: w,
        fontSize: size,
        fontWeight: weight,
        lineHeight: 1.35,
        color: muted ? theme.dialog.muted : theme.dialog.text,
      }}
    >
      {children}
    </div>
  );
};

export const Btn: React.FC<{ box: Box; hover?: number; press?: number; strong?: boolean; children: React.ReactNode }> = ({
  box,
  hover = 0,
  press = 0,
  strong = false,
  children,
}) => {
  const theme = useAppTheme();
  const b = theme.button;
  return (
    <div
      style={{
        position: "absolute",
        left: box.x,
        top: box.y,
        width: box.w,
        height: box.h,
        borderRadius: 4,
        border: `1px solid ${hover > 0.5 ? theme.card.hover : b.line}`,
        background: b.bg,
        overflow: "hidden",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        fontSize: 12.5,
        fontWeight: strong ? 600 : 400,
        color: b.text,
        transform: `scale(${1 - 0.04 * clamp01(press)})`,
      }}
    >
      <div style={{ position: "absolute", inset: 0, background: b.hover, opacity: clamp01(hover) }} />
      <div style={{ position: "absolute", inset: 0, background: b.pressed, opacity: clamp01(press) }} />
      <span style={{ position: "relative" }}>{children}</span>
    </div>
  );
};

export const Field: React.FC<{ box: Box; text: string; caret?: boolean; multiline?: boolean; disabled?: boolean }> = ({
  box,
  text,
  caret = false,
  multiline = false,
  disabled = false,
}) => {
  const theme = useAppTheme();
  return (
    <div
      style={{
        position: "absolute",
        left: box.x,
        top: box.y,
        width: box.w,
        height: box.h,
        borderRadius: 4,
        border: `1px solid ${caret ? theme.selected.line : theme.dialog.line}`,
        background: disabled ? theme.dialog.bg : theme.dialog.input,
        padding: multiline ? "8px 10px" : "0 10px",
        display: "flex",
        alignItems: multiline ? "flex-start" : "center",
        fontSize: 13,
        lineHeight: 1.4,
        color: theme.dialog.text,
        whiteSpace: multiline ? "normal" : "nowrap",
        overflow: "hidden",
      }}
    >
      <span>
        {text}
        {caret ? <span style={{ borderRight: `1.5px solid ${theme.dialog.text}`, marginLeft: 1 }}>&#8203;</span> : null}
      </span>
    </div>
  );
};

export const Check: React.FC<{ x: number; y: number; on: number; w?: number; radio?: boolean; children: React.ReactNode }> = ({
  x,
  y,
  on,
  w,
  radio = false,
  children,
}) => {
  const theme = useAppTheme();
  const c = clamp01(on);
  return (
    <div style={{ position: "absolute", left: x, top: y, width: w, display: "flex", gap: 8, fontSize: 12.5, lineHeight: 1.35 }}>
      <span
        style={{
          flex: "0 0 auto",
          marginTop: 1,
          width: 15,
          height: 15,
          borderRadius: radio ? 8 : 3,
          border: `1px solid ${c > 0.5 ? theme.dialog.check : theme.dialog.muted}`,
          background: theme.dialog.input,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
        }}
      >
        {radio ? (
          <span style={{ width: 7, height: 7, borderRadius: 4, background: theme.dialog.check, opacity: c }} />
        ) : (
          <Glyph code={GLYPH.check} size={11} color={theme.dialog.check} style={{ opacity: c }} />
        )}
      </span>
      <span>{children}</span>
    </div>
  );
};

export const typed = (text: string, p: number) => text.slice(0, Math.round(clamp01(p) * text.length));

