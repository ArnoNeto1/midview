import React from "react";
import { clamp01 } from "../../../../lib/motion";
import { P_TOTAL, PAINEL_PCS, pDialog } from "../geometry";
import { Card, PBtn, PDialog, ResultCard, ScrollThumb, StepBadge, T, TURMA_TODA, icon } from "../kit";
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

export type LabState = { appear: number; hover?: number; hoverStep?: number; press?: number };

export const LabPanelDialog: React.FC<{ s: LabState }> = ({ s }) => (
  <PDialog title="Painel do laboratório" box={LAB} appear={s.appear} icon={PG.settings} group="configurar" heading="Painel do laboratório" hint={TURMA_TODA}>
    <T x={24} y={95} w={558} size={12}>
      Cada função é aplicada na máquina do aluno pelo Midview e continua valendo se ela reiniciar. O Chrome do aluno é fechado ao aplicar, para a
      mudança valer na hora.
    </T>
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
    <Card box={{ x: 24, y: 562, w: 546, h: 120 }} />
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
    <PBtn box={{ x: 364, y: 621, w: 93, h: 24 }}>Ativar</PBtn>
    <PBtn box={{ x: 464, y: 621, w: 93, h: 24 }}>Desativar</PBtn>
    <ScrollThumb x={574} y={124} h={214} />
  </PDialog>
);

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
};

export type MachinesState = { appear: number; page: 0 | 1; navHover?: number; updateHover?: number; updatePress?: number };

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
            background: on ? P.card : i === 1 ? `rgba(31,44,69,${clamp01(s.navHover ?? 0)})` : "transparent",
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
    {s.page === 0 ? <SearchPage /> : <UpdatesPage s={s} />}
  </PDialog>
);

/* ---------- Atualizar pela rede (UpdateWindow.xaml, foto 27) ---------- */

export const UPDATE = pDialog(666, 623);

export type UpdateState = { appear: number; progress: number };

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
      <PBtn box={{ x: 154, y: 247, w: 79, h: 28 }} kind="primary">
        Atualizar
      </PBtn>
      <PBtn box={{ x: 241, y: 247, w: 60, h: 28 }} kind="danger">
        Parar
      </PBtn>
      <T x={24} y={287}>
        {done < P_TOTAL ? `Atualizando ${done} de ${P_TOTAL} máquinas...` : `Pronto: as ${P_TOTAL} máquinas estão na versão 0.0.41.`}
      </T>
      <ResultCard
        box={{ x: 24, y: 331, w: 618, h: 271 }}
        progress={s.progress}
        line={(n) => ({ text: `${n}: atualizado`, ok: true })}
      />
    </PDialog>
  );
};
