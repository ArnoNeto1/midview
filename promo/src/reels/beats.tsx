import { SCHEDULE, SCHEDULE_CTRL, ScheduleDialog } from "../components/app/painel/dialogs/energia";
import { BLOCK_APPS, BLOCK_APPS_CTRL, BLOCK_SITES, BLOCK_SITES_CTRL, BlockAppsDialog, BlockSitesDialog } from "../components/app/painel/dialogs/bloqueios";
import { COLLECT, COLLECT_CTRL, CollectDialog, SEND_FILE, SEND_FILE_CTRL, SendFileDialog } from "../components/app/painel/dialogs/turma";
import type { Box } from "../components/app/geometry";
import { P_CAPTION } from "../components/app/painel/geometry";
import { EASE, ramp } from "../lib/motion";
import { mark } from "../painel/beats/comum";
import type { Beat } from "../painel/engine";
import { btn, ctrl, dlgTop, focus, hov, inside, sp, tap, toolbarClick, typing, win } from "../painel/kit";

/**
 * O roteiro do Reels (1080×1920): as funções que mais chamam a atenção, cada
 * uma em poucos segundos, com a câmera bem perto da janela do Painel. Sem
 * narração (o professor põe a música no Instagram).
 */

/** Duas colunas por três linhas de telas, a partir da coluna `col`. */
const tiles = (col: number): Box => ({ x: 14 + col * 302 - 10, y: 133, w: 616, h: 609 });
/** A barra de botões em volta de um ponto, com as telas embaixo. */
const bar = (cx: number): Box => inside({ x: cx - 330, y: 0, w: 660, h: 620 });
/** Três colunas de telas: o primeiro quadro já mostra a sala ao vivo. */
export const REELS_START: Box = { x: 4, y: 133, w: 920, h: 609 };

/** Abertura: as 15 telas ao vivo, e a câmera chega perto. */
const TELAS: Beat = {
  id: "telas",
  dur: 110,
  eyebrow: "Sala de aula",
  caption: "Veja a tela de todos os alunos, ao vivo.",
  events: { tiles: -60, wake: -400 },
  cams: [
    { at: 8, rect: tiles(0) },
    { at: 62, rect: tiles(3) },
  ],
  draw: () => ({}),
};

/** Bloquear telas: um clique e a turma toda vê o cadeado. */
const BLOQUEAR: Beat = {
  id: "bloquear",
  dur: 140,
  group: "tela",
  caption: "Bloqueie todas as telas com um clique.",
  moves: [{ at: 0, to: btn("lock"), arc: -40 }],
  clicks: [28, 128],
  events: { lockOn: 28, lockOff: 128 },
  cams: [
    { at: 0, rect: bar(btn("lock").x) },
    { at: 36, rect: tiles(1) },
  ],
  draw: (t) => ({ buttons: { lock: { hover: hov(t, 20, 134), press: Math.max(tap(t, 28), tap(t, 128)) } } }),
};

/** Enviar arquivo e Recolher arquivos. */
const ARQUIVOS: Beat = {
  id: "arquivos",
  dur: 200,
  group: "turma",
  caption: "Envie e recolha arquivos da turma toda.",
  moves: [
    { at: 0, to: btn("sendFile"), arc: -40 },
    { at: 30, to: ctrl(SEND_FILE, SEND_FILE_CTRL.choose), arc: 30 },
    { at: 52, to: ctrl(SEND_FILE, SEND_FILE_CTRL.send), arc: 20 },
    { at: 100, to: btn("collect"), arc: -40 },
    { at: 128, to: ctrl(COLLECT, COLLECT_CTRL.run), arc: 30 },
  ],
  clicks: [22, 44, 66, 120, 142],
  events: { fileSent: 66, collected: 142 },
  cams: [
    { at: 0, rect: bar(btn("sendFile").x) },
    { at: 24, rect: dlgTop(SEND_FILE, 380, 30, 30) },
    { at: 74, rect: tiles(0) },
    { at: 100, rect: bar(btn("collect").x) },
    { at: 122, rect: focus(COLLECT, 30, 20) },
  ],
  draw: (t) => ({
    buttons: { ...toolbarClick(t, "sendFile", 22), ...toolbarClick(t, "collect", 120) },
    overlay: (
      <>
        <SendFileDialog
          s={{
            appear: win(t, 24, 72),
            chosen: t >= 45 ? 1 : 0,
            openFolder: 0,
            chooseHover: hov(t, 38, 50),
            sendHover: hov(t, 60, 72),
            sendPress: tap(t, 66),
            sent: ramp(t, 68, 80, EASE.linear),
          }}
        />
        <CollectDialog s={{ appear: win(t, 122, 188), runHover: hov(t, 136, 148), runPress: tap(t, 142), progress: ramp(t, 144, 176, EASE.linear) }} />
      </>
    ),
  }),
};

/** Bloquear programas e Bloquear sites. */
const ROWS = [40, 50, 60];
const SITE_TILE: Box = { x: 14 + 3 * 302 - 8, y: 135, w: 310, h: 207 };
const BLOQUEIOS: Beat = {
  id: "bloqueios",
  dur: 250,
  group: "programas",
  caption: "Bloqueie os programas e os sites que tiram o foco.",
  moves: [
    { at: 0, to: btn("blockApps"), arc: -40 },
    ...ROWS.map((c, i) => ({ at: c - (i === 0 ? 14 : 8), to: mark(BLOCK_APPS, BLOCK_APPS_CTRL.row(i).x, BLOCK_APPS_CTRL.row(i).y), arc: i === 0 ? 30 : 0 })),
    { at: 64, to: ctrl(BLOCK_APPS, BLOCK_APPS_CTRL.apply), arc: 20 },
    { at: 122, to: btn("blockSites"), arc: -40 },
    { at: 150, to: ctrl(BLOCK_SITES, BLOCK_SITES_CTRL.input, -120), arc: 20 },
    { at: 184, to: ctrl(BLOCK_SITES, BLOCK_SITES_CTRL.site), arc: 10 },
  ],
  clicks: [20, ...ROWS, 80, 142, 160, 200],
  events: { appsBlocked: 80, siteBlocked: 204 },
  cams: [
    { at: 0, rect: bar(btn("blockApps").x) },
    { at: 22, rect: { x: BLOCK_APPS.x + 4, y: BLOCK_APPS.y + P_CAPTION + 112, w: 600, h: 470 } },
    { at: 94, rect: tiles(0) },
    { at: 122, rect: bar(btn("blockSites").x) },
    { at: 146, rect: dlgTop(BLOCK_SITES, 330, 20, 20) },
    { at: 210, rect: SITE_TILE },
  ],
  draw: (t) => ({
    buttons: { ...toolbarClick(t, "blockApps", 20), ...toolbarClick(t, "blockSites", 142) },
    overlay: (
      <>
        <BlockAppsDialog
          s={{
            appear: win(t, 22, 90),
            checks: [...ROWS.map((c) => sp(t, c + 1, 5)), 0, 0],
            applyHover: hov(t, 74, 86),
            applyPress: tap(t, 80),
            sent: ramp(t, 82, 90, EASE.linear),
          }}
        />
        <BlockSitesDialog
          s={{ appear: win(t, 144, 210), typed: typing(t, 164, 182), siteHover: hov(t, 194, 206), sitePress: tap(t, 200), sent: ramp(t, 202, 210, EASE.linear) }}
        />
      </>
    ),
  }),
};

/** Agendar: os dias, a hora de ligar e de desligar e a régua do dia. */
const DAYS = [44, 52, 60, 68, 76];
const AGENDAR: Beat = {
  id: "agendar",
  dur: 170,
  group: "energia",
  caption: "Agende quando o laboratório liga e desliga.",
  moves: [
    { at: 0, to: btn("schedule"), arc: -40 },
    ...DAYS.map((c, i) => ({ at: c - (i === 0 ? 12 : 8), to: ctrl(SCHEDULE, SCHEDULE_CTRL.day(i)), arc: i === 0 ? 30 : 0 })),
    { at: 104, to: ctrl(SCHEDULE, SCHEDULE_CTRL.save), arc: 30 },
  ],
  clicks: [22, ...DAYS, 124],
  cams: [
    { at: 0, rect: bar(btn("schedule").x) },
    { at: 26, rect: dlgTop(SCHEDULE, 450, 20, 20) },
    { at: 100, rect: focus(SCHEDULE, 20, 20) },
  ],
  draw: (t) => ({
    buttons: toolbarClick(t, "schedule", 22),
    overlay: (
      <ScheduleDialog
        s={{
          appear: win(t, 24, 999),
          days: [0, 1, 2, 3, 4, 5, 6].map((d) => (d < 5 ? sp(t, DAYS[d] + 1, 6) : 0)),
          on: "07:00",
          off: "18:00",
          ruler: ramp(t, 82, 100),
          saveHover: hov(t, 116, 132),
          savePress: tap(t, 124),
          saved: sp(t, 126, 8),
        }}
      />
    ),
  }),
};

export const REELS_BEATS: Beat[] = [TELAS, BLOQUEAR, ARQUIVOS, BLOQUEIOS, AGENDAR];
