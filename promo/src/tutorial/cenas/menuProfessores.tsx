import React from "react";
import { useAppTheme } from "../../components/app/appTheme";
import { GRID_TOP, WIN } from "../../components/app/geometry";
import { clamp01 } from "../../lib/motion";
import { CLASS_FOLDER, CLASS_FOLDER_CTRL, ClassFolderWindow, type ClassRow } from "../janelas/pasta";
import { MACHINES, MACHINES_CTRL, MachinesWindow, UpdateWindow } from "../janelas/configurar";
import {
  ContextMenu,
  EmptyRoom,
  GIVE,
  GiveAccessWindow,
  JOIN,
  JOIN_CTRL,
  JoinClassWindow,
  UNINSTALL,
  UNINSTALL_CTRL,
  UninstallWindow,
  menuItemY,
  menuItems,
} from "../janelas/outras";
import { FULL, bottom, button, dialogFocus, groupFocus, hov, inside, mid, sp, tap, tileCenter, toolbar, typing, win, type Beat } from "../kit";
import { grid, statusAt } from "./comum";

const OFF = [11];
const REMOVED = [11];
const SINGLE = menuItems(1);
const MULTI = menuItems(3);
const menuAt = (i: number) => {
  const c = tileCenter(i);
  return { x: c.x + 4, y: Math.min(c.y + 4, WIN.h - 690) };
};
const itemPoint = (at: { x: number; y: number }, items: string[], label: string) => ({ x: at.x + 120, y: menuItemY(at, items, label) });
const hovered = (cursorY: number, at: { x: number; y: number }, items: string[]) => {
  for (const item of items) if (item !== "-" && Math.abs(menuItemY(at, items, item) - cursorY) < 12) return item;
  return null;
};
const CLASSES: ClassRow[] = [
  { name: "2º B", info: "38 arquivos · última aula 14/10" },
  { name: "3º A", info: "214 arquivos · última aula 26/10" },
];

/** Plaquinha que mostra que a cena está no computador do colega. */
const PcChip: React.FC<{ o: number; text: string; at?: { x: number; y: number } }> = ({ o, text, at }) => {
  const theme = useAppTheme();
  return o > 0.01 ? (
    <div
      style={{
        position: "absolute",
        ...(at ? { left: at.x, top: at.y } : { right: 16, top: GRID_TOP + 12 }),
        padding: "6px 14px",
        borderRadius: 999,
        background: theme.spotlight,
        color: "#0B1020",
        fontFamily: theme.font,
        fontSize: 15,
        fontWeight: 700,
        opacity: clamp01(o),
        boxShadow: "0 8px 20px rgba(0,0,0,0.35)",
      }}
    >
      {text}
    </div>
  ) : null;
};

const M6 = menuAt(6);
const M5 = menuAt(5);
const M11 = menuAt(11);

export const MENU_PROFESSORES: Record<string, Beat> = {
  // O menu do botão direito de uma máquina.
  t47: {
    moves: [
      { at: 30, to: tileCenter(6), arc: 40 },
      { at: 100, to: itemPoint(M6, SINGLE, "Ver a tela"), arc: 10 },
      { at: 130, to: itemPoint(M6, SINGLE, "Bloquear a tela"), arc: 0 },
      { at: 160, to: itemPoint(M6, SINGLE, "Enviar mensagem..."), arc: 0 },
    ],
    clicks: [70],
    cams: [{ at: 40, rect: inside({ x: M6.x - 520, y: 130, w: 1160, h: 690 }) }],
    draw: (t) => {
      const y = t < 100 ? 0 : t < 130 ? menuItemY(M6, SINGLE, "Ver a tela") : t < 160 ? menuItemY(M6, SINGLE, "Bloquear a tela") : menuItemY(M6, SINGLE, "Enviar mensagem...");
      return {
        app: {
          tiles: grid({ offline: OFF, selected: (i) => (i === 6 ? sp(t, 70, 6) : 0), each: () => ({ blockBadge: 1 }) }),
          toolbar: toolbar({}, t >= 70),
          bottom: bottom({ status: t >= 70 ? "Selecionada: PC-07" : "" }),
          overlay: (
            <>
              <MachinesWindow s={{ appear: 1 - sp(t, 2, 10), status: "", cards: [], cardsP: 0 }} />
              <UpdateWindow s={{ appear: 1 - sp(t, 0, 10), step: "", lines: [] }} />
              <ContextMenu at={M6} items={SINGLE} appear={sp(t, 72, 8) * (1 - sp(t, 196, 6))} hover={t >= 110 ? hovered(y, M6, SINGLE) : null} />
            </>
          ),
        },
      };
    },
  },

  // Várias selecionadas: o menu age em todas.
  t48: {
    moves: [
      { at: 0, to: tileCenter(1), arc: 30 },
      { at: 24, to: tileCenter(2), arc: 20 },
      { at: 46, to: tileCenter(5), arc: 20 },
      { at: 120, to: itemPoint(M5, MULTI, "Enviar mensagem..."), arc: 10 },
    ],
    clicks: [20, 42, 66, 92],
    cams: [{ at: 0, rect: inside({ x: 0, y: 120, w: 1240, h: 700 }) }],
    draw: (t) => {
      const sel = (i: number) => (i === 1 ? sp(t, 20, 6) : i === 2 ? sp(t, 42, 6) : i === 5 ? sp(t, 66, 6) : 0);
      return {
        app: {
          tiles: grid({ offline: OFF, selected: sel, each: () => ({ blockBadge: 1 }) }),
          toolbar: toolbar({}, t >= 20 && t < 42),
          bottom: bottom({ status: statusAt(t, [[20, "Selecionada: PC-02"], [42, "Selecionadas: 2"], [66, "Selecionadas: 3"]]) }),
          overlay: <ContextMenu at={M5} items={MULTI} appear={sp(t, 94, 8) * (1 - sp(t, 162, 6))} hover={t >= 134 ? "Enviar mensagem..." : null} />,
        },
      };
    },
  },

  // "Desinstalar programa".
  t49: {
    moves: [
      { at: 10, to: tileCenter(6), arc: 30 },
      { at: 50, to: itemPoint(M6, SINGLE, "Desinstalar programa..."), arc: 10 },
      { at: 110, to: { x: UNINSTALL.x + 34, y: UNINSTALL.y + 186 + 5 * 32 + 9 }, arc: 20 },
      { at: 150, to: { x: UNINSTALL.x + 24, y: UNINSTALL.y + 479 }, arc: 20 },
      { at: 186, to: mid(UNINSTALL, UNINSTALL_CTRL.run), arc: 20 },
    ],
    clicks: [36, 86, 140, 176, 226],
    cams: [
      { at: 0, rect: inside({ x: M6.x - 520, y: 130, w: 1160, h: 690 }) },
      { at: 90, rect: dialogFocus(UNINSTALL, 40) },
      { at: 236, rect: FULL },
    ],
    draw: (t) => ({
      app: {
        tiles: grid({ offline: OFF, selected: (i) => (i === 6 ? sp(t, 36, 6) : 0), each: () => ({ blockBadge: 1 }) }),
        toolbar: toolbar({}, t >= 36),
        bottom: bottom({ status: t >= 36 ? "Selecionada: PC-07" : "" }),
        overlay: (
          <>
            <ContextMenu at={M6} items={SINGLE} appear={sp(t, 38, 8) * (1 - sp(t, 88, 6))} hover={t >= 64 && t < 90 ? "Desinstalar programa..." : null} />
            <UninstallWindow
              s={{
                appear: sp(t, 90, 12) * (1 - sp(t, 232, 10)),
                pc: "PC-07",
                checks: [0, 0, 0, 0, 0, t >= 140 ? 1 : 0, 0, 0],
                all: t >= 176 ? 1 : 0,
                runHover: hov(t, 210, 236),
                runPress: tap(t, 226),
              }}
            />
          </>
        ),
      },
    }),
  },

  // "Remover da lista da sala".
  t50: {
    moves: [
      { at: 20, to: tileCenter(11), arc: 30 },
      { at: 80, to: itemPoint(M11, SINGLE, "Remover da lista da sala"), arc: 10 },
    ],
    clicks: [56, 130],
    cams: [
      { at: 0, rect: inside({ x: 300, y: 130, w: 1230, h: 690 }) },
      { at: 160, rect: FULL },
    ],
    draw: (t) => ({
      app: {
        tiles: grid({
          offline: OFF,
          selected: (i) => (i === 11 ? sp(t, 56, 6) * (1 - sp(t, 132, 6)) : 0),
          each: (i) => ({ blockBadge: 1, ...(i === 11 ? { appear: 1 - sp(t, 134, 12) } : {}) }),
        }),
        toolbar: toolbar(),
        bottom: bottom({ status: t >= 56 && t < 132 ? "Selecionada: PC-12" : "" }),
        overlay: <ContextMenu at={M11} items={SINGLE} appear={sp(t, 58, 8) * (1 - sp(t, 132, 6))} hover={t >= 100 && t < 134 ? "Remover da lista da sala" : null} />,
      },
    }),
  },

  // "Dar acesso a um colega...".
  t51: {
    moves: [
      { at: 20, to: button("machines"), arc: -40 },
      { at: 100, to: mid(MACHINES, MACHINES_CTRL.giveAccess), arc: 40 },
    ],
    clicks: [56, 170],
    cams: [
      { at: 0, rect: groupFocus("configurar") },
      { at: 60, rect: dialogFocus(MACHINES, 30) },
      { at: 176, rect: dialogFocus(GIVE, 120) },
    ],
    draw: (t, d) => ({
      app: {
        tiles: grid({ offline: REMOVED, each: (i) => ({ blockBadge: 1, appear: i === 11 ? 0 : 1 }) }),
        toolbar: toolbar({ machines: { hover: hov(t, 40, 68), press: tap(t, 56) } }, false, { group: "configurar", amount: win(t, 0, d - 4, 12, 12) }),
        bottom: bottom(),
        overlay: (
          <>
            <MachinesWindow s={{ appear: sp(t, 58, 12), status: "", cards: [], cardsP: 0, giveHover: hov(t, 150, 180), givePress: tap(t, 170) }} />
            <GiveAccessWindow s={{ appear: sp(t, 174, 12), joined: 0 }} />
          </>
        ),
      },
    }),
  },

  // O código: nada aparece no aluno.
  t52: {
    cams: [{ at: 0, rect: dialogFocus(GIVE, 120) }],
    draw: (t) => ({
      split: sp(t, 110, 18) * (1 - sp(t, 222, 18)),
      student: { pc: 2, apps: 1 },
      app: {
        tiles: grid({ offline: REMOVED, each: (i) => ({ blockBadge: 1, appear: i === 11 ? 0 : 1 }) }),
        toolbar: toolbar(),
        bottom: bottom(),
        overlay: (
          <>
            <MachinesWindow s={{ appear: 1, status: "", cards: [], cardsP: 0 }} />
            <GiveAccessWindow s={{ appear: 1, joined: 0 }} />
          </>
        ),
      },
    }),
  },

  // No computador do colega: "Entrar na sala de um colega".
  t53: {
    moves: [{ at: 100, to: mid(JOIN, JOIN_CTRL.enter), arc: 30 }],
    clicks: [146],
    cams: [
      { at: 0, rect: dialogFocus(JOIN, 160) },
      { at: 186, rect: FULL },
    ],
    draw: (t) => {
      const room = sp(t, 190, 12);
      return {
        app: {
          tiles: room > 0.01 ? grid({ offline: REMOVED, each: (i) => ({ appear: i === 11 ? 0 : sp(t, 190 + i * 2, 12) }) }) : [],
          grid: room < 0.99 ? <EmptyRoom appear={1 - room} /> : undefined,
          toolbar: toolbar(),
          bottom: bottom(),
          overlay: (
            <>
              <PcChip o={sp(t, 0, 10)} text="No PC do colega (PROF-2)" at={{ x: JOIN.x, y: JOIN.y - 46 }} />
              <JoinClassWindow s={{ appear: sp(t, 6, 12) * (1 - sp(t, 184, 10)), typed: typing(t, 30, 90), enterHover: hov(t, 130, 156), enterPress: tap(t, 146), done: sp(t, 160, 8) }} />
            </>
          ),
        },
      };
    },
  },

  // "Permitir novo professor (10 minutos)".
  t54: {
    moves: [
      { at: 10, to: button("machines"), arc: -40 },
      { at: 90, to: mid(MACHINES, MACHINES_CTRL.allowNew), arc: 40 },
    ],
    clicks: [40, 150],
    cams: [
      { at: 0, rect: groupFocus("configurar") },
      { at: 44, rect: dialogFocus(MACHINES, 30) },
      { at: 246, rect: FULL },
    ],
    draw: (t) => ({
      app: {
        tiles: grid({ offline: REMOVED, each: (i) => ({ blockBadge: 1, appear: i === 11 ? 0 : 1 }) }),
        toolbar: toolbar({ machines: { hover: hov(t, 26, 52), press: tap(t, 40) } }),
        bottom: bottom(),
        overlay: (
          <MachinesWindow
            s={{ appear: sp(t, 42, 12) * (1 - sp(t, 240, 10)), status: "", cards: [], cardsP: 0, allowHover: hov(t, 134, 160), allowPress: tap(t, 150), allowResult: sp(t, 162, 8) }}
          />
        ),
      },
    }),
  },

  // Pasta da turma com dois PCs de professor.
  t55: {
    moves: [
      { at: 30, to: button("classFolder"), arc: -40 },
      { at: 190, to: mid(CLASS_FOLDER, CLASS_FOLDER_CTRL.takeHome), arc: 30 },
    ],
    clicks: [70],
    cams: [
      { at: 0, rect: FULL },
      { at: 76, rect: dialogFocus(CLASS_FOLDER, 40) },
    ],
    draw: (t) => ({
      app: {
        tiles: grid({ offline: REMOVED, each: (i) => ({ appear: i === 11 ? 0 : 1 }) }),
        toolbar: toolbar({ classFolder: { hover: hov(t, 54, 80), press: tap(t, 70) } }),
        bottom: bottom(),
        overlay: (
          <>
            <PcChip o={sp(t, 6, 10) * (1 - sp(t, 360, 10))} text="No PC do colega (PROF-2)" />
            <ClassFolderWindow
              s={{ appear: sp(t, 72, 12), mode: "list", classes: CLASSES, selected: 1, remote: sp(t, 72, 12), takeHover: win(t, 220, 360, 6, 6) }}
            />
          </>
        ),
      },
    }),
  },
};

