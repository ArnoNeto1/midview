import { POWER, PowerDialog, SCHEDULE, SCHEDULE_CTRL, ScheduleDialog } from "../../components/app/painel/dialogs/energia";
import { ramp } from "../../lib/motion";
import type { Beat } from "../engine";
import { FULL, btn, closeX, ctrl, dlgTop, focus, groupFocus, hov, inside, sp, tap, toolbarClick, win } from "../kit";
import { mark, promoVoice, tutorialVoice } from "./comum";

/** A sala ao vivo: a janela entra e as telas chegam. */
export const SALA: Beat = {
  id: "sala",
  dur: 185,
  eyebrow: "Sala de aula",
  caption: "Todas as telas da turma, ao vivo.",
  // Começa depois de a fala 01 da abertura terminar (frame 192 do vídeo).
  voice: promoVoice("02", 42, 136, "Na janela principal, você vê ao vivo a tela de cada aluno."),
  events: { tiles: 16 },
  cams: [
    { at: 0, rect: FULL },
    { at: 100, rect: inside({ x: 0, y: 112, w: 1010, h: 474 }) },
    { at: 166, rect: FULL },
  ],
  draw: () => ({}),
};

/** Ligar: as duas máquinas desligadas acordam pela rede. */
export const LIGAR: Beat = {
  id: "ligar",
  dur: 118,
  group: "energia",
  caption: "Ligue as máquinas pela rede.",
  voice: promoVoice("14", 6, 109, "Ligue as máquinas pela rede, sem sair da sua mesa."),
  moves: [{ at: 0, to: btn("wake"), arc: -40 }],
  clicks: [26],
  events: { wake: 26 },
  cams: [
    { at: 0, rect: groupFocus("energia") },
    { at: 34, rect: FULL },
  ],
  draw: (t) => ({ buttons: toolbarClick(t, "wake", 26) }),
};

/** Desligar e Reiniciar, com o aviso ao aluno: um ou cinco minutos, ou agora. */
const P_RESTART = mark(POWER, 122, 111);
const P_WAIT1 = mark(POWER, 37, 164);
const P_WAIT5 = mark(POWER, 37, 185);
const P_NOW = mark(POWER, 37, 206);
export const DESLIGAR: Beat = {
  id: "desligar",
  dur: 252,
  group: "energia",
  caption: "Desligue ou reinicie, com aviso ao aluno.",
  voice: tutorialVoice("t16", 8, 235, "Desligar e Reiniciar avisam o aluno antes: escolha esperar um ou cinco minutos, ou agir na hora."),
  moves: [
    { at: 0, to: btn("powerOff"), arc: -30 },
    { at: 44, to: P_RESTART, arc: 30 },
    { at: 112, to: { x: P_WAIT1.x + 40, y: P_WAIT1.y }, arc: 20 },
    { at: 150, to: P_WAIT5, arc: 10 },
    { at: 194, to: P_NOW, arc: 10 },
    { at: 222, to: closeX(POWER), arc: -30 },
  ],
  clicks: [22, 64, 168, 212, 240],
  cams: [
    { at: 0, rect: groupFocus("energia") },
    { at: 26, rect: dlgTop(POWER, 300) },
  ],
  draw: (t) => ({
    buttons: toolbarClick(t, "powerOff", 22),
    overlay: (
      <PowerDialog
        s={{
          appear: win(t, 24, 242),
          restart: t >= 65 ? 1 : 0,
          wait: t >= 213 ? 2 : t >= 169 ? 1 : 0,
          closeHover: hov(t, 232, 246),
        }}
      />
    ),
  }),
};

/** Agendar: os dias, a hora de ligar e de desligar e a régua do dia. */
const DAY_CLICKS = [44, 52, 60, 68, 76];
export const AGENDAR: Beat = {
  id: "agendar",
  dur: 150,
  group: "energia",
  caption: "Agende quando o laboratório liga e desliga.",
  voice: promoVoice("15", 8, 136, "E agende o horário para o laboratório ligar e desligar sozinho."),
  moves: [
    { at: 0, to: btn("schedule"), arc: -30 },
    ...DAY_CLICKS.map((c, i) => ({ at: c - (i === 0 ? 12 : 8), to: ctrl(SCHEDULE, SCHEDULE_CTRL.day(i)), arc: i === 0 ? 30 : 0 })),
    { at: 98, to: ctrl(SCHEDULE, SCHEDULE_CTRL.save), arc: 30 },
  ],
  clicks: [22, ...DAY_CLICKS, 118],
  cams: [
    { at: 0, rect: groupFocus("energia") },
    { at: 26, rect: dlgTop(SCHEDULE, 450) },
    { at: 96, rect: focus(SCHEDULE, 60, 30) },
  ],
  draw: (t) => ({
    buttons: toolbarClick(t, "schedule", 22),
    overlay: (
      <ScheduleDialog
        s={{
          appear: win(t, 24, 138),
          days: [0, 1, 2, 3, 4, 5, 6].map((d) => (d < 5 ? sp(t, DAY_CLICKS[d] + 1, 6) : 0)),
          on: "07:00",
          off: "18:00",
          ruler: ramp(t, 82, 100),
          saveHover: hov(t, 110, 126),
          savePress: tap(t, 118),
          saved: sp(t, 120, 8),
        }}
      />
    ),
  }),
};
