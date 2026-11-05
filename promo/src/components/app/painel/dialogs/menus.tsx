import React from "react";
import { clamp01 } from "../../../../lib/motion";
import type { Box, Point } from "../../geometry";
import { PAINEL_TOOLBAR, pDialog } from "../geometry";
import { Card, PBtn, PDialog, PField, icon } from "../kit";
import { P, PFONT, PG } from "../palette";

/* ---------- menu do botão direito (MainWindow.xaml, foto 69) ---------- */

/** Itens do menu de uma máquina, na ordem do XAML ("-" = separador, "›" = submenu). */
const ITEMS: { label: string; one?: boolean; sep?: boolean }[] = [
  { label: "Desligar..." },
  { label: "Reiniciar..." },
  { label: "Ligar" },
  { label: "-", sep: true },
  { label: "Ver a tela", one: true },
  { label: "Controlar (trava o teclado e o mouse do aluno)", one: true },
  { label: "Bloquear a tela" },
  { label: "-", sep: true },
  { label: "Enviar mensagem..." },
  { label: "Conversar (chat)...", one: true },
  { label: "Enviar arquivo..." },
  { label: "Recolher arquivos..." },
  { label: "Abrir site ›" },
  { label: "Abrir programa ›" },
  { label: "-", sep: true },
  { label: "Fechar programas..." },
  { label: "Bloquear programas..." },
  { label: "Bloquear sites..." },
  { label: "Painel do laboratório..." },
  { label: "Instalar programa..." },
  { label: "Desinstalar programa...", one: true },
  { label: "-", one: true, sep: true },
  { label: "Editar a máquina (avançado)...", one: true },
  { label: "Remover da lista da sala", one: true },
];

export const PMENU = { item: 30, separator: 9, pad: 8 } as const;

/** Os itens para uma máquina, ou para `count` selecionadas (com o título "N máquinas selecionadas"). */
export const pMenuItems = (count: number): string[] =>
  count <= 1
    ? ITEMS.map((i) => i.label)
    : [`${count} máquinas selecionadas`, "-", ...ITEMS.filter((i) => !i.one).map((i) => i.label)];

export const pMenuWidth = (items: string[]) => (items.includes("Controlar (trava o teclado e o mouse do aluno)") ? 330 : 228);
export const pMenuHeight = (items: string[]) => PMENU.pad * 2 + items.reduce((h, i) => h + (i === "-" ? PMENU.separator : PMENU.item), 0);

/** Centro de um item do menu aberto em `at`. */
export const pMenuItem = (at: Point, items: string[], label: string): Point => {
  let y = at.y + PMENU.pad;
  for (const item of items) {
    const h = item === "-" ? PMENU.separator : PMENU.item;
    if (item === label) return { x: at.x + 110, y: y + h / 2 };
    y += h;
  }
  return { x: at.x + 110, y };
};

/** Menu escuro (ContextMenu do Painel): fundo #0F1828, item realçado e setinha nos submenus. */
export const PMenu: React.FC<{ at: Point; items: string[]; appear: number; hover: string | null; width?: number }> = ({ at, items, appear, hover, width }) => {
  if (appear <= 0.01) return null;
  const w = width ?? pMenuWidth(items);
  return (
    <div
      style={{
        position: "absolute",
        left: at.x,
        top: at.y,
        width: w,
        padding: `${PMENU.pad}px 0`,
        background: P.surface,
        border: `1px solid ${P.line}`,
        borderRadius: 6,
        boxShadow: "0 18px 40px -10px rgba(0,0,0,0.7)",
        fontFamily: PFONT,
        fontSize: 12.5,
        color: P.text,
        opacity: clamp01(appear * 1.5),
        transform: `scale(${0.96 + 0.04 * clamp01(appear)})`,
        transformOrigin: "0 0",
      }}
    >
      {items.map((item, i) => {
        if (item === "-") return <div key={`s${i}`} style={{ height: 1, margin: `${(PMENU.separator - 1) / 2}px 8px`, background: P.line }} />;
        const heading = i === 0 && item.includes("selecionadas");
        const sub = item.endsWith(" ›");
        return (
          <div
            key={item}
            style={{
              position: "relative",
              height: PMENU.item,
              margin: "0 4px",
              borderRadius: 4,
              display: "flex",
              alignItems: "center",
              paddingLeft: 32,
              fontWeight: heading ? 600 : 400,
              background: hover === item ? P.toolbarHover : "transparent",
            }}
          >
            {sub ? item.slice(0, -2) : item}
            {sub ? <span style={{ ...icon(9, P.muted), position: "absolute", right: 12 }}>{PG.chevronRight}</span> : null}
          </div>
        );
      })}
    </div>
  );
};

/** Menu de um botão da barra com itens salvos ("Abrir site", "Abrir programa"), logo abaixo dele. */
export const savedMenuAt = (id: "openSite" | "openProgram"): Point => {
  const b = PAINEL_TOOLBAR.buttons[id];
  return { x: b.x + 4, y: b.y + b.h + 2 };
};

/* ---------- caixa de mensagem do Midview (MidviewMessage, fotos 58 a 62) ---------- */

export const MSGBOX = pDialog(446, 220);
export const MSGBOX_CTRL = { yes: { x: 254, y: 175, w: 78, h: 26 }, no: { x: 342, y: 175, w: 80, h: 26 } };

export const MessageBoxDialog: React.FC<{
  s: { appear: number; title: string; text: string; yesHover?: number; yesPress?: number };
}> = ({ s }) => (
  <PDialog title={s.title} box={MSGBOX} appear={s.appear} icon={PG.warning} heading={s.title}>
    <div style={{ position: "absolute", left: 24, top: 92, width: 398, fontSize: 12.5, lineHeight: "17px", color: P.text, whiteSpace: "pre-line" }}>{s.text}</div>
    <PBtn box={MSGBOX_CTRL.yes} kind="danger" hover={s.yesHover} press={s.yesPress}>
      Sim
    </PBtn>
    <div style={{ position: "absolute", left: MSGBOX_CTRL.no.x - 1, top: MSGBOX_CTRL.no.y - 1, width: MSGBOX_CTRL.no.w + 2, height: MSGBOX_CTRL.no.h + 2, borderRadius: 5, border: `1px solid ${P.accent}` }} />
    <PBtn box={MSGBOX_CTRL.no}>Não</PBtn>
  </PDialog>
);

/* ---------- Conversa com um aluno (ChatWindow.xaml, foto 38) ---------- */

export const CONVERSA = pDialog(480, 480);

export const ConversaDialog: React.FC<{ s: { appear: number; pc: string; lines: { text: string; p: number }[]; input?: string; box?: Box } }> = ({ s }) => (
  <PDialog
    title={`Conversa com ${s.pc}`}
    box={s.box ?? CONVERSA}
    appear={s.appear}
    icon={PG.chat}
    group="turma"
    heading="Conversa"
    hint="Conectado: a janela de conversa abriu na tela do aluno."
  >
    <Card box={{ x: 24, y: 94, w: 432, h: 328 }} />
    <div style={{ position: "absolute", left: 37, top: 106, width: 406, fontSize: 12.5, lineHeight: "17px", color: P.text }}>
      {s.lines.map((l) =>
        l.p > 0.01 ? (
          <div key={l.text} style={{ opacity: clamp01(l.p), transform: `translateY(${(1 - clamp01(l.p)) * 6}px)` }}>
            {l.text}
          </div>
        ) : null,
      )}
    </div>
    <PField box={{ x: 24, y: 434, w: 361, h: 30 }} text={s.input ?? ""} />
    <PBtn box={{ x: 394, y: 434, w: 62, h: 30 }} disabled>
      Enviar
    </PBtn>
  </PDialog>
);
