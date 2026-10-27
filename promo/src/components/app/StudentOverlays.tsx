import React from "react";
import { clamp01, mix } from "../../lib/motion";
import { Box } from "./ScreenContent";

/**
 * O que aparece na tela do aluno quando o professor age (src/Midview.Session),
 * no canvas virtual 320×200 do card. A faixa visível no card 16:9 é y 10–190.
 */
export type StudentView = {
  lock: number;
  control: number;
  message: number;
  chat: number;
  toast: number;
  toastText: string;
  website: number;
  blocked: number;
  countdown: number;
  countdownClock: string;
  countdownText: string;
};

const WIN_FONT = '"Segoe UI", system-ui, sans-serif';

const pop = (p: number): React.CSSProperties => ({
  opacity: clamp01(p * 1.6),
  transform: `scale(${mix(0.9, 1, clamp01(p))})`,
});

/** Navegador do aluno com uma página aberta. */
const Browser: React.FC<{ address: string; children: React.ReactNode; style?: React.CSSProperties }> = ({ address, children, style }) => (
  <div
    style={{
      position: "absolute",
      left: 8,
      top: 14,
      width: 304,
      height: 160,
      borderRadius: 6,
      overflow: "hidden",
      background: "#FFFFFF",
      boxShadow: "0 6px 18px rgba(0,0,0,0.45)",
      ...style,
    }}
  >
    <div style={{ position: "absolute", left: 0, top: 0, right: 0, height: 18, background: "#E9EDF2" }}>
      <div
        style={{
          position: "absolute",
          left: 30,
          top: 4,
          width: 200,
          height: 10,
          borderRadius: 5,
          background: "#FFFFFF",
          fontFamily: WIN_FONT,
          fontSize: 6.5,
          lineHeight: "10px",
          paddingLeft: 6,
          color: "#334155",
        }}
      >
        {address}
      </div>
      {[0, 1, 2].map((i) => (
        <Box key={i} x={7 + i * 7} y={7} w={4} h={4} r={2} c="#94A3B8" />
      ))}
    </div>
    <div style={{ position: "absolute", left: 0, top: 18, right: 0, bottom: 0 }}>{children}</div>
  </div>
);

/** O site que o professor abriu em todas as máquinas. */
const SchoolSite: React.FC = () => (
  <>
    <Box x={0} y={0} w={304} h={22} r={0} c="#1E3A8A" />
    <div style={{ position: "absolute", left: 10, top: 6, fontFamily: WIN_FONT, fontSize: 8, fontWeight: 700, color: "#FFFFFF" }}>
      Escola Estadual · Informática
    </div>
    <div style={{ position: "absolute", left: 10, top: 32, fontFamily: WIN_FONT, fontSize: 14, fontWeight: 800, color: "#0F172A", lineHeight: 1.05 }}>
      Atividade 3:
      <br />
      Redes de computadores
    </div>
    {[0, 1, 2].map((i) => (
      <Box key={i} x={10} y={72 + i * 9} w={150 - i * 22} h={4} c="#94A3B8" />
    ))}
    <Box x={196} y={30} w={98} h={72} r={5} c="linear-gradient(135deg, #38BDF8, #6366F1)" />
    <Box x={10} y={110} w={64} h={14} r={7} c="#2563EB" />
  </>
);

/** Página que o navegador mostra num site bloqueado. */
const BlockedSite: React.FC = () => (
  <div style={{ position: "absolute", inset: 0, background: "#F8FAFC", fontFamily: WIN_FONT }}>
    <Box x={40} y={30} w={22} h={26} r={3} c="#94A3B8" />
    <div style={{ position: "absolute", left: 40, top: 64, fontSize: 11, fontWeight: 700, color: "#202124" }}>
      Não é possível acessar esse site
    </div>
    <div style={{ position: "absolute", left: 40, top: 82, fontSize: 7.5, color: "#5F6368" }}>youtube.com recusou a conexão.</div>
    <Box x={40} y={104} w={52} h={14} r={7} c="#1A73E8" />
  </div>
);

/** Janela do Windows no PC do aluno (mensagem, conversa). */
const StudentWindow: React.FC<{ title: string; box: { x: number; y: number; w: number; h: number }; p: number; children: React.ReactNode }> = ({
  title,
  box,
  p,
  children,
}) =>
  p > 0.01 ? (
    <div
      style={{
        position: "absolute",
        left: box.x,
        top: box.y,
        width: box.w,
        height: box.h,
        borderRadius: 5,
        overflow: "hidden",
        background: "#FFFFFF",
        boxShadow: "0 8px 22px rgba(0,0,0,0.55), 0 0 0 0.6px rgba(0,0,0,0.25)",
        fontFamily: WIN_FONT,
        ...pop(p),
      }}
    >
      <div style={{ height: 15, background: "#F3F4F6", fontSize: 7, lineHeight: "15px", paddingLeft: 6, color: "#111827", fontWeight: 600 }}>
        {title}
      </div>
      {children}
    </div>
  ) : null;

export const StudentOverlays: React.FC<{ view: StudentView }> = ({ view }) => {
  const { lock, control, message, chat, toast, toastText, website, blocked, countdown, countdownClock, countdownText } = view;
  return (
    <>
      {website > 0.01 ? (
        <Browser address="escola.edu.br/atividade" style={{ opacity: clamp01(website * 1.4) }}>
          <SchoolSite />
        </Browser>
      ) : null}
      {blocked > 0.01 ? (
        <Browser address="youtube.com" style={{ opacity: clamp01(blocked * 1.4) }}>
          <BlockedSite />
        </Browser>
      ) : null}
      {control > 0.01 ? (
        <div
          style={{
            position: "absolute",
            left: 34,
            top: 14,
            width: 252,
            padding: "4px 0",
            borderRadius: 4,
            background: "rgba(31,42,46,0.96)",
            color: "#FFFFFF",
            fontFamily: WIN_FONT,
            fontSize: 7.5,
            fontWeight: 600,
            textAlign: "center",
            ...pop(control),
          }}
        >
          Este computador está sendo controlado pelo professor
        </div>
      ) : null}
      {message > 0.01 ? (
        <div
          style={{
            position: "absolute",
            left: 60,
            top: 44,
            width: 200,
            height: 96,
            background: "#7A0F14",
            color: "#FFFFFF",
            fontFamily: WIN_FONT,
            boxShadow: "0 8px 22px rgba(0,0,0,0.55)",
            padding: "7px 10px",
            display: "flex",
            flexDirection: "column",
            ...pop(message),
          }}
        >
          <div style={{ fontSize: 7, fontWeight: 600, opacity: 0.85 }}>Mensagem do professor</div>
          <div style={{ marginTop: 5, fontSize: 10, fontWeight: 600, lineHeight: 1.25, flex: 1 }}>A atividade termina em 10 minutos. Salvem seus trabalhos.</div>
          <div style={{ alignSelf: "center", padding: "3px 12px", background: "#FFFFFF", color: "#7A0F14", fontSize: 7, fontWeight: 700 }}>OK, ENTENDI</div>
        </div>
      ) : null}
      <StudentWindow title="Conversa com o professor" box={{ x: 170, y: 44, w: 142, h: 120 }} p={chat}>
        <div
          style={{
            margin: "8px 7px 0",
            padding: "5px 6px",
            borderRadius: 6,
            background: "#E0F2FE",
            fontSize: 7.5,
            lineHeight: 1.25,
            color: "#0C4A6E",
            width: 96,
          }}
        >
          Professor: Dúvidas no exercício 3?
        </div>
        <div style={{ position: "absolute", left: 7, right: 7, bottom: 7, height: 14, borderRadius: 3, border: "0.6px solid #CBD5E1" }} />
      </StudentWindow>
      {toast > 0.01 ? (
        <div
          style={{
            position: "absolute",
            left: 144,
            top: 120,
            width: 168,
            padding: "5px 7px",
            borderRadius: 5,
            background: "rgba(31,41,55,0.97)",
            boxShadow: "0 6px 16px rgba(0,0,0,0.5)",
            fontFamily: WIN_FONT,
            opacity: clamp01(toast * 1.6),
            transform: `translateX(${(1 - clamp01(toast)) * 24}px)`,
          }}
        >
          <div style={{ fontSize: 6.5, color: "#9CA3AF", fontWeight: 600 }}>Midview</div>
          <div style={{ fontSize: 7.5, lineHeight: 1.25, color: "#F9FAFB" }}>{toastText}</div>
        </div>
      ) : null}
      {countdown > 0.01 ? (
        <div
          style={{
            position: "absolute",
            left: 26,
            top: 14,
            width: 268,
            height: 36,
            borderRadius: 4,
            background: "#8B0000",
            color: "#FFFFFF",
            fontFamily: WIN_FONT,
            display: "flex",
            alignItems: "center",
            gap: 8,
            padding: "0 9px",
            boxShadow: "0 6px 16px rgba(0,0,0,0.5)",
            ...pop(countdown),
          }}
        >
          <span style={{ fontSize: 17, fontWeight: 700, minWidth: 38 }}>{countdownClock}</span>
          <span style={{ fontSize: 7.5, fontWeight: 700, lineHeight: 1.25 }}>{countdownText}</span>
        </div>
      ) : null}
      {lock > 0.01 ? (
        <div style={{ position: "absolute", inset: 0, background: "rgb(64,64,64)", opacity: clamp01(lock * 1.3) }}>
          <svg
            width={60}
            height={68}
            viewBox="0 0 60 68"
            style={{ position: "absolute", left: 130, top: 60, transform: `translateY(${(1 - clamp01(lock)) * 6}px)` }}
          >
            <path d="M16 30 V20 a14 14 0 0 1 28 0 V30" fill="none" stroke="rgb(220,220,220)" strokeWidth={6} />
            <rect x={8} y={28} width={44} height={34} rx={5} fill="rgb(220,220,220)" />
            <circle cx={30} cy={42} r={4} fill="rgb(64,64,64)" />
            <rect x={28} y={44} width={4} height={9} rx={2} fill="rgb(64,64,64)" />
          </svg>
        </div>
      ) : null}
    </>
  );
};
