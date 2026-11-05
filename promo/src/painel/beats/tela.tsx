import { VIEW, VIEW_CTRL, ViewWindow } from "../../components/app/painel/dialogs/tela";
import { PAINEL_PCS } from "../../components/app/painel/geometry";
import type { Beat } from "../engine";
import { FULL, btn, clickVisual, ctrl, focus, groupFocus, hov, inside, tap, tileAt, toolbarClick, win } from "../kit";
import { pt, promoVoice } from "./comum";

const PC = PAINEL_PCS[2];
const SCREEN = VIEW_CTRL.screen;

/** Ver a tela: seleciona o ALUNO-03 e abre a tela dele em tamanho grande. */
export const VER: Beat = {
  id: "ver",
  dur: 124,
  group: "tela",
  caption: "Veja a tela de qualquer aluno.",
  voice: promoVoice("04", 6, 106, "No grupo Tela, veja a tela de qualquer aluno."),
  moves: [
    { at: 0, to: tileAt(2), arc: 40 },
    { at: 30, to: btn("view"), arc: -30 },
    { at: 76, to: pt(VIEW, SCREEN.x + 560, SCREEN.y + 300), arc: 20 },
  ],
  clicks: [22, 50],
  events: { select: 22, viewOpen: 50 },
  cams: [
    { at: 0, rect: inside({ x: 150, y: 0, w: 1000, h: 470 }) },
    { at: 54, rect: focus(VIEW, 40, 30) },
  ],
  draw: (t, abs) => ({
    buttons: toolbarClick(t, "view", 50),
    overlay: <ViewWindow s={{ appear: win(t, 52, 999), controlling: false }} pc={PC} index={2} frame={abs} />,
  }),
};

/** Controlar: dentro da mesma janela, o professor assume o teclado e o mouse. */
export const CONTROLAR: Beat = {
  id: "controlar",
  dur: 125,
  group: "tela",
  caption: "Assuma o controle quando precisar.",
  voice: promoVoice("05", 4, 115, "Ou assuma o controle quando o aluno precisar de ajuda."),
  moves: [
    { at: 0, to: ctrl(VIEW, VIEW_CTRL.toggle), arc: -20 },
    { at: 30, to: pt(VIEW, SCREEN.x + 330, SCREEN.y + 210), arc: 16 },
    { at: 56, to: pt(VIEW, SCREEN.x + 640, SCREEN.y + 360), arc: -16 },
    { at: 88, to: ctrl(VIEW, VIEW_CTRL.disconnect(true)), arc: -20 },
  ],
  clicks: [20, 48, 74, 108],
  events: { deselect: 124 },
  cams: [{ at: 112, rect: FULL }],
  draw: (t, abs) => ({
    tiles: (i) => (i === 2 ? { control: win(t, 22, 110) } : {}),
    overlay: (
      <ViewWindow
        s={{
          appear: win(t, -20, 110),
          controlling: t >= 21,
          toggleHover: hov(t, 12, 26),
          disconnectHover: hov(t, 100, 114),
          disconnectPress: tap(t, 108),
        }}
        pc={PC}
        index={2}
        frame={abs}
      />
    ),
  }),
};

/** Bloquear telas: a turma toda com o cadeado, e "Desbloquear telas" devolve. */
export const BLOQUEAR: Beat = {
  id: "bloquear",
  dur: 98,
  group: "tela",
  caption: "Bloqueie todas as telas com um clique.",
  voice: promoVoice("06", 4, 85, "E bloqueie todas as telas com um clique."),
  moves: [{ at: 0, to: btn("lock"), arc: -30 }],
  clicks: [22, 82],
  events: { lockOn: 22, lockOff: 82 },
  cams: [
    { at: 0, rect: groupFocus("tela") },
    { at: 26, rect: FULL },
  ],
  draw: (t) => {
    const a = clickVisual(t, 22);
    const b = clickVisual(t, 82);
    return { buttons: { lock: { hover: Math.max(hov(t, 14, 90), a.hover ?? 0), press: Math.max(a.press ?? 0, b.press ?? 0) } } };
  },
};
