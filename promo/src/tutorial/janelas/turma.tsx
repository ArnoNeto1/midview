import React from "react";
import { useAppTheme } from "../../components/app/appTheme";
import { TOOLBAR_LAYOUT, type ButtonId } from "../../components/app/geometry";
import { Btn, Check, Field, Frame, Text, typed } from "../../components/app/ui";
import { clamp01 } from "../../lib/motion";
import { centered } from "../kit";

/* ---------- Mensagem (SendMessageWindow.xaml) ---------- */

export const MESSAGE = centered(520, 390);
export const MESSAGE_CTRL = { text: { x: 16, y: 70, w: 488, h: 232 }, send: { x: 16, y: 318, w: 488, h: 36 } };
export const MESSAGE_TEXT = "A atividade termina em 10 minutos. Salvem seus trabalhos na pasta Entregar ao professor.";

export const MessageWindow: React.FC<{ s: { appear: number; typed: number; sendHover?: number; sendPress?: number; result?: number } }> = ({ s }) => (
  <Frame title="Enviar mensagem" box={MESSAGE} appear={s.appear}>
    <Text x={16} y={44} weight={600}>
      Para a turma toda (12 máquinas)
    </Text>
    <Field box={MESSAGE_CTRL.text} text={typed(MESSAGE_TEXT, s.typed)} caret={s.typed > 0 && s.typed < 1} multiline />
    <Btn box={MESSAGE_CTRL.send} hover={s.sendHover} press={s.sendPress} strong>
      Enviar
    </Btn>
    <div style={{ opacity: s.result ?? 0 }}>
      <Text x={16} y={362} muted>
        Mensagem enviada para as 12 máquinas.
      </Text>
    </div>
  </Frame>
);

/* ---------- Bate-papo da turma e conversa com um aluno ---------- */

export type ChatLine = { who: string; text: string; p: number };

export const CLASS_CHAT = centered(540, 500);
export const CHAT_CTRL = { input: { x: 16, y: 438, w: 410, h: 32 }, send: { x: 434, y: 438, w: 90, h: 32 }, close: { x: 494, y: 0, w: 46, h: 32 } };

const ChatBody: React.FC<{ status: string; lines: ChatLine[]; typing: string; sendHover?: number }> = ({ status, lines, typing, sendHover }) => {
  const theme = useAppTheme();
  return (
    <>
      <Text x={16} y={42} muted>
        {status}
      </Text>
      <div
        style={{
          position: "absolute",
          left: 16,
          top: 68,
          width: 508,
          height: 358,
          borderRadius: 4,
          border: `1px solid ${theme.dialog.line}`,
          background: theme.dialog.input,
          padding: "10px 12px",
          fontSize: 13,
          lineHeight: 1.5,
          color: theme.dialog.text,
          overflow: "hidden",
        }}
      >
        {lines.map((l) =>
          l.p > 0.01 ? (
            <div key={l.who + l.text} style={{ opacity: clamp01(l.p * 1.5), transform: `translateY(${(1 - clamp01(l.p)) * 6}px)` }}>
              <b>{l.who}:</b> {l.text}
            </div>
          ) : null,
        )}
      </div>
      <Field box={CHAT_CTRL.input} text={typing} caret={typing.length > 0} />
      <Btn box={CHAT_CTRL.send} hover={sendHover}>
        Enviar
      </Btn>
    </>
  );
};

export const ClassChatWindow: React.FC<{ s: { appear: number; lines: ChatLine[]; typing?: string; sendHover?: number; closeHover?: number } }> = ({ s }) => (
  <Frame title="Bate-papo da turma" box={CLASS_CHAT} appear={s.appear} closeHover={s.closeHover}>
    <ChatBody status="Conectado a 12 de 12 máquinas." lines={s.lines} typing={s.typing ?? ""} sendHover={s.sendHover} />
  </Frame>
);

export const ChatWindow: React.FC<{ s: { appear: number; pc: string; lines: ChatLine[]; typing?: string; closeHover?: number } }> = ({ s }) => (
  <Frame title={`Conversa com ${s.pc}`} box={CLASS_CHAT} appear={s.appear} closeHover={s.closeHover}>
    <ChatBody status="Conectado: a janela de conversa abriu na tela do aluno." lines={s.lines} typing={s.typing ?? ""} />
  </Frame>
);

/* ---------- Enviar arquivo (SendFileWindow.xaml) ---------- */

export const SEND_FILE = centered(620, 420);
export const SEND_FILE_CTRL = {
  choose: { x: 450, y: 108, w: 154, h: 30 },
  openFolder: { x: 34, y: 230, w: 330, h: 20 },
  send: { x: 16, y: 362, w: 588, h: 36 },
};

export const SendFileWindow: React.FC<{ s: { appear: number; chosen: number; after: 0 | 1 | 2; chooseHover?: number; choosePress?: number; sendHover?: number; sendPress?: number } }> = ({
  s,
}) => (
  <Frame title="Enviar arquivo" box={SEND_FILE} appear={s.appear}>
    <Text x={16} y={44} weight={600}>
      Para a turma toda (12 máquinas)
    </Text>
    <Text x={16} y={68} w={588} muted>
      O arquivo é salvo na pasta “Arquivos do professor” na Área de Trabalho do aluno, que recebe um aviso.
    </Text>
    <Text x={16} y={114} weight={s.chosen > 0.5 ? 600 : 400} muted={s.chosen < 0.5}>
      {s.chosen > 0.5 ? "atividade-3.pdf (1,2 MB)" : "Nenhum arquivo escolhido."}
    </Text>
    <Btn box={SEND_FILE_CTRL.choose} hover={s.chooseHover} press={s.choosePress}>
      Escolher arquivo...
    </Btn>
    <Check x={16} y={156} on={0}>
      Substituir arquivo com o mesmo nome
    </Check>
    <Text x={16} y={190}>
      Depois de enviar:
    </Text>
    <Check x={34} y={214} on={s.after === 0 ? 1 : 0} radio>
      Não fazer nada
    </Check>
    <Check x={34} y={238} on={s.after === 1 ? 1 : 0} radio>
      Abrir a pasta “Arquivos do professor” no aluno
    </Check>
    <Check x={34} y={262} on={s.after === 2 ? 1 : 0} radio>
      Abrir o arquivo no aluno, com o programa padrão
    </Check>
    <Btn box={SEND_FILE_CTRL.send} hover={s.sendHover} press={s.sendPress} strong>
      Enviar
    </Btn>
  </Frame>
);

/* ---------- Recolher arquivos (CollectFilesWindow.xaml) ---------- */

export const COLLECT = centered(640, 380);
export const COLLECT_CTRL = {
  run: { x: 16, y: 124, w: 360, h: 34 },
  open: { x: 386, y: 124, w: 238, h: 34 },
  emptyStudents: { x: 16, y: 262, w: 360, h: 34 },
  emptyHere: { x: 386, y: 262, w: 238, h: 34 },
};

export type CollectState = {
  appear: number;
  progress: number;
  result: number;
  runHover?: number;
  runPress?: number;
  emptyHover?: number;
  emptyPress?: number;
  emptied?: number;
};

export const CollectWindow: React.FC<{ s: CollectState }> = ({ s }) => (
  <Frame title="Recolher arquivos" box={COLLECT} appear={s.appear}>
    <Text x={16} y={44} weight={600}>
      Da turma toda (12 máquinas)
    </Text>
    <Text x={16} y={68} w={608} muted>
      Traz o que cada aluno colocou na pasta “Entregar ao professor” da Área de Trabalho (subpastas também). Os arquivos continuam no aluno,
      que recebe um aviso. Até 1000 arquivos e 500 MB por máquina.
    </Text>
    <Btn box={COLLECT_CTRL.run} hover={s.runHover} press={s.runPress} strong>
      Recolher
    </Btn>
    <Btn box={COLLECT_CTRL.open}>Abrir pasta</Btn>
    <div style={{ opacity: clamp01(s.progress * 4) * (1 - s.result) }}>
      <Text x={16} y={170} muted>
        Recolhendo... {Math.round(s.progress * 12)} de 12 concluídas
      </Text>
    </div>
    <div style={{ opacity: s.result }}>
      <Text x={16} y={170} w={608}>
        24 arquivo(s), 18,4 MB, de 12 de 12 máquina(s). Salvos em Documentos\Midview\Recolhidos\26-10 13h40
      </Text>
    </div>
    <Text x={16} y={214} w={608} muted>
      Para começar a próxima aula com tudo vazio. Os arquivos vão para a Lixeira, de onde ainda podem ser recuperados.
    </Text>
    <Btn box={COLLECT_CTRL.emptyStudents} hover={s.emptyHover} press={s.emptyPress}>
      Apagar as pastas dos alunos
    </Btn>
    <Btn box={COLLECT_CTRL.emptyHere}>Apagar os recolhidos neste PC</Btn>
    <div style={{ opacity: s.emptied ?? 0 }}>
      <Text x={16} y={310} muted>
        Feito nas 12 máquinas.
      </Text>
    </div>
  </Frame>
);

/* ---------- Abrir site e Abrir programa ---------- */

export const OPEN_SITE = centered(580, 360);
export const OPEN_SITE_CTRL = {
  address: { x: 16, y: 142, w: 548, h: 30 },
  save: { x: 16, y: 190, w: 140, h: 20 },
  name: { x: 130, y: 220, w: 434, h: 30 },
  run: { x: 16, y: 290, w: 548, h: 36 },
};
export const SITE_ADDRESS = "escola.edu.br/atividade";

export const OpenSiteWindow: React.FC<{ s: { appear: number; typed: number; save: number; named: number; runHover?: number; runPress?: number } }> = ({ s }) => (
  <Frame title="Abrir site" box={OPEN_SITE} appear={s.appear}>
    <Text x={16} y={44} weight={600}>
      Para a turma toda (12 máquinas)
    </Text>
    <Text x={16} y={68} w={548} muted>
      Abre o site no navegador do aluno. Um site bloqueado continua bloqueado, e com a tela bloqueada ele abre por baixo do cadeado.
    </Text>
    <Text x={16} y={120}>
      Endereço do site:
    </Text>
    <Field box={OPEN_SITE_CTRL.address} text={typed(SITE_ADDRESS, s.typed)} caret={s.typed > 0 && s.typed < 1} />
    <Check x={16} y={190} on={s.save}>
      Salvar no menu
    </Check>
    <Text x={16} y={226} muted={s.save < 0.5}>
      Nome no menu:
    </Text>
    <Field box={OPEN_SITE_CTRL.name} text={typed("Atividade 3", s.named)} disabled={s.save < 0.5} caret={s.named > 0 && s.named < 1} />
    <Btn box={OPEN_SITE_CTRL.run} hover={s.runHover} press={s.runPress} strong>
      Abrir nas máquinas
    </Btn>
  </Frame>
);

export const OPEN_PROGRAM = centered(600, 400);
export const OpenProgramWindow: React.FC<{ s: { appear: number; typed: number } }> = ({ s }) => (
  <Frame title="Abrir programa" box={OPEN_PROGRAM} appear={s.appear}>
    <Text x={16} y={44} weight={600}>
      Para a turma toda (12 máquinas)
    </Text>
    <Text x={16} y={68} w={568} muted>
      Abre o programa na máquina do aluno, como o próprio aluno (sem ser administrador). Escreva como no Executar do Windows: powershell,
      notepad, chrome https://www.google.com. Um caminho com espaço vai entre aspas. Um programa bloqueado continua bloqueado.
    </Text>
    <Text x={16} y={150}>
      Comando (um programa por linha):
    </Text>
    <Field box={{ x: 16, y: 172, w: 568, h: 90 }} text={typed("chrome https://scratch.mit.edu", s.typed)} caret={s.typed > 0 && s.typed < 1} multiline />
    <Check x={16} y={276} on={0}>
      Salvar no menu
    </Check>
    <Text x={16} y={306} muted>
      Nome no menu:
    </Text>
    <Field box={{ x: 130, y: 300, w: 454, h: 30 }} text="" disabled />
    <Btn box={{ x: 16, y: 346, w: 568, h: 36 }} strong>
      Abrir nas máquinas
    </Btn>
  </Frame>
);

/** Menu de um botão com itens salvos ("Abrir site", "Abrir programa"). */
export const SAVED_MENU = { w: 300, item: 28, pad: 6 };
export const savedMenuAt = (id: ButtonId) => {
  const b = TOOLBAR_LAYOUT.buttons[id];
  return { x: b.x, y: b.y + b.h + 2 };
};
export const SavedMenu: React.FC<{ id: ButtonId; items: string[]; other: string; appear: number; hover: number }> = ({ id, items, other, appear, hover }) => {
  const theme = useAppTheme();
  const at = savedMenuAt(id);
  if (appear <= 0.01) return null;
  const rows = [...items, "-", other];
  return (
    <div
      style={{
        position: "absolute",
        left: at.x,
        top: at.y,
        width: SAVED_MENU.w,
        padding: `${SAVED_MENU.pad}px 0`,
        background: theme.menu.bg,
        border: `1px solid ${theme.menu.line}`,
        borderRadius: 6,
        boxShadow: "0 18px 40px -10px rgba(0,0,0,0.5)",
        fontFamily: theme.font,
        fontSize: 13,
        color: theme.menu.text,
        opacity: clamp01(appear * 1.5),
        transform: `translateY(${(1 - clamp01(appear)) * -6}px)`,
      }}
    >
      {rows.map((r, i) =>
        r === "-" ? (
          <div key={`sep${i}`} style={{ height: 1, margin: "4px 8px", background: theme.menu.line }} />
        ) : (
          <div key={r} style={{ height: SAVED_MENU.item, display: "flex", alignItems: "center", padding: "0 14px", background: hover === i ? theme.menu.hover : "transparent" }}>
            {r}
          </div>
        ),
      )}
      <div style={{ padding: "4px 14px 2px", fontSize: 11, color: theme.menu.muted }}>Para tirar um item do menu, passe o mouse sobre ele e aperte Delete.</div>
    </div>
  );
};
