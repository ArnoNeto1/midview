import React from "react";
import { WIN } from "../../components/app/geometry";
import { MACHINES, MACHINES_CTRL, MachinesDialog } from "../../components/app/painel/dialogs/configurar";
import { PMenu, pMenuHeight, pMenuItem, pMenuItems } from "../../components/app/painel/dialogs/menus";
import { FOLDER, FOLDER_CTRL, FolderDialog, type FolderClass } from "../../components/app/painel/dialogs/pasta";
import { UNINSTALL, UNINSTALL_CTRL, UninstallDialog } from "../../components/app/painel/dialogs/programas";
import { EmptyRoom, GIVE, GiveAccessDialog, JOIN, JOIN_CTRL, JoinClassDialog } from "../../components/app/painel/dialogs/sala";
import { P_GRID_TOP } from "../../components/app/painel/geometry";
import { P, PFONT } from "../../components/app/painel/palette";
import { clamp01, ramp } from "../../lib/motion";
import { statusAt } from "../cenas/comum";
import { FULL, hov, sp, tap, typing, win, type Beat } from "../kit";
import { OFF_LATE, inside, pbottom, pbutton, pdialogFocus, pgrid, pgroupFocus, pmid, pname, pspot, ptile, ptoolbar } from "./kit";

const OFF = OFF_LATE;
/** A ALUNO-15 (desligada) sai da lista da sala em t50. */
const REMOVED = OFF_LATE;
const SINGLE = pMenuItems(1);
const MULTI = pMenuItems(3);
const menuAt = (i: number, items: string[]) => {
  const c = ptile(i);
  return { x: c.x + 4, y: Math.min(c.y + 4, WIN.h - pMenuHeight(items) - 8) };
};
const M6 = menuAt(6, SINGLE);
const M5 = menuAt(5, MULTI);
const M14 = { ...menuAt(14, SINGLE), x: ptile(14).x - 334 };
const CLASSES: FolderClass[] = [
  { name: "2º B", note: "38 arquivos · última aula 14/10" },
  { name: "3º A", note: "214 arquivos · última aula 26/10" },
];
const roomTiles = (extra: Parameters<typeof pgrid>[0] = {}) => pgrid({ offline: OFF, removed: REMOVED, ...extra });

/** Plaquinha que mostra que a cena está no computador do colega. */
const PcChip: React.FC<{ o: number; at?: { x: number; y: number } }> = ({ o, at }) =>
  o > 0.01 ? (
    <div
      style={{
        position: "absolute",
        ...(at ? { left: at.x, top: at.y } : { right: 16, top: P_GRID_TOP + 12 }),
        padding: "6px 14px",
        borderRadius: 999,
        background: P.accent,
        color: P.onAccent,
        fontFamily: PFONT,
        fontSize: 15,
        fontWeight: 700,
        opacity: clamp01(o),
        boxShadow: "0 8px 20px rgba(0,0,0,0.35)",
      }}
    >
      No PC do colega (PROF-02)
    </div>
  ) : null;

export const MENU_P: Record<string, Beat> = {
  // O menu do botão direito de uma máquina.
  t47: {
    moves: [
      { at: 30, to: ptile(6), arc: 40 },
      { at: 100, to: pMenuItem(M6, SINGLE, "Ver a tela"), arc: 10 },
      { at: 130, to: pMenuItem(M6, SINGLE, "Bloquear a tela"), arc: 0 },
      { at: 160, to: pMenuItem(M6, SINGLE, "Enviar mensagem..."), arc: 0 },
    ],
    clicks: [70],
    cams: [{ at: 40, rect: inside({ x: M6.x - 560, y: 110, w: 1200, h: 710 }) }],
    draw: (t) => {
      const item = t < 110 ? null : t < 140 ? "Ver a tela" : t < 170 ? "Bloquear a tela" : "Enviar mensagem...";
      return {
        app: {
          tiles: pgrid({ offline: OFF, selected: (i) => (i === 6 ? sp(t, 70, 6) : 0), each: () => ({ blockBadge: 1 }) }),
          toolbar: ptoolbar({}, t >= 70),
          bottom: pbottom({ status: t >= 70 ? `Selecionada: ${pname(6)}` : "" }),
          overlay: <PMenu at={M6} items={SINGLE} appear={sp(t, 72, 8) * (1 - sp(t, 196, 6))} hover={item} />,
        },
      };
    },
  },

  // Várias selecionadas: o menu age em todas.
  t48: {
    moves: [
      { at: 0, to: ptile(1), arc: 30 },
      { at: 24, to: ptile(2), arc: 20 },
      { at: 46, to: ptile(5), arc: 20 },
      { at: 120, to: pMenuItem(M5, MULTI, "Enviar mensagem..."), arc: 10 },
    ],
    clicks: [20, 42, 66, 92],
    cams: [{ at: 0, rect: inside({ x: 0, y: 110, w: 1200, h: 710 }) }],
    draw: (t) => {
      const sel = (i: number) => (i === 1 ? sp(t, 20, 6) : i === 2 ? sp(t, 42, 6) : i === 5 ? sp(t, 66, 6) : 0);
      return {
        app: {
          tiles: pgrid({ offline: OFF, selected: sel, each: () => ({ blockBadge: 1 }) }),
          toolbar: ptoolbar({}, t >= 20 && t < 42),
          bottom: pbottom({ status: statusAt(t, [[20, `Selecionada: ${pname(1)}`], [42, "Selecionadas: 2"], [66, "Selecionadas: 3"]]) }),
          overlay: <PMenu at={M5} items={MULTI} appear={sp(t, 94, 8) * (1 - sp(t, 162, 6))} hover={t >= 134 ? "Enviar mensagem..." : null} />,
        },
      };
    },
  },

  // "Desinstalar programa".
  t49: {
    moves: [
      { at: 10, to: ptile(6), arc: 30 },
      { at: 50, to: pMenuItem(M6, SINGLE, "Desinstalar programa..."), arc: 10 },
      { at: 110, to: pmid(UNINSTALL, { ...UNINSTALL_CTRL.row(2), w: 16 }), arc: 20 },
      { at: 150, to: pmid(UNINSTALL, { ...UNINSTALL_CTRL.all, w: 16 }), arc: 20 },
      { at: 186, to: pmid(UNINSTALL, UNINSTALL_CTRL.run), arc: 20 },
    ],
    clicks: [36, 86, 140, 176, 226],
    cams: [
      { at: 0, rect: inside({ x: M6.x - 560, y: 110, w: 1200, h: 710 }) },
      { at: 90, rect: pdialogFocus(UNINSTALL, 30) },
      { at: 244, rect: FULL },
    ],
    draw: (t) => ({
      app: {
        tiles: pgrid({ offline: OFF, selected: (i) => (i === 6 ? sp(t, 36, 6) : 0), each: () => ({ blockBadge: 1 }) }),
        toolbar: ptoolbar({}, t >= 36),
        bottom: pbottom({ status: t >= 36 ? `Selecionada: ${pname(6)}` : "" }),
        overlay: (
          <>
            <PMenu at={M6} items={SINGLE} appear={sp(t, 38, 8) * (1 - sp(t, 88, 6))} hover={t >= 64 && t < 90 ? "Desinstalar programa..." : null} />
            <UninstallDialog
              s={{
                appear: sp(t, 90, 12) * (1 - sp(t, 240, 10)),
                pc: pname(6),
                checks: [0, 0, t >= 140 ? 1 : 0],
                all: t >= 176 ? 1 : 0,
                runHover: hov(t, 210, 236),
                runPress: tap(t, 226),
                sent: ramp(t, 228, 240),
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
      { at: 20, to: ptile(14), arc: 30 },
      { at: 80, to: pMenuItem(M14, SINGLE, "Remover da lista da sala"), arc: 10 },
    ],
    clicks: [56, 130],
    cams: [
      { at: 0, rect: inside({ x: 300, y: 110, w: 1230, h: 710 }) },
      { at: 160, rect: FULL },
    ],
    draw: (t) => ({
      app: {
        tiles: pgrid({
          offline: OFF,
          selected: (i) => (i === 14 ? sp(t, 56, 6) * (1 - sp(t, 132, 6)) : 0),
          each: (i) => ({ blockBadge: 1, ...(i === 14 ? { appear: 1 - sp(t, 134, 12) } : {}) }),
        }),
        toolbar: ptoolbar(),
        bottom: pbottom({ status: t >= 56 && t < 132 ? `Selecionada: ${pname(14)}` : "" }),
        overlay: <PMenu at={M14} items={SINGLE} appear={sp(t, 58, 8) * (1 - sp(t, 132, 6))} hover={t >= 100 && t < 134 ? "Remover da lista da sala" : null} />,
      },
    }),
  },

  // "Máquinas da sala" > "Outro professor" > "Dar acesso a um colega...".
  t51: {
    moves: [
      { at: 20, to: pbutton("machines"), arc: -40 },
      { at: 84, to: pmid(MACHINES, MACHINES_CTRL.nav(2)), arc: 30 },
      { at: 130, to: pmid(MACHINES, MACHINES_CTRL.give), arc: 20 },
    ],
    clicks: [56, 110, 170],
    cams: [
      { at: 0, rect: pgroupFocus("configurar") },
      { at: 60, rect: pdialogFocus(MACHINES, 30) },
      { at: 176, rect: pdialogFocus(GIVE, 120) },
    ],
    draw: (t, d) => ({
      app: {
        tiles: roomTiles({ each: () => ({ blockBadge: 1 }) }),
        toolbar: ptoolbar({ machines: { hover: hov(t, 40, 68), press: tap(t, 56) } }, false, pspot("configurar", t, d)),
        bottom: pbottom(),
        overlay: (
          <>
            <MachinesDialog
              s={{ appear: sp(t, 58, 12), page: t >= 111 ? 2 : 0, navHover: hov(t, 98, 114), navHoverIndex: 2, giveHover: hov(t, 150, 180), givePress: tap(t, 170) }}
            />
            <GiveAccessDialog s={{ appear: sp(t, 174, 12), joined: 0 }} />
          </>
        ),
      },
    }),
  },

  // O código: nada aparece no aluno.
  t52: {
    cams: [{ at: 0, rect: pdialogFocus(GIVE, 120) }],
    draw: (t) => ({
      split: sp(t, 110, 18) * (1 - sp(t, 222, 18)),
      student: { pc: 2, apps: 1 },
      app: {
        tiles: roomTiles({ each: () => ({ blockBadge: 1 }) }),
        toolbar: ptoolbar(),
        bottom: pbottom(),
        overlay: (
          <>
            <MachinesDialog s={{ appear: 1, page: 2 }} />
            <GiveAccessDialog s={{ appear: 1, joined: 0 }} />
          </>
        ),
      },
    }),
  },

  // No computador do colega: "Entrar na sala de um colega".
  t53: {
    moves: [{ at: 100, to: pmid(JOIN, JOIN_CTRL.enter), arc: 30 }],
    clicks: [146],
    cams: [
      { at: 0, rect: pdialogFocus(JOIN, 160) },
      { at: 186, rect: FULL },
    ],
    draw: (t) => {
      const room = sp(t, 190, 12);
      return {
        app: {
          tiles: room > 0.01 ? roomTiles({ each: (i) => ({ appear: i === 14 ? 0 : sp(t, 190 + i * 2, 12) }) }) : [],
          grid: room < 0.99 ? <EmptyRoom appear={1 - room} /> : undefined,
          title: room > 0.01 ? undefined : { online: 0, total: 0 },
          toolbar: ptoolbar(),
          bottom: pbottom(),
          overlay: (
            <>
              <PcChip o={sp(t, 0, 10)} at={{ x: JOIN.x, y: JOIN.y - 46 }} />
              <JoinClassDialog
                s={{ appear: sp(t, 6, 12) * (1 - sp(t, 184, 10)), typed: typing(t, 30, 90), enterHover: hov(t, 130, 156), enterPress: tap(t, 146), done: sp(t, 160, 8) }}
              />
            </>
          ),
        },
      };
    },
  },

  // "Permitir novo professor (10 minutos)".
  t54: {
    moves: [
      { at: 10, to: pbutton("machines"), arc: -40 },
      { at: 64, to: pmid(MACHINES, MACHINES_CTRL.nav(2)), arc: 30 },
      { at: 100, to: pmid(MACHINES, MACHINES_CTRL.allow), arc: 30 },
    ],
    clicks: [40, 86, 150],
    cams: [
      { at: 0, rect: pgroupFocus("configurar") },
      { at: 44, rect: pdialogFocus(MACHINES, 30) },
      { at: 246, rect: FULL },
    ],
    draw: (t) => ({
      app: {
        tiles: roomTiles({ each: () => ({ blockBadge: 1 }) }),
        toolbar: ptoolbar({ machines: { hover: hov(t, 26, 52), press: tap(t, 40) } }),
        bottom: pbottom(),
        overlay: (
          <MachinesDialog
            s={{
              appear: sp(t, 42, 12) * (1 - sp(t, 240, 10)),
              page: t >= 87 ? 2 : 0,
              navHover: hov(t, 74, 90),
              navHoverIndex: 2,
              allowHover: hov(t, 134, 160),
              allowPress: tap(t, 150),
              allowResult: sp(t, 162, 8),
            }}
          />
        ),
      },
    }),
  },

  // Pasta da turma com dois PCs de professor.
  t55: {
    moves: [
      { at: 30, to: pbutton("classFolder"), arc: -40 },
      { at: 190, to: pmid(FOLDER, FOLDER_CTRL.takeOver), arc: 30 },
    ],
    clicks: [70],
    cams: [
      { at: 0, rect: FULL },
      { at: 76, rect: pdialogFocus(FOLDER, 30) },
    ],
    draw: (t) => ({
      app: {
        tiles: roomTiles(),
        toolbar: ptoolbar({ classFolder: { hover: hov(t, 54, 80), press: tap(t, 70) } }),
        bottom: pbottom(),
        overlay: (
          <>
            <PcChip o={sp(t, 6, 10) * (1 - sp(t, 360, 10))} />
            <FolderDialog s={{ appear: sp(t, 72, 12), mode: "list", classes: CLASSES, selected: 1, remote: sp(t, 72, 12), takeHover: win(t, 220, 360, 6, 6) }} />
          </>
        ),
      },
    }),
  },
};
