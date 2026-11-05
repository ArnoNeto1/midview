import type { TileItem } from "../components/app/AppWindow";
import type { BottomBarProps } from "../components/app/BottomBar";
import type { ButtonId } from "../components/app/geometry";
import { IDLE_TILE, type TileState } from "../components/app/MachineTile";
import { P_FIT, P_OFFLINE, PAINEL_PCS, pSliderValue, pTileBox, type PTileLayout } from "../components/app/painel/geometry";
import type { PainelTitle } from "../components/app/painel/TitleBar";
import type { ButtonVisual } from "../components/app/Toolbar";
import { PG } from "../components/app/painel/palette";
import { ramp } from "../lib/motion";
import { sp, win } from "./kit";

/**
 * O estado da sala que continua de um trecho para o outro (máquinas ligadas,
 * telas bloqueadas, aula da Pasta da turma, programas bloqueados...), a partir
 * dos acontecimentos de cada trecho (Beat.events) em frames absolutos.
 */
export type Events = Record<string, number>;

const NEVER = 1e9;
const at = (ev: Events, name: string) => ev[name] ?? NEVER;

export const FILE_TOAST = "O professor enviou o arquivo “atividade-3.pdf”.";
export const COLLECT_TOAST = "O professor recolheu 2 arquivo(s) da pasta “Entregar ao professor”.";
const COUNTDOWN_TEXT = "O professor vai fechar seus programas em 1 minuto. Salve seu trabalho agora";
const DONE_TEXT = "O professor fechou seus programas.";

/** Quanto a máquina `i` está ligada (as desligadas acordam depois do "Ligar"). */
const onlineOf = (abs: number, ev: Events, i: number) => {
  if (!P_OFFLINE.includes(i)) return 1;
  return sp(abs, at(ev, "wake") + 40 + P_OFFLINE.indexOf(i) * 12, 14);
};

export const worldTiles = (abs: number, ev: Events, layout: PTileLayout = P_FIT, offline = true): TileItem[] => {
  const lock = sp(abs, at(ev, "lockOn") + 2, 10) * (1 - sp(abs, at(ev, "lockOff") + 2, 10));
  const closing = at(ev, "countdown");
  const closed = sp(abs, closing + 60, 12);
  const left = Math.max(0, 59 - ((abs - closing) * 59) / 60);
  const fileToast = abs < at(ev, "collected");
  return PAINEL_PCS.map((_, i) => {
    const online = offline ? onlineOf(abs, ev, i) : 1;
    const isOff = online < 0.5;
    const reopen = sp(abs, at(ev, "appsReopen") + i * 5, 12);
    const state: TileState = {
      ...IDLE_TILE,
      online,
      status: abs < at(ev, "wake") + 12 ? "Sem resposta" : "Conectando...",
      appear: sp(abs, at(ev, "tiles") + i * 2.5, 12),
      apps: 1 - closed * (1 - reopen),
      lock: lock * online,
      lockBadge: lock * online,
      message: win(abs, at(ev, "messageSent") + 6 + i * 0.8, at(ev, "messageSent") + 62),
      chat: win(abs, at(ev, "chatOpen") + 6 + i, at(ev, "chatClose")),
      toast:
        win(abs, at(ev, "fileSent") + 4 + i, at(ev, "fileSent") + 52) + win(abs, at(ev, "collected") + 6 + i, at(ev, "collected") + 46),
      toastText: fileToast ? FILE_TOAST : COLLECT_TOAST,
      inbox: win(abs, at(ev, "fileSent") + 4, at(ev, "fileSent") + 70),
      classFolder: sp(abs, at(ev, "lesson") + i, 14) + sp(abs, at(ev, "lesson") + 40, 30),
      website: sp(abs, at(ev, "siteOpen") + 4 + i * 1.5, 10) * (1 - closed),
      blocked: i === 3 || i === 8 ? win(abs, at(ev, "siteBlocked") + (i === 8 ? 6 : 0), at(ev, "siteBlocked") + 44) : 0,
      blockBadge: sp(abs, at(ev, "appsBlocked") + 4 + i * 1.5, 10),
      blockText: "3 bloqueados",
      countdown: win(abs, closing + 2 + i * 0.5, closing + 88),
      countdownClock: abs < closing + 60 ? `0:${String(Math.ceil(left)).padStart(2, "0")}` : "0:00",
      countdownText: abs < closing + 60 ? COUNTDOWN_TEXT : DONE_TEXT,
    };
    if (isOff) state.lockBadge = 0;
    return { index: i, box: pTileBox(i, layout), state };
  });
};

const STATUS = (ev: Events): [number, string][] => [
  [0, ""],
  [at(ev, "wake") + 1, "Enviando o pedido para ligar..."],
  [at(ev, "wake") + 12, "Pedido para ligar enviado a 2 máquinas. Elas aparecem na grade em 1 ou 2 minutos."],
  [at(ev, "select"), `Selecionada: ${PAINEL_PCS[2].name}`],
  [at(ev, "viewOpen"), `Conectando a ${PAINEL_PCS[2].name}...`],
  [at(ev, "viewOpen") + 14, `Selecionada: ${PAINEL_PCS[2].name}`],
  [at(ev, "deselect"), ""],
  [at(ev, "lockOn") + 2, `Telas bloqueadas: ${PAINEL_PCS.length} de ${PAINEL_PCS.length}.`],
  [at(ev, "lockOff") + 2, "Telas desbloqueadas."],
  [at(ev, "lockOff") + 50, ""],
];

export const worldBottom = (abs: number, ev: Events, layout: PTileLayout = P_FIT): BottomBarProps => {
  let status = "";
  for (const [frame, text] of [...STATUS(ev)].sort((a, b) => a[0] - b[0])) if (abs >= frame) status = text;
  return {
    status,
    onlineOnly: 0,
    slider: pSliderValue(layout.screenW),
    fitHover: 0,
    fitPress: 0,
    onlineHover: 0,
    sliderGrab: 0,
    fitOn: 1,
  };
};

export const worldButtons = (abs: number, ev: Events): Partial<Record<ButtonId, ButtonVisual>> => {
  const selected = abs >= at(ev, "select") && abs < at(ev, "deselect");
  const locked = abs >= at(ev, "lockOn") + 2 && abs < at(ev, "lockOff") + 2;
  const lessonIn = sp(abs, at(ev, "lesson"), 12);
  return {
    view: { disabled: !selected },
    control: { disabled: !selected },
    lock: locked ? { label: "Desbloquear telas", badge: PG.unlock } : {},
    classFolder: lessonIn > 0.01 ? { caption: "7º A", captionIn: lessonIn } : {},
  };
};

export const worldTitle = (abs: number, ev: Events, tiles: TileItem[]): PainelTitle => ({
  lesson: abs >= at(ev, "lesson") ? "7º A" : undefined,
  lessonIn: sp(abs, at(ev, "lesson"), 12),
  online: tiles.filter((t) => t.state.online > 0.5).length,
  total: tiles.length,
});

/** Seleção do ALUNO-03 (clicado antes de "Ver a tela"). */
export const worldSelected = (abs: number, ev: Events) => (i: number) =>
  i === 2 ? sp(abs, at(ev, "select"), 6) * (1 - ramp(abs, at(ev, "deselect"), at(ev, "deselect") + 6)) : 0;

/** A sala parada, com as 15 máquinas ligadas e, se `lesson`, a aula do 7º A (encerramento e Reels). */
export const idlePainelApp = (abs: number, lesson = true) => {
  const ev: Events = { tiles: -1000, ...(lesson ? { lesson: -1000 } : {}) };
  const tiles = worldTiles(abs, ev, P_FIT, false);
  return {
    tiles,
    toolbar: { buttons: worldButtons(abs, ev), spotlight: null },
    bottom: worldBottom(abs, ev),
    title: worldTitle(abs, ev, tiles),
  };
};
