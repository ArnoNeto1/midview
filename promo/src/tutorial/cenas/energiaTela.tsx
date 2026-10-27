import { WIN } from "../../components/app/geometry";
import { POWER, POWER_CTRL, PowerWindow, SCHEDULE, SCHEDULE_CTRL, ScheduleWindow } from "../janelas/energia";
import { VIEW, VIEW_CTRL, ViewWindow } from "../janelas/tela";
import { FULL, bottom, button, dialogFocus, groupFocus, hov, inside, mid, sp, tap, tileCenter, toolbar, win, type Beat } from "../kit";
import { grid, statusAt } from "./comum";

const ALL_ON: number[] = [];
const SHUT_TEXT = "O professor vai desligar este computador. Salve seu trabalho agora: o que não estiver salvo será perdido.";
const clock = (secondsLeft: number) => `${Math.floor(secondsLeft / 60)}:${String(Math.max(0, Math.floor(secondsLeft % 60))).padStart(2, "0")}`;
const spot = (group: "energia" | "tela", t: number, d: number) => ({ group, amount: win(t, 0, d - 4, 12, 12) });

/** A tela cheia de "Ver a tela": a janela do Midview inteira. */
const FULL_VIEW = { x: 0, y: 0, w: WIN.w, h: WIN.h };
const lerp = (a: number, b: number, t: number) => a + (b - a) * t;

export const ENERGIA_TELA: Record<string, Beat> = {
  // "Ligar": as desligadas aparecem na grade.
  t15: {
    moves: [{ at: 60, to: button("wake"), arc: -40 }],
    clicks: [116],
    cams: [
      { at: 10, rect: groupFocus("energia") },
      { at: 170, rect: FULL },
    ],
    draw: (t, d) => {
      return {
        app: {
          tiles: grid({
            each: (i) =>
              i >= 10 ? { online: sp(t, 250 + (i - 10) * 10, 14), status: t < 200 ? "Tela indisponível" : "Conectando...", appear: 1 } : {},
          }),
          toolbar: toolbar({ wake: { hover: hov(t, 96, 128), press: tap(t, 116) } }, false, spot("energia", t, d)),
          bottom: bottom({
            status: statusAt(t, [
              [118, "Enviando o pedido para ligar..."],
              [140, "Pedido para ligar enviado a 2 máquinas. Elas aparecem na grade em 1 ou 2 minutos."],
            ]),
          }),
        },
      };
    },
  },

  // "Desligar": aviso de 1 ou 5 minutos, ou na hora.
  t16: {
    moves: [
      { at: 30, to: button("powerOff"), arc: 20 },
      { at: 110, to: mid(POWER, POWER_CTRL.wait5, -110), arc: 30 },
      { at: 168, to: mid(POWER, POWER_CTRL.run), arc: 20 },
    ],
    clicks: [60, 146, 214],
    cams: [
      { at: 0, rect: groupFocus("energia") },
      { at: 66, rect: dialogFocus(POWER, 90) },
    ],
    draw: (t, d) => ({
      app: {
        tiles: grid({ offline: ALL_ON }),
        toolbar: toolbar({ powerOff: { hover: hov(t, 44, 72), press: tap(t, 60) } }, false, spot("energia", t, d)),
        bottom: bottom(),
        overlay: (
          <PowerWindow
            s={{
              appear: sp(t, 62, 12) * (1 - sp(t, 218, 10)),
              action: "Desligar",
              delay: t >= 146 ? 1 : 0,
              runHover: hov(t, 196, 224),
              runPress: tap(t, 214),
            }}
          />
        ),
      },
    }),
  },

  // A contagem no aluno e "Cancelar desligamento".
  t17: {
    moves: [
      { at: 120, to: button("powerOff"), arc: 20 },
      { at: 176, to: mid(POWER, POWER_CTRL.cancel), arc: 30 },
    ],
    clicks: [150, 222],
    cams: [
      { at: 0, rect: inside({ x: 40, y: 120, w: 1100, h: 700 }) },
      { at: 152, rect: dialogFocus(POWER, 60) },
      { at: 240, rect: FULL },
    ],
    draw: (t) => {
      const counting = win(t, 0, 226, 1, 10);
      const c = clock(299 - t / 30);
      return {
        split: sp(t, 6, 18) * (1 - sp(t, 252, 18)),
        student: { pc: 2, countdown: { p: counting, clock: c, text: SHUT_TEXT } },
        app: {
          tiles: grid({ offline: ALL_ON, each: () => ({ countdown: counting, countdownClock: c, countdownText: SHUT_TEXT }) }),
          toolbar: toolbar({ powerOff: { hover: hov(t, 136, 160), press: tap(t, 150) } }),
          bottom: bottom(),
          overlay: (
            <PowerWindow
              s={{ appear: sp(t, 152, 12) * (1 - sp(t, 226, 10)), action: "Desligar", delay: 1, cancelHover: hov(t, 206, 232), cancelPress: tap(t, 222) }}
            />
          ),
        },
      };
    },
  },

  // "Agendar": dias e horários.
  t18: {
    moves: [
      { at: 10, to: button("schedule"), arc: -20 },
      ...[0, 1, 2, 3, 4].map((i) => ({ at: 66 + i * 12, to: mid(SCHEDULE, SCHEDULE_CTRL.day(i)), arc: 6 })),
      { at: 132, to: mid(SCHEDULE, { x: 16, y: 138, w: 18, h: 18 }), arc: 10 },
      { at: 150, to: mid(SCHEDULE, { x: 16, y: 260, w: 18, h: 18 }), arc: 10 },
      { at: 170, to: mid(SCHEDULE, SCHEDULE_CTRL.save), arc: 20 },
    ],
    clicks: [36, ...[0, 1, 2, 3, 4].map((i) => 80 + i * 12), 144, 162, 196],
    cams: [
      { at: 0, rect: groupFocus("energia") },
      { at: 40, rect: dialogFocus(SCHEDULE, 60) },
      { at: 204, rect: FULL },
    ],
    draw: (t, d) => ({
      app: {
        tiles: grid({ offline: ALL_ON }),
        toolbar: toolbar({ schedule: { hover: hov(t, 22, 46), press: tap(t, 36) } }, false, spot("energia", t, d)),
        bottom: bottom(),
        overlay: (
          <ScheduleWindow
            s={{
              appear: sp(t, 38, 12) * (1 - sp(t, 202, 10)),
              days: [0, 1, 2, 3, 4, 5, 6].map((i) => (i < 5 && t >= 80 + i * 12 ? 1 : 0)),
              wake: t >= 144 ? 1 : 0,
              off: t >= 162 ? 1 : 0,
              saveHover: hov(t, 184, 204),
              savePress: tap(t, 196),
            }}
          />
        ),
      },
    }),
  },

  // "Ver a tela".
  t19: {
    moves: [
      { at: 20, to: tileCenter(2), arc: 40 },
      { at: 80, to: button("view"), arc: -40 },
    ],
    clicks: [56, 124],
    cams: [
      { at: 0, rect: inside({ x: 120, y: 0, w: 1180, h: 680 }) },
      { at: 130, rect: dialogFocus(VIEW, 26) },
    ],
    draw: (t, d) => ({
      app: {
        tiles: grid({ offline: ALL_ON, selected: (i) => (i === 2 ? sp(t, 56, 6) : 0) }),
        toolbar: toolbar({ view: { hover: hov(t, 106, 134), press: tap(t, 124) } }, t >= 56, spot("tela", t, d)),
        bottom: bottom({ status: statusAt(t, [[56, "Selecionada: PC-03"], [126, "Conectando a PC-03..."], [140, "Selecionada: PC-03"]]) }),
        overlay: <ViewWindow s={{ appear: sp(t, 128, 14), pc: 2, controlling: false, frame: t }} />,
      },
    }),
  },

  // Só vendo; "Tela Cheia".
  t20: {
    moves: [
      { at: 40, to: mid(VIEW, VIEW_CTRL.full), arc: 30 },
      { at: 170, to: { x: 270, y: 58 }, arc: 20 },
    ],
    clicks: [110, 200],
    cams: [
      { at: 112, rect: FULL },
      { at: 204, rect: dialogFocus(VIEW, 26) },
    ],
    draw: (t) => {
      const full = sp(t, 112, 16) * (1 - sp(t, 202, 16));
      const box = {
        x: lerp(VIEW.x, FULL_VIEW.x, full),
        y: lerp(VIEW.y, FULL_VIEW.y, full),
        w: lerp(VIEW.w, FULL_VIEW.w, full),
        h: lerp(VIEW.h, FULL_VIEW.h, full),
      };
      return {
        app: {
          tiles: grid({ offline: ALL_ON, selected: (i) => (i === 2 ? 1 : 0) }),
          toolbar: toolbar({}, true),
          bottom: bottom({ status: "Selecionada: PC-03" }),
          overlay: (
            <ViewWindow
              s={{ appear: 1, box, pc: 2, controlling: false, full: t >= 112 && t < 202, fullHover: hov(t, 96, 122) + hov(t, 186, 212), fullPress: tap(t, 110) + tap(t, 200), frame: t + 300 }}
            />
          ),
        },
      };
    },
  },

  // "Controlar": o aviso vermelho no aluno.
  t21: {
    moves: [
      { at: 10, to: mid(VIEW, VIEW_CTRL.control), arc: 20 },
      { at: 110, to: { x: VIEW.x + 520, y: VIEW.y + 330 }, arc: 40 },
      { at: 170, to: { x: VIEW.x + 330, y: VIEW.y + 260 }, arc: 20 },
      { at: 250, to: mid(VIEW, VIEW_CTRL.disconnect), arc: -30 },
    ],
    clicks: [40, 200, 300],
    cams: [{ at: 0, rect: dialogFocus(VIEW, 26) }],
    draw: (t) => {
      const controlling = t >= 42;
      return {
        split: sp(t, 60, 18) * (1 - sp(t, 304, 16)),
        student: { pc: 2, control: win(t, 48, 302, 10, 8), pointer: null },
        app: {
          tiles: grid({ offline: ALL_ON, selected: (i) => (i === 2 ? 1 : 0), each: (i) => (i === 2 ? { control: win(t, 48, 302) } : {}) }),
          toolbar: toolbar({}, true),
          bottom: bottom({ status: "Selecionada: PC-03" }),
          overlay: (
            <ViewWindow
              s={{
                appear: 1 - sp(t, 304, 10),
                pc: 2,
                controlling,
                notice: win(t, 48, 302, 10, 8),
                controlHover: hov(t, 26, 52),
                controlPress: tap(t, 40),
                disconnectHover: hov(t, 284, 310),
                disconnectPress: tap(t, 300),
                frame: t + 540,
              }}
            />
          ),
        },
      };
    },
  },

  // "Bloquear telas".
  t22: {
    moves: [{ at: 20, to: button("lock"), arc: -30 }],
    clicks: [64],
    cams: [
      { at: 0, rect: groupFocus("tela") },
      { at: 80, rect: FULL },
    ],
    draw: (t, d) => {
      const lock = sp(t, 66, 10);
      return {
        split: sp(t, 70, 18),
        student: { pc: 2, lock },
        app: {
          tiles: grid({ offline: ALL_ON, each: () => ({ lock, lockBadge: lock }) }),
          toolbar: toolbar({ lock: { hover: hov(t, 50, d), press: tap(t, 64), label: t >= 66 ? "Desbloquear telas" : undefined } }, false, spot("tela", t, d)),
          bottom: bottom({ status: statusAt(t, [[66, "Telas bloqueadas: 12 de 12."]]) }),
        },
      };
    },
  },

  // "Tela bloqueada" em cada card; "Desbloquear telas".
  t23: {
    moves: [
      { at: 20, to: tileCenter(6), arc: 40 },
      { at: 110, to: button("lock"), arc: -40 },
    ],
    clicks: [170],
    cams: [
      { at: 0, rect: inside({ x: 180, y: 300, w: 980, h: 520 }) },
      { at: 112, rect: groupFocus("tela") },
      { at: 200, rect: FULL },
    ],
    draw: (t, d) => {
      const lock = 1 - sp(t, 172, 10);
      return {
        split: 1 - sp(t, 186, 18),
        student: { pc: 2, lock },
        app: {
          tiles: grid({ offline: ALL_ON, each: () => ({ lock, lockBadge: lock }) }),
          toolbar: toolbar(
            { lock: { hover: hov(t, 150, 182), press: tap(t, 170), label: t < 172 ? "Desbloquear telas" : undefined } },
            false,
            spot("tela", t, d),
          ),
          bottom: bottom({ status: t < 172 ? "Telas bloqueadas: 12 de 12." : "Telas desbloqueadas." }),
        },
      };
    },
  },
};

