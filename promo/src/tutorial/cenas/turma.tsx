import { typed } from "../../components/app/ui";
import { ramp } from "../../lib/motion";
import { CLASS_FOLDER, CLASS_FOLDER_CTRL, ClassFolderWindow, type ClassRow, type PcStatus } from "../janelas/pasta";
import { ContextMenu, menuItemY, menuItems } from "../janelas/outras";
import {
  CHAT_CTRL,
  CLASS_CHAT,
  COLLECT,
  COLLECT_CTRL,
  ChatWindow,
  ClassChatWindow,
  CollectWindow,
  MESSAGE,
  MESSAGE_CTRL,
  MESSAGE_TEXT,
  MessageWindow,
  OPEN_PROGRAM,
  OPEN_SITE,
  OPEN_SITE_CTRL,
  OpenProgramWindow,
  OpenSiteWindow,
  SAVED_MENU,
  SEND_FILE,
  SEND_FILE_CTRL,
  SavedMenu,
  SendFileWindow,
  savedMenuAt,
  type ChatLine,
} from "../janelas/turma";
import { FULL, bottom, button, dialogFocus, groupFocus, hov, inside, mid, sp, tap, tileCenter, toolbar, typing, win, type Beat } from "../kit";
import { grid } from "./comum";

const ON: number[] = [];
const spot = (t: number, d: number) => ({ group: "turma" as const, amount: win(t, 0, d - 4, 12, 12) });
const turmaToolbar = (t: number, d: number, extra: Parameters<typeof toolbar>[0] = {}) => toolbar(extra, false, spot(t, d));
const ask = "Dúvidas no exercício 3?";

const classLines = (t: number, sent: number): ChatLine[] => [
  { who: "Você", text: ask, p: sp(t, sent, 8) },
  { who: "PC-05", text: "Como salvo o arquivo?", p: sp(t, sent + 40, 8) },
  { who: "PC-09", text: "Já terminei!", p: sp(t, sent + 72, 8) },
];

const MENU_AT_4 = (() => {
  const c = tileCenter(4);
  return { x: c.x + 4, y: c.y + 4 };
})();
const SINGLE = menuItems(1);
const CLASSES: ClassRow[] = [{ name: "2º B", info: "38 arquivos · última aula 14/10" }];
const WITH_3A: ClassRow[] = [...CLASSES, { name: "3º A", info: "vazia" }];

/** "Enviar arquivo", vindo da conversa com um aluno (tutorial) ou direto (resumo). */
const sendFileBeat = (fromChat: boolean): Beat => ({
    moves: [
      ...(fromChat ? [{ at: 0, to: mid(CLASS_CHAT, CHAT_CTRL.close), arc: 20 }] : []),
      { at: 30, to: button("sendFile"), arc: -30 },
      { at: 92, to: mid(SEND_FILE, SEND_FILE_CTRL.choose), arc: 30 },
      { at: 150, to: mid(SEND_FILE, SEND_FILE_CTRL.openFolder, -60), arc: 20 },
      { at: 190, to: mid(SEND_FILE, SEND_FILE_CTRL.openFolder, -40, 24), arc: 10 },
      { at: 236, to: mid(SEND_FILE, SEND_FILE_CTRL.send), arc: 30 },
    ],
    clicks: fromChat ? [20, 62, 124, 280] : [62, 124, 280],
    cams: [
      { at: 0, rect: groupFocus("turma") },
      { at: 66, rect: dialogFocus(SEND_FILE, 70) },
      { at: 290, rect: FULL },
    ],
    draw: (t, d) => ({
      split: sp(t, 296, 18),
      student: {
        pc: 2,
        apps: 1,
        inbox: sp(t, 296, 10),
        redBox: { p: sp(t, 304, 12), lines: ["O professor enviou o arquivo \"atividade-3.pdf\".", "Ele foi salvo em: Área de Trabalho\\Arquivos do professor"] },
      },
      app: {
        tiles: grid({ offline: ON, selected: (i) => (fromChat && i === 4 ? 1 - sp(t, 40, 6) : 0), each: () => ({ inbox: sp(t, 292, 10) }) }),
        toolbar: turmaToolbar(t, d, { sendFile: { hover: hov(t, 50, 72), press: tap(t, 62) } }),
        bottom: bottom(),
        overlay: (
          <>
            {fromChat ? <ChatWindow s={{ appear: 1 - sp(t, 22, 10), pc: "PC-05", lines: [] }} /> : null}
            <SendFileWindow
              s={{
                appear: sp(t, 64, 12) * (1 - sp(t, 284, 10)),
                chosen: t >= 128 ? 1 : 0,
                after: 0,
                chooseHover: hov(t, 110, 134),
                choosePress: tap(t, 124),
                sendHover: hov(t, 266, 290),
                sendPress: tap(t, 280),
              }}
            />
          </>
        ),
      },
    }),
});

export const TURMA: Record<string, Beat> = {
  // "Mensagem".
  t24: {
    moves: [
      { at: 10, to: button("message"), arc: -30 },
      { at: 110, to: mid(MESSAGE, MESSAGE_CTRL.send), arc: 30 },
    ],
    clicks: [34, 150],
    cams: [
      { at: 0, rect: groupFocus("turma") },
      { at: 40, rect: dialogFocus(MESSAGE, 70) },
    ],
    draw: (t, d) => ({
      app: {
        tiles: grid({ offline: ON }),
        toolbar: turmaToolbar(t, d, { message: { hover: hov(t, 20, 46), press: tap(t, 34) } }),
        bottom: bottom(),
        overlay: (
          <MessageWindow s={{ appear: sp(t, 36, 12), typed: typing(t, 52, 106), sendHover: hov(t, 134, 160), sendPress: tap(t, 150), result: sp(t, 156, 8) }} />
        ),
      },
    }),
  },

  // O recado na tela do aluno, "OK, ENTENDI".
  t25: {
    cams: [{ at: 0, rect: FULL }],
    draw: (t) => {
      const shown = sp(t, 10, 12) * (1 - sp(t, 156, 10));
      const pointer = { x: 230 + (161 - 230) * ramp(t, 90, 140), y: 170 + (121 - 170) * ramp(t, 90, 140), o: 1 };
      return {
        split: sp(t, 4, 18) * (1 - sp(t, 176, 18)),
        student: { pc: 2, redBox: { p: shown, lines: [MESSAGE_TEXT], press: tap(t, 150) }, pointer },
        app: {
          tiles: grid({ offline: ON, each: (i) => ({ message: sp(t, 8 + i, 10) * (1 - sp(t, 150 + i * 3, 10)) }) }),
          toolbar: toolbar(),
          bottom: bottom(),
          overlay: <MessageWindow s={{ appear: 1 - sp(t, 2, 10), typed: 1, result: 1 }} />,
        },
      };
    },
  },

  // "Bate-papo" com a turma.
  t26: {
    moves: [
      { at: 10, to: button("chat"), arc: -30 },
      { at: 100, to: mid(CLASS_CHAT, CHAT_CTRL.send), arc: 30 },
    ],
    clicks: [30, 126],
    cams: [
      { at: 0, rect: groupFocus("turma") },
      { at: 36, rect: dialogFocus(CLASS_CHAT, 50) },
    ],
    draw: (t, d) => ({
      split: sp(t, 160, 18),
      student: {
        pc: 4,
        chat: {
          p: sp(t, 128, 12),
          lines: [
            { who: "Professor", text: ask, p: sp(t, 130, 8) },
            { who: "Você", text: "Como salvo o arquivo?", p: sp(t, 166, 8) },
          ],
        },
      },
      app: {
        tiles: grid({ offline: ON, each: (i) => ({ chat: sp(t, 128 + i, 10) }) }),
        toolbar: turmaToolbar(t, d, { chat: { hover: hov(t, 18, 42), press: tap(t, 30) } }),
        bottom: bottom(),
        overlay: (
          <ClassChatWindow
            s={{ appear: sp(t, 32, 12), lines: classLines(t, 128), typing: t < 128 ? typed(ask, typing(t, 56, 96)) : "", sendHover: hov(t, 112, 136) }}
          />
        ),
      },
    }),
  },

  // "Conversar", no botão direito.
  t27: {
    moves: [
      { at: 0, to: mid(CLASS_CHAT, CHAT_CTRL.close), arc: 20 },
      { at: 40, to: tileCenter(4), arc: 40 },
      { at: 92, to: { x: MENU_AT_4.x + 110, y: menuItemY(MENU_AT_4, SINGLE, "Conversar (chat)...") }, arc: 10 },
    ],
    clicks: [22, 76, 128],
    cams: [
      { at: 30, rect: inside({ x: 60, y: 200, w: 1100, h: 620 }) },
      { at: 134, rect: dialogFocus(CLASS_CHAT, 50) },
    ],
    draw: (t) => {
      const menu = sp(t, 78, 8) * (1 - sp(t, 130, 6));
      const hover = t >= 100 && t < 132 ? "Conversar (chat)..." : null;
      return {
        split: 1 - sp(t, 18, 18),
        student: { pc: 4, chat: { p: 1 - sp(t, 22, 8), lines: [] } },
        app: {
          tiles: grid({ offline: ON, selected: (i) => (i === 4 ? sp(t, 76, 6) : 0), each: (i) => ({ chat: 1 - sp(t, 22 + i, 10) }) }),
          toolbar: toolbar({}, t >= 76),
          bottom: bottom({ status: t >= 76 ? "Selecionada: PC-05" : "" }),
          overlay: (
            <>
              <ClassChatWindow s={{ appear: 1 - sp(t, 24, 10), lines: classLines(t, -200), closeHover: hov(t, 6, 28) }} />
              <ContextMenu at={MENU_AT_4} items={SINGLE} appear={menu} hover={hover} />
              <ChatWindow
                s={{
                  appear: sp(t, 132, 12),
                  pc: "PC-05",
                  lines: [{ who: "Você", text: "Salve na pasta Entregar ao professor.", p: sp(t, 170, 8) }],
                }}
              />
            </>
          ),
        },
      };
    },
  },

  // "Enviar arquivo".
  t28: sendFileBeat(true),
  t28r: sendFileBeat(false),

  // "Recolher arquivos".
  t29: {
    moves: [
      { at: 10, to: button("collect"), arc: -30 },
      { at: 60, to: mid(COLLECT, COLLECT_CTRL.run), arc: 30 },
    ],
    clicks: [34, 96],
    cams: [
      { at: 0, rect: groupFocus("turma") },
      { at: 38, rect: dialogFocus(COLLECT, 60) },
    ],
    draw: (t, d) => ({
      split: 1,
      student: {
        pc: 2,
        inbox: 0,
        redBox: {
          p: (1 - sp(t, 0, 8)) + sp(t, 150, 12),
          lines:
            t < 100
              ? ["O professor enviou o arquivo \"atividade-3.pdf\".", "Ele foi salvo em: Área de Trabalho\\Arquivos do professor"]
              : ["O professor recolheu 2 arquivo(s) da pasta \"Entregar ao professor\".", "A pasta fica em: Área de Trabalho\\Entregar ao professor"],
        },
      },
      app: {
        tiles: grid({ offline: ON }),
        toolbar: turmaToolbar(t, d, { collect: { hover: hov(t, 22, 46), press: tap(t, 34) } }),
        bottom: bottom(),
        overlay: (
          <CollectWindow s={{ appear: sp(t, 36, 12), progress: ramp(t, 98, 150), result: sp(t, 150, 10), runHover: hov(t, 82, 106), runPress: tap(t, 96) }} />
        ),
      },
    }),
  },

  // "Apagar as pastas dos alunos".
  t30: {
    moves: [{ at: 40, to: mid(COLLECT, COLLECT_CTRL.emptyStudents), arc: 30 }],
    clicks: [86],
    cams: [{ at: 0, rect: dialogFocus(COLLECT, 60) }],
    draw: (t) => ({
      split: 1 - sp(t, 200, 18),
      student: {
        pc: 2,
        redBox: {
          p: t < 90 ? 1 - sp(t, 20, 8) : sp(t, 96, 12),
          lines:
            t < 90
              ? ["O professor recolheu 2 arquivo(s) da pasta \"Entregar ao professor\".", "A pasta fica em: Área de Trabalho\\Entregar ao professor"]
              : ["O professor esvaziou a pasta \"Entregar ao professor\". O que estava nela foi para a Lixeira."],
        },
      },
      app: {
        tiles: grid({ offline: ON }),
        toolbar: toolbar(),
        bottom: bottom(),
        overlay: (
          <CollectWindow s={{ appear: 1 - sp(t, 196, 10), progress: 1, result: 1, emptyHover: hov(t, 70, 96), emptyPress: tap(t, 86), emptied: sp(t, 92, 8) }} />
        ),
      },
    }),
  },

  // "Pasta da turma": criar a turma e começar a aula.
  t31: {
    moves: [
      { at: 10, to: button("classFolder"), arc: -30 },
      { at: 60, to: mid(CLASS_FOLDER, CLASS_FOLDER_CTRL.newClass), arc: 40 },
      { at: 140, to: mid(CLASS_FOLDER, CLASS_FOLDER_CTRL.create), arc: 30 },
      { at: 200, to: mid(CLASS_FOLDER, CLASS_FOLDER_CTRL.start), arc: 30 },
    ],
    clicks: [36, 90, 172, 250],
    cams: [
      { at: 0, rect: groupFocus("turma") },
      { at: 40, rect: dialogFocus(CLASS_FOLDER, 40) },
    ],
    draw: (t, d) => {
      const created = t >= 174;
      return {
        app: {
          tiles: grid({ offline: ON }),
          toolbar: turmaToolbar(t, d, { classFolder: { hover: hov(t, 22, 46), press: tap(t, 36) } }),
          bottom: bottom(),
          overlay: (
            <ClassFolderWindow
              s={{
                appear: sp(t, 38, 12),
                mode: "list",
                classes: created ? WITH_3A : CLASSES,
                selected: created ? 1 : -1,
                creating: { p: sp(t, 92, 8) * (1 - sp(t, 174, 6)), typed: typing(t, 110, 136) },
                newHover: hov(t, 76, 100),
                newPress: tap(t, 90),
                createHover: hov(t, 158, 182),
                createPress: tap(t, 172),
                startHover: hov(t, 236, 262),
                startPress: tap(t, 250),
                status: t >= 252 ? "Começando a aula do 3º A..." : created ? "Turma 3º A criada." : undefined,
              }}
            />
          ),
        },
      };
    },
  },

  // A pasta na Área de Trabalho do aluno e o aviso azul.
  t32: {
    cams: [{ at: 20, rect: FULL }],
    draw: (t) => ({
      split: sp(t, 14, 18),
      student: { pc: 2, apps: 1 - sp(t, 40, 12), classFolder: sp(t, 30, 12), turma: "3º A", classToast: win(t, 44, 250, 12, 12) },
      app: {
        tiles: grid({ offline: ON }),
        toolbar: toolbar(),
        bottom: bottom(),
        overlay: (
          <ClassFolderWindow
            s={{ appear: 1 - sp(t, 30, 10), mode: "list", classes: WITH_3A, selected: 1, status: "A aula do 3º A começou em todos os 12 PCs." }}
          />
        ),
      },
    }),
  },

  // Durante a aula: em dia, copiando, desligado.
  t33: {
    moves: [{ at: 20, to: button("classFolder"), arc: -40 }],
    clicks: [50],
    cams: [{ at: 54, rect: dialogFocus(CLASS_FOLDER, 40) }],
    draw: (t) => {
      const pcs: PcStatus[] = Array.from({ length: 12 }, (_, i) =>
        i === 11 ? "off" : (i === 3 || i === 7) && t < 160 + i * 6 ? "copying" : "ok",
      );
      return {
        split: 1 - sp(t, 40, 18),
        student: { pc: 2, apps: 0, classFolder: 1, turma: "3º A" },
        app: {
          tiles: grid({ offline: [11] }),
          toolbar: toolbar({ classFolder: { hover: hov(t, 36, 60), press: tap(t, 50) } }),
          bottom: bottom(),
          overlay: <ClassFolderWindow s={{ appear: sp(t, 52, 12), mode: "lesson", classes: WITH_3A, selected: 1, pcs, endChoice: 0 }} />,
        },
      };
    },
  },

  // Encerrar a aula: manter, apagar os de hoje ou tudo.
  t34: {
    moves: [
      { at: 20, to: mid(CLASS_FOLDER, { x: 34, y: 326, w: 200, h: 18 }), arc: 20 },
      { at: 70, to: mid(CLASS_FOLDER, { x: 34, y: 372, w: 200, h: 18 }), arc: 10 },
      { at: 120, to: mid(CLASS_FOLDER, { x: 34, y: 418, w: 200, h: 18 }), arc: 10 },
      { at: 170, to: mid(CLASS_FOLDER, CLASS_FOLDER_CTRL.end), arc: 30 },
    ],
    clicks: [232],
    cams: [
      { at: 0, rect: dialogFocus(CLASS_FOLDER, 40) },
      { at: 246, rect: FULL },
    ],
    draw: (t) => ({
      app: {
        tiles: grid({ offline: [11] }),
        toolbar: toolbar(),
        bottom: bottom(),
        overlay: (
          <ClassFolderWindow
            s={{ appear: 1 - sp(t, 238, 10), mode: "lesson", classes: WITH_3A, selected: 1, endChoice: 0, endHover: hov(t, 214, 244), endPress: tap(t, 232) }}
          />
        ),
      },
    }),
  },

  // "Abrir site" com "Salvar no menu".
  t35: {
    moves: [
      { at: 10, to: button("openSite"), arc: -30 },
      { at: 110, to: mid(OPEN_SITE, OPEN_SITE_CTRL.save, -50), arc: 30 },
      { at: 178, to: mid(OPEN_SITE, OPEN_SITE_CTRL.run), arc: 30 },
    ],
    clicks: [34, 132, 212],
    cams: [
      { at: 0, rect: groupFocus("turma") },
      { at: 38, rect: dialogFocus(OPEN_SITE, 70) },
      { at: 220, rect: FULL },
    ],
    draw: (t, d) => ({
      split: sp(t, 226, 18),
      student: { pc: 2, browser: { p: sp(t, 230, 10), url: "escola.edu.br/atividade" } },
      app: {
        tiles: grid({ offline: [11], each: (i) => ({ website: sp(t, 218 + i * 1.5, 10) }) }),
        toolbar: turmaToolbar(t, d, { openSite: { hover: hov(t, 22, 46), press: tap(t, 34) } }),
        bottom: bottom(),
        overlay: (
          <OpenSiteWindow
            s={{
              appear: sp(t, 36, 12) * (1 - sp(t, 214, 10)),
              typed: typing(t, 54, 100),
              save: t >= 132 ? 1 : 0,
              named: typing(t, 140, 170),
              runHover: hov(t, 198, 222),
              runPress: tap(t, 212),
            }}
          />
        ),
      },
    }),
  },

  // O site no menu do botão; "Abrir programa".
  t36: (() => {
    const at = savedMenuAt("openSite");
    return {
      moves: [
        { at: 20, to: button("openSite"), arc: -20 },
        { at: 66, to: { x: at.x + 120, y: at.y + SAVED_MENU.pad + SAVED_MENU.item / 2 }, arc: 10 },
        { at: 140, to: button("openProgram"), arc: -20 },
      ],
      clicks: [46, 170],
      cams: [
        { at: 0, rect: groupFocus("turma") },
        { at: 176, rect: dialogFocus(OPEN_PROGRAM, 70) },
      ],
      draw: (t: number, d: number) => ({
        split: 1 - sp(t, 10, 18),
        student: { pc: 2, browser: { p: 1, url: "escola.edu.br/atividade" } },
        app: {
          tiles: grid({ offline: [11], each: () => ({ website: 1 }) }),
          toolbar: turmaToolbar(t, d, {
            openSite: { hover: hov(t, 34, 60), press: tap(t, 46) },
            openProgram: { hover: hov(t, 158, 182), press: tap(t, 170) },
          }),
          bottom: bottom(),
          overlay: (
            <>
              <SavedMenu id="openSite" items={["Atividade 3"]} other="Outro site..." appear={sp(t, 48, 8) * (1 - sp(t, 132, 6))} hover={t >= 84 && t < 134 ? 0 : -1} />
              <OpenProgramWindow s={{ appear: sp(t, 172, 12), typed: typing(t, 190, 236) }} />
            </>
          ),
        },
      }),
    } satisfies Beat;
  })(),
};

