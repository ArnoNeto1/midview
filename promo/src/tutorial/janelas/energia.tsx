import React from "react";
import { Btn, Check, Field, Frame, Text } from "../../components/app/ui";
import { centered } from "../kit";

/** "Desligar" e "Reiniciar" (src/Midview.Master/PowerWindow.xaml). */
export const POWER = centered(560, 340);
export const POWER_CTRL = {
  restart: { x: 16, y: 154, w: 120, h: 20 },
  wait5: { x: 16, y: 222, w: 300, h: 20 },
  run: { x: 16, y: 288, w: 150, h: 34 },
  cancel: { x: 370, y: 288, w: 174, h: 34 },
};

export type PowerState = {
  appear: number;
  action: "Desligar" | "Reiniciar";
  delay: 0 | 1 | 2;
  runHover?: number;
  runPress?: number;
  cancelHover?: number;
  cancelPress?: number;
};

export const PowerWindow: React.FC<{ s: PowerState }> = ({ s }) => {
  const off = s.action === "Desligar";
  return (
    <Frame title={s.action} box={POWER} appear={s.appear}>
      <Text x={16} y={44} weight={600}>
        Para a turma toda (12 máquinas)
      </Text>
      <Text x={16} y={70} w={528} muted>
        O Windows mostra ao aluno um aviso com a contagem. Quando o tempo acaba, os programas abertos são fechados sem salvar. Vale também
        para máquinas sem ninguém logado.
      </Text>
      <Check x={16} y={128} on={off ? 1 : 0} radio>
        Desligar
      </Check>
      <Check x={16} y={154} on={off ? 0 : 1} radio>
        Reiniciar
      </Check>
      <Check x={16} y={196} on={s.delay === 0 ? 1 : 0} radio>
        Avisar o aluno e esperar 1 minuto
      </Check>
      <Check x={16} y={222} on={s.delay === 1 ? 1 : 0} radio>
        Avisar o aluno e esperar 5 minutos
      </Check>
      <Check x={16} y={248} on={s.delay === 2 ? 1 : 0} radio>
        Agora, sem esperar
      </Check>
      <Btn box={POWER_CTRL.run} hover={s.runHover} press={s.runPress} strong>
        {s.action}
      </Btn>
      <Btn box={POWER_CTRL.cancel} hover={s.cancelHover} press={s.cancelPress}>
        {off ? "Cancelar desligamento" : "Cancelar reinício"}
      </Btn>
    </Frame>
  );
};

/** "Agendar ligar e desligar" (ScheduleWindow.xaml). */
export const SCHEDULE = centered(600, 450);
const DAYS = ["Seg", "Ter", "Qua", "Qui", "Sex", "Sáb", "Dom"];
export const SCHEDULE_CTRL = {
  day: (i: number) => ({ x: 16 + i * 74, y: 96, w: 18, h: 18 }),
  wakeTime: { x: 120, y: 132, w: 80, h: 28 },
  offTime: { x: 120, y: 254, w: 80, h: 28 },
  save: { x: 16, y: 372, w: 190, h: 34 },
};

export type ScheduleState = {
  appear: number;
  days: number[];
  wake: number;
  off: number;
  saveHover?: number;
  savePress?: number;
};

export const ScheduleWindow: React.FC<{ s: ScheduleState }> = ({ s }) => (
  <Frame title="Agendar ligar e desligar" box={SCHEDULE} appear={s.appear}>
    <Text x={16} y={44} weight={600}>
      Na turma toda (12 máquinas)
    </Text>
    <Text x={16} y={72}>
      Nos dias:
    </Text>
    {DAYS.map((d, i) => (
      <Check key={d} x={16 + i * 74} y={98} on={s.days[i] ?? 0}>
        {d}
      </Check>
    ))}
    <Check x={16} y={138} on={s.wake}>
      Ligar às
    </Check>
    <Field box={SCHEDULE_CTRL.wakeTime} text="07:00" />
    <Text x={16} y={172} w={568} muted>
      Este computador manda o pedido para ligar (Wake-on-LAN) no horário: ele precisa estar ligado ou em suspensão, com você conectado no
      Windows. As máquinas precisam do Wake-on-LAN ativado na BIOS.
    </Text>
    <Check x={16} y={260} on={s.off}>
      Desligar às
    </Check>
    <Field box={SCHEDULE_CTRL.offTime} text="18:00" />
    <Text x={16} y={294} w={568} muted>
      Cada máquina desliga sozinha, mesmo com este computador desligado. O aluno é avisado 5 minutos antes, e o que não estiver salvo é
      perdido. Durante o aviso, “Desligar” &gt; “Cancelar desligamento” cancela.
    </Text>
    <Btn box={SCHEDULE_CTRL.save} hover={s.saveHover} press={s.savePress} strong>
      Salvar agendamento
    </Btn>
    <Text x={16} y={420} muted size={11.5}>
      Última vez que a tarefa para ligar rodou: segunda-feira, 07:00
    </Text>
  </Frame>
);
