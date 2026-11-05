import React from "react";
import { clamp01 } from "../../../../lib/motion";
import { P_TOTAL, pDialog } from "../geometry";
import { Card, DayPill, PBtn, PCheck, PDialog, PRadio, PSwitch, ResultCard, T, TURMA_TODA, icon } from "../kit";
import { GROUP_COLOR, P, PG } from "../palette";

/* ---------- Desligar / Reiniciar (PowerWindow.xaml, fotos 31 e 32) ---------- */

export const POWER = pDialog(506, 643);
export const POWER_CTRL = {
  restart: { x: 120, y: 111, w: 80, h: 18 },
  wait1: { x: 37, y: 164, w: 230, h: 18 },
  wait5: { x: 37, y: 185, w: 230, h: 18 },
  now: { x: 37, y: 206, w: 150, h: 18 },
  run: { x: 24, y: 402, w: 296, h: 28 },
  cancel: { x: 328, y: 402, w: 154, h: 28 },
};

export type PowerState = {
  appear: number;
  /** 0 = Desligar, 1 = Reiniciar. */
  restart: number;
  /** 0 = 1 minuto, 1 = 5 minutos, 2 = agora. */
  wait: 0 | 1 | 2;
  closeHover?: number;
  runHover?: number;
  hover?: number;
  runPress?: number;
  cancelHover?: number;
  cancelPress?: number;
  /** Envio para as máquinas (0→1) na lista de resultado. */
  sent?: number;
};

export const PowerDialog: React.FC<{ s: PowerState }> = ({ s }) => {
  const restart = s.restart > 0.5;
  const verb = restart ? "Reiniciar" : "Desligar";
  return (
    <PDialog
      title={verb}
      box={POWER}
      appear={s.appear}
      icon={restart ? PG.restart : PG.power}
      group="energia"
      heading={verb}
      hint={TURMA_TODA}
      closeHover={s.closeHover}
    >
      <Card box={{ x: 24, y: 98, w: 458, h: 44 }} />
      <PRadio x={37} y={111} on={1 - s.restart}>
        Desligar
      </PRadio>
      <PRadio x={122} y={111} on={s.restart}>
        Reiniciar
      </PRadio>
      <Card box={{ x: 24, y: 151, w: 458, h: 136 }} />
      <PRadio x={37} y={164} on={s.wait === 0 ? 1 : 0}>
        Avisar o aluno e esperar 1 minuto
      </PRadio>
      <PRadio x={37} y={185} on={s.wait === 1 ? 1 : 0}>
        Avisar o aluno e esperar 5 minutos
      </PRadio>
      <PRadio x={37} y={206} on={s.wait === 2 ? 1 : 0}>
        Agora, sem esperar
      </PRadio>
      <T x={37} y={232} w={436} muted>
        O Windows mostra ao aluno um aviso com a contagem. Quando o tempo acaba, os programas abertos são fechados sem salvar. Vale também para
        máquinas sem ninguém logado.
      </T>
      <Card box={{ x: 24, y: 297, w: 458, h: 90 }} />
      <PCheck x={37} y={311} on={0} bold>
        Instalar as atualizações do Windows que estiverem esperando
      </PCheck>
      <T x={61} y={332} w={412} muted>
        Como &quot;Atualizar e {restart ? "reiniciar" : "desligar"}&quot; no menu Iniciar do Windows: instala só o que o Windows já baixou. Pode
        levar alguns minutos a mais em cada máquina. Máquinas sem atualização esperando só {restart ? "reiniciam" : "desligam"}.
      </T>
      <PBtn box={POWER_CTRL.run} kind="danger" hover={s.runHover} press={s.runPress}>
        {verb}
      </PBtn>
      <PBtn box={POWER_CTRL.cancel} hover={s.cancelHover} press={s.cancelPress}>
        {restart ? "Cancelar reinício" : "Cancelar desligamento"}
      </PBtn>
      <ResultCard box={{ x: 24, y: 440, w: 458, h: 182 }} progress={s.sent ?? 0} head={`Enviando para ${P_TOTAL} máquinas...`} />
    </PDialog>
  );
};

/* ---------- Agendar ligar e desligar (ScheduleWindow.xaml, foto 33) ---------- */

export const SCHEDULE = pDialog(586, 663);
const DAYS = ["Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom"];
export const SCHEDULE_CTRL = {
  day: (i: number) => ({ x: 24 + i * 52, y: 121, w: 46, h: 24 }),
  wakeSwitch: { x: 233, y: 182, w: 36, h: 20 },
  offSwitch: { x: 508, y: 182, w: 36, h: 20 },
  onTime: { x: 38, y: 216, w: 233, h: 40 },
  save: { x: 417, y: 612, w: 145, h: 30 },
};

export type ScheduleState = {
  appear: number;
  /** Cada dia aceso (0→1). */
  days: number[];
  /** Horas que aparecem nos campos. */
  on: string;
  off: string;
  /** A régua acendendo (0→1). */
  ruler: number;
  saveHover?: number;
  savePress?: number;
  saved?: number;
  /** As chaves "Ligar às" e "Desligar às" (padrão: ligadas). */
  wakeOn?: number;
  offOn?: number;
};

const minutes = (hhmm: string) => {
  const [h, m] = hhmm.split(":").map(Number);
  return h * 60 + m;
};

const TimeCard: React.FC<{ x: number; glyph: string; label: string; time: string; on?: number; children: React.ReactNode }> = ({
  x,
  glyph,
  label,
  time,
  on = 1,
  children,
}) => (
  <>
    <Card box={{ x, y: 163, w: 263, h: 221 }} />
    <span style={{ ...icon(17, GROUP_COLOR.energia), position: "absolute", left: x + 14, top: 183 }}>{glyph}</span>
    <T x={x + 42} y={184} size={13.5} weight={600}>
      {label}
    </T>
    <PSwitch x={x + 209} y={182} on={on} />
    <div
      style={{
        position: "absolute",
        left: x + 14,
        top: 216,
        width: 233,
        height: 40,
        borderRadius: 6,
        border: `1px solid ${on > 0.5 ? P.accent : P.line}`,
        background: P.bg,
        boxSizing: "border-box",
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        fontFamily: 'Consolas, "Cascadia Mono", monospace',
        fontSize: 26,
        color: on > 0.5 ? P.accentText : P.disabled,
        letterSpacing: "0.04em",
      }}
    >
      {time}
    </div>
    <T x={x + 14} y={267} w={238} muted>
      {children}
    </T>
  </>
);

export const ScheduleDialog: React.FC<{ s: ScheduleState }> = ({ s }) => {
  const RULER = { x: 24, y: 401, w: 538, h: 28 };
  const from = RULER.x + (RULER.w * minutes(s.on)) / 1440;
  const to = RULER.x + (RULER.w * minutes(s.off)) / 1440;
  const lit = clamp01(s.ruler);
  return (
    <PDialog title="Agendar ligar e desligar" box={SCHEDULE} appear={s.appear} icon={PG.calendar} group="energia" heading="Agendar ligar e desligar" hint={TURMA_TODA}>
      <T x={24} y={96} size={13.5} weight={600}>
        Nos dias
      </T>
      {DAYS.map((d, i) => {
        const b = SCHEDULE_CTRL.day(i);
        return (
          <DayPill key={d} x={b.x} y={b.y} on={s.days[i] ?? 0}>
            {d}
          </DayPill>
        );
      })}
      <TimeCard x={24} glyph={PG.wake} label="Ligar às" time={s.on} on={s.wakeOn ?? 1}>
        Este computador manda o pedido para ligar (Wake-on-LAN) no horário: ele precisa estar ligado ou em suspensão, com você conectado no Windows.
        As máquinas precisam do Wake-on-LAN ativado na BIOS (veja o LEIA-ME do pacote de instalação).
      </TimeCard>
      <TimeCard x={299} glyph={PG.power} label="Desligar às" time={s.off} on={s.offOn ?? 1}>
        Cada máquina desliga sozinha, mesmo com este computador desligado. O aluno é avisado 5 minutos antes, e o que não estiver salvo é perdido.
        Durante o aviso, &quot;Desligar a turma...&quot; &gt; &quot;Cancelar desligamento&quot; cancela.
      </TimeCard>
      <div style={{ position: "absolute", left: RULER.x, top: RULER.y, width: RULER.w, height: RULER.h, borderRadius: 6, background: P.card }} />
      <div
        style={{
          position: "absolute",
          left: from,
          top: RULER.y + 3,
          width: (to - from) * lit,
          height: RULER.h - 6,
          borderRadius: 5,
          background: "rgba(45,212,191,0.16)",
          border: `1px solid ${P.accent}`,
          boxSizing: "border-box",
          overflow: "hidden",
          display: "flex",
          alignItems: "center",
          justifyContent: "center",
          fontSize: 12,
          color: P.accentText,
          whiteSpace: "nowrap",
          opacity: lit > 0.01 ? 1 : 0,
        }}
      >
        Ligado das {s.on} às {s.off}
      </div>
      {["0h", "6h", "12h", "18h", "24h"].map((label, i) => (
        <div
          key={label}
          style={{
            position: "absolute",
            left: RULER.x + (RULER.w * i) / 4 - (i === 0 ? 0 : i === 4 ? 26 : 13),
            width: 26,
            top: RULER.y + RULER.h + 6,
            textAlign: i === 0 ? "left" : i === 4 ? "right" : "center",
            fontSize: 10.5,
            color: P.muted,
          }}
        >
          {label}
        </div>
      ))}
      {s.saved && s.saved > 0.01 ? (
        <div style={{ position: "absolute", left: 24, top: 618, fontSize: 12, color: P.online, opacity: clamp01(s.saved) }}>
          <span style={{ ...icon(12), marginRight: 6 }}>{PG.check}</span>
          Agendamento salvo.
        </div>
      ) : null}
      <PBtn box={SCHEDULE_CTRL.save} kind="primary" hover={s.saveHover} press={s.savePress}>
        Salvar agendamento
      </PBtn>
    </PDialog>
  );
};
