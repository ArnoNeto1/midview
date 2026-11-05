import React from "react";
import { clamp01 } from "../../../../lib/motion";
import { P_TOTAL, PAINEL_PCS, pDialog } from "../geometry";
import { Card, PBtn, PDialog, ResultCard, ScrollThumb, StepBadge, T, TURMA_TODA, icon, type BtnKind } from "../kit";
import { FoundCards, type FoundCardP } from "./sala";
import { GROUP_COLOR, P, PG } from "../palette";

/* ---------- Painel do laboratório (LabConfigWindow.xaml, foto 54) ---------- */

export const LAB = pDialog(606, 663);
export const LAB_STEPS = [
  { title: "Passo 1 — Bloqueador de anúncios", text: "Instala o bloqueador no Chrome; aparece sozinho na próxima vez que o aluno abrir o Chrome (vale no modo anônimo também).", y: 200, btn: 218, h: 62 },
  { title: "Passo 2 — Limpeza ao fechar", text: "O Chrome apaga histórico, cookies, senhas e downloads quando fecha.", y: 280, btn: 291, h: 46 },
  { title: "Passo 3 — Blindagem total", text: "Trava as configurações, a personalização, o modo anônimo e a página inicial do Chrome, e o papel de parede do Windows.", y: 346, btn: 365, h: 62 },
  { title: "Passo 4 — Bloquear o Microsoft Edge", text: "Fecha o Edge sempre que o aluno abrir, pelo mesmo bloqueio de programas do Midview.", y: 428, btn: 438, h: 46 },
  { title: "Passo 5 — Bloquear downloads", text: "Impede qualquer download pelo Chrome.", y: 494, btn: 497, h: 30 },
];
export const LAB_CTRL = { on: (i: number) => ({ x: 364, y: LAB_STEPS[i].btn, w: 93, h: 24 }) };

export type LabState = {
  appear: number;
  hover?: number;
  hoverStep?: number;
  press?: number;
  /** Rolagem (px) até os bloqueios extras, a manutenção e a consulta. */
  scroll?: number;
  /** Botão realçado numa linha de baixo (LAB_LOWER, pelo título). */
  hoverLower?: string | null;
};

/** As linhas de baixo do painel: título, texto, botões (com estilo) e onde ficam (conteúdo sem rolagem). */
const LAB_LOWER: { card?: { title: string; y: number; h: number }; title: string; text: string; y: number; buttons: { label: string; kind?: BtnKind; x: number; w: number }[]; textW?: number }[] = [
  { title: "Travar a barra de tarefas", text: "Impede o aluno de fixar ou desafixar ícones da barra de tarefas.", y: 680, buttons: [{ label: "Ativar", x: 364, w: 93 }, { label: "Desativar", x: 464, w: 93 }] },
  {
    title: "Travar a Área de Trabalho",
    text: "O aluno não consegue criar, apagar nem renomear nada na Área de Trabalho; dentro das pastas continua livre. Para salvar um arquivo, ele usa outra pasta (Documentos, Downloads).",
    y: 750,
    buttons: [{ label: "Ativar", x: 364, w: 93 }, { label: "Desativar", x: 464, w: 93 }],
  },
  {
    title: "Bloquear sites de distração",
    text: 'Redes sociais, jogos e YouTube, pelo mesmo "Bloquear sites" do Midview (vale em qualquer navegador). "Liberar" desbloqueia esses endereços.',
    y: 852,
    buttons: [{ label: "Bloquear", x: 364, w: 93 }, { label: "Liberar", x: 464, w: 93 }],
  },
  {
    card: { title: "Manutenção", y: 944, h: 241 },
    title: "Limpar perfis do Chrome",
    text: "Fecha o Chrome e apaga os perfis salvos de todos os usuários, deixando o navegador zerado para a próxima turma. Não dá para desfazer.",
    y: 992,
    textW: 380,
    buttons: [{ label: "Limpar agora", kind: "danger", x: 455, w: 93 }],
  },
  {
    title: "Apagar os downloads dos alunos",
    text: "Apaga a pasta Downloads de cada aluno: só os arquivos de hoje, ou tudo. Não dá para desfazer.",
    y: 1058,
    buttons: [{ label: "De hoje", x: 364, w: 93 }, { label: "Tudo", kind: "danger", x: 464, w: 93 }],
  },
  {
    title: "RESET geral",
    text: "Remove tudo o que este painel aplicou (e o que um script antigo tenha deixado) e devolve a máquina ao padrão.",
    y: 1124,
    textW: 380,
    buttons: [{ label: "RESET geral", kind: "danger", x: 457, w: 91 }],
  },
  {
    card: { title: "Consulta", y: 1199, h: 93 },
    title: "Ver o que está aplicado",
    text: "Lê de cada máquina o que está ligado agora.",
    y: 1247,
    buttons: [{ label: "Ver agora", x: 439, w: 110 }],
  },
];
/** Rolagem que mostra a manutenção e a consulta. */
export const LAB_SCROLL_MAX = 639;
/** Onde fica o primeiro botão de uma linha de baixo, no conteúdo sem rolagem. */
export const labLowerButton = (title: string) => {
  const row = LAB_LOWER.find((r) => r.title === title)!;
  const b = row.buttons[0];
  return { x: b.x, y: row.y + 12, w: b.w, h: 24 };
};
const CLIP_TOP = 124;

/** O conteúdo que rola, cortado abaixo do texto de cima; parado, fica como está (sem camada a mais). */
const ScrollClip: React.FC<{ scroll: number; children: React.ReactNode }> = ({ scroll, children }) =>
  scroll > 0 ? (
    <div style={{ position: "absolute", left: 0, top: CLIP_TOP, width: LAB.w, height: LAB.h - 32 - CLIP_TOP, overflow: "hidden" }}>
      <div style={{ position: "absolute", left: 0, top: -CLIP_TOP, width: LAB.w, height: 1400, transform: `translateY(${-scroll}px)` }}>{children}</div>
    </div>
  ) : (
    <>{children}</>
  );

export const LabPanelDialog: React.FC<{ s: LabState }> = ({ s }) => {
  const scroll = s.scroll ?? 0;
  return (
  <PDialog title="Painel do laboratório" box={LAB} appear={s.appear} icon={PG.settings} group="configurar" heading="Painel do laboratório" hint={TURMA_TODA}>
    <T x={24} y={95} w={558} size={12}>
      Cada função é aplicada na máquina do aluno pelo Midview e continua valendo se ela reiniciar. O Chrome do aluno é fechado ao aplicar, para a
      mudança valer na hora.
    </T>
    <ScrollClip scroll={scroll}>
    <Card box={{ x: 24, y: 135, w: 546, h: 413 }} />
    <T x={37} y={149} title>
      Sequência de configuração ideal (início da aula)
    </T>
    <T x={37} y={168} muted>
      Para preparar as máquinas no começo da aula, aplique os passos 1 a 5 nesta ordem.
    </T>
    {LAB_STEPS.map((step, i) => {
      const b = LAB_CTRL.on(i);
      return (
        <React.Fragment key={step.title}>
          <div style={{ position: "absolute", left: 37, top: step.y - 9, width: 520, height: 1, background: P.line }} />
          <StepBadge x={37} y={step.y + 1} n={i + 1} />
          <T x={71} y={step.y} weight={600} size={12.5}>
            {step.title}
          </T>
          <T x={71} y={step.y + 17} w={282} muted>
            {step.text}
          </T>
          <PBtn box={b} hover={s.hoverStep === i ? s.hover : 0} press={s.hoverStep === i ? s.press : 0}>
            Ativar
          </PBtn>
          <PBtn box={{ x: 464, y: step.btn, w: 93, h: 24 }}>Desativar</PBtn>
        </React.Fragment>
      );
    })}
    <Card box={{ x: 24, y: 562, w: 546, h: scroll > 0 ? 368 : 120 }} />
    <T x={37} y={577} title>
      Bloqueios extras
    </T>
    <div style={{ position: "absolute", left: 37, top: 601, width: 520, height: 1, background: P.line }} />
    <T x={37} y={610} weight={600} size={12}>
      Modo visitante
    </T>
    <T x={37} y={627} w={300} muted>
      O Chrome só abre como visitante, sem perfis, login nem histórico.
    </T>
    <PBtn box={{ x: 364, y: 621, w: 93, h: 24 }} hover={s.hoverLower === "Modo visitante" ? 1 : 0}>
      Ativar
    </PBtn>
    <PBtn box={{ x: 464, y: 621, w: 93, h: 24 }}>Desativar</PBtn>
    {scroll > 0
      ? LAB_LOWER.map((row) => (
          <React.Fragment key={row.title}>
            {row.card ? (
              <>
                <Card box={{ x: 24, y: row.card.y, w: 546, h: row.card.h }} />
                <T x={37} y={row.card.y + 15} title>
                  {row.card.title}
                </T>
              </>
            ) : null}
            <div style={{ position: "absolute", left: 37, top: row.y - 9, width: 520, height: 1, background: P.line }} />
            <T x={37} y={row.y} weight={600} size={12}>
              {row.title}
            </T>
            <T x={37} y={row.y + 17} w={row.textW ?? 300} muted>
              {row.text}
            </T>
            {row.buttons.map((b, bi) => (
              <PBtn key={b.label} box={{ x: b.x, y: row.y + 12, w: b.w, h: 24 }} kind={b.kind} hover={bi === 0 && s.hoverLower === row.title ? 1 : 0}>
                {b.label}
              </PBtn>
            ))}
          </React.Fragment>
        ))
      : null}
    </ScrollClip>
    <ScrollThumb x={574} y={124 + (scroll / LAB_SCROLL_MAX) * (663 - 124 - 214 - 10)} h={214} />
  </PDialog>
  );
};

/* ---------- Máquinas da sala, página Atualizações (MachinesWindow.xaml, foto 12) ---------- */

export const MACHINES = pDialog(806, 563);
const NAV = [
  { label: "Procurar e parear", glyph: PG.search },
  { label: "Atualizações", glyph: PG.restart },
  { label: "Outro professor", glyph: PG.people },
  { label: "Senha de administrador", glyph: PG.lock },
  { label: "Avançado", glyph: PG.settings },
];
export const MACHINES_CTRL = {
  nav: (i: number) => ({ x: 8, y: 18 + i * 38, w: 180, h: 32 }),
  update: { x: 237, y: 137, w: 211, h: 28 },
  search: { x: 224, y: 115, w: 121, h: 28 },
  pair: { x: 353, y: 115, w: 156, h: 28 },
  give: { x: 237, y: 128, w: 167, h: 28 },
  allow: { x: 412, y: 128, w: 226, h: 28 },
};

export type MachinesState = {
  appear: number;
  /** 0 Procurar e parear, 1 Atualizações, 2 Outro professor. */
  page: 0 | 1 | 2;
  navHover?: number;
  /** Item do menu à esquerda sob o mouse (padrão: Atualizações, com `navHover`). */
  navHoverIndex?: number;
  updateHover?: number;
  updatePress?: number;
  /** Procurar e parear com máquinas achadas (sem isto, a lista parada do vídeo promocional). */
  found?: FoundCardP[];
  foundP?: number;
  status?: string;
  searchHover?: number;
  searchPress?: number;
  pairHover?: number;
  pairPress?: number;
  pairEnabled?: boolean;
  giveHover?: number;
  givePress?: number;
  allowHover?: number;
  allowPress?: number;
  allowResult?: number;
};

const PageHeader: React.FC<{ glyph: string; title: string; hint?: string }> = ({ glyph, title, hint }) => (
  <>
    <div style={{ position: "absolute", left: 224, top: 24, width: 40, height: 40 }}>
      <div style={{ position: "absolute", inset: 0, borderRadius: 12, background: GROUP_COLOR.configurar, opacity: 0.18 }} />
      <span style={{ ...icon(19, GROUP_COLOR.configurar), position: "absolute", left: 0, right: 0, top: 10, textAlign: "center" }}>{glyph}</span>
    </div>
    <T x={278} y={31} size={20} weight={600}>
      {title}
    </T>
    {hint ? (
      <T x={278} y={70} w={500} muted>
        {hint}
      </T>
    ) : null}
  </>
);

/** Procurar e parear, com as máquinas achadas, o código e o "Confere" (foto 11). */
const FoundPage: React.FC<{ s: MachinesState }> = ({ s }) => (
  <>
    <PageHeader glyph={PG.search} title="Procurar e parear" hint="Procura as máquinas nesta rede. Ao parear, confira se o código na tela do aluno é o mesmo daqui. Você tem 5 minutos para isso." />
    <PBtn box={MACHINES_CTRL.search} kind="primary" hover={s.searchHover} press={s.searchPress}>
      Procurar na rede
    </PBtn>
    <PBtn box={MACHINES_CTRL.pair} hover={s.pairHover} press={s.pairPress} disabled={!s.pairEnabled}>
      Parear as selecionadas
    </PBtn>
    <T x={521} y={121} muted>
      {s.status ?? ""}
    </T>
    <div style={{ position: "absolute", left: 0, top: 150, width: MACHINES.w, height: 372, overflow: "hidden" }}>
      <div style={{ position: "absolute", left: 0, top: -150, width: MACHINES.w, height: 900 }}>
        <FoundCards cards={s.found ?? []} p={s.foundP ?? 1} dialogW={MACHINES.w} />
      </div>
    </div>
    <div style={{ position: "absolute", left: 224, top: 535, fontSize: 12, color: P.muted }}>
      Um colega já tem? <span style={{ color: P.accentText }}>Entrar na sala de um colega...</span>
    </div>
  </>
);

/** Outro professor: dar acesso a um colega ou liberar o pareamento por 10 minutos (foto 13). */
const OtherTeacherPage: React.FC<{ s: MachinesState }> = ({ s }) => (
  <>
    <PageHeader
      glyph={PG.people}
      title="Outro professor"
      hint="Dê um código a um colega (nada aparece na tela dos alunos) ou deixe as máquinas aceitarem o pareamento dele por 10 minutos."
    />
    <Card box={{ x: 224, y: 115, w: 554, h: 54 + ((s.allowResult ?? 0) > 0.01 ? 24 : 0) }} />
    <PBtn box={MACHINES_CTRL.give} hover={s.giveHover} press={s.givePress}>
      Dar acesso a um colega...
    </PBtn>
    <PBtn box={MACHINES_CTRL.allow} hover={s.allowHover} press={s.allowPress}>
      Permitir novo professor (10 minutos)
    </PBtn>
    {(s.allowResult ?? 0) > 0.01 ? (
      <div style={{ position: "absolute", left: 237, top: 165, fontSize: 12, opacity: clamp01(s.allowResult ?? 0) }}>
        {P_TOTAL - 1} de {P_TOTAL - 1} máquinas liberadas por 10 minutos.
      </div>
    ) : null}
  </>
);

const SearchPage: React.FC = () => (
  <>
    <div style={{ position: "absolute", left: 224, top: 24, width: 40, height: 40 }}>
      <div style={{ position: "absolute", inset: 0, borderRadius: 12, background: GROUP_COLOR.configurar, opacity: 0.18 }} />
      <span style={{ ...icon(19, GROUP_COLOR.configurar), position: "absolute", left: 0, right: 0, top: 10, textAlign: "center" }}>{PG.search}</span>
    </div>
    <T x={278} y={31} size={20} weight={600}>
      Procurar e parear
    </T>
    <T x={278} y={70} w={500} muted>
      Procura as máquinas nesta rede. Ao parear, confira se o código na tela do aluno é o mesmo daqui. Você tem 5 minutos para isso.
    </T>
    <PBtn box={{ x: 224, y: 115, w: 121, h: 28 }} kind="primary">
      Procurar na rede
    </PBtn>
    <PBtn box={{ x: 353, y: 115, w: 156, h: 28 }} disabled>
      Parear as selecionadas
    </PBtn>
    {PAINEL_PCS.slice(0, 4).map((pc, i) => (
      <React.Fragment key={pc.name}>
        <Card box={{ x: 224, y: 153 + i * 60, w: 554, h: 52 }} />
        <span style={{ ...icon(19, GROUP_COLOR.configurar), position: "absolute", left: 262, top: 167 + i * 60 }}>{PG.laptop}</span>
        <T x={295} y={164 + i * 60} weight={600} size={12}>
          {pc.name}
        </T>
        <T x={295} y={180 + i * 60} muted size={11.5}>
          192.168.0.{11 + i}
        </T>
        <div
          style={{
            position: "absolute",
            left: 706,
            top: 170 + i * 60,
            padding: "1px 10px",
            borderRadius: 999,
            border: `1px solid ${P.line}`,
            background: P.bg,
            fontSize: 11,
            fontWeight: 600,
          }}
        >
          Na sala
        </div>
      </React.Fragment>
    ))}
  </>
);

const UpdatesPage: React.FC<{ s: MachinesState }> = ({ s }) => (
  <>
    <div style={{ position: "absolute", left: 224, top: 24, width: 40, height: 40 }}>
      <div style={{ position: "absolute", inset: 0, borderRadius: 12, background: GROUP_COLOR.configurar, opacity: 0.18 }} />
      <span style={{ ...icon(19, GROUP_COLOR.configurar), position: "absolute", left: 0, right: 0, top: 10, textAlign: "center" }}>{PG.sync}</span>
    </div>
    <T x={278} y={31} size={20} weight={600}>
      Atualizações
    </T>
    <Card box={{ x: 224, y: 80, w: 554, h: 99 }} />
    <T x={237} y={94} title>
      Atualizar as máquinas
    </T>
    <T x={237} y={113} muted>
      Versão do programa do aluno neste PC: 0.0.41
    </T>
    <PBtn box={MACHINES_CTRL.update} hover={s.updateHover} press={s.updatePress}>
      Atualizar as máquinas pela rede...
    </PBtn>
    <Card box={{ x: 224, y: 190, w: 554, h: 113 }} />
    <T x={237} y={204} title>
      Atualizar professor
    </T>
    <T x={237} y={223} w={528} muted>
      Versão deste PC: 0.0.41, a mais nova publicada.
    </T>
    <PBtn box={{ x: 237, y: 262, w: 122, h: 28 }} disabled>
      Procurar de novo
    </PBtn>
  </>
);

export const MachinesDialog: React.FC<{ s: MachinesState }> = ({ s }) => (
  <PDialog title="Máquinas da sala" box={MACHINES} appear={s.appear}>
    <div style={{ position: "absolute", left: 0, top: 0, width: 196, height: MACHINES.h, background: P.bg, borderRight: `1px solid ${P.line}` }} />
    {NAV.map((item, i) => {
      const b = MACHINES_CTRL.nav(i);
      const on = s.page === i;
      return (
        <div
          key={item.label}
          style={{
            position: "absolute",
            left: b.x,
            top: b.y,
            width: b.w,
            height: b.h,
            borderRadius: 6,
            background: on ? P.card : i === (s.navHoverIndex ?? 1) ? `rgba(31,44,69,${clamp01(s.navHover ?? 0)})` : "transparent",
            display: "flex",
            alignItems: "center",
            fontSize: 12.5,
            fontWeight: on ? 600 : 400,
            color: on ? P.text : P.muted,
          }}
        >
          {on ? <div style={{ position: "absolute", left: 0, top: 7, width: 3, height: 18, borderRadius: 2, background: P.accent }} /> : null}
          <span style={{ ...icon(15, on ? P.accentText : P.muted), width: 39, textAlign: "center" }}>{item.glyph}</span>
          {item.label}
        </div>
      );
    })}
    {s.page === 0 ? s.found ? <FoundPage s={s} /> : <SearchPage /> : s.page === 1 ? <UpdatesPage s={s} /> : <OtherTeacherPage s={s} />}
  </PDialog>
);

/* ---------- Atualizar pela rede (UpdateWindow.xaml, foto 27) ---------- */

export const UPDATE = pDialog(666, 623);

export type UpdateState = {
  appear: number;
  progress: number;
  /** Andamento passo a passo: o texto de cima e uma linha por máquina (no lugar do contador). */
  step?: string;
  lines?: { name: string; text: string; ok?: boolean }[];
  runHover?: number;
  runPress?: number;
};
export const UPDATE_CTRL = { run: { x: 154, y: 247, w: 79, h: 28 } };

export const UpdateDialog: React.FC<{ s: UpdateState }> = ({ s }) => {
  const done = Math.round(clamp01(s.progress) * P_TOTAL);
  return (
    <PDialog
      title="Atualizar pela rede"
      box={UPDATE}
      appear={s.appear}
      icon={PG.sync}
      group="configurar"
      heading="Atualizar pela rede"
      hint={<span style={{ color: P.text }}>Programa do aluno: C:\Program Files\Midview Professor\aluno</span>}
    >
      <Card box={{ x: 24, y: 98, w: 618, h: 135 }} />
      <T x={37} y={110} weight={600}>
        Versão do pacote: 0.0.41
      </T>
      <T x={37} y={133} w={596} muted>
        Atualiza o programa dos alunos pela rede: uma máquina primeiro, e as outras só depois que ela confirmar que a versão nova funciona; se não
        funcionar, ela volta sozinha para a anterior. Por 1 a 3 minutos você perde a visão e o controle de cada máquina; os alunos podem continuar
        usando o computador, mas mensagens e conversas abertas fecham e o teclado e o mouse são destravados. Não desligue as máquinas até terminar.
      </T>
      <PBtn box={{ x: 24, y: 247, w: 122, h: 28 }}>Verificar versões</PBtn>
      <PBtn box={UPDATE_CTRL.run} kind="primary" hover={s.runHover} press={s.runPress}>
        Atualizar
      </PBtn>
      <PBtn box={{ x: 241, y: 247, w: 60, h: 28 }} kind="danger">
        Parar
      </PBtn>
      {s.lines ? (
        <>
          <T x={24} y={287}>
            {s.step ?? ""}
          </T>
          <Card box={{ x: 24, y: 331, w: 618, h: 271 }} />
          <div style={{ position: "absolute", left: 36, top: 345, fontSize: 12, lineHeight: "14.5px", fontWeight: 500 }}>
            {s.lines.map((l) => (
              <div key={l.name} style={{ color: l.ok ? P.text : P.muted }}>
                {l.name}: {l.text}
              </div>
            ))}
          </div>
        </>
      ) : (
        <>
          <T x={24} y={287}>
            {done < P_TOTAL ? `Atualizando ${done} de ${P_TOTAL} máquinas...` : `Pronto: as ${P_TOTAL} máquinas estão na versão 0.0.41.`}
          </T>
          <ResultCard
            box={{ x: 24, y: 331, w: 618, h: 271 }}
            progress={s.progress}
            line={(n) => ({ text: `${n}: atualizado`, ok: true })}
          />
        </>
      )}
    </PDialog>
  );
};
