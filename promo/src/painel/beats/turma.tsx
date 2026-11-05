import {
  CHAT,
  CLASS_FOLDER,
  CLASS_FOLDER_CTRL,
  COLLECT,
  COLLECT_CTRL,
  ChatDialog,
  ClassFolderDialog,
  CollectDialog,
  MESSAGE,
  MESSAGE_CTRL,
  MessageDialog,
  SEND_FILE,
  SEND_FILE_CTRL,
  SendFileDialog,
} from "../../components/app/painel/dialogs/turma";
import { EASE, ramp } from "../../lib/motion";
import type { Beat } from "../engine";
import { FULL, btn, closeX, ctrl, dlgTop, focus, groupFocus, hov, sp, tap, toolbarClick, typing, win } from "../kit";
import { promoVoice, tutorialVoice } from "./comum";

/** Mensagem: o recado digitado e enviado; cada aluno vê a janela do recado. */
export const MENSAGEM: Beat = {
  id: "mensagem",
  dur: 142,
  group: "turma",
  caption: "Mande um recado para a turma toda.",
  voice: promoVoice("07", 4, 136, "Em Turma, mande uma mensagem para todos os alunos de uma vez."),
  moves: [
    { at: 0, to: btn("message"), arc: -40 },
    { at: 80, to: ctrl(MESSAGE, MESSAGE_CTRL.send), arc: 30 },
  ],
  clicks: [22, 98],
  events: { messageSent: 98 },
  cams: [
    { at: 0, rect: groupFocus("turma") },
    { at: 26, rect: focus(MESSAGE, 60, 30) },
    { at: 112, rect: FULL },
  ],
  draw: (t) => ({
    buttons: toolbarClick(t, "message", 22),
    overlay: (
      <MessageDialog
        s={{
          appear: win(t, 24, 114),
          typed: typing(t, 30, 74),
          sendHover: hov(t, 90, 104),
          sendPress: tap(t, 98),
          sent: ramp(t, 100, 112, EASE.linear),
        }}
      />
    ),
  }),
};

/** Bate-papo: a conversa com a turma; cada aluno vê a janela da conversa. */
export const BATEPAPO: Beat = {
  id: "batepapo",
  dur: 85,
  group: "turma",
  caption: "Converse com a turma no bate-papo.",
  voice: promoVoice("08", 6, 73, "Converse com a turma no bate-papo."),
  moves: [
    { at: 0, to: btn("chat"), arc: -40 },
    { at: 54, to: closeX(CHAT), arc: -20 },
  ],
  clicks: [20, 72],
  events: { chatOpen: 20, chatClose: 74 },
  cams: [
    { at: 0, rect: groupFocus("turma") },
    { at: 24, rect: focus(CHAT, 260, 40) },
  ],
  draw: (t) => ({
    buttons: toolbarClick(t, "chat", 20),
    overlay: <ChatDialog s={{ appear: win(t, 22, 74), lines: [30, 40, 50].map((at) => sp(t, at, 8)), closeHover: hov(t, 64, 78) }} />,
  }),
};

/** Enviar arquivo e Recolher arquivos. */
export const ARQUIVOS: Beat = {
  id: "arquivos",
  dur: 160,
  group: "turma",
  caption: "Envie e recolha arquivos.",
  voice: promoVoice("09", 6, 129, "Envie arquivos para os alunos e recolha os trabalhos entregues."),
  moves: [
    { at: 0, to: btn("sendFile"), arc: -30 },
    { at: 28, to: ctrl(SEND_FILE, SEND_FILE_CTRL.choose), arc: 30 },
    { at: 50, to: ctrl(SEND_FILE, SEND_FILE_CTRL.send), arc: 20 },
    { at: 88, to: btn("collect"), arc: -30 },
    { at: 112, to: ctrl(COLLECT, COLLECT_CTRL.run), arc: 30 },
  ],
  clicks: [20, 42, 64, 106, 126],
  events: { fileSent: 64, collected: 126 },
  cams: [
    { at: 0, rect: groupFocus("turma") },
    { at: 22, rect: dlgTop(SEND_FILE, 380) },
    { at: 66, rect: FULL },
    { at: 108, rect: focus(COLLECT, 60, 20) },
    { at: 150, rect: FULL },
  ],
  draw: (t) => ({
    buttons: { ...toolbarClick(t, "sendFile", 20), ...toolbarClick(t, "collect", 106) },
    overlay: (
      <>
        <SendFileDialog
          s={{
            appear: win(t, 22, 90),
            chosen: t >= 43 ? 1 : 0,
            openFolder: 0,
            chooseHover: hov(t, 36, 48),
            sendHover: hov(t, 58, 70),
            sendPress: tap(t, 64),
            sent: ramp(t, 66, 84, EASE.linear),
          }}
        />
        <CollectDialog s={{ appear: win(t, 108, 150), runHover: hov(t, 120, 132), runPress: tap(t, 126), progress: ramp(t, 128, 146, EASE.linear) }} />
      </>
    ),
  }),
};

/** Pasta da turma: escolhe o 7º A e começa a aula; a pasta chega em todos os computadores. */
export const PASTA: Beat = {
  id: "pasta",
  dur: 140,
  group: "turma",
  caption: "Uma pasta da turma em todos os PCs.",
  voice: tutorialVoice("t32", 6, 118, "A Pasta da turma aparece na Área de Trabalho de cada aluno.", { after: 118 }),
  moves: [
    { at: 0, to: btn("classFolder"), arc: -30 },
    { at: 30, to: ctrl(CLASS_FOLDER, CLASS_FOLDER_CTRL.row(1), -150), arc: 30 },
    { at: 50, to: ctrl(CLASS_FOLDER, CLASS_FOLDER_CTRL.start), arc: 30 },
    // Sai de cima de "Encerrar a aula", que aparece no mesmo lugar, e olha os computadores.
    { at: 76, to: ctrl(CLASS_FOLDER, CLASS_FOLDER_CTRL.row(2), 120), arc: 20 },
    { at: 112, to: closeX(CLASS_FOLDER), arc: -30 },
  ],
  clicks: [20, 44, 68, 128],
  events: { lesson: 130 },
  cams: [
    { at: 0, rect: groupFocus("turma") },
    { at: 24, rect: focus(CLASS_FOLDER, 60, 20) },
    { at: 128, rect: groupFocus("turma") },
  ],
  draw: (t) => ({
    buttons: toolbarClick(t, "classFolder", 20),
    overlay: (
      <ClassFolderDialog
        s={{
          appear: win(t, 22, 130),
          page: t >= 70 ? 1 : 0,
          selected: t >= 45 ? 1 : 0,
          startHover: hov(t, 60, 72),
          startPress: tap(t, 68),
          sync: ramp(t, 74, 110, EASE.linear),
          closeHover: hov(t, 120, 134),
        }}
      />
    ),
  }),
};
