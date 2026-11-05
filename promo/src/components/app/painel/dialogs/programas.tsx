import React from "react";
import { P_TOTAL, PAINEL_PCS, pDialog } from "../geometry";
import { Card, PBtn, PCheck, PDialog, PField, ResultCard, ScrollThumb, T, TURMA_TODA, typed } from "../kit";
import { PG } from "../palette";

/** As janelas abrem depois do "Ligar": as 15 máquinas respondem. */
export const ONLINE_PCS = PAINEL_PCS.map((_, i) => i);
const sending = (n: number) => `Enviando para ${n} máquinas...`;

/* ---------- Abrir site (OpenWebsiteWindow.xaml, foto 46) ---------- */

export const OPEN_SITE = pDialog(506, 543);
export const SITE_TEXT = "escola.edu.br/atividade";
export const OPEN_SITE_CTRL = { run: { x: 24, y: 302, w: 135, h: 27 } };

export type OpenSiteState = {
  appear: number;
  typed: number;
  runHover?: number;
  runPress?: number;
  sent: number;
  /** "Salvar no menu" (padrão: marcado) e o nome digitado no menu (padrão: "Atividade" quando o endereço está pronto). */
  save?: number;
  name?: string;
};
export const OPEN_SITE_SAVE = { x: 37, y: 228, w: 120, h: 16 };

export const OpenSiteDialog: React.FC<{ s: OpenSiteState }> = ({ s }) => (
  <PDialog title="Abrir site" box={OPEN_SITE} appear={s.appear} icon={PG.globe} group="programas" heading="Abrir site" hint={TURMA_TODA}>
    <Card box={{ x: 24, y: 98, w: 458, h: 106 }} />
    <T x={37} y={110} w={436} muted>
      Abre o site no navegador do aluno. Um site bloqueado continua bloqueado, e com a tela bloqueada ele abre por baixo do cadeado.
    </T>
    <T x={37} y={150}>
      Endereço do site:
    </T>
    <PField box={{ x: 37, y: 168, w: 432, h: 23 }} text={typed(SITE_TEXT, s.typed)} caret={s.typed < 1} focus />
    <Card box={{ x: 24, y: 214, w: 458, h: 74 }} />
    <PCheck x={37} y={228} on={s.save ?? 1}>
      Salvar no menu
    </PCheck>
    <T x={61} y={257}>
      Nome no menu:
    </T>
    <PField box={{ x: 153, y: 252, w: 220, h: 23 }} text={s.name ?? (s.typed > 0.99 ? "Atividade" : "")} caret={s.name !== undefined && s.name.length > 0} />
    <PBtn box={OPEN_SITE_CTRL.run} kind="primary" hover={s.runHover} press={s.runPress}>
      Abrir nas máquinas
    </PBtn>
    <ResultCard box={{ x: 24, y: 340, w: 458, h: 182 }} progress={s.sent} head={sending(ONLINE_PCS.length)} machines={ONLINE_PCS} />
  </PDialog>
);

/* ---------- Abrir programa (OpenProgramWindow.xaml, foto 48) ---------- */

export const OPEN_PROGRAM = pDialog(526, 623);
export const PROGRAM_TEXT = "notepad\nchrome https://www.google.com";
export const OPEN_PROGRAM_CTRL = { run: { x: 24, y: 372, w: 135, h: 27 } };

export type OpenProgramState = { appear: number; typed: number; runHover?: number; runPress?: number; sent: number };

export const OpenProgramDialog: React.FC<{ s: OpenProgramState }> = ({ s }) => (
  <PDialog title="Abrir programa" box={OPEN_PROGRAM} appear={s.appear} icon={PG.console} group="programas" heading="Abrir programa" hint={TURMA_TODA}>
    <Card box={{ x: 24, y: 98, w: 478, h: 176 }} />
    <T x={37} y={110} w={456} muted>
      Abre o programa na máquina do aluno, como o próprio aluno (sem ser administrador). Escreva como no Executar do Windows: powershell, notepad,
      chrome https://www.google.com. Um caminho com espaço vai entre aspas. Um programa bloqueado continua bloqueado.
    </T>
    <T x={37} y={180}>
      Comando (um programa por linha):
    </T>
    <PField box={{ x: 37, y: 198, w: 452, h: 63 }} text={typed(PROGRAM_TEXT, s.typed)} caret={s.typed < 1} focus multiline />
    <Card box={{ x: 24, y: 284, w: 478, h: 74 }} />
    <PCheck x={37} y={298} on={0}>
      Salvar no menu
    </PCheck>
    <T x={61} y={327}>
      Nome no menu:
    </T>
    <PField box={{ x: 153, y: 322, w: 220, h: 23 }} />
    <PBtn box={OPEN_PROGRAM_CTRL.run} kind="primary" hover={s.runHover} press={s.runPress}>
      Abrir nas máquinas
    </PBtn>
    <ResultCard box={{ x: 24, y: 410, w: 478, h: 192 }} progress={s.sent} head={sending(ONLINE_PCS.length)} machines={ONLINE_PCS} />
  </PDialog>
);

/* ---------- Instalar programa (InstallWindow.xaml, foto 49) ---------- */

export const INSTALL = pDialog(546, 643);
export const INSTALL_CTRL = { choose: { x: 374, y: 164, w: 135, h: 24 }, run: { x: 24, y: 302, w: 71, h: 28 } };

export type InstallState = { appear: number; chosen: number; chooseHover?: number; runHover?: number; runPress?: number; sent: number };

export const InstallDialog: React.FC<{ s: InstallState }> = ({ s }) => (
  <PDialog title="Instalar programa" box={INSTALL} appear={s.appear} icon={PG.box} group="programas" heading="Instalar programa" hint={TURMA_TODA}>
    <Card box={{ x: 24, y: 98, w: 498, h: 190 }} />
    <T x={37} y={110} w={476} muted>
      O Midview de cada máquina instala o programa como administrador: não aparece a tela do UAC e ninguém precisa digitar senha. O instalador tem
      de funcionar sem perguntas (modo silencioso) e instalar para todos os usuários.
    </T>
    <T x={37} y={169} w={330} nowrap weight={s.chosen > 0.5 ? 600 : 400}>
      {s.chosen > 0.5 ? "LibreOffice_7.6_Win_x64.msi" : "Nenhum instalador ou script escolhido (.exe, .msi, .bat ou .c..."}
    </T>
    <PBtn box={INSTALL_CTRL.choose} hover={s.chooseHover}>
      Escolher instalador...
    </PBtn>
    <T x={37} y={197}>
      Opções do instalador:
    </T>
    <PField box={{ x: 37, y: 215, w: 472, h: 24 }} text={s.chosen > 0.5 ? "/qn" : ""} />
    <T x={37} y={246} w={476} muted>
      Um instalador que fica esperando uma resposta é encerrado depois de 30 minutos. Na dúvida, teste numa máquina antes da turma toda.
    </T>
    <PBtn box={INSTALL_CTRL.run} kind="primary" hover={s.runHover} press={s.runPress} disabled={s.chosen < 0.5}>
      Instalar
    </PBtn>
    <ResultCard
      box={{ x: 24, y: 340, w: 498, h: 282 }}
      progress={s.sent}
      head={sending(ONLINE_PCS.length)}
      machines={ONLINE_PCS}
      line={(n) => ({ text: `${n}: instalado`, ok: true })}
    />
  </PDialog>
);

/* ---------- Desinstalar programa (UninstallWindow.xaml, foto 50) ---------- */

export const UNINSTALL = pDialog(640, 660);
export const UNINSTALL_PROGRAMS = [
  "7-Zip 23.01",
  "Google Chrome",
  "Jogo da Cobrinha 2.0",
  "LibreOffice 7.6",
  "VLC media player",
  "Notepad++",
  "GIMP 2.10",
  "Audacity",
  "Scratch Desktop",
  "Python 3.12",
  "Visual Studio Code",
  "Zoom",
];
export const UNINSTALL_CTRL = {
  row: (i: number) => ({ x: 37, y: 225 + i * 23, w: 200, h: 16 }),
  all: { x: 24, y: 515, w: 230, h: 16 },
  run: { x: 24, y: 542, w: 158, h: 29 },
};

export type UninstallState = { appear: number; checks: number[]; all: number; runHover?: number; runPress?: number; sent: number; pc?: string };

export const UninstallDialog: React.FC<{ s: UninstallState }> = ({ s }) => (
  <PDialog
    title="Desinstalar programa"
    box={UNINSTALL}
    appear={s.appear}
    icon={PG.trash}
    group="programas"
    heading="Desinstalar programa"
    hint={`Programas instalados em ${s.pc ?? PAINEL_PCS[0].name}`}
  >
    <T x={24} y={96} w={592} size={12}>
      A lista é lida de uma máquina. Marque os programas e desinstale nela ou na turma toda; cada máquina acha o programa pelo nome, mesmo numa versão
      diferente. Só remove programas com desinstalação silenciosa; os que não têm, o resultado avisa.
    </T>
    <PField box={{ x: 24, y: 172, w: 391, h: 28 }} />
    <T x={425} y={179} muted>
      (filtrar pelo nome)
    </T>
    <PBtn box={{ x: 533, y: 172, w: 83, h: 28 }}>Recarregar</PBtn>
    <Card box={{ x: 24, y: 208, w: 592, h: 297 }} />
    {UNINSTALL_PROGRAMS.map((name, i) => {
      const r = UNINSTALL_CTRL.row(i);
      return (
        <PCheck key={name} x={r.x} y={r.y} on={s.checks[i] ?? 0}>
          {name}
        </PCheck>
      );
    })}
    <ScrollThumb x={593} y={222} h={262} />
    <PCheck x={UNINSTALL_CTRL.all.x} y={UNINSTALL_CTRL.all.y} on={s.all}>
      Aplicar na turma toda ({P_TOTAL} máquinas)
    </PCheck>
    <PBtn box={UNINSTALL_CTRL.run} kind="danger" hover={s.runHover} press={s.runPress}>
      Desinstalar os marcados
    </PBtn>
    <ResultCard
      box={{ x: 24, y: 582, w: 592, h: 57 }}
      progress={s.sent}
      machines={ONLINE_PCS}
      line={(n) => ({ text: `${n}: Jogo da Cobrinha 2.0 removido`, ok: true })}
    />
  </PDialog>
);
