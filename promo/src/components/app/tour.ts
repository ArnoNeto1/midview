import { spring } from "remotion";
import { EASE, SPRING, mix, pulse, ramp } from "../../lib/motion";
import { FPS } from "../../timing";
import { cursorPosition, type CursorMove } from "../Cursor";
import type { TileItem } from "./AppWindow";
import type { BottomBarProps } from "./BottomBar";
import { CTRL, DLG, MENU, MENU_ITEMS, controlTarget } from "./dialogLayout";
import type { DialogsState } from "./Dialogs";
import {
  BOTTOM,
  LAYOUT_FIT,
  LAYOUT_SMALL,
  TOOLBAR_LAYOUT,
  WIN,
  buttonTarget,
  center,
  screenHeight,
  sliderKnob,
  sliderValue,
  tileBox,
  type Box,
  type ButtonId,
  type GroupId,
  type Point,
} from "./geometry";
import { IDLE_TILE } from "./MachineTile";
import { OFFLINE_AT_START, PCS } from "./pcs";
import type { ButtonVisual, ToolbarProps } from "./Toolbar";

/**
 * O roteiro do tour pelo app (Cenas 2 a 7), em frames absolutos do vídeo.
 * Tudo o que aparece na janela é função do frame, então as cenas se emendam
 * sem costura: cada cena só escolhe a legenda.
 */
export const E = {
  // Sala de aula
  enter: 176,
  tiles: 196,
  sliderMove: 300,
  sliderGrab: 326,
  sliderDrag: 330,
  toFit: 362,
  clickFit: 386,
  toOnlineOnly: 398,
  clickOnlineOnly: 422,
  // Tela
  telaSpot: 482,
  toTile: 486,
  clickTile: 510,
  toView: 518,
  clickView: 542,
  viewInside: 562,
  toDisconnect1: 586,
  clickDisconnect1: 610,
  toControl: 616,
  clickControl: 640,
  remote1: 656,
  remoteClick: 676,
  toDisconnect2: 682,
  clickDisconnect2: 706,
  toLock: 712,
  clickLock: 736,
  clickUnlock: 800,
  toEmpty: 806,
  clickEmpty: 828,
  // Turma
  turmaSpot: 842,
  toMessage: 846,
  clickMessage: 870,
  typeMessage: 878,
  toSend: 912,
  clickSend: 936,
  toChat: 966,
  clickChat: 990,
  chatLines: [1000, 1016, 1032],
  toChatClose: 1040,
  clickChatClose: 1062,
  toSendFile: 1066,
  clickSendFile: 1090,
  toSendFileSend: 1100,
  clickSendFileSend: 1124,
  toCollect: 1150,
  clickCollect: 1174,
  toCollectRun: 1180,
  clickCollectRun: 1204,
  closeCollect: 1244,
  toOpenSite: 1248,
  clickOpenSite: 1270,
  typeSite: 1276,
  toOpenSiteRun: 1290,
  clickOpenSiteRun: 1312,
  // Programas e sites
  progSpot: 1322,
  toBlockApps: 1326,
  clickBlockApps: 1350,
  checks: [1362, 1370, 1378],
  toBlockApply: 1382,
  clickBlockApply: 1406,
  toBlockSites: 1432,
  clickBlockSites: 1456,
  typeBlockSite: 1462,
  toBlockSite: 1474,
  clickBlockSite: 1496,
  closeBlockSites: 1510,
  blockedPages: 1514,
  toCloseApps: 1520,
  clickCloseApps: 1544,
  toCloseRun: 1550,
  clickCloseRun: 1574,
  countdown: 1580,
  appsClosed: 1640,
  doneMessageEnd: 1666,
  // Energia
  energiaSpot: 1682,
  toWake: 1686,
  reopen: 1700,
  clickWake: 1710,
  wakeSent: 1722,
  wakeAppear: 1736,
  wakeOnline: 1766,
  toSchedule: 1790,
  clickSchedule: 1814,
  days: 1822,
  toScheduleSave: 1846,
  clickScheduleSave: 1870,
  toPowerOff: 1886,
  tooltip: 1910,
  tooltipEnd: 1970,
  // Configurar
  configSpot: 1982,
  toLab: 1984,
  clickLab: 2006,
  toLabFirst: 2012,
  clickLabFirst: 2034,
  labSteps: 2040,
  toMachines: 2066,
  closeLab: 2068,
  clickMachines: 2090,
  toUpdate: 2096,
  clickUpdate: 2118,
  updateProgress: 2124,
  closeUpdate: 2166,
  toTile7: 2168,
  rightClick: 2192,
  menuHover: 2200,
} as const;

/* ---------- ajudantes de tempo ---------- */

const sp = (abs: number, at: number, dur = 12) => spring({ frame: abs - at, fps: FPS, config: SPRING.smooth, durationInFrames: dur });
const pop = (abs: number, at: number) => spring({ frame: abs - at, fps: FPS, config: SPRING.snappy });
const win = (abs: number, on: number, off: number, inDur = 10, outDur = 10) => sp(abs, on, inDur) * (1 - sp(abs, off, outDur));
const hov = (abs: number, from: number, to: number) => ramp(abs, from, from + 5) * (1 - ramp(abs, to, to + 5));
const tap = (abs: number, at: number) => pulse(abs, at, 3, 8);
const lerpBox = (a: Box, b: Box, t: number): Box => ({ x: mix(a.x, b.x, t), y: mix(a.y, b.y, t), w: mix(a.w, b.w, t), h: mix(a.h, b.h, t) });

/* ---------- pontos do cursor ---------- */

const screenCenter = (b: Box): Point => ({ x: b.x + b.w / 2, y: b.y + screenHeight(b.w) / 2 });
const SLIDER_FIT = sliderValue(LAYOUT_FIT.w);
const SLIDER_SMALL = sliderValue(LAYOUT_SMALL.w);
const VIEW_SCREEN = { x: DLG.view.x + CTRL.viewScreen.x, y: DLG.view.y + CTRL.viewScreen.y };
const TILE_RIGHT_CLICK = 6;

const MENU_H = MENU.pad * 2 + MENU_ITEMS.reduce((sum, item) => sum + (item === "-" ? MENU.separator : MENU.item), 0);
const MENU_AT: Point = (() => {
  const c = screenCenter(tileBox(TILE_RIGHT_CLICK, LAYOUT_FIT));
  return { x: c.x + 4, y: Math.min(c.y + 4, WIN.h - MENU_H - 10) };
})();
/** Centro vertical de um item do menu. */
const menuItemY = (index: number) => {
  let y = MENU_AT.y + MENU.pad;
  for (let i = 0; i < index; i++) y += MENU_ITEMS[i] === "-" ? MENU.separator : MENU.item;
  return y + MENU.item / 2;
};
const MENU_TARGET = MENU_ITEMS.indexOf("Desinstalar programa...");

export const CURSOR_START: Point = { x: WIN.w + 80, y: WIN.h + 70 };

export const CURSOR_MOVES: CursorMove[] = [
  { at: E.sliderMove, to: sliderKnob(SLIDER_FIT), arc: 40 },
  { at: E.sliderDrag, to: sliderKnob(SLIDER_SMALL), arc: 0 },
  { at: E.toFit, to: center(BOTTOM.fitAll), arc: -20 },
  { at: E.toOnlineOnly, to: center(BOTTOM.onlineOnly), arc: -14 },
  { at: E.toTile, to: screenCenter(tileBox(2, LAYOUT_FIT)), arc: 60 },
  { at: E.toView, to: buttonTarget("view"), arc: -30 },
  { at: E.viewInside, to: { x: VIEW_SCREEN.x + 560, y: VIEW_SCREEN.y + 300 }, arc: 20 },
  { at: E.toDisconnect1, to: controlTarget("view", "viewDisconnect"), arc: -20 },
  { at: E.toControl, to: buttonTarget("control"), arc: -20 },
  { at: E.remote1, to: { x: VIEW_SCREEN.x + 330, y: VIEW_SCREEN.y + 210 }, arc: 16 },
  { at: E.toDisconnect2, to: controlTarget("view", "viewDisconnect"), arc: -20 },
  { at: E.toLock, to: buttonTarget("lock"), arc: -20 },
  { at: E.toEmpty, to: screenCenter(tileBox(10, LAYOUT_FIT)), arc: 30 },
  { at: E.toMessage, to: buttonTarget("message"), arc: -40 },
  { at: E.toSend, to: controlTarget("message", "messageSend"), arc: 30 },
  { at: E.toChat, to: buttonTarget("chat"), arc: -40 },
  { at: E.toChatClose, to: controlTarget("chat", "chatClose"), arc: -20 },
  { at: E.toSendFile, to: buttonTarget("sendFile"), arc: -30 },
  { at: E.toSendFileSend, to: controlTarget("sendFile", "sendFileSend"), arc: 30 },
  { at: E.toCollect, to: buttonTarget("collect"), arc: -40 },
  { at: E.toCollectRun, to: controlTarget("collect", "collectRun"), arc: 20 },
  { at: E.toOpenSite, to: buttonTarget("openSite"), arc: -30 },
  { at: E.toOpenSiteRun, to: controlTarget("openSite", "openSiteRun"), arc: 30 },
  { at: E.toBlockApps, to: buttonTarget("blockApps"), arc: -30 },
  { at: E.checks[0] - 10, to: { x: DLG.blockApps.x + CTRL.blockAppsList.x + 120, y: DLG.blockApps.y + CTRL.blockAppsList.y + 48 }, arc: 10 },
  { at: E.toBlockApply, to: controlTarget("blockApps", "blockAppsApply"), arc: 20 },
  { at: E.toBlockSites, to: buttonTarget("blockSites"), arc: -40 },
  { at: E.toBlockSite, to: controlTarget("blockSites", "blockSitesSite"), arc: 20 },
  { at: E.toCloseApps, to: buttonTarget("closeApps"), arc: -30 },
  { at: E.toCloseRun, to: controlTarget("closeApps", "closeAppsRun"), arc: 30 },
  { at: E.toWake, to: buttonTarget("wake"), arc: -40 },
  { at: E.toSchedule, to: buttonTarget("schedule"), arc: -20 },
  { at: E.toScheduleSave, to: controlTarget("schedule", "scheduleSave"), arc: 30 },
  { at: E.toPowerOff, to: buttonTarget("powerOff"), arc: -30 },
  { at: E.toLab, to: buttonTarget("labPanel"), arc: -30 },
  { at: E.toLabFirst, to: controlTarget("labPanel", "labFirstOn"), arc: 30 },
  { at: E.toMachines, to: buttonTarget("machines"), arc: -40 },
  { at: E.toUpdate, to: controlTarget("machines", "machinesUpdate"), arc: 30 },
  { at: E.toTile7, to: screenCenter(tileBox(TILE_RIGHT_CLICK, LAYOUT_FIT)), arc: 40 },
  { at: E.menuHover, to: { x: MENU_AT.x + 120, y: menuItemY(MENU_TARGET) }, arc: 0 },
];

/** Cliques (botão esquerdo), para o aperto do cursor e a onda no ponto. */
const CLICK_FRAMES: number[] = [
  E.clickFit,
  E.clickOnlineOnly,
  E.clickTile,
  E.clickView,
  E.clickDisconnect1,
  E.clickControl,
  E.remoteClick,
  E.clickDisconnect2,
  E.clickLock,
  E.clickUnlock,
  E.clickEmpty,
  E.clickMessage,
  E.clickSend,
  E.clickChat,
  E.clickChatClose,
  E.clickSendFile,
  E.clickSendFileSend,
  E.clickCollect,
  E.clickCollectRun,
  E.clickOpenSite,
  E.clickOpenSiteRun,
  E.clickBlockApps,
  E.clickBlockApply,
  E.clickBlockSites,
  E.clickBlockSite,
  E.clickCloseApps,
  E.clickCloseRun,
  E.clickWake,
  E.clickSchedule,
  E.clickScheduleSave,
  E.clickLab,
  E.clickLabFirst,
  E.clickMachines,
  E.clickUpdate,
  E.rightClick,
];

export const cursorPress = (abs: number) =>
  Math.max(pulse(abs, E.sliderGrab, 3, 30), ...CLICK_FRAMES.map((at) => pulse(abs, at, 3, 7)));

export const CLICKS = CLICK_FRAMES;

export const cursorOpacity = (abs: number) => ramp(abs, E.sliderMove, E.sliderMove + 8);

/* ---------- câmera ---------- */

/** Mantém o enquadramento dentro da janela (sem fundo vazio nas bordas). */
const inside = (r: Box): Box => ({
  ...r,
  x: Math.min(Math.max(r.x, 0), Math.max(0, WIN.w - r.w)),
  y: Math.min(Math.max(r.y, 0), Math.max(0, WIN.h - r.h)),
});
const FULL: Box = { x: 0, y: 0, w: WIN.w, h: WIN.h };
const BOTTOM_FOCUS: Box = { x: 200, y: 140, w: 1240, h: 680 };
const dialogFocus = (b: Box): Box => inside({ x: b.x - 70, y: b.y - 50, w: b.w + 140, h: b.h + 100 });
const groupFocus = (g: GroupId): Box => {
  const gb = TOOLBAR_LAYOUT.groups[g];
  const cx = gb.x + gb.w / 2;
  return inside({ x: cx - 540, y: 0, w: 1080, h: 560 });
};
const MENU_FOCUS: Box = inside({ x: MENU_AT.x - 420, y: MENU_AT.y - 40, w: MENU.w + 640, h: MENU_H + 80 });

const CAMERA_KEYS: { at: number; rect: Box }[] = [
  { at: 0, rect: FULL },
  { at: E.sliderMove, rect: BOTTOM_FOCUS },
  { at: 440, rect: FULL },
  { at: E.telaSpot, rect: groupFocus("tela") },
  { at: E.clickView + 2, rect: dialogFocus(DLG.view) },
  { at: E.clickDisconnect1 + 2, rect: groupFocus("tela") },
  { at: E.clickControl + 2, rect: dialogFocus(DLG.view) },
  { at: E.clickDisconnect2 + 2, rect: FULL },
  { at: E.turmaSpot, rect: groupFocus("turma") },
  { at: E.clickMessage + 2, rect: dialogFocus(DLG.message) },
  { at: E.clickSend + 2, rect: FULL },
  { at: E.toChat, rect: groupFocus("turma") },
  { at: E.clickChat + 2, rect: dialogFocus(DLG.chat) },
  { at: E.clickChatClose + 2, rect: groupFocus("turma") },
  { at: E.clickSendFile + 2, rect: dialogFocus(DLG.sendFile) },
  { at: E.clickSendFileSend + 2, rect: FULL },
  { at: E.toCollect, rect: groupFocus("turma") },
  { at: E.clickCollect + 2, rect: dialogFocus(DLG.collect) },
  { at: E.closeCollect, rect: groupFocus("turma") },
  { at: E.clickOpenSite + 2, rect: dialogFocus(DLG.openSite) },
  { at: E.clickOpenSiteRun + 2, rect: FULL },
  { at: E.toBlockApps + 4, rect: groupFocus("programas") },
  { at: E.clickBlockApps + 2, rect: dialogFocus(DLG.blockApps) },
  { at: E.clickBlockApply + 2, rect: FULL },
  { at: E.toBlockSites, rect: groupFocus("programas") },
  { at: E.clickBlockSites + 2, rect: dialogFocus(DLG.blockSites) },
  { at: E.closeBlockSites, rect: FULL },
  { at: E.toCloseApps + 4, rect: groupFocus("programas") },
  { at: E.clickCloseApps + 2, rect: dialogFocus(DLG.closeApps) },
  { at: E.clickCloseRun + 2, rect: FULL },
  { at: E.toWake, rect: groupFocus("energia") },
  { at: E.clickWake + 4, rect: FULL },
  { at: E.toSchedule, rect: groupFocus("energia") },
  { at: E.clickSchedule + 2, rect: dialogFocus(DLG.schedule) },
  { at: E.clickScheduleSave + 2, rect: groupFocus("energia") },
  { at: E.toLab, rect: groupFocus("configurar") },
  { at: E.clickLab + 2, rect: dialogFocus(DLG.labPanel) },
  { at: E.closeLab, rect: groupFocus("configurar") },
  { at: E.clickMachines + 2, rect: dialogFocus(DLG.machines) },
  { at: E.clickUpdate + 2, rect: dialogFocus(DLG.update) },
  { at: E.closeUpdate, rect: FULL },
  { at: E.rightClick, rect: MENU_FOCUS },
];

/** Região da janela que a câmera enquadra; cada chave parte de onde a anterior estava. */
export const cameraRect = (abs: number): Box => {
  let rect = CAMERA_KEYS[0].rect;
  for (let i = 1; i < CAMERA_KEYS.length; i++) {
    const key = CAMERA_KEYS[i];
    if (abs < key.at) break;
    const next = CAMERA_KEYS[i + 1];
    const until = next && next.at <= abs ? next.at : abs;
    rect = lerpBox(rect, key.rect, sp(until, key.at, 30));
  }
  return rect;
};

/* ---------- barra de ferramentas ---------- */

const BUTTON_CLICKS: { id: ButtonId; at: number }[] = [
  { id: "view", at: E.clickView },
  { id: "control", at: E.clickControl },
  { id: "lock", at: E.clickLock },
  { id: "lock", at: E.clickUnlock },
  { id: "message", at: E.clickMessage },
  { id: "chat", at: E.clickChat },
  { id: "sendFile", at: E.clickSendFile },
  { id: "collect", at: E.clickCollect },
  { id: "openSite", at: E.clickOpenSite },
  { id: "blockApps", at: E.clickBlockApps },
  { id: "blockSites", at: E.clickBlockSites },
  { id: "closeApps", at: E.clickCloseApps },
  { id: "wake", at: E.clickWake },
  { id: "schedule", at: E.clickSchedule },
  { id: "labPanel", at: E.clickLab },
  { id: "machines", at: E.clickMachines },
];

const SPOTS: { group: GroupId; on: number; off: number }[] = [
  { group: "tela", on: E.telaSpot, off: 832 },
  { group: "turma", on: E.turmaSpot, off: 1314 },
  { group: "programas", on: E.progSpot, off: 1674 },
  { group: "energia", on: E.energiaSpot, off: 1974 },
  { group: "configurar", on: E.configSpot, off: 2600 },
];

export const toolbarState = (abs: number): ToolbarProps => {
  const buttons: Partial<Record<ButtonId, ButtonVisual>> = {};
  for (const { id, at } of BUTTON_CLICKS) {
    const v = (buttons[id] ??= { hover: 0, press: 0 });
    v.hover = Math.max(v.hover ?? 0, hov(abs, at - 8, at + 8));
    v.press = Math.max(v.press ?? 0, tap(abs, at));
  }
  // O cursor fica parado em "Bloquear telas" até desbloquear.
  buttons.lock = { ...buttons.lock, hover: Math.max(buttons.lock?.hover ?? 0, hov(abs, E.clickLock - 8, E.clickUnlock + 6)) };
  buttons.lock.label = abs >= E.clickLock + 2 && abs < E.clickUnlock + 2 ? "Desbloquear telas" : undefined;
  buttons.powerOff = { hover: hov(abs, E.toPowerOff + 16, E.tooltipEnd) };
  const selected = abs >= E.clickTile && abs < E.clickEmpty;
  buttons.view = { ...buttons.view, disabled: !selected };
  buttons.control = { ...buttons.control, disabled: !selected };
  const spot = SPOTS.find((s) => abs >= s.on - 2 && abs < s.off + 12);
  return {
    buttons,
    spotlight: spot ? { group: spot.group, amount: win(abs, spot.on, spot.off, 12, 12) } : null,
  };
};

/* ---------- barra de baixo ---------- */

const STATUS: [number, string][] = [
  [0, ""],
  [E.clickTile, "Selecionada: PC-03"],
  [E.clickView + 1, "Conectando a PC-03..."],
  [E.clickView + 14, ""],
  [E.clickControl + 1, "Conectando a PC-03..."],
  [E.clickControl + 14, ""],
  [E.clickLock + 2, "Telas bloqueadas: 10 de 12."],
  [E.clickUnlock + 2, "Telas desbloqueadas."],
  [E.clickWake + 2, "Enviando o pedido para ligar..."],
  [E.wakeSent, "Pedido para ligar enviado a 2 máquinas. Elas aparecem na grade em 1 ou 2 minutos."],
];

export const bottomState = (abs: number): BottomBarProps => {
  let status = "";
  for (const [at, text] of STATUS) if (abs >= at) status = text;
  const shrink = sp(abs, E.sliderDrag, 24) * (1 - sp(abs, E.clickFit + 2, 24));
  return {
    status,
    onlineOnly: sp(abs, E.clickOnlineOnly + 2, 8),
    slider: mix(SLIDER_FIT, SLIDER_SMALL, shrink),
    fitHover: hov(abs, E.clickFit - 8, E.clickFit + 8),
    fitPress: tap(abs, E.clickFit),
    onlineHover: hov(abs, E.clickOnlineOnly - 8, E.clickOnlineOnly + 8),
    sliderGrab: win(abs, E.sliderGrab, E.sliderDrag + 26, 4, 6),
  };
};

/** Destaque do vídeo em volta dos controles da barra de baixo (Cena 2). */
export const bottomSpot = (abs: number) => win(abs, E.sliderMove, 440, 12, 12);
export const BOTTOM_SPOT_BOX: Box = {
  x: BOTTOM.fitAll.x - 8,
  y: BOTTOM.y + 4,
  w: BOTTOM.larger.x + BOTTOM.larger.w - BOTTOM.fitAll.x + 16,
  h: WIN.bottomBar - 8,
};

/* ---------- cards ---------- */

const COUNTDOWN_TEXT = "O professor vai fechar seus programas em 1 minuto. Salve seu trabalho agora";
const DONE_TEXT = "O professor fechou seus programas.";
const FILE_TOAST = "O professor enviou o arquivo “atividade-3.pdf”.";
const COLLECT_TOAST = "O professor recolheu 2 arquivo(s) da pasta “Entregar ao professor”.";

export const tourTiles = (abs: number): TileItem[] => {
  const layout = sp(abs, E.sliderDrag, 26) * (1 - sp(abs, E.clickFit + 2, 26));
  const onlineOnly = sp(abs, E.clickOnlineOnly + 2, 10);
  const lock = sp(abs, E.clickLock + 2, 10) * (1 - sp(abs, E.clickUnlock + 2, 10));
  const closed = sp(abs, E.appsClosed, 12);
  const left = Math.max(0, 59 - ((abs - E.countdown) * 59) / (E.appsClosed - E.countdown));
  const clock = `0:${String(Math.ceil(left)).padStart(2, "0")}`;
  const fileToastOn = abs < E.clickCollectRun;

  return PCS.map((_, i) => {
    const box = lerpBox(tileBox(i, LAYOUT_FIT), tileBox(i, LAYOUT_SMALL), layout);
    const offline = OFFLINE_AT_START.includes(i);
    const wakeOrder = offline ? OFFLINE_AT_START.indexOf(i) : 0;
    const enter = E.tiles + i * 2.5;
    let appear = pop(abs, enter);
    let online = sp(abs, enter + 10, 14);
    let status = "Conectando...";
    let apps = 1 - closed * (1 - sp(abs, E.reopen + i * 7, 12));
    if (offline) {
      const back = sp(abs, E.wakeAppear + wakeOrder * 8, 12);
      appear *= 1 - onlineOnly * (1 - back);
      online = sp(abs, E.wakeOnline + wakeOrder * 8, 14);
      status = abs < E.wakeAppear ? "Tela indisponível" : "Conectando...";
      apps = sp(abs, E.wakeOnline + 24 + wakeOrder * 8, 12);
    }
    const live = offline ? 0 : 1;
    return {
      index: i,
      box,
      state: {
        ...IDLE_TILE,
        appear,
        online,
        status,
        apps,
        selected: i === 2 ? sp(abs, E.clickTile, 6) * (1 - sp(abs, E.clickEmpty, 6)) : 0,
        lock: lock * live,
        lockBadge: lock * live,
        control: i === 2 ? win(abs, E.clickControl + 4, E.clickDisconnect2) : 0,
        message: live * win(abs, E.clickSend + 6 + i * 0.8, E.clickSend + 68),
        chat: live * win(abs, E.clickChat + 6 + i, E.clickChatClose + 2),
        toast: live * (win(abs, E.clickSendFileSend + 6 + i, E.clickSendFileSend + 58) + win(abs, E.clickCollectRun + 8 + i, E.clickCollectRun + 52)),
        toastText: fileToastOn ? FILE_TOAST : COLLECT_TOAST,
        inbox: live * win(abs, E.clickSendFileSend + 6, E.clickSendFileSend + 80),
        website: live * sp(abs, E.clickOpenSiteRun + 4 + i * 1.5, 10) * (1 - closed),
        blocked: i === 3 || i === 8 ? win(abs, E.blockedPages + (i === 8 ? 6 : 0), E.blockedPages + 34) : 0,
        blockBadge: live * sp(abs, E.clickBlockApply + 4 + i * 1.5, 10),
        countdown: live * win(abs, E.countdown + i * 0.5, E.doneMessageEnd),
        countdownClock: abs < E.appsClosed ? clock : "0:00",
        countdownText: abs < E.appsClosed ? COUNTDOWN_TEXT : DONE_TEXT,
      },
    };
  });
};

/* ---------- janelas ---------- */

/** Item do menu embaixo do cursor (o realce segue o mouse, como no Windows). */
const menuIndexAt = (y: number) => {
  let top = MENU_AT.y + MENU.pad;
  for (let i = 0; i < MENU_ITEMS.length; i++) {
    const h = MENU_ITEMS[i] === "-" ? MENU.separator : MENU.item;
    if (y >= top && y < top + h) return MENU_ITEMS[i] === "-" ? -1 : i;
    top += h;
  }
  return -1;
};

export const dialogsState = (abs: number): DialogsState => {
  const cursor = cursorPosition(abs, FPS, CURSOR_START, CURSOR_MOVES);
  const insideMenu = abs >= E.rightClick + 6 && cursor.x > MENU_AT.x && cursor.x < MENU_AT.x + MENU.w;
  return {
    view: {
      appear: win(abs, E.clickView + 2, E.clickDisconnect1 + 2) + win(abs, E.clickControl + 2, E.clickDisconnect2 + 2),
      controlling: abs >= E.clickControl,
      disconnectHover: hov(abs, E.clickDisconnect1 - 8, E.clickDisconnect1 + 4) + hov(abs, E.clickDisconnect2 - 8, E.clickDisconnect2 + 4),
      disconnectPress: tap(abs, E.clickDisconnect1) + tap(abs, E.clickDisconnect2),
      remote: null,
      remotePress: 0,
    },
    message: {
      appear: win(abs, E.clickMessage + 2, E.clickSend + 2),
      typed: ramp(abs, E.typeMessage, E.typeMessage + 40, EASE.linear),
      sendHover: hov(abs, E.clickSend - 8, E.clickSend + 4),
      sendPress: tap(abs, E.clickSend),
    },
    chat: {
      appear: win(abs, E.clickChat + 2, E.clickChatClose + 2),
      lines: E.chatLines.map((t) => sp(abs, t, 10)),
      closeHover: hov(abs, E.clickChatClose - 8, E.clickChatClose + 4),
    },
    sendFile: {
      appear: win(abs, E.clickSendFile + 2, E.clickSendFileSend + 2),
      sendHover: hov(abs, E.clickSendFileSend - 8, E.clickSendFileSend + 4),
      sendPress: tap(abs, E.clickSendFileSend),
    },
    collect: {
      appear: win(abs, E.clickCollect + 2, E.closeCollect),
      runHover: hov(abs, E.clickCollectRun - 8, E.clickCollectRun + 4),
      runPress: tap(abs, E.clickCollectRun),
      progress: ramp(abs, E.clickCollectRun + 2, E.clickCollectRun + 30, EASE.linear),
    },
    openSite: {
      appear: win(abs, E.clickOpenSite + 2, E.clickOpenSiteRun + 2),
      typed: ramp(abs, E.typeSite, E.typeSite + 20, EASE.linear),
      runHover: hov(abs, E.clickOpenSiteRun - 8, E.clickOpenSiteRun + 4),
      runPress: tap(abs, E.clickOpenSiteRun),
    },
    blockApps: {
      appear: win(abs, E.clickBlockApps + 2, E.clickBlockApply + 2),
      checks: [...E.checks.map((t) => sp(abs, t, 6)), 0, 0],
      applyHover: hov(abs, E.clickBlockApply - 8, E.clickBlockApply + 4),
      applyPress: tap(abs, E.clickBlockApply),
    },
    blockSites: {
      appear: win(abs, E.clickBlockSites + 2, E.closeBlockSites),
      typed: ramp(abs, E.typeBlockSite, E.typeBlockSite + 18, EASE.linear),
      siteHover: hov(abs, E.clickBlockSite - 8, E.clickBlockSite + 6),
      sitePress: tap(abs, E.clickBlockSite),
    },
    closeApps: {
      appear: win(abs, E.clickCloseApps + 2, E.clickCloseRun + 2),
      runHover: hov(abs, E.clickCloseRun - 8, E.clickCloseRun + 4),
      runPress: tap(abs, E.clickCloseRun),
    },
    schedule: {
      appear: win(abs, E.clickSchedule + 2, E.clickScheduleSave + 2),
      days: [0, 1, 2, 3, 4, 5, 6].map((d) => (d < 5 ? sp(abs, E.days + d * 5, 6) : 0)),
      saveHover: hov(abs, E.clickScheduleSave - 8, E.clickScheduleSave + 4),
      savePress: tap(abs, E.clickScheduleSave),
    },
    labPanel: {
      appear: win(abs, E.clickLab + 2, E.closeLab),
      steps: [sp(abs, E.clickLabFirst + 2, 8), ...[0, 1, 2, 3].map((k) => sp(abs, E.labSteps + k * 6, 8))],
      firstHover: hov(abs, E.clickLabFirst - 8, E.clickLabFirst + 4),
      firstPress: tap(abs, E.clickLabFirst),
    },
    machines: {
      appear: win(abs, E.clickMachines + 2, E.clickUpdate + 2),
      updateHover: hov(abs, E.clickUpdate - 8, E.clickUpdate + 4),
      updatePress: tap(abs, E.clickUpdate),
    },
    update: {
      appear: win(abs, E.clickUpdate + 2, E.closeUpdate),
      progress: ramp(abs, E.updateProgress, E.updateProgress + 36, EASE.inOut),
    },
    menu: { appear: sp(abs, E.rightClick + 1, 8), at: MENU_AT, hover: insideMenu ? menuIndexAt(cursor.y) : -1 },
    tooltip: win(abs, E.tooltip, E.tooltipEnd, 6, 6),
  };
};

/** Máquina mostrada em "Ver a tela" e "Controlar". */
export const VIEW_PC = PCS[2];

/** O app parado, com as 12 máquinas online e nada selecionado (encerramento). */
export const idleApp = (): { tiles: TileItem[]; toolbar: ToolbarProps; bottom: BottomBarProps } => ({
  tiles: PCS.map((_, i) => ({ index: i, box: tileBox(i, LAYOUT_FIT), state: IDLE_TILE })),
  toolbar: { buttons: { view: { disabled: true }, control: { disabled: true } }, spotlight: null },
  bottom: { status: "", onlineOnly: 0, slider: SLIDER_FIT, fitHover: 0, fitPress: 0, onlineHover: 0, sliderGrab: 0 },
});
