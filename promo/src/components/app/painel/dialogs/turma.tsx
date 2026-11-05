import React from "react";
import { clamp01 } from "../../../../lib/motion";
import { P_TOTAL, PAINEL_PCS, pDialog } from "../geometry";
import { Card, PBtn, PCheck, PDialog, PField, PRadio, ResultCard, T, typed } from "../kit";
import { P, PG } from "../palette";

const PARA_TURMA = `Para a turma toda (${P_TOTAL} máquinas)`;
/** As janelas abrem depois do "Ligar": as 15 máquinas respondem. */
const ONLINE = PAINEL_PCS.map((_, i) => i);

/* ---------- Enviar mensagem (SendMessageWindow.xaml, foto 37) ---------- */

export const MESSAGE = pDialog(506, 423);
export const MESSAGE_TEXT = "A atividade termina em 10 minutos. Salvem seus trabalhos.";
export const MESSAGE_CTRL = { text: { x: 24, y: 98, w: 458, h: 198 }, send: { x: 24, y: 310, w: 64, h: 27 } };

export type MessageState = { appear: number; typed: number; sendHover?: number; sendPress?: number; sent: number };

export const MessageDialog: React.FC<{ s: MessageState }> = ({ s }) => (
  <PDialog title="Enviar mensagem" box={MESSAGE} appear={s.appear} icon={PG.message} group="turma" heading="Enviar mensagem" hint={PARA_TURMA}>
    <PField box={MESSAGE_CTRL.text} text={typed(MESSAGE_TEXT, s.typed)} caret={s.typed < 1} focus multiline size={13} />
    <PBtn box={MESSAGE_CTRL.send} kind="primary" hover={s.sendHover} press={s.sendPress}>
      Enviar
    </PBtn>
    <ResultCard box={{ x: 24, y: 347, w: 458, h: 55 }} progress={s.sent} machines={ONLINE} line={(n) => ({ text: `${n}: ok`, ok: true })} />
  </PDialog>
);

/* ---------- Bate-papo da turma (ClassChatWindow.xaml, foto 39) ---------- */

export const CHAT = pDialog(520, 520);
export const CHAT_LINES = ["Você: Dúvidas no exercício 3?", "ALUNO-05: Na parte 2, professor!", "ALUNO-02: Eu também."];
export const CHAT_CTRL = { close: { x: 474, y: -32, w: 46, h: 32 } };

export type ChatState = {
  appear: number;
  lines: number[];
  closeHover?: number;
  /** Outra conversa (texto de cada linha e a entrada 0→1), no lugar de CHAT_LINES. */
  transcript?: { text: string; p: number }[];
  /** Texto sendo digitado no campo e realce do "Enviar". */
  input?: string;
  sendHover?: number;
};

export const ChatDialog: React.FC<{ s: ChatState }> = ({ s }) => (
  <PDialog
    title="Bate-papo da turma"
    box={CHAT}
    appear={s.appear}
    icon={PG.chat}
    group="turma"
    heading="Bate-papo da turma"
    hint={`Conectado a ${ONLINE.length} de ${P_TOTAL} máquinas.`}
    closeHover={s.closeHover}
  >
    <Card box={{ x: 24, y: 94, w: 472, h: 368 }} />
    <div style={{ position: "absolute", left: 37, top: 106, fontSize: 12.5, lineHeight: "17px", color: P.text }}>
      {(s.transcript ?? CHAT_LINES.map((text, i) => ({ text, p: s.lines[i] ?? 0 }))).map((line) => {
        const p = clamp01(line.p);
        return p > 0.01 ? (
          <div key={line.text} style={{ opacity: p, transform: `translateY(${(1 - p) * 6}px)` }}>
            {line.text}
          </div>
        ) : null;
      })}
    </div>
    <PField box={{ x: 24, y: 474, w: 401, h: 30 }} text={s.input ?? ""} caret={Boolean(s.input)} focus={Boolean(s.input)} />
    <PBtn box={{ x: 433, y: 474, w: 63, h: 30 }} kind={s.input ? "primary" : "normal"} hover={s.sendHover} disabled={!s.input}>
      Enviar
    </PBtn>
  </PDialog>
);

/* ---------- Enviar arquivo (SendFileWindow.xaml, foto 40) ---------- */

export const SEND_FILE = pDialog(506, 603);
export const SEND_FILE_CTRL = {
  choose: { x: 340, y: 111, w: 129, h: 28 },
  folder: { x: 37, y: 282, w: 300, h: 16 },
  send: { x: 24, y: 345, w: 64, h: 27 },
};

export type SendFileState = { appear: number; chosen: number; openFolder: number; chooseHover?: number; sendHover?: number; sendPress?: number; sent: number };

export const SendFileDialog: React.FC<{ s: SendFileState }> = ({ s }) => (
  <PDialog title="Enviar arquivo" box={SEND_FILE} appear={s.appear} icon={PG.upload} group="turma" heading="Enviar arquivo" hint={PARA_TURMA}>
    <Card box={{ x: 24, y: 98, w: 458, h: 91 }} />
    <T x={37} y={118} weight={s.chosen > 0.5 ? 600 : 400}>
      {s.chosen > 0.5 ? "atividade-3.pdf" : "Nenhum arquivo escolhido."}
    </T>
    <PBtn box={SEND_FILE_CTRL.choose} hover={s.chooseHover}>
      Escolher arquivo...
    </PBtn>
    <T x={37} y={148} w={436} muted>
      O arquivo é salvo na pasta &quot;Arquivos do professor&quot; na Área de Trabalho do aluno, que recebe um aviso.
    </T>
    <Card box={{ x: 24, y: 200, w: 458, h: 130 }} />
    <PCheck x={37} y={213} on={0}>
      Substituir arquivo com o mesmo nome
    </PCheck>
    <T x={37} y={240} weight={600}>
      Depois de enviar:
    </T>
    <PRadio x={37} y={263} on={1 - s.openFolder}>
      Não fazer nada
    </PRadio>
    <PRadio x={37} y={282} on={s.openFolder}>
      Abrir a pasta &quot;Arquivos do professor&quot; no aluno
    </PRadio>
    <PRadio x={37} y={301} on={0}>
      Abrir ou executar o arquivo no aluno
    </PRadio>
    <PBtn box={SEND_FILE_CTRL.send} kind="primary" hover={s.sendHover} press={s.sendPress} disabled={s.chosen < 0.5}>
      Enviar
    </PBtn>
    <ResultCard box={{ x: 24, y: 383, w: 458, h: 199 }} progress={s.sent} head={`Enviando para ${ONLINE.length} máquinas...`} machines={ONLINE} />
  </PDialog>
);

/* ---------- Recolher arquivos (CollectFilesWindow.xaml, foto 41) ---------- */

export const COLLECT = pDialog(566, 643);
export const COLLECT_CTRL = { run: { x: 37, y: 185, w: 393, h: 28 }, emptyStudents: { x: 37, y: 310, w: 288, h: 27 } };

export type CollectState = {
  appear: number;
  runHover?: number;
  runPress?: number;
  progress: number;
  emptyHover?: number;
  emptyPress?: number;
  /** "Apagar as pastas dos alunos" (0→1): a lista de resultado troca para o esvaziamento. */
  emptied?: number;
};

export const CollectDialog: React.FC<{ s: CollectState }> = ({ s }) => (
  <PDialog title="Recolher arquivos" box={COLLECT} appear={s.appear} icon={PG.download} group="turma" heading="Recolher arquivos" hint={`Da turma toda (${P_TOTAL} máquinas)`}>
    <Card box={{ x: 24, y: 98, w: 518, h: 128 }} />
    <T x={37} y={112} title>
      Recolher
    </T>
    <T x={37} y={132} w={496} muted>
      Traz o que cada aluno colocou na pasta &quot;Entregar ao professor&quot; da Área de Trabalho (subpastas também). Os arquivos continuam no
      aluno, que recebe um aviso. Até 1000 arquivos e 500 MB por máquina.
    </T>
    <PBtn box={COLLECT_CTRL.run} kind="primary" hover={s.runHover} press={s.runPress}>
      Recolher
    </PBtn>
    <PBtn box={{ x: 438, y: 185, w: 91, h: 28 }} disabled={s.progress < 1}>
      Abrir pasta
    </PBtn>
    <Card box={{ x: 24, y: 237, w: 518, h: 113 }} />
    <T x={37} y={250} title>
      Para começar a próxima aula
    </T>
    <T x={37} y={270} w={496} muted>
      Para começar a próxima aula com tudo vazio. Os arquivos vão para a Lixeira, de onde ainda podem ser recuperados.
    </T>
    <PBtn box={COLLECT_CTRL.emptyStudents} kind="danger" hover={s.emptyHover} press={s.emptyPress}>
      Apagar as pastas dos alunos
    </PBtn>
    <PBtn box={{ x: 333, y: 310, w: 196, h: 27 }} kind="danger">
      Apagar os recolhidos neste PC
    </PBtn>
    {(s.emptied ?? 0) > 0.01 ? (
      <ResultCard box={{ x: 24, y: 361, w: 518, h: 261 }} progress={s.emptied ?? 0} head="Pastas dos alunos esvaziadas." machines={ONLINE} />
    ) : (
      <ResultCard
        box={{ x: 24, y: 361, w: 518, h: 261 }}
        progress={s.progress}
        head={`Recolhendo de ${ONLINE.length} máquinas...`}
        machines={ONLINE}
        line={(n, i) => ({ text: `${n}: ${2 + (i % 2)} arquivo(s), ${180 + ((i * 37) % 300)} KB`, ok: true })}
      />
    )}
  </PDialog>
);

/* ---------- Pasta da turma (ClassFolderWindow.xaml, fotos 42 e 44) ---------- */

export const CLASS_FOLDER = pDialog(586, 663);
export const CLASSES = [
  { name: "6º A", note: "42 arquivos · última aula 04/11" },
  { name: "7º A", note: "17 arquivos · última aula 30/10" },
  { name: "8º B", note: "vazia" },
  { name: "9º Robótica", note: "128 arquivos · última aula 02/11" },
];
export const CLASS_FOLDER_CTRL = {
  row: (i: number) => ({ x: 34, y: 132 + i * 46, w: 518, h: 44 }),
  start: { x: 380, y: 614, w: 182, h: 29 },
  close: { x: 540, y: -32, w: 46, h: 32 },
};

export type ClassFolderState = {
  appear: number;
  /** 0 = escolher a turma; 1 = aula ligada. */
  page: 0 | 1;
  selected: number;
  startHover?: number;
  startPress?: number;
  /** Os computadores recebendo a pasta (0→1). */
  sync: number;
  closeHover?: number;
};

const ChoosePage: React.FC<{ s: ClassFolderState }> = ({ s }) => (
  <>
    <T x={24} y={82} w={538} muted size={12.5}>
      Escolha a turma desta aula. Tudo o que os alunos baixarem ou salvarem na pasta da turma aparece em todos os computadores.
    </T>
    <Card box={{ x: 24, y: 122, w: 538, h: 330 }} />
    {CLASSES.map((c, i) => {
      const r = CLASS_FOLDER_CTRL.row(i);
      return (
        <React.Fragment key={c.name}>
          <div style={{ position: "absolute", left: r.x, top: r.y, width: r.w, height: r.h, borderRadius: 4, background: "#253552", opacity: i === 1 ? clamp01(s.selected) : 0 }} />
          <T x={r.x + 10} y={r.y + 7} weight={600} size={13}>
            {c.name}
          </T>
          <T x={r.x + 10} y={r.y + 25} muted size={11}>
            {c.note}
          </T>
        </React.Fragment>
      );
    })}
    <PBtn box={{ x: 24, y: 464, w: 99, h: 27 }}>+ Nova turma</PBtn>
    <PBtn box={{ x: 130, y: 464, w: 82, h: 27 }}>Renomear</PBtn>
    <PBtn box={{ x: 220, y: 464, w: 63, h: 27 }} kind="danger">
      Excluir
    </PBtn>
    <PBtn box={{ x: 24, y: 614, w: 64, h: 29 }}>Fechar</PBtn>
    <PBtn box={CLASS_FOLDER_CTRL.start} kind="primary" hover={s.startHover} press={s.startPress} disabled={s.selected < 0.5}>
      {s.selected > 0.5 ? "Começar a aula do 7º A" : "Começar a aula"}
    </PBtn>
  </>
);

const LessonPage: React.FC<{ s: ClassFolderState }> = ({ s }) => {
  const done = ONLINE.length;
  return (
    <>
      <T x={24} y={84} size={15} weight={600}>
        Pasta da turma: 7º A
      </T>
      <T x={24} y={106} muted>
        Aula começou às 07:30 · 17 arquivos (24 MB) · disco deste PC com 180 GB livres
      </T>
      <Card box={{ x: 24, y: 132, w: 538, h: 238 }} />
      <T x={37} y={146} title>
        Computadores
      </T>
      <div style={{ position: "absolute", left: 37, top: 172, width: 512, display: "flex", flexWrap: "wrap", rowGap: 6, columnGap: 18, fontSize: 12.5 }}>
        {PAINEL_PCS.map((pc, i) => {
          const off = false;
          const order = ONLINE.indexOf(i);
          const ok = !off && s.sync * done > order + 0.5;
          const symbol = off ? "○" : ok ? "✓" : "⟳";
          const color = off ? P.muted : ok ? P.online : P.accentText;
          return (
            <span key={pc.name} style={{ width: 84, color, whiteSpace: "nowrap" }}>
              {symbol} {pc.name}
            </span>
          );
        })}
      </div>
      <T x={37} y={344} muted size={11}>
        ✓ em dia &nbsp;&nbsp; ⟳ copiando &nbsp;&nbsp; ○ desligado (recebe ao ligar) &nbsp;&nbsp; ✗ com problema
      </T>
      <Card box={{ x: 24, y: 381, w: 538, h: 221 }} />
      <T x={37} y={395} title>
        Ao encerrar a aula
      </T>
      {[
        { t: "Manter os arquivos", d: "Voltam para todos os PCs na próxima aula do 7º A." },
        { t: "Apagar só os de hoje", d: "Os arquivos desta aula saem; os das aulas anteriores ficam." },
        { t: "Apagar tudo do 7º A", d: "Os 17 arquivos saem do seu PC e de todos os PCs. Vão para a Lixeira deste PC." },
      ].map((o, i) => (
        <React.Fragment key={o.t}>
          <PRadio x={37} y={424 + i * 40} on={i === 0 ? 1 : 0}>
            <span style={{ fontWeight: 600 }}>{o.t}</span>
          </PRadio>
          <T x={61} y={441 + i * 40} muted>
            {o.d}
          </T>
        </React.Fragment>
      ))}
      <T x={37} y={576} muted>
        Pode fechar esta janela: a aula continua até você encerrá-la.
      </T>
      <PBtn box={{ x: 24, y: 614, w: 119, h: 28 }}>Trocar de turma...</PBtn>
      <PBtn box={{ x: 444, y: 614, w: 118, h: 28 }} kind="primary">
        Encerrar a aula
      </PBtn>
    </>
  );
};

export const ClassFolderDialog: React.FC<{ s: ClassFolderState }> = ({ s }) => (
  <PDialog title="Pasta da turma" box={CLASS_FOLDER} appear={s.appear} icon={PG.syncFolder} group="turma" heading="Pasta da turma" closeHover={s.closeHover}>
    {s.page === 0 ? <ChoosePage s={s} /> : <LessonPage s={s} />}
  </PDialog>
);
