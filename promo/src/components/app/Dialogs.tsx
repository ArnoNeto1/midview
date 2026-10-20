import React from "react";
import { clamp01, mix } from "../../lib/motion";
import { MidviewLogo } from "../MidviewLogo";
import { useAppTheme } from "./appTheme";
import { CTRL, DLG, LAB_ROW, LAB_STEPS, MENU, MENU_ITEMS } from "./dialogLayout";
import { TOOLBAR_LAYOUT, type Box, type Point } from "./geometry";
import { GLYPH, Glyph } from "./Glyph";
import type { Pc } from "./pcs";
import { ScreenContent } from "./ScreenContent";
import { StudentOverlays } from "./StudentOverlays";
import { IDLE_TILE } from "./MachineTile";

/* ---------- peças comuns ---------- */

const Frame: React.FC<{ title: string; box: Box; appear: number; closeHover?: number; children: React.ReactNode }> = ({
  title,
  box,
  appear,
  closeHover = 0,
  children,
}) => {
  const theme = useAppTheme();
  const d = theme.dialog;
  if (appear <= 0.01) return null;
  return (
    <div
      style={{
        position: "absolute",
        left: box.x,
        top: box.y,
        width: box.w,
        height: box.h,
        borderRadius: 8,
        overflow: "hidden",
        background: d.bg,
        border: `1px solid ${d.line}`,
        boxShadow: "0 28px 70px -14px rgba(0,0,0,0.6), 0 0 0 1px rgba(0,0,0,0.05)",
        opacity: clamp01(appear * 1.4),
        transform: `scale(${mix(0.95, 1, clamp01(appear))})`,
        fontFamily: theme.font,
        color: d.text,
      }}
    >
      <div
        style={{
          position: "relative",
          height: 32,
          background: d.title,
          borderBottom: `1px solid ${d.line}`,
          display: "flex",
          alignItems: "center",
          gap: 8,
          paddingLeft: 12,
          fontSize: 12,
        }}
      >
        <MidviewLogo size={15} />
        {title}
        <div
          style={{
            position: "absolute",
            right: 0,
            top: 0,
            width: 46,
            height: 32,
            display: "flex",
            alignItems: "center",
            justifyContent: "center",
            background: `rgba(196,43,28,${clamp01(closeHover)})`,
            color: closeHover > 0.5 ? "#FFFFFF" : d.text,
          }}
        >
          <Glyph code={GLYPH.close} size={10} />
        </div>
      </div>
      {children}
    </div>
  );
};

const Text: React.FC<{ x: number; y: number; w?: number; size?: number; weight?: number; muted?: boolean; children: React.ReactNode }> = ({
  x,
  y,
  w,
  size = 12.5,
  weight = 400,
  muted = false,
  children,
}) => {
  const theme = useAppTheme();
  return (
    <div
      style={{
        position: "absolute",
        left: x,
        top: y,
        width: w,
        fontSize: size,
        fontWeight: weight,
        lineHeight: 1.35,
        color: muted ? theme.dialog.muted : theme.dialog.text,
      }}
    >
      {children}
    </div>
  );
};

const Btn: React.FC<{ box: Box; hover?: number; press?: number; strong?: boolean; children: React.ReactNode }> = ({
  box,
  hover = 0,
  press = 0,
  strong = false,
  children,
}) => {
  const theme = useAppTheme();
  const b = theme.button;
  return (
    <div
      style={{
        position: "absolute",
        left: box.x,
        top: box.y,
        width: box.w,
        height: box.h,
        borderRadius: 4,
        border: `1px solid ${hover > 0.5 ? theme.card.hover : b.line}`,
        background: b.bg,
        overflow: "hidden",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        fontSize: 12.5,
        fontWeight: strong ? 600 : 400,
        color: b.text,
        transform: `scale(${1 - 0.04 * clamp01(press)})`,
      }}
    >
      <div style={{ position: "absolute", inset: 0, background: b.hover, opacity: clamp01(hover) }} />
      <div style={{ position: "absolute", inset: 0, background: b.pressed, opacity: clamp01(press) }} />
      <span style={{ position: "relative" }}>{children}</span>
    </div>
  );
};

const Field: React.FC<{ box: Box; text: string; caret?: boolean; multiline?: boolean; disabled?: boolean }> = ({
  box,
  text,
  caret = false,
  multiline = false,
  disabled = false,
}) => {
  const theme = useAppTheme();
  return (
    <div
      style={{
        position: "absolute",
        left: box.x,
        top: box.y,
        width: box.w,
        height: box.h,
        borderRadius: 4,
        border: `1px solid ${caret ? theme.selected.line : theme.dialog.line}`,
        background: disabled ? theme.dialog.bg : theme.dialog.input,
        padding: multiline ? "8px 10px" : "0 10px",
        display: "flex",
        alignItems: multiline ? "flex-start" : "center",
        fontSize: 13,
        lineHeight: 1.4,
        color: theme.dialog.text,
        whiteSpace: multiline ? "normal" : "nowrap",
        overflow: "hidden",
      }}
    >
      <span>
        {text}
        {caret ? <span style={{ borderRight: `1.5px solid ${theme.dialog.text}`, marginLeft: 1 }}>&#8203;</span> : null}
      </span>
    </div>
  );
};

const Check: React.FC<{ x: number; y: number; on: number; w?: number; radio?: boolean; children: React.ReactNode }> = ({
  x,
  y,
  on,
  w,
  radio = false,
  children,
}) => {
  const theme = useAppTheme();
  const c = clamp01(on);
  return (
    <div style={{ position: "absolute", left: x, top: y, width: w, display: "flex", gap: 8, fontSize: 12.5, lineHeight: 1.35 }}>
      <span
        style={{
          flex: "0 0 auto",
          marginTop: 1,
          width: 15,
          height: 15,
          borderRadius: radio ? 8 : 3,
          border: `1px solid ${c > 0.5 ? theme.dialog.check : theme.dialog.muted}`,
          background: theme.dialog.input,
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
        }}
      >
        {radio ? (
          <span style={{ width: 7, height: 7, borderRadius: 4, background: theme.dialog.check, opacity: c }} />
        ) : (
          <Glyph code={GLYPH.check} size={11} color={theme.dialog.check} style={{ opacity: c }} />
        )}
      </span>
      <span>{children}</span>
    </div>
  );
};

const typed = (text: string, p: number) => text.slice(0, Math.round(clamp01(p) * text.length));

/* ---------- diálogos ---------- */

export type DialogsState = {
  view: { appear: number; controlling: boolean; disconnectHover: number; disconnectPress: number; remote: Point | null; remotePress: number };
  message: { appear: number; typed: number; sendHover: number; sendPress: number };
  chat: { appear: number; lines: number[]; closeHover: number };
  sendFile: { appear: number; sendHover: number; sendPress: number };
  collect: { appear: number; runHover: number; runPress: number; progress: number };
  openSite: { appear: number; typed: number; runHover: number; runPress: number };
  blockApps: { appear: number; checks: number[]; applyHover: number; applyPress: number };
  blockSites: { appear: number; typed: number; siteHover: number; sitePress: number };
  closeApps: { appear: number; runHover: number; runPress: number };
  schedule: { appear: number; days: number[]; saveHover: number; savePress: number };
  labPanel: { appear: number; steps: number[]; firstHover: number; firstPress: number };
  machines: { appear: number; updateHover: number; updatePress: number };
  update: { appear: number; progress: number };
  menu: { appear: number; at: Point; hover: number };
  tooltip: number;
};

export const MESSAGE_TEXT = "A atividade termina em 10 minutos. Salvem seus trabalhos.";
export const SITE_TEXT = "escola.edu.br/atividade";
export const BLOCKED_SITE = "youtube.com";

const ViewWindow: React.FC<{ s: DialogsState["view"]; pc: Pc; frame: number }> = ({ s, pc, frame }) => {
  const theme = useAppTheme();
  const box = DLG.view;
  const scr = CTRL.viewScreen;
  const k = scr.w / 320;
  return (
    <Frame title={`Midview - ${s.controlling ? "controlando" : "vendo"} ${pc.name}`} box={box} appear={s.appear}>
      <Btn box={CTRL.viewFull}>
        <Glyph code={GLYPH.fullScreen} size={11} style={{ marginRight: 6 }} />
        Tela Cheia
      </Btn>
      <Btn box={CTRL.viewDisconnect} hover={s.disconnectHover} press={s.disconnectPress}>
        <Glyph code={GLYPH.disconnect} size={11} style={{ marginRight: 6 }} />
        Desconectar
      </Btn>
      <div
        style={{
          position: "absolute",
          left: scr.x,
          top: scr.y,
          width: scr.w,
          height: scr.h,
          borderRadius: 4,
          overflow: "hidden",
          background: theme.card.screen,
        }}
      >
        <div style={{ position: "absolute", left: 0, top: 0, width: 320, height: 200, transformOrigin: "0 0", transform: `scale(${k}) translateY(-10px)` }}>
          <ScreenContent kind={pc.kind} frame={frame} variant={pc.variant} wallpaper={2} />
          <StudentOverlays view={{ ...IDLE_TILE, control: s.controlling ? 1 : 0 }} />
        </div>
        {s.remote ? (
          <svg
            width={24}
            height={30}
            viewBox="0 0 30 36"
            style={{ position: "absolute", left: s.remote.x - 3, top: s.remote.y - 2, transform: `scale(${1 - 0.15 * s.remotePress})` }}
          >
            <path d="M3 2 L3 27.5 L9.6 21.6 L14 31.8 L18.6 29.9 L14.3 19.9 L23 19.9 Z" fill="#111827" stroke="#FFFFFF" strokeWidth={1.6} />
          </svg>
        ) : null}
      </div>
    </Frame>
  );
};

const MessageDialog: React.FC<{ s: DialogsState["message"] }> = ({ s }) => (
  <Frame title="Enviar mensagem" box={DLG.message} appear={s.appear}>
    <Field box={CTRL.messageText} text={typed(MESSAGE_TEXT, s.typed)} caret={s.typed < 1} multiline />
    <Btn box={CTRL.messageSend} hover={s.sendHover} press={s.sendPress} strong>
      Enviar
    </Btn>
  </Frame>
);

const CHAT_LINES = [
  { who: "Você (para a turma)", text: "Dúvidas no exercício 3?", me: true },
  { who: "PC-05", text: "Na parte 2, professor!", me: false },
  { who: "PC-02", text: "Eu também.", me: false },
];

const ChatDialog: React.FC<{ s: DialogsState["chat"] }> = ({ s }) => {
  const theme = useAppTheme();
  return (
    <Frame title="Bate-papo da turma" box={DLG.chat} appear={s.appear} closeHover={s.closeHover}>
      <div
        style={{
          position: "absolute",
          left: CTRL.chatList.x,
          top: CTRL.chatList.y,
          width: CTRL.chatList.w,
          height: CTRL.chatList.h,
          borderRadius: 4,
          border: `1px solid ${theme.dialog.line}`,
          background: theme.dialog.panel,
          padding: 12,
          display: "flex",
          flexDirection: "column",
          gap: 10,
        }}
      >
        {CHAT_LINES.map((line, i) => {
          const p = clamp01(s.lines[i] ?? 0);
          return p > 0.01 ? (
            <div key={i} style={{ alignSelf: line.me ? "flex-end" : "flex-start", maxWidth: 280, opacity: p, transform: `translateY(${(1 - p) * 8}px)` }}>
              <div style={{ fontSize: 11, color: theme.dialog.muted, marginBottom: 2, textAlign: line.me ? "right" : "left" }}>{line.who}</div>
              <div
                style={{
                  padding: "7px 10px",
                  borderRadius: 8,
                  fontSize: 13,
                  background: line.me ? theme.selected.fill : theme.dialog.bg,
                  border: `1px solid ${line.me ? theme.selected.line : theme.dialog.line}`,
                }}
              >
                {line.text}
              </div>
            </div>
          ) : null;
        })}
      </div>
      <Field box={CTRL.chatInput} text="" />
      <Btn box={CTRL.chatSend}>Enviar</Btn>
    </Frame>
  );
};

const SendFileDialog: React.FC<{ s: DialogsState["sendFile"] }> = ({ s }) => (
  <Frame title="Enviar arquivo" box={DLG.sendFile} appear={s.appear}>
    <Text x={16} y={44} w={528}>
      O arquivo é salvo na pasta "Arquivos do professor" na Área de Trabalho do aluno, que recebe um aviso.
    </Text>
    <Btn box={CTRL.sendFileChoose}>Escolher arquivo...</Btn>
    <Text x={184} y={98} weight={600}>
      atividade-3.pdf
    </Text>
    <Check x={16} y={138} on={0}>
      Substituir arquivo com o mesmo nome
    </Check>
    <Text x={16} y={172} muted>
      Depois de enviar:
    </Text>
    <Check x={16} y={194} on={0} radio>
      Não fazer nada
    </Check>
    <Check x={16} y={218} on={1} radio>
      Abrir a pasta "Arquivos do professor" no aluno
    </Check>
    <Check x={16} y={242} on={0} radio>
      Abrir o arquivo no aluno, com o programa padrão
    </Check>
    <Btn box={CTRL.sendFileSend} hover={s.sendHover} press={s.sendPress} strong>
      Enviar
    </Btn>
  </Frame>
);

const CollectDialog: React.FC<{ s: DialogsState["collect"] }> = ({ s }) => {
  const theme = useAppTheme();
  const rows = ["PC-01", "PC-02", "PC-03", "PC-04", "PC-05"];
  return (
    <Frame title="Recolher arquivos" box={DLG.collect} appear={s.appear}>
      <Text x={16} y={44} w={528}>
        Traz o que cada aluno colocou na pasta "Entregar ao professor" da Área de Trabalho. Os arquivos continuam no aluno, que recebe um aviso.
      </Text>
      <Btn box={CTRL.collectRun} hover={s.runHover} press={s.runPress} strong>
        Recolher
      </Btn>
      <Btn box={CTRL.collectOpen}>Abrir pasta</Btn>
      {rows.map((name, i) => {
        const done = clamp01(s.progress * rows.length - i);
        return (
          <div key={name} style={{ position: "absolute", left: 16, top: 146 + i * 24, width: 528, display: "flex", fontSize: 12.5, opacity: 0.35 + 0.65 * done }}>
            <span style={{ width: 80, fontWeight: 600 }}>{name}</span>
            <span style={{ color: theme.dialog.muted }}>{done > 0.99 ? `${2 + (i % 2)} arquivos recolhidos` : "aguardando..."}</span>
          </div>
        );
      })}
      <Text x={16} y={276} muted size={11.5}>
        Os recolhidos ficam neste PC, numa pasta por máquina.
      </Text>
    </Frame>
  );
};

const OpenSiteDialog: React.FC<{ s: DialogsState["openSite"] }> = ({ s }) => (
  <Frame title="Abrir site" box={DLG.openSite} appear={s.appear}>
    <Text x={16} y={44} w={488}>
      Abre o site no navegador do aluno. Um site bloqueado continua bloqueado.
    </Text>
    <Text x={16} y={82} muted>
      Endereço do site:
    </Text>
    <Field box={CTRL.openSiteInput} text={typed(SITE_TEXT, s.typed)} caret={s.typed < 1} />
    <Check x={16} y={150} on={0}>
      Salvar no menu
    </Check>
    <Text x={16} y={180} muted>
      Nome no menu:
    </Text>
    <Btn box={CTRL.openSiteRun} hover={s.runHover} press={s.runPress} strong>
      Abrir nas máquinas
    </Btn>
  </Frame>
);

const APPS = ["Roblox (RobloxPlayerBeta.exe)", "Discord (Discord.exe)", "Steam (steam.exe)", "Paint (mspaint.exe)", "Bloco de Notas (notepad.exe)"];

const BlockAppsDialog: React.FC<{ s: DialogsState["blockApps"] }> = ({ s }) => {
  const theme = useAppTheme();
  const list = CTRL.blockAppsList;
  return (
    <Frame title="Bloquear programas" box={DLG.blockApps} appear={s.appear}>
      <Text x={16} y={44} weight={600}>
        Modo
      </Text>
      <Check x={16} y={66} on={1} radio w={560}>
        Bloquear só os programas escolhidos — o resto continua funcionando.
      </Check>
      <Check x={16} y={90} on={0} radio w={560}>
        Permitir só os programas escolhidos — todo o resto é fechado ao abrir.
      </Check>
      <Text x={16} y={150} weight={600}>
        Programas abertos
      </Text>
      <div
        style={{
          position: "absolute",
          left: list.x,
          top: list.y,
          width: list.w,
          height: list.h,
          borderRadius: 4,
          border: `1px solid ${theme.dialog.line}`,
          background: theme.dialog.panel,
        }}
      >
        {APPS.map((app, i) => (
          <Check key={app} x={12} y={10 + i * 30} on={s.checks[i] ?? 0}>
            {app}
          </Check>
        ))}
      </div>
      <Btn box={CTRL.blockAppsApply} hover={s.applyHover} press={s.applyPress} strong>
        Aplicar bloqueio
      </Btn>
      <Btn box={{ x: 164, y: 378, w: 120, h: 32 }}>Liberar tudo</Btn>
    </Frame>
  );
};

const BlockSitesDialog: React.FC<{ s: DialogsState["blockSites"] }> = ({ s }) => (
  <Frame title="Bloquear sites" box={DLG.blockSites} appear={s.appear}>
    <Text x={16} y={44} w={548}>
      O bloqueio continua valendo se a máquina reiniciar. O Midview continua funcionando mesmo com a internet bloqueada.
    </Text>
    <Text x={16} y={96} muted>
      Internet inteira:
    </Text>
    <Btn box={{ x: 16, y: 118, w: 160, h: 30 }}>Bloquear a internet</Btn>
    <Btn box={{ x: 184, y: 118, w: 150, h: 30 }}>Liberar a internet</Btn>
    <Text x={16} y={172} muted>
      Um site (por exemplo youtube.com):
    </Text>
    <Field box={CTRL.blockSitesInput} text={typed(BLOCKED_SITE, s.typed)} caret={s.typed < 1} />
    <Btn box={CTRL.blockSitesSite} hover={s.siteHover} press={s.sitePress} strong>
      Bloquear o site
    </Btn>
    <Btn box={{ x: 164, y: 240, w: 130, h: 30 }}>Liberar o site</Btn>
  </Frame>
);

const CloseAppsDialog: React.FC<{ s: DialogsState["closeApps"] }> = ({ s }) => (
  <Frame title="Fechar programas" box={DLG.closeApps} appear={s.appear}>
    <Text x={16} y={44} w={548}>
      Fecha os programas abertos pelo aluno. A área de trabalho, a barra de tarefas e o Midview nunca são fechados.
    </Text>
    <Check x={16} y={92} on={1} radio w={548}>
      Pedir para fechar: como clicar no X de cada janela.
    </Check>
    <Check x={16} y={118} on={0} radio w={548}>
      Forçar: encerra os programas sem perguntar.
    </Check>
    <Check x={16} y={166} on={1} radio>
      Avisar o aluno e esperar 1 minuto
    </Check>
    <Check x={16} y={192} on={0} radio>
      Avisar o aluno e esperar 30 segundos
    </Check>
    <Check x={16} y={218} on={0} radio>
      Agora, sem esperar (o aluno é avisado na hora)
    </Check>
    <Btn box={CTRL.closeAppsRun} hover={s.runHover} press={s.runPress} strong>
      Fechar programas
    </Btn>
  </Frame>
);

const DAYS = ["Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom"];

const ScheduleDialog: React.FC<{ s: DialogsState["schedule"] }> = ({ s }) => {
  const theme = useAppTheme();
  return (
    <Frame title="Agendar ligar e desligar" box={DLG.schedule} appear={s.appear}>
      <Text x={16} y={44} muted>
        Nos dias:
      </Text>
      {DAYS.map((day, i) => {
        const on = clamp01(s.days[i] ?? 0);
        return (
          <div
            key={day}
            style={{
              position: "absolute",
              left: 16 + i * 62,
              top: 66,
              width: 54,
              height: 30,
              borderRadius: 4,
              border: `1px solid ${on > 0.5 ? theme.toggleOn.line : theme.button.line}`,
              background: theme.button.bg,
              overflow: "hidden",
              display: "flex",
              alignItems: "center",
              justifyContent: "center",
              fontSize: 12.5,
              fontWeight: on > 0.5 ? 600 : 400,
            }}
          >
            <div style={{ position: "absolute", inset: 0, background: theme.toggleOn.bg, opacity: on }} />
            <span style={{ position: "relative" }}>{day}</span>
          </div>
        );
      })}
      <Text x={16} y={118}>
        Ligar às
      </Text>
      <Field box={{ x: 110, y: 112, w: 80, h: 30 }} text="07:00" />
      <Text x={16} y={150} w={548} size={11.5} muted>
        Este computador manda o pedido para ligar (Wake-on-LAN) no horário.
      </Text>
      <Text x={16} y={190}>
        Desligar às
      </Text>
      <Field box={{ x: 110, y: 184, w: 80, h: 30 }} text="18:00" />
      <Text x={16} y={222} w={548} size={11.5} muted>
        Cada máquina desliga sozinha. O aluno é avisado 5 minutos antes.
      </Text>
      <Btn box={CTRL.scheduleSave} hover={s.saveHover} press={s.savePress} strong>
        Salvar agendamento
      </Btn>
    </Frame>
  );
};

const LabPanelDialog: React.FC<{ s: DialogsState["labPanel"] }> = ({ s }) => {
  const theme = useAppTheme();
  return (
    <Frame title="Painel do laboratório" box={DLG.labPanel} appear={s.appear}>
      <Text x={16} y={44} w={608} size={12}>
        Cada função é aplicada na máquina do aluno pelo Midview e continua valendo se ela reiniciar.
      </Text>
      <Text x={16} y={84} weight={700}>
        Sequência de configuração ideal (início da aula)
      </Text>
      <Text x={16} y={104} size={11.5} muted>
        Para preparar as máquinas no começo da aula, aplique os passos 1 a 5 nesta ordem.
      </Text>
      {LAB_STEPS.map((step, i) => {
        const y = LAB_ROW.top + i * LAB_ROW.height;
        const on = clamp01(s.steps[i] ?? 0);
        return (
          <React.Fragment key={step.title}>
            <div style={{ position: "absolute", left: 16, top: y - 6, width: 608, height: 1, background: theme.dialog.line }} />
            <Text x={16} y={y} weight={600} size={12.5}>
              {step.title}
            </Text>
            <Text x={16} y={y + 19} w={330} size={11} muted>
              {step.text}
            </Text>
            {on > 0.01 ? (
              <div style={{ position: "absolute", left: 360, top: y + 6, fontSize: 12, fontWeight: 600, color: theme.dialog.check, opacity: on }}>
                <Glyph code={GLYPH.check} size={11} style={{ marginRight: 5 }} />
                Ativo
              </div>
            ) : null}
            <Btn
              box={{ x: CTRL.labFirstOn.x, y: y + 2, w: CTRL.labFirstOn.w, h: CTRL.labFirstOn.h }}
              hover={i === 0 ? s.firstHover : 0}
              press={i === 0 ? s.firstPress : 0}
              strong
            >
              Ativar
            </Btn>
            <Btn box={{ x: 528, y: y + 2, w: 96, h: 28 }}>Desativar</Btn>
          </React.Fragment>
        );
      })}
    </Frame>
  );
};

const MachinesDialog: React.FC<{ s: DialogsState["machines"] }> = ({ s }) => (
  <Frame title="Máquinas da sala" box={DLG.machines} appear={s.appear}>
    <Text x={16} y={44} weight={700}>
      Procurar e parear
    </Text>
    <Text x={16} y={64} w={568} size={11.5} muted>
      Procura as máquinas com o Midview Aluno nesta rede.
    </Text>
    <Btn box={{ x: 16, y: 88, w: 150, h: 30 }}>Procurar na rede</Btn>
    <Btn box={{ x: 174, y: 88, w: 180, h: 30 }}>Parear as selecionadas</Btn>
    <Text x={16} y={140} weight={700}>
      Atualizar as máquinas
    </Text>
    <Text x={16} y={160} w={568} size={11.5} muted>
      Leva a versão nova do Midview a todas as máquinas, pela rede.
    </Text>
    <Btn box={CTRL.machinesUpdate} hover={s.updateHover} press={s.updatePress} strong>
      Atualizar as máquinas pela rede...
    </Btn>
    <Text x={16} y={236} weight={700}>
      Outro professor
    </Text>
    <Text x={16} y={256} w={568} size={11.5} muted>
      Para um colega ter as máquinas desta sala: dê a ele um código, ou deixe as máquinas aceitarem o pareamento dele por 10 minutos.
    </Text>
    <Btn box={{ x: 16, y: 298, w: 190, h: 30 }}>Dar acesso a um colega...</Btn>
    <Btn box={{ x: 214, y: 298, w: 250, h: 30 }}>Permitir novo professor (10 minutos)</Btn>
    <Text x={16} y={350} weight={700}>
      Avançado
    </Text>
    <Btn box={{ x: 16, y: 376, w: 170, h: 30 }}>Ajustes avançados...</Btn>
  </Frame>
);

const UpdateDialog: React.FC<{ s: DialogsState["update"] }> = ({ s }) => {
  const theme = useAppTheme();
  const done = Math.round(clamp01(s.progress) * 12);
  return (
    <Frame title="Atualizar pela rede" box={DLG.update} appear={s.appear}>
      <Text x={16} y={50} weight={600}>
        {done < 12 ? "Atualizando as máquinas da sala..." : "Pronto: todas as máquinas atualizadas."}
      </Text>
      <div style={{ position: "absolute", left: 16, top: 90, width: 448, height: 8, borderRadius: 4, background: theme.dialog.line }}>
        <div style={{ width: `${clamp01(s.progress) * 100}%`, height: "100%", borderRadius: 4, background: theme.dialog.check }} />
      </div>
      <Text x={16} y={112} muted>
        {done} de 12 máquinas
      </Text>
      <Btn box={{ x: 374, y: 166, w: 90, h: 30 }}>Fechar</Btn>
    </Frame>
  );
};

/** Menu do botão direito numa máquina. */
const ContextMenu: React.FC<{ s: DialogsState["menu"] }> = ({ s }) => {
  const theme = useAppTheme();
  if (s.appear <= 0.01) return null;
  return (
    <div
      style={{
        position: "absolute",
        left: s.at.x,
        top: s.at.y,
        width: MENU.w,
        padding: `${MENU.pad}px 0`,
        borderRadius: 8,
        background: theme.menu.bg,
        border: `1px solid ${theme.menu.line}`,
        boxShadow: "0 16px 40px -8px rgba(0,0,0,0.45)",
        fontFamily: theme.font,
        fontSize: 12.5,
        color: theme.menu.text,
        opacity: clamp01(s.appear * 1.5),
        transformOrigin: "0 0",
        transform: `scale(${mix(0.96, 1, clamp01(s.appear))})`,
      }}
    >
      {MENU_ITEMS.map((item, i) => {
        if (item === "-") {
          return <div key={i} style={{ height: 1, margin: `${(MENU.separator - 1) / 2}px 10px`, background: theme.menu.line }} />;
        }
        const hovered = Math.abs(s.hover - i) < 0.5;
        return (
          <div
            key={i}
            style={{
              height: MENU.item,
              margin: "0 5px",
              padding: "0 14px",
              borderRadius: 4,
              display: "flex",
              alignItems: "center",
              background: hovered ? theme.menu.hover : "transparent",
            }}
          >
            {item}
          </div>
        );
      })}
    </div>
  );
};

/** Dica do botão "Desligar", como o ToolTip do XAML. */
const PowerTooltip: React.FC<{ appear: number }> = ({ appear }) => {
  const theme = useAppTheme();
  if (appear <= 0.01) return null;
  const b = TOOLBAR_LAYOUT.buttons.powerOff;
  return (
    <div
      style={{
        position: "absolute",
        left: b.x + 10,
        top: b.y + b.h + 30,
        padding: "6px 9px",
        borderRadius: 4,
        background: theme.menu.bg,
        border: `1px solid ${theme.menu.line}`,
        boxShadow: "0 8px 20px -6px rgba(0,0,0,0.4)",
        fontFamily: theme.font,
        fontSize: 12,
        color: theme.menu.text,
        whiteSpace: "nowrap",
        opacity: clamp01(appear),
      }}
    >
      Desliga a turma, com aviso aos alunos.
    </div>
  );
};

/** Todas as janelas e menus do tour, em coordenadas da janela principal. */
export const Dialogs: React.FC<{ s: DialogsState; viewPc: Pc; frame: number }> = ({ s, viewPc, frame }) => (
  <>
    <ViewWindow s={s.view} pc={viewPc} frame={frame} />
    <MessageDialog s={s.message} />
    <ChatDialog s={s.chat} />
    <SendFileDialog s={s.sendFile} />
    <CollectDialog s={s.collect} />
    <OpenSiteDialog s={s.openSite} />
    <BlockAppsDialog s={s.blockApps} />
    <BlockSitesDialog s={s.blockSites} />
    <CloseAppsDialog s={s.closeApps} />
    <ScheduleDialog s={s.schedule} />
    <LabPanelDialog s={s.labPanel} />
    <MachinesDialog s={s.machines} />
    <UpdateDialog s={s.update} />
    <ContextMenu s={s.menu} />
    <PowerTooltip appear={s.tooltip} />
  </>
);
