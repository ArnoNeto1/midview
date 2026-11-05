import React from "react";
import { P_TOTAL, pDialog } from "../geometry";
import { Card, PBtn, PCheck, PDialog, PField, PRadio, ResultCard, ScrollThumb, T, TURMA_TODA, typed } from "../kit";
import { P, PG } from "../palette";
import { ONLINE_PCS } from "./programas";

const sending = `Enviando para ${ONLINE_PCS.length} máquinas...`;

/* ---------- Fechar programas (CloseAppsWindow.xaml, foto 51) ---------- */

export const CLOSE_APPS = pDialog(526, 643);
export const CLOSE_APPS_CTRL = { run: { x: 24, y: 324, w: 129, h: 28 } };

export type CloseAppsState = { appear: number; runHover?: number; runPress?: number; sent: number };

export const CloseAppsDialog: React.FC<{ s: CloseAppsState }> = ({ s }) => (
  <PDialog title="Fechar programas" box={CLOSE_APPS} appear={s.appear} icon={PG.cancel} group="programas" heading="Fechar programas" hint={TURMA_TODA}>
    <T x={24} y={95} w={478} muted>
      Fecha os programas abertos pelo aluno. A área de trabalho, a barra de tarefas e o Midview nunca são fechados.
    </T>
    <Card box={{ x: 24, y: 135, w: 478, h: 80 }} />
    <PRadio x={37} y={150} w={452} on={1}>
      Pedir para fechar: como clicar no X de cada janela. Programas com trabalho não salvo perguntam ao aluno antes.
    </PRadio>
    <PRadio x={37} y={188} w={452} on={0}>
      Forçar: encerra os programas sem perguntar. Trabalho não salvo é perdido.
    </PRadio>
    <Card box={{ x: 24, y: 225, w: 478, h: 86 }} />
    <PRadio x={37} y={239} on={1}>
      Avisar o aluno e esperar 1 minuto
    </PRadio>
    <PRadio x={37} y={261} on={0}>
      Avisar o aluno e esperar 30 segundos
    </PRadio>
    <PRadio x={37} y={283} on={0}>
      Agora, sem esperar (o aluno é avisado na hora)
    </PRadio>
    <PBtn box={CLOSE_APPS_CTRL.run} kind="primary" hover={s.runHover} press={s.runPress}>
      Fechar programas
    </PBtn>
    <ResultCard box={{ x: 24, y: 362, w: 478, h: 260 }} progress={s.sent} head={sending} machines={ONLINE_PCS} />
  </PDialog>
);

/* ---------- Bloquear programas (AppBlockWindow.xaml, foto 52) ---------- */

export const BLOCK_APPS = pDialog(760, 660);
export const BLOCK_APPS_ROWS = [
  { name: "Roblox (RobloxPlayerBeta.exe)", note: "— aberto em 3 de 15 máquinas" },
  { name: "Minecraft (Minecraft.exe)", note: "— aberto em 2 de 15 máquinas" },
  { name: "Discord (Discord.exe)", note: "— aberto em 1 de 15 máquinas" },
  { name: "Google Chrome (chrome.exe)", note: "— aberto em 15 de 15 máquinas" },
  { name: "Scratch (Scratch.exe)", note: "— aberto em 6 de 15 máquinas" },
];
export const BLOCK_APPS_CTRL = {
  row: (i: number) => ({ x: 37, y: 347 + i * 23, w: 220, h: 16 }),
  apply: { x: 24, y: 527, w: 119, h: 29 },
};

export type BlockAppsState = { appear: number; checks: number[]; applyHover?: number; applyPress?: number; sent: number };

export const BlockAppsDialog: React.FC<{ s: BlockAppsState }> = ({ s }) => (
  <PDialog title="Bloquear programas" box={BLOCK_APPS} appear={s.appear} icon={PG.apps} group="programas" heading="Bloquear programas" hint={`Em ${P_TOTAL} máquinas`}>
    <T x={24} y={96} size={12}>
      Nenhuma máquina tem bloqueio de programas.
    </T>
    <T x={24} y={122} size={14} weight={600}>
      Modo
    </T>
    <Card box={{ x: 24, y: 147, w: 712, h: 38 }} />
    <PRadio x={37} y={158} on={1}>
      Bloquear só os programas escolhidos — o resto continua funcionando.
    </PRadio>
    <Card box={{ x: 24, y: 193, w: 712, h: 38 }} />
    <PRadio x={37} y={204} on={0}>
      Permitir só os programas escolhidos — todo o resto é fechado ao abrir (o Windows e o Midview continuam funcionando).
    </PRadio>
    <T x={24} y={240} w={712} muted>
      O Prompt de Comando e o PowerShell também são fechados, a menos que estejam na lista. Alguns programas usam auxiliares sem janela (o Terminal do
      Windows usa o OpenConsole.exe): se algo fechar sozinho, adicione também o auxiliar. Os aplicativos da Microsoft Store (Calculadora, Fotos) não
      aparecem na lista. Teste antes em uma máquina.
    </T>
    <Card box={{ x: 24, y: 298, w: 712, h: 160 }} />
    <T x={37} y={317} title>
      Programas abertos
    </T>
    <PBtn box={{ x: 630, y: 311, w: 93, h: 26 }}>Atualizar lista</PBtn>
    {BLOCK_APPS_ROWS.map((row, i) => {
      const r = BLOCK_APPS_CTRL.row(i);
      return (
        <PCheck key={row.name} x={r.x} y={r.y} on={s.checks[i] ?? 0}>
          <span style={{ fontWeight: 600 }}>{row.name}</span> <span style={{ color: P.muted }}>{row.note}</span>
        </PCheck>
      );
    })}
    <ScrollThumb x={713} y={346} h={34} />
    <PField box={{ x: 24, y: 468, w: 628, h: 28 }} />
    <PBtn box={{ x: 660, y: 468, w: 76, h: 28 }}>Adicionar</PBtn>
    <T x={24} y={503} muted>
      Adicionar pelo nome (por exemplo chrome.exe).
    </T>
    <PBtn box={BLOCK_APPS_CTRL.apply} kind="primary" hover={s.applyHover} press={s.applyPress}>
      Aplicar bloqueio
    </PBtn>
    <PBtn box={{ x: 152, y: 527, w: 94, h: 29 }}>Liberar tudo</PBtn>
    <ResultCard box={{ x: 24, y: 567, w: 712, h: 72 }} progress={s.sent} machines={ONLINE_PCS} line={(n) => ({ text: `${n}: aplicado`, ok: true })} />
  </PDialog>
);

/* ---------- Bloquear sites (SiteBlockWindow.xaml, foto 53) ---------- */

export const BLOCK_SITES = pDialog(526, 643);
export const BLOCKED_SITE = "youtube.com";
export const BLOCK_SITES_CTRL = {
  internet: { x: 37, y: 173, w: 130, h: 26 },
  input: { x: 37, y: 261, w: 452, h: 24 },
  site: { x: 37, y: 292, w: 108, h: 26 },
};

export type BlockSitesState = { appear: number; typed: number; siteHover?: number; sitePress?: number; sent: number };

export const BlockSitesDialog: React.FC<{ s: BlockSitesState }> = ({ s }) => (
  <PDialog title="Bloquear sites" box={BLOCK_SITES} appear={s.appear} icon={PG.globe} group="programas" heading="Bloquear sites" hint={TURMA_TODA}>
    <T x={24} y={94} w={478} muted>
      O bloqueio continua valendo se a máquina reiniciar. O Midview continua funcionando mesmo com a internet bloqueada.
    </T>
    <Card box={{ x: 24, y: 135, w: 478, h: 77 }} />
    <T x={37} y={148} title>
      Internet inteira
    </T>
    <PBtn box={BLOCK_SITES_CTRL.internet} kind="danger">
      Bloquear a internet
    </PBtn>
    <PBtn box={{ x: 175, y: 173, w: 120, h: 26 }}>Liberar a internet</PBtn>
    <Card box={{ x: 24, y: 222, w: 478, h: 161 }} />
    <T x={37} y={235} title>
      Um site (por exemplo youtube.com)
    </T>
    <PField box={BLOCK_SITES_CTRL.input} text={typed(BLOCKED_SITE, s.typed)} caret={s.typed > 0 && s.typed < 1} focus={s.typed > 0} />
    <PBtn box={BLOCK_SITES_CTRL.site} kind="danger" hover={s.siteHover} press={s.sitePress}>
      Bloquear o site
    </PBtn>
    <PBtn box={{ x: 153, y: 292, w: 97, h: 26 }}>Liberar o site</PBtn>
    <T x={37} y={326} w={452} muted>
      Bloqueia o endereço com e sem www (youtube.com e www.youtube.com). Outros endereços do mesmo site, como m.youtube.com, e abas que já estavam
      abertas podem continuar funcionando.
    </T>
    <ResultCard box={{ x: 24, y: 393, w: 478, h: 229 }} progress={s.sent} head={sending} machines={ONLINE_PCS} />
  </PDialog>
);
