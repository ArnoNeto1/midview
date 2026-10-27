import React from "react";
import { Img, staticFile } from "remotion";
import { Cursor } from "../../components/Cursor";
import type { Point } from "../../components/app/geometry";
import { MidviewLogo } from "../../components/MidviewLogo";
import { clamp01, mix } from "../../lib/motion";

/**
 * O MidviewSetup.exe (installer/Midview.iss, Inno Setup em português) sobre a
 * Área de Trabalho do Windows: as páginas do assistente com os textos reais.
 */
const WIN_FONT = '"Segoe UI", system-ui, sans-serif';
export const WIZARD = { w: 600, h: 444, scale: 1.55 };
/** Onde o assistente fica na tela de 1920×1080. */
export const WIZARD_AT = { x: (1920 - WIZARD.w * WIZARD.scale) / 2, y: 236 };
/** Botões do assistente, relativos a ele. */
export const WIZARD_CTRL = {
  next: { x: 404, y: 404, w: 88, h: 26 },
  back: { x: 310, y: 404, w: 88, h: 26 },
  cancel: { x: 500, y: 404, w: 88, h: 26 },
  accept: { x: 24, y: 334, w: 160, h: 18 },
  professor: { x: 64, y: 158, w: 380, h: 18 },
};
/** Ponto do assistente em coordenadas da tela. */
export const wizardPoint = (b: { x: number; y: number; w: number; h: number }): Point => ({
  x: WIZARD_AT.x + (b.x + b.w / 2) * WIZARD.scale,
  y: WIZARD_AT.y + (b.y + b.h / 2) * WIZARD.scale,
});

export type WizardPage = "welcome" | "license" | "tipo" | "installing" | "finished";

const Button: React.FC<{ box: { x: number; y: number; w: number; h: number }; label: string; hover?: number; press?: number; primary?: boolean; disabled?: boolean }> = ({
  box,
  label,
  hover = 0,
  press = 0,
  primary,
  disabled,
}) => (
  <div
    style={{
      position: "absolute",
      left: box.x,
      top: box.y,
      width: box.w,
      height: box.h,
      borderRadius: 4,
      border: `1px solid ${primary ? "#005FB8" : "#D1D1D1"}`,
      background: primary ? (disabled ? "#9CB8D6" : mix(0, 1, hover) > 0.5 ? "#196EBF" : "#005FB8") : hover > 0.5 ? "#F3F3F3" : "#FBFBFB",
      color: primary ? "#FFFFFF" : disabled ? "#9E9E9E" : "#1B1B1B",
      fontSize: 11.5,
      display: "flex",
      alignItems: "center",
      justifyContent: "center",
      transform: `scale(${1 - 0.04 * clamp01(press)})`,
    }}
  >
    {label}
  </div>
);

const Radio: React.FC<{ x: number; y: number; on: boolean; children: React.ReactNode }> = ({ x, y, on, children }) => (
  <div style={{ position: "absolute", left: x, top: y, display: "flex", alignItems: "center", gap: 8, fontSize: 11.5 }}>
    <span style={{ width: 14, height: 14, borderRadius: 7, border: `1px solid ${on ? "#005FB8" : "#8A8A8A"}`, background: on ? "#005FB8" : "#FFFFFF", display: "flex", alignItems: "center", justifyContent: "center" }}>
      {on ? <span style={{ width: 6, height: 6, borderRadius: 3, background: "#FFFFFF" }} /> : null}
    </span>
    {children}
  </div>
);

const Header: React.FC<{ title: string; text: string }> = ({ title, text }) => (
  <div style={{ position: "absolute", left: 0, top: 0, width: WIZARD.w, height: 70, background: "#FFFFFF", borderBottom: "1px solid #E5E5E5" }}>
    <div style={{ position: "absolute", left: 24, top: 14, fontSize: 13, fontWeight: 700 }}>{title}</div>
    <div style={{ position: "absolute", left: 40, top: 34, width: 440, fontSize: 11.5, color: "#3B3B3B" }}>{text}</div>
    <Img src={staticFile("tutorial/instalador-pequena.png")} style={{ position: "absolute", right: 12, top: 8, width: 55, height: 55 }} />
  </div>
);

const LICENSE_START =
  "LICENÇA DE USO DO MIDVIEW\n\nVersão 1.0, de 20 de outubro de 2026.\n\nCopyright © 2026 MidView. Todos os direitos reservados.\n\nEsta licença é um acordo entre MidView (\"Titular\") e a escola, a rede de ensino ou a pessoa que instala ou usa o Midview (\"Licenciado\"). Ao aceitar esta licença no instalador, ou ao instalar, copiar ou usar o Midview, o Licenciado declara que leu e aceita os termos abaixo.\n\n1. DEFINIÇÕES\n\n1.1. \"Midview\" ou \"Software\": o programa Midview em todas as suas partes...";

export type WizardState = {
  page: WizardPage;
  appear: number;
  accepted?: boolean;
  tipo?: "professor" | "aluno" | null;
  progress?: number;
  nextHover?: number;
  nextPress?: number;
};

const Page: React.FC<{ s: WizardState }> = ({ s }) => {
  if (s.page === "welcome" || s.page === "finished") {
    const welcome = s.page === "welcome";
    return (
      <div style={{ position: "absolute", left: 0, top: 0, width: WIZARD.w, height: 392, background: "#FFFFFF" }}>
        <Img src={staticFile("tutorial/instalador-grande.png")} style={{ position: "absolute", left: 0, top: 0, width: 164, height: 392, objectFit: "cover" }} />
        <div style={{ position: "absolute", left: 184, top: 22, width: 396, fontSize: 17, fontWeight: 700, lineHeight: 1.25 }}>
          {welcome ? "Bem-vindo ao Assistente do Instalador do Midview" : "Completando o Assistente do Instalador do Midview"}
        </div>
        <div style={{ position: "absolute", left: 184, top: 100, width: 396, fontSize: 11.5, lineHeight: 1.45, color: "#1B1B1B" }}>
          {welcome ? (
            <>
              Isto instalará o Midview 0.0.25 no seu computador.
              <br />
              <br />É recomendado que você feche todos os outros aplicativos antes de continuar.
            </>
          ) : (
            <>
              O Midview Professor está instalado: abra “Midview - Sala de aula” na Área de Trabalho.
              <br />
              <br />
              Para trazer as máquinas, instale o Midview Aluno nelas e clique em “Máquinas da sala” &gt; “Procurar na rede”.
            </>
          )}
        </div>
      </div>
    );
  }
  if (s.page === "license") {
    return (
      <>
        <Header title="Acordo de Licença" text="Por favor leia as seguintes informações importantes antes de continuar." />
        <div style={{ position: "absolute", left: 24, top: 82, width: 552, fontSize: 11.5 }}>
          Por favor leia o seguinte Acordo de Licença. Você deve aceitar os termos deste acordo antes de continuar com a instalação.
        </div>
        <div style={{ position: "absolute", left: 24, top: 118, width: 552, height: 204, border: "1px solid #CFCFCF", background: "#FFFFFF", padding: "8px 10px", fontSize: 10.5, lineHeight: 1.4, whiteSpace: "pre-wrap", overflow: "hidden" }}>
          {LICENSE_START}
        </div>
        <Radio x={24} y={334} on={Boolean(s.accepted)}>
          Eu aceito o acordo
        </Radio>
        <Radio x={24} y={358} on={!s.accepted}>
          Eu não aceito o acordo
        </Radio>
      </>
    );
  }
  if (s.page === "tipo") {
    return (
      <>
        <Header title="Tipo de instalação" text="Este computador é o do professor ou uma máquina de aluno?" />
        <div style={{ position: "absolute", left: 40, top: 100, fontSize: 11.5 }}>Escolha uma opção e clique em Avançar.</div>
        <div style={{ position: "absolute", left: 40, top: 136, width: 520, height: 80, border: "1px solid #CFCFCF", background: "#FFFFFF" }} />
        <Radio x={56} y={150} on={s.tipo === "professor"}>
          Professor: o computador de onde você controla a sala
        </Radio>
        <Radio x={56} y={182} on={s.tipo === "aluno"}>
          Aluno: uma máquina do laboratório
        </Radio>
      </>
    );
  }
  return (
    <>
      <Header title="Instalando" text="Por favor espere enquanto o instalador instala o Midview no seu computador." />
      <div style={{ position: "absolute", left: 40, top: 110, fontSize: 11.5 }}>Extraindo arquivos...</div>
      <div style={{ position: "absolute", left: 40, top: 132, width: 520, fontSize: 10.5, color: "#5B5B5B" }}>C:\Program Files\Midview Professor\Midview.Master.exe</div>
      <div style={{ position: "absolute", left: 40, top: 158, width: 520, height: 14, background: "#E6E6E6", borderRadius: 7, overflow: "hidden" }}>
        <div style={{ width: `${clamp01(s.progress ?? 0) * 100}%`, height: "100%", background: "#005FB8" }} />
      </div>
    </>
  );
};

/** O assistente inteiro (janela, página e botões). */
export const Wizard: React.FC<{ s: WizardState }> = ({ s }) => {
  const nextLabel = s.page === "finished" ? "Concluir" : s.page === "tipo" ? "Avançar >" : "Avançar >";
  const nextDisabled = (s.page === "license" && !s.accepted) || (s.page === "tipo" && !s.tipo) || s.page === "installing";
  return (
    <div
      style={{
        position: "absolute",
        left: WIZARD_AT.x,
        top: WIZARD_AT.y,
        width: WIZARD.w,
        height: WIZARD.h + 30,
        transformOrigin: "0 0",
        transform: `scale(${WIZARD.scale * mix(0.94, 1, clamp01(s.appear))})`,
        opacity: clamp01(s.appear * 1.5),
        fontFamily: WIN_FONT,
        color: "#1B1B1B",
        boxShadow: "0 40px 90px -20px rgba(0,0,0,0.7), 0 0 0 1px rgba(0,0,0,0.2)",
        borderRadius: 8,
        overflow: "hidden",
        background: "#F3F3F3",
      }}
    >
      <div style={{ height: 30, background: "#FFFFFF", display: "flex", alignItems: "center", gap: 8, paddingLeft: 10, fontSize: 11.5, borderBottom: "1px solid #E5E5E5" }}>
        <MidviewLogo size={14} />
        Midview - Instalador
      </div>
      <div style={{ position: "relative", width: WIZARD.w, height: WIZARD.h }}>
        <Page s={s} />
        <div style={{ position: "absolute", left: 0, top: 392, width: WIZARD.w, height: 1, background: "#E0E0E0" }} />
        {s.page !== "finished" && s.page !== "welcome" ? <Button box={WIZARD_CTRL.back} label="< Voltar" /> : null}
        <Button box={WIZARD_CTRL.next} label={nextLabel} hover={s.nextHover} press={s.nextPress} primary disabled={nextDisabled} />
        {s.page !== "finished" ? <Button box={WIZARD_CTRL.cancel} label="Cancelar" /> : null}
      </div>
    </div>
  );
};

/** Área de Trabalho do Windows atrás do instalador, com o atalho do professor. */
export const WindowsDesktop: React.FC<{ shortcut: number; glow: number }> = ({ shortcut, glow }) => (
  <div style={{ position: "absolute", inset: 0, background: "radial-gradient(ellipse at 30% 30%, #1E3A8A 0%, #0B1A3B 55%, #050B1A 100%)" }}>
    <div style={{ position: "absolute", left: 40, top: 220, width: 120, textAlign: "center", opacity: clamp01(shortcut * 1.5), transform: `scale(${mix(0.8, 1, clamp01(shortcut))})`, fontFamily: WIN_FONT }}>
      <div
        style={{
          margin: "0 auto",
          width: 72,
          height: 72,
          borderRadius: 16,
          background: "rgba(255,255,255,0.08)",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          boxShadow: glow > 0.01 ? `0 0 ${40 * glow}px ${12 * glow}px rgba(34,211,238,${0.5 * glow})` : undefined,
        }}
      >
        <MidviewLogo size={54} />
      </div>
      <div style={{ marginTop: 8, fontSize: 17, color: "#FFFFFF", textShadow: "0 1px 3px rgba(0,0,0,0.9)" }}>Midview - Sala de aula</div>
    </div>
    <div style={{ position: "absolute", left: 0, right: 0, bottom: 0, height: 56, background: "rgba(20,24,36,0.92)", borderTop: "1px solid rgba(255,255,255,0.08)" }} />
  </div>
);

/** Instalação silenciosa: o comando e as máquinas recebendo o Midview Aluno. */
export const SilentInstall: React.FC<{ typed: number; done: number[] }> = ({ typed, done }) => {
  const cmd = "MidviewSetup.exe /VERYSILENT /SUPPRESSMSGBOXES /TIPO=aluno";
  return (
    <div style={{ position: "absolute", inset: 0, fontFamily: WIN_FONT }}>
      <div
        style={{
          position: "absolute",
          left: 260,
          top: 300,
          width: 1400,
          height: 120,
          borderRadius: 12,
          background: "#0C0C0C",
          border: "1px solid rgba(255,255,255,0.12)",
          boxShadow: "0 30px 70px -20px rgba(0,0,0,0.8)",
          padding: "26px 30px",
          fontFamily: "Consolas, monospace",
          fontSize: 34,
          color: "#E5E7EB",
        }}
      >
        <span style={{ color: "#22D3EE" }}>PS C:\&gt; </span>
        {cmd.slice(0, Math.round(clamp01(typed) * cmd.length))}
        <span style={{ opacity: typed < 1 ? 1 : 0, borderRight: "3px solid #E5E7EB", marginLeft: 2 }}>&#8203;</span>
      </div>
      <div style={{ position: "absolute", left: 260, top: 480, width: 1400, display: "grid", gridTemplateColumns: "repeat(6, 1fr)", gap: 22 }}>
        {done.map((p, i) => (
          <div
            key={i}
            style={{
              height: 120,
              borderRadius: 12,
              border: `2px solid ${p > 0.5 ? "#22D3EE" : "rgba(255,255,255,0.12)"}`,
              background: p > 0.5 ? "rgba(34,211,238,0.12)" : "rgba(255,255,255,0.04)",
              display: "flex",
              flexDirection: "column",
              alignItems: "center",
              justifyContent: "center",
              gap: 8,
              color: "#E2E8F0",
              fontSize: 22,
              fontWeight: 600,
            }}
          >
            PC-{String(i + 1).padStart(2, "0")}
            <span style={{ fontSize: 17, fontWeight: 400, color: p > 0.5 ? "#67E8F9" : "#94A3B8" }}>{p > 0.5 ? "✓ Midview Aluno" : "instalando..."}</span>
          </div>
        ))}
      </div>
    </div>
  );
};

export { Cursor };
