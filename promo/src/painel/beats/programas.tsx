import { BLOCK_APPS, BLOCK_APPS_CTRL, BLOCK_SITES, BLOCK_SITES_CTRL, BlockAppsDialog, BlockSitesDialog, CLOSE_APPS, CLOSE_APPS_CTRL, CloseAppsDialog } from "../../components/app/painel/dialogs/bloqueios";
import {
  INSTALL,
  INSTALL_CTRL,
  InstallDialog,
  OPEN_PROGRAM,
  OPEN_PROGRAM_CTRL,
  OPEN_SITE,
  OPEN_SITE_CTRL,
  OpenProgramDialog,
  OpenSiteDialog,
  UNINSTALL,
  UNINSTALL_CTRL,
  UninstallDialog,
} from "../../components/app/painel/dialogs/programas";
import { EASE, ramp } from "../../lib/motion";
import type { Beat } from "../engine";
import { FULL, btn, ctrl, dlgTop, focus, groupFocus, hov, inside, sp, tap, toolbarClick, typing, win } from "../kit";
import { mark, promoVoice, tutorialVoice } from "./comum";

/** Abrir site: o mesmo endereço no navegador de todos. */
export const ABRIR_SITE: Beat = {
  id: "abrirSite",
  dur: 100,
  group: "programas",
  caption: "Abra o mesmo site em todas as máquinas.",
  voice: promoVoice("10", 4, 89, "Abra um site em todas as máquinas."),
  moves: [
    { at: 0, to: btn("openSite"), arc: -40 },
    { at: 54, to: ctrl(OPEN_SITE, OPEN_SITE_CTRL.run), arc: 30 },
  ],
  clicks: [20, 70],
  events: { siteOpen: 70 },
  cams: [
    { at: 0, rect: groupFocus("programas") },
    { at: 24, rect: dlgTop(OPEN_SITE, 340) },
    { at: 82, rect: FULL },
  ],
  draw: (t) => ({
    buttons: toolbarClick(t, "openSite", 20),
    overlay: (
      <OpenSiteDialog
        s={{ appear: win(t, 22, 88), typed: typing(t, 28, 50), runHover: hov(t, 62, 76), runPress: tap(t, 70), sent: ramp(t, 72, 86, EASE.linear) }}
      />
    ),
  }),
};

/** Abrir programa: do mesmo jeito, com o comando do programa. */
export const ABRIR_PROGRAMA: Beat = {
  id: "abrirPrograma",
  dur: 134,
  group: "programas",
  caption: "Abra programas do mesmo jeito.",
  voice: tutorialVoice("t36", 4, 134, "Abrir programa funciona do mesmo jeito, com o comando do programa.", { before: 92 }),
  moves: [
    { at: 0, to: btn("openProgram"), arc: -40 },
    { at: 76, to: ctrl(OPEN_PROGRAM, OPEN_PROGRAM_CTRL.run), arc: 30 },
  ],
  clicks: [20, 92],
  cams: [
    { at: 0, rect: groupFocus("programas") },
    { at: 24, rect: focus(OPEN_PROGRAM, 60, 20) },
  ],
  draw: (t) => ({
    buttons: toolbarClick(t, "openProgram", 20),
    overlay: (
      <OpenProgramDialog
        s={{ appear: win(t, 22, 122), typed: typing(t, 30, 70), runHover: hov(t, 84, 98), runPress: tap(t, 92), sent: ramp(t, 94, 116, EASE.linear) }}
      />
    ),
  }),
};

/** Instalar e Desinstalar programa (sem narração: não há fala gravada para eles). */
const U_GAME = mark(UNINSTALL, UNINSTALL_CTRL.row(2).x, UNINSTALL_CTRL.row(2).y);
const U_ALL = mark(UNINSTALL, UNINSTALL_CTRL.all.x, UNINSTALL_CTRL.all.y);
export const INSTALAR: Beat = {
  id: "instalar",
  dur: 138,
  group: "programas",
  caption: "Instale e desinstale programas na turma toda.",
  moves: [
    { at: 0, to: btn("install"), arc: -40 },
    { at: 20, to: ctrl(INSTALL, INSTALL_CTRL.choose), arc: 30 },
    { at: 34, to: ctrl(INSTALL, INSTALL_CTRL.run), arc: 20 },
    { at: 56, to: btn("uninstall"), arc: -30 },
    { at: 76, to: U_GAME, arc: 20 },
    { at: 90, to: U_ALL, arc: 10 },
    { at: 100, to: ctrl(UNINSTALL, UNINSTALL_CTRL.run), arc: 10 },
  ],
  clicks: [16, 30, 44, 70, 86, 98, 110],
  cams: [
    { at: 0, rect: groupFocus("programas") },
    { at: 18, rect: focus(INSTALL, 60, 20) },
    { at: 58, rect: groupFocus("programas") },
    { at: 72, rect: focus(UNINSTALL, 40, 20) },
  ],
  draw: (t) => ({
    buttons: { ...toolbarClick(t, "install", 16), ...toolbarClick(t, "uninstall", 70) },
    overlay: (
      <>
        <InstallDialog
          s={{
            appear: win(t, 18, 60),
            chosen: t >= 31 ? 1 : 0,
            chooseHover: hov(t, 24, 36),
            runHover: hov(t, 38, 50),
            runPress: tap(t, 44),
            sent: ramp(t, 46, 58, EASE.linear),
          }}
        />
        <UninstallDialog
          s={{
            appear: win(t, 72, 126),
            checks: [0, 0, sp(t, 87, 5), 0, 0, 0, 0, 0, 0, 0, 0, 0],
            all: sp(t, 99, 5),
            runHover: hov(t, 104, 116),
            runPress: tap(t, 110),
            sent: ramp(t, 112, 124, EASE.linear),
          }}
        />
      </>
    ),
  }),
};

/** Bloquear programas: marca os jogos e aplica; cada cartão avisa "3 bloqueados". */
const ROW_CLICKS = [36, 46, 56];
export const BLOQUEAR_PROGRAMAS: Beat = {
  id: "bloquearProgramas",
  dur: 112,
  group: "programas",
  caption: "Bloqueie os programas que tiram o foco.",
  voice: promoVoice("11", 4, 100, "Bloqueie os programas que tiram o foco da aula."),
  moves: [
    { at: 0, to: btn("blockApps"), arc: -40 },
    ...ROW_CLICKS.map((c, i) => ({ at: c - (i === 0 ? 12 : 8), to: mark(BLOCK_APPS, BLOCK_APPS_CTRL.row(i).x, BLOCK_APPS_CTRL.row(i).y), arc: i === 0 ? 30 : 0 })),
    { at: 60, to: ctrl(BLOCK_APPS, BLOCK_APPS_CTRL.apply), arc: 20 },
  ],
  clicks: [18, ...ROW_CLICKS, 74],
  events: { appsBlocked: 74 },
  cams: [
    { at: 0, rect: groupFocus("programas") },
    { at: 22, rect: focus(BLOCK_APPS, 40, 20) },
    { at: 92, rect: FULL },
  ],
  draw: (t) => ({
    buttons: toolbarClick(t, "blockApps", 18),
    overlay: (
      <BlockAppsDialog
        s={{
          appear: win(t, 20, 92),
          checks: [...ROW_CLICKS.map((c) => sp(t, c + 1, 5)), 0, 0],
          applyHover: hov(t, 68, 80),
          applyPress: tap(t, 74),
          sent: ramp(t, 76, 88, EASE.linear),
        }}
      />
    ),
  }),
};

/** Bloquear sites: a internet inteira ou só um site; quem tenta abrir vê a página recusada. */
export const BLOQUEAR_SITES: Beat = {
  id: "bloquearSites",
  dur: 108,
  group: "programas",
  caption: "Bloqueie a internet inteira ou só alguns sites.",
  voice: promoVoice("12", 4, 100, "Bloqueie a internet ou só alguns sites."),
  moves: [
    { at: 0, to: btn("blockSites"), arc: -40 },
    { at: 24, to: ctrl(BLOCK_SITES, BLOCK_SITES_CTRL.internet), arc: 30 },
    { at: 44, to: ctrl(BLOCK_SITES, BLOCK_SITES_CTRL.input, -120), arc: 20 },
    { at: 70, to: ctrl(BLOCK_SITES, BLOCK_SITES_CTRL.site), arc: 10 },
  ],
  clicks: [18, 54, 86],
  events: { siteBlocked: 86 },
  cams: [
    { at: 0, rect: groupFocus("programas") },
    { at: 22, rect: dlgTop(BLOCK_SITES, 400) },
    { at: 96, rect: inside({ x: 300, y: 112, w: 1000, h: 474 }) },
  ],
  draw: (t) => ({
    buttons: toolbarClick(t, "blockSites", 18),
    overlay: (
      <BlockSitesDialog
        s={{ appear: win(t, 20, 98), typed: typing(t, 56, 70), siteHover: hov(t, 78, 92), sitePress: tap(t, 86), sent: ramp(t, 88, 98, EASE.linear) }}
      />
    ),
  }),
};

/** Fechar programas: o aluno vê a contagem para salvar, e os programas fecham. */
export const FECHAR: Beat = {
  id: "fechar",
  dur: 154,
  group: "programas",
  caption: "Feche os programas, com aviso aos alunos.",
  voice: promoVoice("13", 6, 145, "E feche os programas da turma, com um aviso para o aluno salvar o trabalho."),
  moves: [
    { at: 0, to: btn("closeApps"), arc: -40 },
    { at: 26, to: ctrl(CLOSE_APPS, CLOSE_APPS_CTRL.run), arc: 30 },
  ],
  clicks: [18, 40],
  events: { countdown: 40 },
  cams: [
    { at: 0, rect: groupFocus("programas") },
    { at: 22, rect: dlgTop(CLOSE_APPS, 370) },
    { at: 60, rect: inside({ x: 0, y: 112, w: 1010, h: 474 }) },
    { at: 136, rect: FULL },
  ],
  draw: (t) => ({
    buttons: toolbarClick(t, "closeApps", 18),
    overlay: <CloseAppsDialog s={{ appear: win(t, 20, 60), runHover: hov(t, 34, 46), runPress: tap(t, 40), sent: ramp(t, 42, 56, EASE.linear) }} />,
  }),
};
