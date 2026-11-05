import { WIN, type Box } from "../../components/app/geometry";
import { POWER, POWER_CTRL, PowerDialog, SCHEDULE, SCHEDULE_CTRL, ScheduleDialog } from "../../components/app/painel/dialogs/energia";
import { VIEW, VIEW_CTRL, ViewWindow } from "../../components/app/painel/dialogs/tela";
import { PAINEL_PCS } from "../../components/app/painel/geometry";
import { PG } from "../../components/app/painel/palette";
import { mix, ramp } from "../../lib/motion";
import { statusAt } from "../cenas/comum";
import { FULL, hov, sp, tap, win, type Beat } from "../kit";
import { OFF_START, dlgTop, inside, pbottom, pbutton, pdialogFocus, pgrid, pgroupFocus, pmid, pname, pspot, ptile, ptoolbar } from "./kit";

const ON: number[] = [];
const SHUT_TEXT = "O professor vai desligar este computador. Salve seu trabalho agora: o que não estiver salvo será perdido.";
const clock = (secondsLeft: number) => `${Math.floor(secondsLeft / 60)}:${String(Math.max(0, Math.floor(secondsLeft % 60))).padStart(2, "0")}`;
const radio = (x: number, y: number) => pmid(POWER, { x, y, w: 16, h: 16 });
const PC3 = PAINEL_PCS[2];
const FULL_BOX: Box = { x: 0, y: 0, w: WIN.w, h: WIN.h };
const lerpBox = (a: Box, b: Box, t: number): Box => ({ x: mix(a.x, b.x, t), y: mix(a.y, b.y, t), w: mix(a.w, b.w, t), h: mix(a.h, b.h, t) });
const sel3 = `Selecionada: ${pname(2)}`;

export const ENERGIA_TELA_P: Record<string, Beat> = {
  // "Ligar": as desligadas aparecem na grade.
  t15: {
    moves: [{ at: 60, to: pbutton("wake"), arc: -40 }],
    clicks: [116],
    cams: [
      { at: 10, rect: pgroupFocus("energia") },
      { at: 170, rect: FULL },
    ],
    draw: (t, d) => ({
      app: {
        tiles: pgrid({
          each: (i) =>
            OFF_START.includes(i)
              ? { online: sp(t, 250 + OFF_START.indexOf(i) * 10, 14), status: t < 200 ? "Sem resposta" : "Conectando...", appear: 1 }
              : {},
        }),
        toolbar: ptoolbar({ wake: { hover: hov(t, 96, 128), press: tap(t, 116) } }, false, pspot("energia", t, d)),
        bottom: pbottom({
          status: statusAt(t, [
            [118, "Enviando o pedido para ligar..."],
            [140, "Pedido para ligar enviado a 2 máquinas. Elas aparecem na grade em 1 ou 2 minutos."],
          ]),
        }),
      },
    }),
  },

  // "Desligar" e "Reiniciar": aviso de 1 ou 5 minutos, ou na hora.
  t16: {
    moves: [
      { at: 30, to: pbutton("powerOff"), arc: 20 },
      { at: 76, to: radio(122, 111), arc: 30 },
      { at: 112, to: radio(37, 111), arc: 10 },
      { at: 140, to: radio(37, 185), arc: 20 },
      { at: 176, to: pmid(POWER, POWER_CTRL.run), arc: 20 },
    ],
    clicks: [60, 98, 128, 160, 214],
    cams: [
      { at: 0, rect: pgroupFocus("energia") },
      { at: 66, rect: dlgTop(POWER, 440) },
    ],
    draw: (t, d) => ({
      app: {
        tiles: pgrid({ offline: ON }),
        toolbar: ptoolbar({ powerOff: { hover: hov(t, 44, 72), press: tap(t, 60) } }, false, pspot("energia", t, d)),
        bottom: pbottom(),
        overlay: (
          <PowerDialog
            s={{
              appear: sp(t, 62, 12) * (1 - sp(t, 236, 10)),
              restart: t >= 99 && t < 129 ? 1 : 0,
              wait: t >= 161 ? 1 : 0,
              runHover: hov(t, 200, 224),
              runPress: tap(t, 214),
              sent: ramp(t, 216, 234),
            }}
          />
        ),
      },
    }),
  },

  // A contagem no aluno e "Cancelar desligamento".
  t17: {
    moves: [
      { at: 120, to: pbutton("powerOff"), arc: 20 },
      { at: 176, to: pmid(POWER, POWER_CTRL.cancel), arc: 30 },
    ],
    clicks: [150, 222],
    cams: [
      { at: 0, rect: inside({ x: 0, y: 110, w: 1110, h: 710 }) },
      { at: 152, rect: dlgTop(POWER, 440) },
      { at: 240, rect: FULL },
    ],
    draw: (t) => {
      const counting = win(t, 0, 226, 1, 10);
      const c = clock(299 - t / 30);
      return {
        split: sp(t, 6, 18) * (1 - sp(t, 252, 18)),
        student: { pc: 2, countdown: { p: counting, clock: c, text: SHUT_TEXT } },
        app: {
          tiles: pgrid({ offline: ON, each: () => ({ countdown: counting, countdownClock: c, countdownText: SHUT_TEXT }) }),
          toolbar: ptoolbar({ powerOff: { hover: hov(t, 136, 160), press: tap(t, 150) } }),
          bottom: pbottom(),
          overlay: (
            <PowerDialog
              s={{ appear: sp(t, 152, 12) * (1 - sp(t, 226, 10)), restart: 0, wait: 1, cancelHover: hov(t, 206, 232), cancelPress: tap(t, 222) }}
            />
          ),
        },
      };
    },
  },

  // "Agendar": dias, as chaves de ligar e desligar e a régua do dia.
  t18: {
    moves: [
      { at: 10, to: pbutton("schedule"), arc: -20 },
      ...[0, 1, 2, 3, 4].map((i) => ({ at: 66 + i * 12, to: pmid(SCHEDULE, SCHEDULE_CTRL.day(i)), arc: 6 })),
      { at: 130, to: pmid(SCHEDULE, SCHEDULE_CTRL.wakeSwitch), arc: 10 },
      { at: 150, to: pmid(SCHEDULE, SCHEDULE_CTRL.offSwitch), arc: 10 },
      { at: 170, to: pmid(SCHEDULE, SCHEDULE_CTRL.save), arc: 20 },
    ],
    clicks: [36, ...[0, 1, 2, 3, 4].map((i) => 80 + i * 12), 144, 162, 196],
    cams: [
      { at: 0, rect: pgroupFocus("energia") },
      { at: 40, rect: pdialogFocus(SCHEDULE, 40) },
      { at: 214, rect: FULL },
    ],
    draw: (t, d) => ({
      app: {
        tiles: pgrid({ offline: ON }),
        toolbar: ptoolbar({ schedule: { hover: hov(t, 22, 46), press: tap(t, 36) } }, false, pspot("energia", t, d)),
        bottom: pbottom(),
        overlay: (
          <ScheduleDialog
            s={{
              appear: sp(t, 38, 12) * (1 - sp(t, 212, 10)),
              days: [0, 1, 2, 3, 4, 5, 6].map((i) => (i < 5 && t >= 80 + i * 12 ? 1 : 0)),
              on: "07:00",
              off: "18:00",
              wakeOn: t >= 144 ? 1 : 0,
              offOn: t >= 162 ? 1 : 0,
              ruler: ramp(t, 164, 182),
              saveHover: hov(t, 184, 204),
              savePress: tap(t, 196),
              saved: sp(t, 198, 8),
            }}
          />
        ),
      },
    }),
  },

  // "Ver a tela".
  t19: {
    moves: [
      { at: 20, to: ptile(2), arc: 40 },
      { at: 80, to: pbutton("view"), arc: -40 },
    ],
    clicks: [56, 124],
    cams: [
      { at: 0, rect: inside({ x: 150, y: 0, w: 1120, h: 560 }) },
      { at: 130, rect: pdialogFocus(VIEW, 26) },
    ],
    draw: (t, d) => ({
      app: {
        tiles: pgrid({ offline: ON, selected: (i) => (i === 2 ? sp(t, 56, 6) : 0) }),
        toolbar: ptoolbar({ view: { hover: hov(t, 106, 134), press: tap(t, 124) } }, t >= 56, pspot("tela", t, d)),
        bottom: pbottom({ status: statusAt(t, [[56, sel3], [126, `Conectando a ${pname(2)}...`], [140, sel3]]) }),
        overlay: <ViewWindow s={{ appear: sp(t, 128, 14), controlling: false }} pc={PC3} index={2} frame={t} />,
      },
    }),
  },

  // Só vendo; "Tela Cheia".
  t20: {
    moves: [
      { at: 40, to: pmid(VIEW, { x: 89, y: 6, w: 77, h: 24 }), arc: 30 },
      { at: 170, to: { x: 127, y: 50 }, arc: 20 },
    ],
    clicks: [110, 200],
    cams: [
      { at: 112, rect: FULL },
      { at: 204, rect: pdialogFocus(VIEW, 26) },
    ],
    draw: (t) => {
      const full = sp(t, 112, 16) * (1 - sp(t, 202, 16));
      return {
        app: {
          tiles: pgrid({ offline: ON, selected: (i) => (i === 2 ? 1 : 0) }),
          toolbar: ptoolbar({}, true),
          bottom: pbottom({ status: sel3 }),
          overlay: (
            <ViewWindow
              s={{
                appear: 1,
                controlling: false,
                box: full > 0.001 ? lerpBox(VIEW, FULL_BOX, full) : undefined,
                fullHover: hov(t, 96, 122) + hov(t, 186, 212),
                fullPress: tap(t, 110) + tap(t, 200),
              }}
              pc={PC3}
              index={2}
              frame={t + 300}
            />
          ),
        },
      };
    },
  },

  // "Controlar": o aviso no aluno.
  t21: {
    moves: [
      { at: 10, to: pmid(VIEW, VIEW_CTRL.toggle), arc: 20 },
      { at: 110, to: pmid(VIEW, { x: VIEW_CTRL.screen.x + 500, y: VIEW_CTRL.screen.y + 300, w: 0, h: 0 }), arc: 40 },
      { at: 170, to: pmid(VIEW, { x: VIEW_CTRL.screen.x + 320, y: VIEW_CTRL.screen.y + 230, w: 0, h: 0 }), arc: 20 },
      { at: 250, to: pmid(VIEW, VIEW_CTRL.disconnect(true)), arc: -30 },
    ],
    clicks: [40, 200, 300],
    cams: [{ at: 0, rect: pdialogFocus(VIEW, 26) }],
    draw: (t) => ({
      split: sp(t, 60, 18) * (1 - sp(t, 304, 16)),
      student: { pc: 2, control: win(t, 48, 302, 10, 8), pointer: null },
      app: {
        tiles: pgrid({ offline: ON, selected: (i) => (i === 2 ? 1 : 0), each: (i) => (i === 2 ? { control: win(t, 48, 302) } : {}) }),
        toolbar: ptoolbar({}, true),
        bottom: pbottom({ status: sel3 }),
        overlay: (
          <ViewWindow
            s={{
              appear: 1 - sp(t, 304, 10),
              controlling: t >= 42,
              toggleHover: hov(t, 26, 52),
              togglePress: tap(t, 40),
              disconnectHover: hov(t, 284, 310),
              disconnectPress: tap(t, 300),
            }}
            pc={PC3}
            index={2}
            frame={t + 540}
          />
        ),
      },
    }),
  },

  // "Bloquear telas".
  t22: {
    moves: [{ at: 20, to: pbutton("lock"), arc: -30 }],
    clicks: [64],
    cams: [
      { at: 0, rect: pgroupFocus("tela") },
      { at: 80, rect: FULL },
    ],
    draw: (t, d) => {
      const lock = sp(t, 66, 10);
      const on = t >= 66;
      return {
        split: sp(t, 70, 18),
        student: { pc: 2, lock },
        app: {
          tiles: pgrid({ offline: ON, each: () => ({ lock, lockBadge: lock }) }),
          toolbar: ptoolbar(
            { lock: { hover: hov(t, 50, d), press: tap(t, 64), label: on ? "Desbloquear telas" : undefined, badge: on ? PG.unlock : undefined } },
            false,
            pspot("tela", t, d),
          ),
          bottom: pbottom({ status: statusAt(t, [[66, "Telas bloqueadas: 15 de 15."]]) }),
        },
      };
    },
  },

  // Cada máquina mostra que está bloqueada; "Desbloquear telas".
  t23: {
    moves: [
      { at: 20, to: ptile(6), arc: 40 },
      { at: 110, to: pbutton("lock"), arc: -40 },
    ],
    clicks: [170],
    cams: [
      { at: 0, rect: inside({ x: 0, y: 260, w: 1100, h: 560 }) },
      { at: 112, rect: pgroupFocus("tela") },
      { at: 200, rect: FULL },
    ],
    draw: (t, d) => {
      const lock = 1 - sp(t, 172, 10);
      const on = t < 172;
      return {
        split: 1 - sp(t, 186, 18),
        student: { pc: 2, lock },
        app: {
          tiles: pgrid({ offline: ON, each: () => ({ lock, lockBadge: lock }) }),
          toolbar: ptoolbar(
            { lock: { hover: hov(t, 150, 182), press: tap(t, 170), label: on ? "Desbloquear telas" : undefined, badge: on ? PG.unlock : undefined } },
            false,
            pspot("tela", t, d),
          ),
          bottom: pbottom({ status: on ? "Telas bloqueadas: 15 de 15." : "Telas desbloqueadas." }),
        },
      };
    },
  },
};
