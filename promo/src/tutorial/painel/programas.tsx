import {
  BLOCK_APPS,
  BLOCK_APPS_CTRL,
  BLOCK_SITES,
  BLOCK_SITES_CTRL,
  BlockAppsDialog,
  BlockSitesDialog,
  CLOSE_APPS,
  CLOSE_APPS_CTRL,
  CLOSE_APPS_MODE,
  CloseAppsDialog,
} from "../../components/app/painel/dialogs/bloqueios";
import { PMenu, savedMenuAt } from "../../components/app/painel/dialogs/menus";
import {
  INSTALL,
  INSTALL_CTRL,
  InstallDialog,
  OPEN_PROGRAM,
  OpenProgramDialog,
  OPEN_SITE,
  OPEN_SITE_CTRL,
  OpenSiteDialog,
} from "../../components/app/painel/dialogs/programas";
import { typed } from "../../components/app/painel/kit";
import { ramp } from "../../lib/motion";
import { FULL, hov, sp, tap, typing, win, type Beat } from "../kit";
import { OFF_LATE, inside, pbottom, pbutton, pdialogFocus, pgrid, pgroupFocus, pmid, pspot, ptoolbar } from "./kit";

const OFF = OFF_LATE;
const ASK_TEXT = "O professor vai fechar seus programas. Salve seu trabalho agora.";
const DONE_TEXT = "O professor fechou seus programas.";
const bar = (t: number, d: number, extra: Parameters<typeof ptoolbar>[0] = {}) => ptoolbar(extra, false, pspot("programas", t, d));
const SAVED = ["Atividade 3", "-", "Outro site..."];
const SAVED_AT = savedMenuAt("openSite");

export const PROGRAMAS_P: Record<string, Beat> = {
  // "Abrir site" com "Salvar no menu".
  t35: {
    moves: [
      { at: 10, to: pbutton("openSite"), arc: -30 },
      { at: 110, to: pmid(OPEN_SITE, { x: 37, y: 228, w: 16, h: 16 }), arc: 30 },
      { at: 178, to: pmid(OPEN_SITE, OPEN_SITE_CTRL.run), arc: 30 },
    ],
    clicks: [34, 132, 212],
    cams: [
      { at: 0, rect: pgroupFocus("programas") },
      { at: 38, rect: pdialogFocus(OPEN_SITE, 60) },
      { at: 232, rect: FULL },
    ],
    draw: (t, d) => ({
      split: sp(t, 236, 18),
      student: { pc: 2, browser: { p: sp(t, 240, 10), url: "escola.edu.br/atividade" } },
      app: {
        tiles: pgrid({ offline: OFF, each: (i) => ({ website: sp(t, 218 + i * 1.5, 10) }) }),
        toolbar: bar(t, d, { openSite: { hover: hov(t, 22, 46), press: tap(t, 34) } }),
        bottom: pbottom(),
        overlay: (
          <OpenSiteDialog
            s={{
              appear: sp(t, 36, 12) * (1 - sp(t, 228, 10)),
              typed: typing(t, 54, 100),
              save: t >= 132 ? 1 : 0,
              name: typed("Atividade 3", typing(t, 140, 170)),
              runHover: hov(t, 198, 222),
              runPress: tap(t, 212),
              sent: ramp(t, 214, 228),
            }}
          />
        ),
      },
    }),
  },

  // O site no menu do botão; "Abrir programa".
  t36: {
    moves: [
      { at: 20, to: pbutton("openSite"), arc: -20 },
      { at: 66, to: { x: SAVED_AT.x + 90, y: SAVED_AT.y + 8 + 15 }, arc: 10 },
      { at: 140, to: pbutton("openProgram"), arc: -20 },
    ],
    clicks: [46, 170],
    cams: [
      { at: 0, rect: pgroupFocus("programas") },
      { at: 176, rect: pdialogFocus(OPEN_PROGRAM, 50) },
    ],
    draw: (t, d) => ({
      split: 1 - sp(t, 10, 18),
      student: { pc: 2, browser: { p: 1, url: "escola.edu.br/atividade" } },
      app: {
        tiles: pgrid({ offline: OFF, each: () => ({ website: 1 }) }),
        toolbar: bar(t, d, { openSite: { hover: hov(t, 34, 60), press: tap(t, 46) }, openProgram: { hover: hov(t, 158, 182), press: tap(t, 170) } }),
        bottom: pbottom(),
        overlay: (
          <>
            <PMenu at={SAVED_AT} items={SAVED} width={170} appear={sp(t, 48, 8) * (1 - sp(t, 132, 6))} hover={t >= 84 && t < 134 ? "Atividade 3" : null} />
            <OpenProgramDialog s={{ appear: sp(t, 172, 12), typed: typing(t, 190, 236), sent: 0 }} />
          </>
        ),
      },
    }),
  },

  // "Instalar programa" (sem fala gravada: só a legenda).
  p01: {
    min: 190,
    moves: [
      { at: 0, to: pbutton("install"), arc: -30 },
      { at: 40, to: pmid(INSTALL, INSTALL_CTRL.choose), arc: 30 },
      { at: 78, to: pmid(INSTALL, INSTALL_CTRL.run), arc: 20 },
    ],
    clicks: [22, 62, 98],
    cams: [
      { at: 0, rect: pgroupFocus("programas") },
      { at: 26, rect: pdialogFocus(INSTALL, 40) },
      { at: 176, rect: FULL },
    ],
    draw: (t, d) => ({
      app: {
        tiles: pgrid({ offline: OFF, each: () => ({ website: 1 }) }),
        toolbar: bar(t, d, { install: { hover: hov(t, 12, 30), press: tap(t, 22) } }),
        bottom: pbottom(),
        overlay: (
          <>
            <OpenProgramDialog s={{ appear: 1 - sp(t, 0, 10), typed: 1, sent: 0 }} />
            <InstallDialog
              s={{
                appear: sp(t, 24, 12) * (1 - sp(t, 172, 10)),
                chosen: t >= 63 ? 1 : 0,
                chooseHover: hov(t, 52, 68),
                runHover: hov(t, 90, 104),
                runPress: tap(t, 98),
                sent: ramp(t, 100, 150),
              }}
            />
          </>
        ),
      },
    }),
  },

  // "Fechar programas": pedir ou forçar.
  t37: {
    moves: [
      { at: 20, to: pbutton("closeApps"), arc: -30 },
      { at: 96, to: pmid(CLOSE_APPS, CLOSE_APPS_MODE(0), 120), arc: 30 },
      { at: 136, to: pmid(CLOSE_APPS, CLOSE_APPS_MODE(1)), arc: 10 },
      { at: 172, to: pmid(CLOSE_APPS, CLOSE_APPS_MODE(0)), arc: 10 },
      { at: 200, to: pmid(CLOSE_APPS, CLOSE_APPS_CTRL.run), arc: 30 },
    ],
    clicks: [56, 152, 188, 234],
    cams: [
      { at: 0, rect: pgroupFocus("programas") },
      { at: 60, rect: pdialogFocus(CLOSE_APPS, 50) },
    ],
    draw: (t, d) => ({
      app: {
        tiles: pgrid({ offline: OFF, each: () => ({ website: 1 - sp(t, 4, 10) }) }),
        toolbar: bar(t, d, { closeApps: { hover: hov(t, 42, 68), press: tap(t, 56) } }),
        bottom: pbottom(),
        overlay: (
          <CloseAppsDialog
            s={{
              appear: sp(t, 58, 12) * (1 - sp(t, 246, 10)),
              mode: t >= 153 && t < 189 ? 1 : 0,
              runHover: hov(t, 220, 246),
              runPress: tap(t, 234),
              sent: ramp(t, 236, 246),
            }}
          />
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
          tiles: pgrid({ offline: OFF, each: () => ({ apps, countdown: banner, countdownClock: c, countdownText: text }) }),
          toolbar: ptoolbar(),
          bottom: pbottom(),
        },
      };
    },
  },

  // "Bloquear programas".
  t39: {
    moves: [
      { at: 10, to: pbutton("blockApps"), arc: -30 },
      ...[0, 1, 2].map((i) => ({ at: 70 + i * 18, to: pmid(BLOCK_APPS, { ...BLOCK_APPS_CTRL.row(i), w: 16 }), arc: 8 })),
      { at: 140, to: pmid(BLOCK_APPS, BLOCK_APPS_CTRL.apply), arc: 30 },
    ],
    clicks: [30, 92, 110, 128, 178],
    cams: [
      { at: 0, rect: pgroupFocus("programas") },
      { at: 34, rect: pdialogFocus(BLOCK_APPS, 30) },
    ],
    draw: (t, d) => ({
      split: 1 - sp(t, 4, 18),
      student: { pc: 2, apps: 0, countdown: { p: 1 - sp(t, 0, 8), clock: "0:00", text: DONE_TEXT } },
      app: {
        tiles: pgrid({ offline: OFF, each: (i) => ({ apps: sp(t, 20 + i * 8, 12) }) }),
        toolbar: bar(t, d, { blockApps: { hover: hov(t, 18, 44), press: tap(t, 30) } }),
        bottom: pbottom(),
        overlay: (
          <BlockAppsDialog
            s={{
              appear: sp(t, 32, 12),
              checks: [0, 1, 2].map((i) => (t >= 92 + i * 18 ? 1 : 0)),
              applyHover: hov(t, 164, 190),
              applyPress: tap(t, 178),
              sent: ramp(t, 180, 200),
            }}
          />
        ),
      },
    }),
  },

  // O aviso do programa bloqueado e "3 bloqueados" na grade.
  t40: {
    cams: [
      { at: 10, rect: inside({ x: 0, y: 120, w: 1100, h: 560 }) },
      { at: 200, rect: FULL },
    ],
    draw: (t) => ({
      split: sp(t, 30, 18) * (1 - sp(t, 270, 18)),
      student: { pc: 2, apps: 1, msgbox: { p: win(t, 70, 250, 10, 10), text: "O professor bloqueou este programa: Roblox." } },
      app: {
        tiles: pgrid({ offline: OFF, each: () => ({ blockBadge: sp(t, 12, 10) }) }),
        toolbar: ptoolbar(),
        bottom: pbottom(),
        overlay: <BlockAppsDialog s={{ appear: 1 - sp(t, 4, 10), checks: [1, 1, 1], sent: 1 }} />,
      },
    }),
  },

  // "Bloquear sites".
  t41: {
    moves: [
      { at: 10, to: pbutton("blockSites"), arc: -30 },
      { at: 70, to: pmid(BLOCK_SITES, BLOCK_SITES_CTRL.input, -120), arc: 20 },
      { at: 116, to: pmid(BLOCK_SITES, BLOCK_SITES_CTRL.site), arc: 20 },
    ],
    clicks: [30, 92, 150],
    cams: [
      { at: 0, rect: pgroupFocus("programas") },
      { at: 34, rect: pdialogFocus(BLOCK_SITES, 50) },
      { at: 214, rect: FULL },
    ],
    draw: (t, d) => ({
      split: sp(t, 214, 18),
      student: { pc: 2, browser: { p: sp(t, 222, 10), url: "youtube.com", blocked: true } },
      app: {
        tiles: pgrid({ offline: OFF, each: (i) => ({ blockBadge: 1, blocked: i === 3 || i === 8 ? win(t, 220, 600) : 0 }) }),
        toolbar: bar(t, d, { blockSites: { hover: hov(t, 18, 44), press: tap(t, 30) } }),
        bottom: pbottom(),
        overlay: (
          <BlockSitesDialog
            s={{ appear: sp(t, 32, 12) * (1 - sp(t, 206, 10)), typed: typing(t, 96, 130), siteHover: hov(t, 136, 160), sitePress: tap(t, 150), sent: ramp(t, 156, 190) }}
          />
        ),
      },
    }),
  },
};
