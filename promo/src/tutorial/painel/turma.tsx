import { CONVERSA, ConversaDialog, MSGBOX, MSGBOX_CTRL, MessageBoxDialog, PMenu, pMenuHeight, pMenuItem, pMenuItems, pMenuWidth } from "../../components/app/painel/dialogs/menus";
import { FOLDER, FOLDER_CTRL, FolderDialog, type FolderClass, type FolderPc } from "../../components/app/painel/dialogs/pasta";
import {
  CHAT,
  COLLECT,
  COLLECT_CTRL,
  ChatDialog,
  CollectDialog,
  MESSAGE,
  MESSAGE_CTRL,
  MESSAGE_TEXT,
  MessageDialog,
  SEND_FILE,
  SEND_FILE_CTRL,
  SendFileDialog,
} from "../../components/app/painel/dialogs/turma";
import { PAINEL_PCS } from "../../components/app/painel/geometry";
import { ramp } from "../../lib/motion";
import { typed } from "../../components/app/painel/kit";
import { FULL, hov, sp, tap, typing, win, type Beat } from "../kit";
import { OFF_LATE, inside, pbottom, pbutton, pdialogFocus, pgrid, pgroupFocus, pmid, pname, pspot, ptile, ptitle, ptoolbar } from "./kit";

const ON: number[] = [];
const ask = "Dúvidas no exercício 3?";
const turmaBar = (t: number, d: number, extra: Parameters<typeof ptoolbar>[0] = {}, lessonIn = 0) =>
  ptoolbar({ ...extra, ...(lessonIn > 0.01 ? { classFolder: { ...extra.classFolder, caption: "3º A", captionIn: lessonIn } } : {}) }, false, pspot("turma", t, d));
const FILE_LINES = ['O professor enviou o arquivo "atividade-3.pdf".', "Ele foi salvo em: Área de Trabalho\\Arquivos do professor"];
const COLLECT_LINES = ['O professor recolheu 2 arquivo(s) da pasta "Entregar ao professor".', "A pasta fica em: Área de Trabalho\\Entregar ao professor"];
const CLASSES: FolderClass[] = [{ name: "2º B", note: "38 arquivos · última aula 14/10" }];
const WITH_3A: FolderClass[] = [...CLASSES, { name: "3º A", note: "vazia" }];
const SINGLE = pMenuItems(1);
/** Onde o menu abre: ao lado do clique, ou à esquerda dele quando não cabe à direita (como no Windows). */
const menuAt = (i: number) => {
  const c = ptile(i);
  const w = pMenuWidth(SINGLE);
  return { x: c.x + 4 + w > 1530 ? c.x - w - 4 : c.x + 4, y: Math.min(c.y + 4, 820 - pMenuHeight(SINGLE) - 8) };
};
const M4 = menuAt(4);

/** A conversa do bate-papo da turma (t26), com cada linha entrando no seu frame. */
const classLines = (t: number) => [
  { text: `Você: ${ask}`, p: sp(t, 128, 8) },
  { text: `${pname(4)}: Como salvo o arquivo?`, p: sp(t, 168, 8) },
  { text: `${pname(8)}: Já terminei!`, p: sp(t, 200, 8) },
];

/** Selo da aula e pílula "3º A" enquanto a aula do 3º A está ligada. */
const lesson = (tiles: ReturnType<typeof pgrid>, p: number) => ptitle(tiles, p > 0.01 ? "3º A" : undefined, p);

export const TURMA_P: Record<string, Beat> = {
  // "Mensagem".
  t24: {
    moves: [
      { at: 10, to: pbutton("message"), arc: -30 },
      { at: 110, to: pmid(MESSAGE, MESSAGE_CTRL.send), arc: 30 },
    ],
    clicks: [34, 150],
    cams: [
      { at: 0, rect: pgroupFocus("turma") },
      { at: 40, rect: pdialogFocus(MESSAGE, 70) },
    ],
    draw: (t, d) => ({
      app: {
        tiles: pgrid({ offline: ON }),
        toolbar: turmaBar(t, d, { message: { hover: hov(t, 20, 46), press: tap(t, 34) } }),
        bottom: pbottom(),
        overlay: (
          <MessageDialog s={{ appear: sp(t, 36, 12), typed: typing(t, 52, 106), sendHover: hov(t, 134, 160), sendPress: tap(t, 150), sent: ramp(t, 156, 176) }} />
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
          tiles: pgrid({ offline: ON, each: (i) => ({ message: sp(t, 8 + i, 10) * (1 - sp(t, 150 + i * 3, 10)) }) }),
          toolbar: ptoolbar(),
          bottom: pbottom(),
          overlay: <MessageDialog s={{ appear: 1 - sp(t, 2, 10), typed: 1, sent: 1 }} />,
        },
      };
    },
  },

  // "Bate-papo" com a turma.
  t26: {
    moves: [
      { at: 10, to: pbutton("chat"), arc: -30 },
      { at: 100, to: pmid(CHAT, { x: 433, y: 474, w: 63, h: 30 }), arc: 30 },
    ],
    clicks: [30, 126],
    cams: [
      { at: 0, rect: pgroupFocus("turma") },
      { at: 36, rect: pdialogFocus(CHAT, 50) },
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
        tiles: pgrid({ offline: ON, each: (i) => ({ chat: sp(t, 128 + i, 10) }) }),
        toolbar: turmaBar(t, d, { chat: { hover: hov(t, 18, 42), press: tap(t, 30) } }),
        bottom: pbottom(),
        overlay: (
          <ChatDialog
            s={{
              appear: sp(t, 32, 12),
              lines: [],
              transcript: classLines(t),
              input: t < 128 ? typed(ask, typing(t, 56, 96)) : "",
              sendHover: hov(t, 112, 136),
            }}
          />
        ),
      },
    }),
  },

  // "Conversar", no botão direito.
  t27: {
    moves: [
      { at: 0, to: { x: CHAT.x + CHAT.w - 23, y: CHAT.y + 16 }, arc: 20 },
      { at: 40, to: ptile(4), arc: 40 },
      { at: 92, to: pMenuItem(M4, SINGLE, "Conversar (chat)..."), arc: 10 },
    ],
    clicks: [22, 76, 128],
    cams: [
      { at: 30, rect: inside({ x: 380, y: 100, w: 1150, h: 720 }) },
      { at: 134, rect: pdialogFocus(CONVERSA, 50) },
    ],
    draw: (t) => {
      const menu = sp(t, 78, 8) * (1 - sp(t, 130, 6));
      return {
        split: 1 - sp(t, 18, 18),
        student: { pc: 4, chat: { p: 1 - sp(t, 22, 8), lines: [] } },
        app: {
          tiles: pgrid({ offline: ON, selected: (i) => (i === 4 ? sp(t, 76, 6) : 0), each: (i) => ({ chat: 1 - sp(t, 22 + i, 10) }) }),
          toolbar: ptoolbar({}, t >= 76),
          bottom: pbottom({ status: t >= 76 ? `Selecionada: ${pname(4)}` : "" }),
          overlay: (
            <>
              <ChatDialog s={{ appear: 1 - sp(t, 24, 10), lines: [], transcript: classLines(400), closeHover: hov(t, 6, 28) }} />
              <PMenu at={M4} items={SINGLE} appear={menu} hover={t >= 100 && t < 132 ? "Conversar (chat)..." : null} />
              <ConversaDialog s={{ appear: sp(t, 132, 12), pc: pname(4), lines: [{ text: "Você: Salve na pasta Entregar ao professor.", p: sp(t, 170, 8) }] }} />
            </>
          ),
        },
      };
    },
  },

  // "Enviar arquivo".
  t28: {
    moves: [
      { at: 0, to: { x: CONVERSA.x + CONVERSA.w - 23, y: CONVERSA.y + 16 }, arc: 20 },
      { at: 30, to: pbutton("sendFile"), arc: -30 },
      { at: 92, to: pmid(SEND_FILE, SEND_FILE_CTRL.choose), arc: 30 },
      { at: 150, to: pmid(SEND_FILE, { x: 37, y: 282, w: 16, h: 16 }, 60), arc: 20 },
      { at: 190, to: pmid(SEND_FILE, { x: 37, y: 301, w: 16, h: 16 }, 60), arc: 10 },
      { at: 236, to: pmid(SEND_FILE, SEND_FILE_CTRL.send), arc: 30 },
    ],
    clicks: [20, 62, 124, 280],
    cams: [
      { at: 0, rect: pgroupFocus("turma") },
      { at: 66, rect: pdialogFocus(SEND_FILE, 50) },
      { at: 296, rect: FULL },
    ],
    draw: (t, d) => ({
      split: sp(t, 300, 18),
      student: { pc: 2, apps: 1, inbox: sp(t, 300, 10), redBox: { p: sp(t, 308, 12), lines: FILE_LINES } },
      app: {
        tiles: pgrid({ offline: ON, selected: (i) => (i === 4 ? 1 - sp(t, 40, 6) : 0), each: () => ({ inbox: sp(t, 296, 10) }) }),
        toolbar: turmaBar(t, d, { sendFile: { hover: hov(t, 50, 72), press: tap(t, 62) } }),
        bottom: pbottom(),
        overlay: (
          <>
            <ConversaDialog s={{ appear: 1 - sp(t, 22, 10), pc: pname(4), lines: [{ text: "Você: Salve na pasta Entregar ao professor.", p: 1 }] }} />
            <SendFileDialog
              s={{
                appear: sp(t, 64, 12) * (1 - sp(t, 296, 10)),
                chosen: t >= 128 ? 1 : 0,
                openFolder: 0,
                chooseHover: hov(t, 110, 134),
                sendHover: hov(t, 266, 290),
                sendPress: tap(t, 280),
                sent: ramp(t, 282, 296),
              }}
            />
          </>
        ),
      },
    }),
  },

  // "Recolher arquivos".
  t29: {
    moves: [
      { at: 10, to: pbutton("collect"), arc: -30 },
      { at: 60, to: pmid(COLLECT, COLLECT_CTRL.run), arc: 30 },
    ],
    clicks: [34, 96],
    cams: [
      { at: 0, rect: pgroupFocus("turma") },
      { at: 38, rect: pdialogFocus(COLLECT, 40) },
    ],
    draw: (t, d) => ({
      split: 1,
      student: { pc: 2, inbox: 0, redBox: { p: 1 - sp(t, 0, 8) + sp(t, 150, 12), lines: t < 100 ? FILE_LINES : COLLECT_LINES } },
      app: {
        tiles: pgrid({ offline: ON }),
        toolbar: turmaBar(t, d, { collect: { hover: hov(t, 22, 46), press: tap(t, 34) } }),
        bottom: pbottom(),
        overlay: <CollectDialog s={{ appear: sp(t, 36, 12), progress: ramp(t, 98, 150), runHover: hov(t, 82, 106), runPress: tap(t, 96) }} />,
      },
    }),
  },

  // "Apagar as pastas dos alunos".
  t30: {
    moves: [{ at: 40, to: pmid(COLLECT, COLLECT_CTRL.emptyStudents), arc: 30 }],
    clicks: [86],
    cams: [{ at: 0, rect: pdialogFocus(COLLECT, 40) }],
    draw: (t) => ({
      split: 1 - sp(t, 200, 18),
      student: {
        pc: 2,
        redBox: {
          p: t < 90 ? 1 - sp(t, 20, 8) : sp(t, 96, 12),
          lines: t < 90 ? COLLECT_LINES : ['O professor esvaziou a pasta "Entregar ao professor". O que estava nela foi para a Lixeira.'],
        },
      },
      app: {
        tiles: pgrid({ offline: ON }),
        toolbar: ptoolbar(),
        bottom: pbottom(),
        overlay: (
          <CollectDialog s={{ appear: 1 - sp(t, 196, 10), progress: 1, emptyHover: hov(t, 70, 96), emptyPress: tap(t, 86), emptied: ramp(t, 92, 116) }} />
        ),
      },
    }),
  },

  // "Pasta da turma": criar a turma e começar a aula.
  t31: {
    moves: [
      { at: 10, to: pbutton("classFolder"), arc: -30 },
      { at: 60, to: pmid(FOLDER, FOLDER_CTRL.newClass), arc: 40 },
      { at: 140, to: pmid(FOLDER, FOLDER_CTRL.create), arc: 30 },
      { at: 200, to: pmid(FOLDER, FOLDER_CTRL.start("3º A")), arc: 30 },
    ],
    clicks: [36, 90, 172, 250],
    cams: [
      { at: 0, rect: pgroupFocus("turma") },
      { at: 40, rect: pdialogFocus(FOLDER, 30) },
    ],
    draw: (t, d) => {
      const created = t >= 174;
      const on = sp(t, 256, 12);
      const tiles = pgrid({ offline: ON });
      return {
        app: {
          tiles,
          title: lesson(tiles, on),
          toolbar: turmaBar(t, d, { classFolder: { hover: hov(t, 22, 46), press: tap(t, 36) } }, on),
          bottom: pbottom(),
          overlay: (
            <FolderDialog
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
                result: { p: sp(t, 254, 8), text: "Aula iniciada em 15 computadores." },
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
    draw: (t) => {
      const tiles = pgrid({ offline: ON, each: () => ({ classFolder: 1 }) });
      return {
        split: sp(t, 14, 18),
        student: { pc: 2, apps: 1 - sp(t, 40, 12), classFolder: sp(t, 30, 12), turma: "3º A", classToast: win(t, 44, 250, 12, 12) },
        app: {
          tiles,
          title: lesson(tiles, 1),
          toolbar: ptoolbar({ classFolder: { caption: "3º A" } }),
          bottom: pbottom(),
          overlay: (
            <FolderDialog
              s={{ appear: 1 - sp(t, 30, 10), mode: "list", classes: WITH_3A, selected: 1, result: { p: 1, text: "Aula iniciada em 15 computadores." } }}
            />
          ),
        },
      };
    },
  },

  // Durante a aula: em dia, copiando, desligado.
  t33: {
    moves: [{ at: 20, to: pbutton("classFolder"), arc: -40 }],
    clicks: [50],
    cams: [{ at: 54, rect: pdialogFocus(FOLDER, 30) }],
    draw: (t) => {
      const pcs: FolderPc[] = PAINEL_PCS.map((_, i) => (OFF_LATE.includes(i) ? "off" : (i === 3 || i === 7) && t < 160 + i * 6 ? "copying" : "ok"));
      const tiles = pgrid({ offline: OFF_LATE });
      return {
        split: 1 - sp(t, 40, 18),
        student: { pc: 2, apps: 0, classFolder: 1, turma: "3º A" },
        app: {
          tiles,
          title: lesson(tiles, 1),
          toolbar: ptoolbar({ classFolder: { hover: hov(t, 36, 60), press: tap(t, 50), caption: "3º A" } }),
          bottom: pbottom(),
          overlay: <FolderDialog s={{ appear: sp(t, 52, 12), mode: "lesson", classes: WITH_3A, selected: 1, turma: "3º A", pcs, endChoice: 0 }} />,
        },
      };
    },
  },

  // Encerrar a aula: manter, apagar os de hoje ou tudo; a pergunta antes de apagar.
  t34: {
    moves: [
      { at: 20, to: pmid(FOLDER, FOLDER_CTRL.choice(0), 60), arc: 20 },
      { at: 60, to: pmid(FOLDER, FOLDER_CTRL.choice(1), 60), arc: 10 },
      { at: 110, to: pmid(FOLDER, FOLDER_CTRL.choice(2), 60), arc: 10 },
      { at: 150, to: pmid(FOLDER, FOLDER_CTRL.choice(1)), arc: 10 },
      { at: 186, to: pmid(FOLDER, FOLDER_CTRL.end), arc: 30 },
      { at: 236, to: pmid(MSGBOX, MSGBOX_CTRL.yes), arc: 20 },
    ],
    clicks: [168, 214, 262],
    cams: [
      { at: 0, rect: pdialogFocus(FOLDER, 30) },
      { at: 270, rect: FULL },
    ],
    draw: (t) => {
      const on = 1 - sp(t, 264, 10);
      const tiles = pgrid({ offline: OFF_LATE });
      return {
        app: {
          tiles,
          title: lesson(tiles, on),
          toolbar: ptoolbar({ classFolder: { caption: "3º A", captionIn: on } }),
          bottom: pbottom(),
          overlay: (
            <>
              <FolderDialog
                s={{
                  appear: 1 - sp(t, 264, 10),
                  mode: "lesson",
                  classes: WITH_3A,
                  selected: 1,
                  turma: "3º A",
                  pcs: PAINEL_PCS.map((_, i) => (OFF_LATE.includes(i) ? "off" : "ok")),
                  endChoice: t >= 169 ? 1 : 0,
                  endHover: hov(t, 200, 226),
                  endPress: tap(t, 214),
                }}
              />
              <MessageBoxDialog
                s={{
                  appear: sp(t, 218, 10) * (1 - sp(t, 264, 8)),
                  title: "Encerrar a aula",
                  text: "Encerrar a aula do 3º A e apagar os arquivos de hoje?\n\nOs 12 arquivos desta aula saem; os das aulas anteriores ficam.",
                  yesHover: hov(t, 250, 270),
                  yesPress: tap(t, 262),
                }}
              />
            </>
          ),
        },
      };
    },
  },
};
