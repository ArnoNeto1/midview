import React from "react";
import { useAppTheme } from "../../components/app/appTheme";
import { MENU, MENU_ITEMS } from "../../components/app/dialogLayout";
import { GRID_BOTTOM, GRID_TOP, WIN, type Point } from "../../components/app/geometry";
import { Btn, Check, Field, Frame, Text, typed } from "../../components/app/ui";
import { clamp01 } from "../../lib/motion";
import { centered } from "../kit";

/* ---------- menu do botão direito ---------- */

/** Itens que só valem com uma máquina selecionada (somem com várias). */
const SINGLE_ONLY = new Set([
  "Ver a tela",
  "Controlar (trava o teclado e o mouse do aluno)",
  "Conversar (chat)...",
  "Desinstalar programa...",
  "Editar a máquina (avançado)...",
  "Remover da lista da sala",
]);

/** Itens do menu para uma máquina ou para várias selecionadas ("N máquinas selecionadas" no topo). */
export const menuItems = (count: number) => {
  if (count <= 1) return MENU_ITEMS;
  const items = MENU_ITEMS.filter((i) => !SINGLE_ONLY.has(i));
  // Separadores seguidos ou no fim não aparecem.
  const clean = items.filter((it, i) => !(it === "-" && (items[i - 1] === "-" || i === items.length - 1)));
  return [`${count} máquinas selecionadas`, "-", ...clean];
};

export const menuHeight = (items: string[]) => MENU.pad * 2 + items.reduce((sum, i) => sum + (i === "-" ? MENU.separator : MENU.item), 0);
/** Centro vertical de um item do menu aberto em `at`. */
export const menuItemY = (at: Point, items: string[], label: string) => {
  let y = at.y + MENU.pad;
  for (const item of items) {
    const h = item === "-" ? MENU.separator : MENU.item;
    if (item === label) return y + h / 2;
    y += h;
  }
  return y;
};

export const ContextMenu: React.FC<{ at: Point; items: string[]; appear: number; hover: string | null }> = ({ at, items, appear, hover }) => {
  const theme = useAppTheme();
  if (appear <= 0.01) return null;
  return (
    <div
      style={{
        position: "absolute",
        left: at.x,
        top: at.y,
        width: MENU.w,
        padding: `${MENU.pad}px 0`,
        background: theme.menu.bg,
        border: `1px solid ${theme.menu.line}`,
        borderRadius: 6,
        boxShadow: "0 18px 40px -10px rgba(0,0,0,0.5)",
        fontFamily: theme.font,
        fontSize: 13,
        color: theme.menu.text,
        opacity: clamp01(appear * 1.5),
        transform: `scale(${0.96 + 0.04 * clamp01(appear)})`,
        transformOrigin: "0 0",
      }}
    >
      {items.map((item, i) =>
        item === "-" ? (
          <div key={`s${i}`} style={{ height: 1, margin: `${(MENU.separator - 1) / 2}px 8px`, background: theme.menu.line }} />
        ) : (
          <div
            key={item}
            style={{
              height: MENU.item,
              display: "flex",
              alignItems: "center",
              padding: "0 14px",
              fontWeight: i === 0 && items[1] === "-" && item.includes("selecionadas") ? 700 : 400,
              color: i === 0 && item.includes("selecionadas") ? theme.menu.muted : theme.menu.text,
              background: hover === item ? theme.menu.hover : "transparent",
            }}
          >
            {item}
          </div>
        ),
      )}
    </div>
  );
};

/* ---------- sala vazia ---------- */

export const EMPTY_SEARCH = { x: WIN.w / 2 - 230, y: GRID_TOP + (GRID_BOTTOM - GRID_TOP) / 2 + 40, w: 220, h: 36 };

export const EmptyRoom: React.FC<{ hover?: number; press?: number; appear: number }> = ({ hover, press, appear }) => {
  const theme = useAppTheme();
  return (
    <div style={{ position: "absolute", inset: 0, opacity: appear, fontFamily: theme.font, color: theme.inkMuted, textAlign: "center" }}>
      <div style={{ position: "absolute", left: 0, right: 0, top: (GRID_BOTTOM - GRID_TOP) / 2 - 70, fontSize: 16, lineHeight: 1.6 }}>
        Nenhuma máquina na sala ainda.
        <br />
        Instale o Midview Aluno nas máquinas e procure-as na rede,
        <br />
        ou peça um código a um colega que já tem a sala.
      </div>
      <Btn box={{ ...EMPTY_SEARCH, y: EMPTY_SEARCH.y - GRID_TOP }} hover={hover} press={press} strong>
        Procurar máquinas na rede
      </Btn>
      <Btn box={{ x: WIN.w / 2 + 10, y: EMPTY_SEARCH.y - GRID_TOP, w: 220, h: 36 }}>Entrar na sala de um colega</Btn>
    </div>
  );
};

/* ---------- Desinstalar programa (UninstallWindow.xaml) ---------- */

export const UNINSTALL = centered(560, 620);
export const UNINSTALL_ROWS = ["Adobe Reader DC", "CCleaner", "Google Chrome", "LibreOffice 24.8", "Minecraft Launcher", "Roblox Player", "Scratch 3", "VLC media player"];
export const UNINSTALL_CTRL = { row: (i: number) => ({ x: 26, y: 186 + i * 30, w: 200, h: 20 }), all: { x: 16, y: 470, w: 300, h: 20 }, run: { x: 16, y: 560, w: 528, h: 36 } };

export const UninstallWindow: React.FC<{ s: { appear: number; pc: string; checks: number[]; all: number; runHover?: number; runPress?: number } }> = ({ s }) => {
  const theme = useAppTheme();
  return (
    <Frame title="Desinstalar programa" box={UNINSTALL} appear={s.appear}>
      <Text x={16} y={44} weight={600}>
        Programas instalados em {s.pc}
      </Text>
      <Text x={16} y={66} w={528} muted size={10.5}>
        A lista é lida de uma máquina. Marque os programas e desinstale nela ou na turma toda; cada máquina acha o programa pelo nome, mesmo
        numa versão diferente. Só remove programas com desinstalação silenciosa; os que não têm, o resultado avisa.
      </Text>
      <Field box={{ x: 16, y: 130, w: 400, h: 30 }} text="(filtrar pelo nome)" />
      <Btn box={{ x: 424, y: 130, w: 120, h: 30 }}>Recarregar</Btn>
      <div style={{ position: "absolute", left: 16, top: 172, width: 528, height: 280, borderRadius: 4, border: `1px solid ${theme.dialog.line}`, background: theme.dialog.input }} />
      {UNINSTALL_ROWS.map((r, i) => (
        <Check key={r} x={26} y={186 + i * 32} on={s.checks[i] ?? 0}>
          {r}
        </Check>
      ))}
      <Check x={16} y={470} on={s.all}>
        Aplicar na turma toda (12 máquinas)
      </Check>
      <Btn box={UNINSTALL_CTRL.run} hover={s.runHover} press={s.runPress} strong>
        Desinstalar os marcados
      </Btn>
    </Frame>
  );
};

/* ---------- Dar acesso a um colega / Entrar na sala de um colega ---------- */

export const GIVE = centered(500, 330);
export const INVITE_CODE = "K7P Q2M 94R";

export const GiveAccessWindow: React.FC<{ s: { appear: number; joined: number } }> = ({ s }) => (
  <Frame title="Dar acesso a um colega" box={GIVE} appear={s.appear}>
    <Text x={20} y={48}>
      Passe este código ao colega:
    </Text>
    <div style={{ position: "absolute", left: 20, top: 80, width: 460, textAlign: "center", fontFamily: "Consolas, monospace", fontSize: 40, fontWeight: 700, letterSpacing: 2 }}>
      {INVITE_CODE}
    </div>
    <Text x={20} y={150} w={460} muted size={12}>
      Vale por 10 minutos, uma vez (até 13:55). Deixe esta janela aberta até o colega entrar. Vai liberar as 11 máquinas da sua sala.
    </Text>
    <div style={{ opacity: clamp01(s.joined) }}>
      <Text x={20} y={212} w={460} weight={600}>
        PROF-2 recebeu acesso a 11 de 11 máquinas.
      </Text>
    </div>
    <Btn box={{ x: 360, y: 270, w: 120, h: 34 }}>{s.joined > 0.5 ? "Fechar" : "Cancelar"}</Btn>
  </Frame>
);

export const JOIN = centered(500, 280);
export const JOIN_CTRL = { code: { x: 20, y: 136, w: 330, h: 32 }, enter: { x: 360, y: 136, w: 120, h: 32 } };

export const JoinClassWindow: React.FC<{ s: { appear: number; typed: number; enterHover?: number; enterPress?: number; done: number } }> = ({ s }) => (
  <Frame title="Entrar na sala de um colega" box={JOIN} appear={s.appear}>
    <Text x={20} y={48} w={460}>
      Peça ao colega que já tem a sala para clicar em “Máquinas da sala” &gt; “Dar acesso a um colega”, e digite aqui o código que aparecer
      na tela dele.
    </Text>
    <Text x={20} y={112}>
      Código do convite:
    </Text>
    <Field box={JOIN_CTRL.code} text={typed(INVITE_CODE, s.typed)} caret={s.typed > 0 && s.typed < 1} />
    <Btn box={JOIN_CTRL.enter} hover={s.enterHover} press={s.enterPress} strong>
      Entrar
    </Btn>
    <div style={{ opacity: clamp01(s.done) }}>
      <Text x={20} y={196} weight={600}>
        Pronto: 11 de 11 máquinas na sua sala.
      </Text>
    </div>
  </Frame>
);
