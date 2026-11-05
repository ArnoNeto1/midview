import React from "react";
import { Img, staticFile } from "remotion";
import { clamp01, mix } from "../../../lib/motion";
import { WIN } from "../geometry";
import { PAINEL } from "./geometry";
import { P, PFONT, PG, PICONS } from "./palette";

/** O que a barra de título mostra: o selo da aula (Pasta da turma) e quantas máquinas estão ligadas. */
export type PainelTitle = {
  /** Turma da aula ligada ("7º A"); vazio = sem aula. */
  lesson?: string;
  /** Entrada do selo da aula (0→1). */
  lessonIn?: number;
  online: number;
  total: number;
};

/** O logo do Midview, do ícone do programa (assets/midview.ico), como no título da janela. */
export const MidviewIcon: React.FC<{ size: number; style?: React.CSSProperties }> = ({ size, style }) => (
  <Img src={staticFile("painel/midview-icone.png")} style={{ width: size, height: size, display: "block", ...style }} />
);

const Chip: React.FC<{ accent?: boolean; style?: React.CSSProperties; children: React.ReactNode }> = ({ accent, style, children }) => (
  <div
    style={{
      display: "flex",
      alignItems: "center",
      height: 20,
      padding: "0 10px",
      marginRight: 8,
      borderRadius: 10,
      background: P.card,
      border: `1px solid ${accent ? "rgba(45,212,191,0.5)" : P.line}`,
      fontSize: 11.5,
      color: accent ? P.accentText : P.text,
      whiteSpace: "nowrap",
      ...style,
    }}
  >
    {children}
  </div>
);

/** A barra de título própria da sala de aula (só nesta janela): logo, nome, selos e os botões da janela. */
export const PainelTitleBar: React.FC<{ title: PainelTitle }> = ({ title }) => {
  const lessonIn = clamp01(title.lessonIn ?? (title.lesson ? 1 : 0));
  return (
    <div
      style={{
        position: "absolute",
        left: 0,
        top: 0,
        width: WIN.w,
        height: PAINEL.titleBar,
        background: P.bg,
        display: "flex",
        alignItems: "center",
        paddingLeft: 12,
        fontFamily: PFONT,
        color: P.text,
      }}
    >
      <MidviewIcon size={20} />
      <div style={{ marginLeft: 8, display: "flex", alignItems: "baseline", whiteSpace: "nowrap" }}>
        <span style={{ fontSize: 15, fontWeight: 600 }}>Midview</span>
        <span style={{ fontSize: 12, color: P.muted, marginLeft: 6 }}>Sala de aula</span>
      </div>
      <div style={{ marginLeft: 18, display: "flex", alignItems: "center" }}>
        {lessonIn > 0.01 && title.lesson ? (
          <Chip accent style={{ opacity: lessonIn, transform: `scale(${mix(0.8, 1, lessonIn)})` }}>
            <span style={{ fontFamily: PICONS, fontSize: 11, marginRight: 5 }}>{PG.syncFolder}</span>
            Aula: {title.lesson}
          </Chip>
        ) : null}
        {/* Some com a sala vazia, como no app (MachineCountText). */}
        {title.total > 0 ? (
          <Chip>
            <span style={{ width: 7, height: 7, borderRadius: 4, background: P.online, marginRight: 5 }} />
            {title.online} de {title.total} {title.online === 1 ? "ligada" : "ligadas"}
          </Chip>
        ) : null}
      </div>
      <div style={{ position: "absolute", right: 0, top: 0, height: PAINEL.titleBar, display: "flex" }}>
        {[PG.minimize, PG.maximize, PG.close].map((code) => (
          <div key={code} style={{ width: 46, display: "flex", alignItems: "center", justifyContent: "center" }}>
            <span style={{ fontFamily: PICONS, fontSize: 10, color: P.text }}>{code}</span>
          </div>
        ))}
      </div>
    </div>
  );
};
