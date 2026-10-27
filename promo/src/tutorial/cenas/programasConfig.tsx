import { PCS } from "../../components/app/pcs";
import { ramp } from "../../lib/motion";
import { LAB, LAB_ROWS, LabWindow, MACHINES, MACHINES_CTRL, MachinesWindow, UPDATE, UPDATE_CTRL, UpdateWindow, type UpdateLine } from "../janelas/configurar";
import { APP_BLOCK, APP_BLOCK_CTRL, AppBlockWindow, CLOSE_APPS, CLOSE_CTRL, CloseAppsWindow, SITE_BLOCK, SITE_BLOCK_CTRL, SiteBlockWindow } from "../janelas/programas";
import { OpenProgramWindow } from "../janelas/turma";
import { FULL, bottom, button, dialogFocus, groupFocus, hov, inside, mid, sp, tap, toolbar, typing, win, type Beat } from "../kit";
import { grid } from "./comum";

const OFF = [11];
const spot = (group: "programas" | "configurar", t: number, d: number) => ({ group, amount: win(t, 0, d - 4, 12, 12) });
const ASK_TEXT = "O professor vai fechar seus programas. Salve seu trabalho agora.";
const DONE_TEXT = "O professor fechou seus programas.";
const BROWSER_MSG = "O professor atualizou as configurações do navegador; o Chrome foi fechado para aplicar. Pode abri-lo de novo.";

/** Rolagem do Painel do laboratório até as seções de baixo. */
const LAB_SCROLL = 360;
const labPoint = (index: number, scroll: number) => ({ x: LAB.x + 16 + 414 + 44, y: LAB.y + LAB_ROWS[index].y + 10 + 14 - scroll });

const updateLines = (t: number): UpdateLine[] =>
  PCS.filter((_, i) => i !== 11).map((pc, i) => {
    if (i === 0) {
      if (t < 92) return { name: pc.name, text: "• verificando..." };
      if (t < 130) return { name: pc.name, text: `• enviando ${Math.min(100, Math.round(ramp(t, 92, 128) * 100))}%` };
      if (t < 150) return { name: pc.name, text: "• recebida; conferindo..." };
      if (t < 172) return { name: pc.name, text: "• reiniciando..." };
      return { name: pc.name, text: "✓ atualizada (2026-10-26 13:05 b1d0c70)", ok: true };
    }
    const start = 182 + i * 6;
    if (t < start) return { name: pc.name, text: "• verificando..." };
    if (t < start + 30) return { name: pc.name, text: `• enviando ${Math.round(ramp(t, start, start + 30) * 100)}%` };
    return { name: pc.name, text: "✓ atualizada (2026-10-26 13:05 b1d0c70)", ok: true };
  });

export const PROGRAMAS_CONFIG: Record<string, Beat> = {
  // "Fechar programas": pedir ou forçar.
  t37: {
    moves: [
      { at: 20, to: button("closeApps"), arc: -30 },
      { at: 96, to: mid(CLOSE_APPS, { x: 16, y: 108, w: 200, h: 20 }), arc: 30 },
      { at: 140, to: mid(CLOSE_APPS, CLOSE_CTRL.force, -180), arc: 10 },
      { at: 190, to: mid(CLOSE_APPS, CLOSE_CTRL.run), arc: 30 },
    ],
    clicks: [56, 234],
    cams: [
      { at: 0, rect: groupFocus("programas") },
      { at: 60, rect: dialogFocus(CLOSE_APPS, 70) },
    ],
    draw: (t, d) => ({
      app: {
        tiles: grid({ offline: OFF, each: () => ({ website: 1 - sp(t, 4, 10) }) }),
        toolbar: toolbar({ closeApps: { hover: hov(t, 42, 68), press: tap(t, 56) } }, false, spot("programas", t, d)),
        bottom: bottom(),
        overlay: (
          <>
            <OpenProgramWindow s={{ appear: 1 - sp(t, 2, 10), typed: 1 }} />
            <CloseAppsWindow s={{ appear: sp(t, 58, 12) * (1 - sp(t, 238, 10)), mode: 0, delay: 0, runHover: hov(t, 220, 246), runPress: tap(t, 234) }} />
          </>
        ),
      },
    }),
  },

  // A contagem no aluno; os programas fecham.
  t38: {
    cams: [{ at: 0, rect: FULL }],
    draw: (t) => {
      const left = Math.max(0, 59 - (t * 59) / 200);
      const c = t < 200 ? `0:${String(Math.ceil(left)).padStart(2, "0")}` : "0:00";
      const text = t < 200 ? ASK_TEXT : DONE_TEXT;
      const banner = sp(t, 2, 10);
      const apps = 1 - sp(t, 200, 12);
      return {
        split: sp(t, 6, 18),
        student: { pc: 2, apps, countdown: { p: banner, clock: c, text } },
        app: {
          tiles: grid({ offline: OFF, each: () => ({ apps, countdown: banner, countdownClock: c, countdownText: text }) }),
          toolbar: toolbar(),
          bottom: bottom(),
        },
      };
    },
  },

  // "Bloquear programas".
  t39: {
    moves: [
      { at: 10, to: button("blockApps"), arc: -30 },
      ...[0, 1, 2].map((i) => ({ at: 70 + i * 18, to: mid(APP_BLOCK, APP_BLOCK_CTRL.row(i), -130), arc: 8 })),
      { at: 140, to: mid(APP_BLOCK, APP_BLOCK_CTRL.apply), arc: 30 },
    ],
    clicks: [30, 92, 110, 128, 178],
    cams: [
      { at: 0, rect: groupFocus("programas") },
      { at: 34, rect: dialogFocus(APP_BLOCK, 40) },
    ],
    draw: (t, d) => ({
      split: 1 - sp(t, 4, 18),
      student: { pc: 2, apps: 0, countdown: { p: 1 - sp(t, 0, 8), clock: "0:00", text: DONE_TEXT } },
      app: {
        tiles: grid({ offline: OFF, each: (i) => ({ apps: sp(t, 20 + i * 8, 12) }) }),
        toolbar: toolbar({ blockApps: { hover: hov(t, 18, 44), press: tap(t, 30) } }, false, spot("programas", t, d)),
        bottom: bottom(),
        overlay: (
          <AppBlockWindow
            s={{ appear: sp(t, 32, 12), mode: 0, checks: [0, 1, 2].map((i) => (t >= 92 + i * 18 ? 1 : 0)), applyHover: hov(t, 164, 190), applyPress: tap(t, 178), applied: t >= 182 ? 1 : 0 }}
          />
        ),
      },
    }),
  },

  // O aviso do programa bloqueado e "3 bloqueados" na grade.
  t40: {
    cams: [
      { at: 10, rect: inside({ x: 160, y: 120, w: 1000, h: 560 }) },
      { at: 200, rect: FULL },
    ],
    draw: (t) => ({
      split: sp(t, 30, 18) * (1 - sp(t, 270, 18)),
      student: { pc: 2, apps: 1, msgbox: { p: win(t, 70, 250, 10, 10), text: "O professor bloqueou este programa: Roblox." } },
      app: {
        tiles: grid({ offline: OFF, each: () => ({ blockBadge: sp(t, 12, 10) }) }),
        toolbar: toolbar(),
        bottom: bottom(),
        overlay: <AppBlockWindow s={{ appear: 1 - sp(t, 4, 10), mode: 0, checks: [1, 1, 1], applied: 1 }} />,
      },
    }),
  },

  // "Bloquear sites".
  t41: {
    moves: [
      { at: 10, to: button("blockSites"), arc: -30 },
      { at: 70, to: mid(SITE_BLOCK, SITE_BLOCK_CTRL.site, -200), arc: 20 },
      { at: 116, to: mid(SITE_BLOCK, SITE_BLOCK_CTRL.blockSite), arc: 20 },
    ],
    clicks: [30, 92, 150],
    cams: [
      { at: 0, rect: groupFocus("programas") },
      { at: 34, rect: dialogFocus(SITE_BLOCK, 70) },
      { at: 214, rect: FULL },
    ],
    draw: (t, d) => ({
      split: sp(t, 214, 18),
      student: { pc: 2, browser: { p: sp(t, 222, 10), url: "youtube.com", blocked: true } },
      app: {
        tiles: grid({ offline: OFF, each: (i) => ({ blockBadge: 1, blocked: i === 3 || i === 8 ? win(t, 220, 600) : 0 }) }),
        toolbar: toolbar({ blockSites: { hover: hov(t, 18, 44), press: tap(t, 30) } }, false, spot("programas", t, d)),
        bottom: bottom(),
        overlay: (
          <SiteBlockWindow
            s={{ appear: sp(t, 32, 12) * (1 - sp(t, 206, 10)), typed: typing(t, 96, 130), siteHover: hov(t, 136, 160), sitePress: tap(t, 150), done: sp(t, 156, 8) }}
          />
        ),
      },
    }),
  },

  // "Painel do laboratório".
  t42: {
    moves: [{ at: 30, to: button("labPanel"), arc: -30 }],
    clicks: [80],
    cams: [
      { at: 0, rect: groupFocus("configurar") },
      { at: 86, rect: dialogFocus(LAB, 40) },
    ],
    draw: (t, d) => ({
      split: 1 - sp(t, 4, 18),
      student: { pc: 2, browser: { p: 1, url: "youtube.com", blocked: true } },
      app: {
        tiles: grid({ offline: OFF, each: () => ({ blockBadge: 1 }) }),
        toolbar: toolbar({ labPanel: { hover: hov(t, 64, 92), press: tap(t, 80) } }, false, spot("configurar", t, d)),
        bottom: bottom(),
        overlay: <LabWindow s={{ appear: sp(t, 82, 12), scroll: 0, on: [] }} />,
      },
    }),
  },

  // Os cinco passos.
  t43: {
    moves: [0, 1, 2, 3, 4].map((i) => ({ at: 30 + i * 52, to: labPoint(i, 0), arc: 10 })),
    clicks: [0, 1, 2, 3, 4].map((i) => 66 + i * 52),
    cams: [{ at: 0, rect: dialogFocus(LAB, 40) }],
    draw: (t) => {
      const current = [0, 1, 2, 3, 4].findIndex((i) => t < 66 + i * 52 + 10);
      return {
        split: sp(t, 80, 18) * (1 - sp(t, 320, 18)),
        student: { pc: 2, apps: 1, msgbox: { p: win(t, 96, 300, 10, 10), text: BROWSER_MSG } },
        app: {
          tiles: grid({ offline: OFF, each: () => ({ blockBadge: 1 }) }),
          toolbar: toolbar(),
          bottom: bottom(),
          overlay: (
            <LabWindow
              s={{
                appear: 1,
                scroll: 0,
                on: [0, 1, 2, 3, 4].map((i) => sp(t, 68 + i * 52, 8)),
                hover: current >= 0 ? current : undefined,
                press: [0, 1, 2, 3, 4].find((i) => tap(t, 66 + i * 52) > 0.5),
              }}
            />
          ),
        },
      };
    },
  },

  // Bloqueios extras e manutenção.
  t44: {
    moves: [
      { at: 96, to: labPoint(5, LAB_SCROLL), arc: 20 },
      { at: 170, to: labPoint(10, LAB_SCROLL), arc: 20 },
    ],
    cams: [{ at: 0, rect: dialogFocus(LAB, 40) }],
    draw: (t) => {
      const scroll = ramp(t, 20, 80) * LAB_SCROLL;
      return {
        app: {
          tiles: grid({ offline: OFF, each: () => ({ blockBadge: 1 }) }),
          toolbar: toolbar(),
          bottom: bottom(),
          overlay: <LabWindow s={{ appear: 1 - sp(t, 254, 10), scroll, on: [1, 1, 1, 1, 1], hover: t > 110 && t < 170 ? 5 : t >= 190 && t < 250 ? 10 : undefined }} />,
        },
      };
    },
  },

  // "Máquinas da sala" > "Atualizar as máquinas pela rede...".
  t45: {
    moves: [
      { at: 10, to: button("machines"), arc: -30 },
      { at: 96, to: mid(MACHINES, MACHINES_CTRL.update), arc: 40 },
    ],
    clicks: [44, 156],
    cams: [
      { at: 0, rect: groupFocus("configurar") },
      { at: 48, rect: dialogFocus(MACHINES, 30) },
      { at: 164, rect: dialogFocus(UPDATE, 50) },
    ],
    draw: (t, d) => ({
      app: {
        tiles: grid({ offline: OFF, each: () => ({ blockBadge: 1 }) }),
        toolbar: toolbar({ machines: { hover: hov(t, 30, 56), press: tap(t, 44) } }, false, spot("configurar", t, d)),
        bottom: bottom(),
        overlay: (
          <>
            <MachinesWindow s={{ appear: sp(t, 46, 12), status: "", cards: [], cardsP: 0, updateHover: hov(t, 140, 166), updatePress: tap(t, 156) }} />
            <UpdateWindow s={{ appear: sp(t, 160, 12), step: "", lines: [] }} />
          </>
        ),
      },
    }),
  },

  // Uma máquina primeiro; as outras depois.
  t46: {
    moves: [{ at: 4, to: mid(UPDATE, UPDATE_CTRL.run), arc: 30 }],
    clicks: [34],
    cams: [{ at: 0, rect: dialogFocus(UPDATE, 50) }],
    draw: (t) => {
      const step =
        t < 36
          ? ""
          : t < 64
            ? "Verificando as versões..."
            : t < 88
              ? "Preparando o pacote..."
              : t < 176
                ? "Atualizando a primeira máquina..."
                : t < 270
                  ? "Atualizando as outras máquinas..."
                  : "Terminado. 11 de 11 máquina(s) nesta versão.";
      return {
        app: {
          tiles: grid({ offline: OFF, each: () => ({ blockBadge: 1 }) }),
          toolbar: toolbar(),
          bottom: bottom(),
          overlay: (
            <>
              <MachinesWindow s={{ appear: 1, status: "", cards: [], cardsP: 0 }} />
              <UpdateWindow s={{ appear: 1, runHover: hov(t, 20, 46), runPress: tap(t, 34), step, lines: t < 40 ? [] : updateLines(t) }} />
            </>
          ),
        },
      };
    },
  },
};

