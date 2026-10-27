import React from "react";
import { useAppTheme } from "../components/app/appTheme";
import { PCS } from "../components/app/pcs";
import { Box as Shape, ScreenContent } from "../components/app/ScreenContent";
import { clamp01, mix } from "../lib/motion";

/**
 * A tela de uma máquina de aluno em tamanho grande, com o que o Midview mostra
 * nela (src/Midview.Session e src/Midview.Agent), no canvas de 320×200 do card.
 */
export type StudentScreenState = {
  pc: number;
  /** 0 = programa fechado, só a área de trabalho. */
  apps?: number;
  /** Realce da pasta "Arquivos do professor". */
  inbox?: number;
  /** Pasta "Pasta da turma – <turma>" na Área de Trabalho (0–1). */
  classFolder?: number;
  turma?: string;
  lock?: number;
  control?: number;
  /** Caixa vermelha "Mensagem do professor" (recado e avisos de arquivos). */
  redBox?: { p: number; lines: string[]; press?: number };
  chat?: { p: number; lines: { who: "Professor" | "Você"; text: string; p: number }[] };
  countdown?: { p: number; clock: string; text: string };
  pairing?: { p: number; code: string };
  classToast?: number;
  msgbox?: { p: number; text: string };
  browser?: { p: number; url: string; blocked?: boolean };
  /** Ponteiro do próprio aluno (some com teclado e mouse travados). */
  pointer?: { x: number; y: number; o: number } | null;
};

const WIN_FONT = '"Segoe UI", system-ui, sans-serif';

const pop = (p: number): React.CSSProperties => ({
  opacity: clamp01(p * 1.6),
  transform: `scale(${mix(0.92, 1, clamp01(p))})`,
});

const DesktopFolder: React.FC<{ label: string; y: number; p: number }> = ({ label, y, p }) => (
  <div style={{ position: "absolute", left: 10, top: y, width: 58, textAlign: "center", ...pop(p) }}>
    <div style={{ margin: "0 auto", width: 26, height: 20, borderRadius: 3, background: "linear-gradient(180deg, #FDE68A, #F59E0B)" }} />
    <div style={{ marginTop: 3, fontSize: 7, lineHeight: 1.15, color: "#F8FAFC", textShadow: "0 1px 2px rgba(0,0,0,0.9)" }}>{label}</div>
  </div>
);

/** A caixa vermelha do professor (WinFormsMessageDisplay): título, texto grande e "OK, ENTENDI". */
const RedBox: React.FC<{ p: number; lines: string[]; press: number }> = ({ p, lines, press }) => (
  <div
    style={{
      position: "absolute",
      left: 94,
      top: 54,
      width: 134,
      height: 74,
      background: "#7A0F14",
      color: "#FFFFFF",
      fontFamily: WIN_FONT,
      boxShadow: "0 8px 22px rgba(0,0,0,0.55)",
      padding: "6px 8px",
      display: "flex",
      flexDirection: "column",
      ...pop(p),
    }}
  >
    <div style={{ fontSize: 5.5, fontWeight: 600, opacity: 0.85 }}>Mensagem do professor</div>
    <div style={{ marginTop: 4, fontSize: 7.5, fontWeight: 600, lineHeight: 1.25, flex: 1 }}>
      {lines.map((l) => (
        <div key={l} style={{ marginBottom: 2 }}>
          {l}
        </div>
      ))}
    </div>
    <div
      style={{
        alignSelf: "center",
        padding: "2px 9px",
        background: "#FFFFFF",
        color: "#7A0F14",
        fontSize: 5.5,
        fontWeight: 700,
        transform: `scale(${1 - 0.06 * clamp01(press)})`,
      }}
    >
      OK, ENTENDI
    </div>
  </div>
);

const ChatWindow: React.FC<{ s: NonNullable<StudentScreenState["chat"]> }> = ({ s }) => (
  <div
    style={{
      position: "absolute",
      left: 196,
      top: 46,
      width: 118,
      height: 104,
      background: "#FFFFFF",
      fontFamily: WIN_FONT,
      boxShadow: "0 8px 20px rgba(0,0,0,0.5), 0 0 0 0.5px rgba(0,0,0,0.3)",
      ...pop(s.p),
    }}
  >
    <div style={{ height: 13, background: "#F3F4F6", fontSize: 6.5, lineHeight: "13px", paddingLeft: 5, color: "#111827" }}>Conversa com o professor</div>
    <div style={{ padding: "4px 5px", fontSize: 7, lineHeight: 1.3, color: "#111827" }}>
      {s.lines.map((l) =>
        l.p > 0.01 ? (
          <div key={l.text} style={{ opacity: clamp01(l.p * 1.5), marginBottom: 1.5 }}>
            <b>{l.who}:</b> {l.text}
          </div>
        ) : null,
      )}
    </div>
    <div style={{ position: "absolute", left: 5, right: 30, bottom: 5, height: 11, border: "0.5px solid #CBD5E1" }} />
    <div style={{ position: "absolute", right: 5, bottom: 5, width: 22, height: 11, background: "#E5E7EB", fontSize: 5.5, lineHeight: "11px", textAlign: "center" }}>
      Enviar
    </div>
  </div>
);

/** Faixa da contagem (CountdownBanner): vermelho escuro no alto, relógio grande e o aviso. */
const Countdown: React.FC<{ s: NonNullable<StudentScreenState["countdown"]> }> = ({ s }) => (
  <div
    style={{
      position: "absolute",
      left: 40,
      top: 4,
      width: 240,
      height: 28,
      background: "#7A0F14",
      color: "#FFFFFF",
      fontFamily: WIN_FONT,
      display: "flex",
      alignItems: "center",
      gap: 7,
      padding: "0 8px",
      boxShadow: "0 5px 14px rgba(0,0,0,0.5)",
      ...pop(s.p),
    }}
  >
    <span style={{ fontSize: 15, fontWeight: 700, minWidth: 34 }}>{s.clock}</span>
    <span style={{ fontSize: 7, fontWeight: 700, lineHeight: 1.2 }}>{s.text}</span>
  </div>
);

/** Janela do código de pareamento (PairingCodeWindow): cinza escura, sem nada para clicar. */
const Pairing: React.FC<{ s: NonNullable<StudentScreenState["pairing"]> }> = ({ s }) => (
  <div
    style={{
      position: "absolute",
      left: 108,
      top: 50,
      width: 104,
      height: 62,
      background: "#2B2B2B",
      color: "#FFFFFF",
      fontFamily: WIN_FONT,
      textAlign: "center",
      padding: "5px 5px",
      boxShadow: "0 8px 22px rgba(0,0,0,0.6)",
      ...pop(s.p),
    }}
  >
    <div style={{ fontSize: 5.6, fontWeight: 700 }}>Pareamento com o professor</div>
    <div style={{ marginTop: 2, fontSize: 4.2, color: "#D1D5DB" }}>O computador PROF-1 está conectando esta máquina ao Midview.</div>
    <div style={{ marginTop: 3, fontSize: 15, fontWeight: 700, letterSpacing: 1 }}>{s.code}</div>
    <div style={{ marginTop: 2, fontSize: 4.2, color: "#D1D5DB" }}>O professor confere se o mesmo código aparece na tela dele.</div>
  </div>
);

const ClassToast: React.FC<{ p: number; turma: string }> = ({ p, turma }) => (
  <div
    style={{
      position: "absolute",
      right: 6,
      bottom: 30,
      width: 92,
      padding: "4px 5px",
      background: "#1D4ED8",
      color: "#FFFFFF",
      fontFamily: WIN_FONT,
      boxShadow: "0 6px 16px rgba(0,0,0,0.5)",
      opacity: clamp01(p * 1.6),
      transform: `translateY(${(1 - clamp01(p)) * 10}px)`,
    }}
  >
    <div style={{ fontSize: 5.6, fontWeight: 700 }}>Aula do {turma}</div>
    <div style={{ marginTop: 2, fontSize: 4.3, lineHeight: 1.25 }}>
      O que você baixar ou salvar na Pasta da turma – {turma} (na Área de Trabalho) aparece em todos os computadores da sala.
    </div>
    <div style={{ position: "absolute", right: 4, top: 2, fontSize: 5 }}>✕</div>
  </div>
);

const MsgBox: React.FC<{ s: NonNullable<StudentScreenState["msgbox"]> }> = ({ s }) => (
  <div
    style={{
      position: "absolute",
      left: 85,
      top: 58,
      width: 150,
      height: 64,
      background: "#FFFFFF",
      fontFamily: WIN_FONT,
      boxShadow: "0 8px 20px rgba(0,0,0,0.5), 0 0 0 0.5px rgba(0,0,0,0.3)",
      ...pop(s.p),
    }}
  >
    <div style={{ height: 13, fontSize: 6.5, lineHeight: "13px", paddingLeft: 5, color: "#111827" }}>Midview</div>
    <div style={{ padding: "5px 7px", fontSize: 7.5, lineHeight: 1.25, color: "#111827" }}>{s.text}</div>
    <div style={{ position: "absolute", right: 6, bottom: 6, width: 28, height: 12, background: "#E5E7EB", border: "0.5px solid #2563EB", fontSize: 6.5, lineHeight: "12px", textAlign: "center" }}>
      OK
    </div>
  </div>
);

const Browser: React.FC<{ s: NonNullable<StudentScreenState["browser"]> }> = ({ s }) => (
  <div
    style={{
      position: "absolute",
      left: 8,
      top: 8,
      width: 304,
      height: 166,
      background: "#FFFFFF",
      overflow: "hidden",
      boxShadow: "0 6px 18px rgba(0,0,0,0.45)",
      fontFamily: WIN_FONT,
      opacity: clamp01(s.p * 1.5),
    }}
  >
    <div style={{ position: "absolute", left: 0, top: 0, right: 0, height: 16, background: "#E9EDF2" }}>
      <div style={{ position: "absolute", left: 28, top: 3, width: 200, height: 10, borderRadius: 5, background: "#FFFFFF", fontSize: 6, lineHeight: "10px", paddingLeft: 6, color: "#334155" }}>
        {s.url}
      </div>
    </div>
    {s.blocked ? (
      <div style={{ position: "absolute", left: 40, top: 44 }}>
        <Shape x={0} y={0} w={20} h={24} r={3} c="#94A3B8" />
        <div style={{ position: "absolute", left: 0, top: 32, width: 220, fontSize: 10, fontWeight: 700, color: "#202124" }}>Não é possível acessar esse site</div>
        <div style={{ position: "absolute", left: 0, top: 48, width: 220, fontSize: 6.5, color: "#5F6368" }}>{s.url} recusou a conexão.</div>
      </div>
    ) : (
      <>
        <Shape x={0} y={16} w={304} h={22} r={0} c="#1E3A8A" />
        <div style={{ position: "absolute", left: 10, top: 23, fontSize: 7.5, fontWeight: 700, color: "#FFFFFF" }}>Escola Estadual · Informática</div>
        <div style={{ position: "absolute", left: 10, top: 48, fontSize: 13, fontWeight: 800, color: "#0F172A", lineHeight: 1.1 }}>
          Atividade 3:
          <br />
          Redes de computadores
        </div>
        <Shape x={196} y={46} w={98} h={72} r={5} c="linear-gradient(135deg, #38BDF8, #6366F1)" />
      </>
    )}
  </div>
);

const Padlock: React.FC<{ p: number }> = ({ p }) => (
  <div style={{ position: "absolute", inset: 0, background: "rgb(64,64,64)", opacity: clamp01(p * 1.3) }}>
    <svg width={60} height={68} viewBox="0 0 60 68" style={{ position: "absolute", left: 130, top: 60 }}>
      <path d="M16 30 V20 a14 14 0 0 1 28 0 V30" fill="none" stroke="rgb(220,220,220)" strokeWidth={6} />
      <rect x={8} y={28} width={44} height={34} rx={5} fill="rgb(220,220,220)" />
      <circle cx={30} cy={42} r={4} fill="rgb(64,64,64)" />
      <rect x={28} y={44} width={4} height={9} rx={2} fill="rgb(64,64,64)" />
    </svg>
  </div>
);

/** O monitor do aluno, com a etiqueta "Tela do aluno · PC-03" acima. */
export const StudentScreen: React.FC<{ s: StudentScreenState; frame: number; width: number }> = ({ s, frame, width }) => {
  const theme = useAppTheme();
  const pc = PCS[s.pc];
  const k = width / 320;
  const height = Math.round(190 * k);
  return (
    <div style={{ width }}>
      <div
        style={{
          marginBottom: 14,
          display: "flex",
          alignItems: "center",
          gap: 10,
          fontFamily: theme.font,
          fontSize: 22,
          fontWeight: 600,
          color: "#E2E8F0",
        }}
      >
        <span style={{ width: 10, height: 10, borderRadius: 5, background: "#22D3EE", boxShadow: "0 0 12px #22D3EE" }} />
        Tela do aluno · {pc.name}
      </div>
      <div
        style={{
          width,
          height,
          borderRadius: 14,
          padding: 0,
          overflow: "hidden",
          position: "relative",
          background: "#000",
          boxShadow: "0 40px 90px -30px rgba(0,0,0,0.9), 0 0 0 10px #0B0F18, 0 0 0 11px rgba(255,255,255,0.12)",
        }}
      >
        <div style={{ position: "absolute", left: 0, top: 0, width: 320, height: 200, transformOrigin: "0 0", transform: `scale(${k})` }}>
          <ScreenContent kind={pc.kind} frame={frame} variant={pc.variant} wallpaper={s.pc} apps={s.apps ?? 1} inbox={s.inbox ?? 0} />
          {(s.classFolder ?? 0) > 0.01 ? <DesktopFolder label={`Pasta da turma – ${s.turma ?? "3º A"}`} y={114} p={s.classFolder ?? 0} /> : null}
          {s.browser && s.browser.p > 0.01 ? <Browser s={s.browser} /> : null}
          {s.chat && s.chat.p > 0.01 ? <ChatWindow s={s.chat} /> : null}
          {s.msgbox && s.msgbox.p > 0.01 ? <MsgBox s={s.msgbox} /> : null}
          {s.redBox && s.redBox.p > 0.01 ? <RedBox p={s.redBox.p} lines={s.redBox.lines} press={s.redBox.press ?? 0} /> : null}
          {(s.classToast ?? 0) > 0.01 ? <ClassToast p={s.classToast ?? 0} turma={s.turma ?? "3º A"} /> : null}
          {s.pairing && s.pairing.p > 0.01 ? <Pairing s={s.pairing} /> : null}
          {(s.lock ?? 0) > 0.01 ? <Padlock p={s.lock ?? 0} /> : null}
          {s.countdown && s.countdown.p > 0.01 && (s.lock ?? 0) < 0.5 ? <Countdown s={s.countdown} /> : null}
          {(s.control ?? 0) > 0.01 ? (
            <div
              style={{
                position: "absolute",
                left: 70,
                top: 3,
                width: 180,
                padding: "2.5px 0",
                background: "rgba(196,30,30,0.9)",
                color: "#FFFFFF",
                fontFamily: WIN_FONT,
                fontSize: 6,
                fontWeight: 600,
                textAlign: "center",
                ...pop(s.control ?? 0),
              }}
            >
              Este computador está sendo controlado pelo professor
            </div>
          ) : null}
          {s.pointer && s.pointer.o > 0.01 ? (
            <svg width={8} height={10} viewBox="0 0 30 36" style={{ position: "absolute", left: s.pointer.x, top: s.pointer.y, opacity: s.pointer.o }}>
              <path d="M3 2 L3 27.5 L9.6 21.6 L14 31.8 L18.6 29.9 L14.3 19.9 L23 19.9 Z" fill="#FFFFFF" stroke="#0B1020" strokeWidth={2} />
            </svg>
          ) : null}
        </div>
      </div>
    </div>
  );
};
