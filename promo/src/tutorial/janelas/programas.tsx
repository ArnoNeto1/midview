import React from "react";
import { useAppTheme } from "../../components/app/appTheme";
import { Btn, Check, Field, Frame, Text, typed } from "../../components/app/ui";
import { clamp01 } from "../../lib/motion";
import { centered } from "../kit";

/* ---------- Fechar programas (CloseAppsWindow.xaml) ---------- */

export const CLOSE_APPS = centered(620, 420);
export const CLOSE_CTRL = { force: { x: 16, y: 134, w: 560, h: 20 }, run: { x: 16, y: 352, w: 588, h: 36 } };

export const CloseAppsWindow: React.FC<{ s: { appear: number; mode: 0 | 1; delay: 0 | 1 | 2; runHover?: number; runPress?: number } }> = ({ s }) => (
  <Frame title="Fechar programas" box={CLOSE_APPS} appear={s.appear}>
    <Text x={16} y={44} weight={600}>
      Na turma toda (12 máquinas)
    </Text>
    <Text x={16} y={68} w={588} muted>
      Fecha os programas abertos pelo aluno. A área de trabalho, a barra de tarefas e o Midview nunca são fechados.
    </Text>
    <Check x={16} y={108} on={s.mode === 0 ? 1 : 0} radio w={588}>
      Pedir para fechar: como clicar no X de cada janela. Programas com trabalho não salvo perguntam ao aluno antes.
    </Check>
    <Check x={16} y={152} on={s.mode === 1 ? 1 : 0} radio w={588}>
      Forçar: encerra os programas sem perguntar. Trabalho não salvo é perdido.
    </Check>
    <Check x={16} y={214} on={s.delay === 0 ? 1 : 0} radio>
      Avisar o aluno e esperar 1 minuto
    </Check>
    <Check x={16} y={240} on={s.delay === 1 ? 1 : 0} radio>
      Avisar o aluno e esperar 30 segundos
    </Check>
    <Check x={16} y={266} on={s.delay === 2 ? 1 : 0} radio>
      Agora, sem esperar (o aluno é avisado na hora)
    </Check>
    <Btn box={CLOSE_CTRL.run} hover={s.runHover} press={s.runPress} strong>
      Fechar programas
    </Btn>
  </Frame>
);

/* ---------- Bloquear programas (AppBlockWindow.xaml) ---------- */

export const APP_BLOCK = centered(620, 640);
export const APP_BLOCK_CTRL = {
  allow: { x: 16, y: 122, w: 560, h: 20 },
  row: (i: number) => ({ x: 26, y: 290 + i * 30, w: 300, h: 22 }),
  apply: { x: 16, y: 586, w: 180, h: 36 },
  release: { x: 206, y: 586, w: 140, h: 36 },
};

export const BLOCK_ROWS = [
  { name: "Roblox (RobloxPlayerBeta.exe)", note: "— aberto em 3 de 12 máquina(s)" },
  { name: "Minecraft (Minecraft.exe)", note: "— aberto em 2 de 12 máquina(s)" },
  { name: "Discord (Discord.exe)", note: "— aberto em 1 de 12 máquina(s)" },
  { name: "Google Chrome (chrome.exe)", note: "— aberto agora" },
  { name: "Word (WINWORD.EXE)", note: "— aberto agora" },
  { name: "Scratch 3 (Scratch 3.exe)", note: "— aberto agora" },
];

export type AppBlockState = { appear: number; mode: 0 | 1; checks: number[]; applyHover?: number; applyPress?: number; applied?: number };

export const AppBlockWindow: React.FC<{ s: AppBlockState }> = ({ s }) => {
  const theme = useAppTheme();
  return (
    <Frame title="Bloquear programas" box={APP_BLOCK} appear={s.appear}>
      <Text x={16} y={44} weight={600}>
        Na turma toda (12 máquinas)
      </Text>
      <Text x={16} y={66} muted>
        {s.applied && s.applied > 0.5 ? "Bloqueando Roblox, Minecraft e Discord em 12 máquinas (salvo em 26/10 13:42)" : "Nenhum programa bloqueado em 12 máquinas"}
      </Text>
      <Text x={16} y={90} weight={600}>
        Modo
      </Text>
      <Check x={16} y={112} on={s.mode === 0 ? 1 : 0} radio w={588}>
        Bloquear só os programas escolhidos — o resto continua funcionando.
      </Check>
      <Check x={16} y={138} on={s.mode === 1 ? 1 : 0} radio w={588}>
        Permitir só os programas escolhidos — todo o resto é fechado ao abrir (o Windows e o Midview continuam funcionando).
      </Check>
      <Text x={38} y={178} w={566} muted size={11}>
        O Prompt de Comando e o PowerShell também são fechados, a menos que estejam na lista. Alguns programas usam auxiliares sem janela (o
        Terminal do Windows usa o OpenConsole.exe): se algo fechar sozinho, adicione também o auxiliar. Os aplicativos da Microsoft Store
        (Calculadora, Fotos) não aparecem na lista. Teste antes em uma máquina.
      </Text>
      <Text x={16} y={256} weight={600}>
        Programas abertos
      </Text>
      <Btn box={{ x: 476, y: 250, w: 128, h: 28 }}>Atualizar lista</Btn>
      <div
        style={{
          position: "absolute",
          left: 16,
          top: 288,
          width: 588,
          height: 190,
          borderRadius: 4,
          border: `1px solid ${theme.dialog.line}`,
          background: theme.dialog.input,
        }}
      />
      {BLOCK_ROWS.map((r, i) => (
        <Check key={r.name} x={26} y={298 + i * 30} on={s.checks[i] ?? 0} w={560}>
          {r.name} <span style={{ color: theme.dialog.muted }}>{s.applied && s.applied > 0.5 && (s.checks[i] ?? 0) > 0.5 ? "— bloqueado agora" : r.note}</span>
        </Check>
      ))}
      <Field box={{ x: 16, y: 486, w: 460, h: 30 }} text="" />
      <Btn box={{ x: 484, y: 486, w: 120, h: 30 }}>Adicionar</Btn>
      <Text x={16} y={522} muted size={11.5}>
        Adicionar pelo nome (por exemplo chrome.exe).
      </Text>
      <Btn box={APP_BLOCK_CTRL.apply} hover={s.applyHover} press={s.applyPress} strong>
        Aplicar bloqueio
      </Btn>
      <Btn box={APP_BLOCK_CTRL.release}>Liberar tudo</Btn>
    </Frame>
  );
};

/* ---------- Bloquear sites (SiteBlockWindow.xaml) ---------- */

export const SITE_BLOCK = centered(620, 380);
export const SITE_BLOCK_CTRL = {
  blockAll: { x: 16, y: 120, w: 180, h: 32 },
  site: { x: 16, y: 196, w: 588, h: 30 },
  blockSite: { x: 16, y: 236, w: 150, h: 32 },
};
export const SITE_TO_BLOCK = "youtube.com";

export const SiteBlockWindow: React.FC<{ s: { appear: number; typed: number; siteHover?: number; sitePress?: number; done?: number } }> = ({ s }) => (
  <Frame title="Bloquear sites" box={SITE_BLOCK} appear={s.appear}>
    <Text x={16} y={44} weight={600}>
      Na turma toda (12 máquinas)
    </Text>
    <Text x={16} y={66} w={588} muted>
      O bloqueio continua valendo se a máquina reiniciar. O Midview continua funcionando mesmo com a internet bloqueada.
    </Text>
    <Text x={16} y={100}>
      Internet inteira:
    </Text>
    <Btn box={SITE_BLOCK_CTRL.blockAll}>Bloquear a internet</Btn>
    <Btn box={{ x: 206, y: 120, w: 170, h: 32 }}>Liberar a internet</Btn>
    <Text x={16} y={174}>
      Um site (por exemplo youtube.com):
    </Text>
    <Field box={SITE_BLOCK_CTRL.site} text={typed(SITE_TO_BLOCK, s.typed)} caret={s.typed > 0 && s.typed < 1} />
    <Btn box={SITE_BLOCK_CTRL.blockSite} hover={s.siteHover} press={s.sitePress} strong>
      Bloquear o site
    </Btn>
    <Btn box={{ x: 176, y: 236, w: 150, h: 32 }}>Liberar o site</Btn>
    <div style={{ opacity: clamp01(s.done ?? 0) }}>
      <Text x={340} y={243} muted>
        Feito nas 12 máquinas.
      </Text>
    </div>
    <Text x={16} y={290} w={588} muted size={11.5}>
      Bloqueia o endereço com e sem www (youtube.com e www.youtube.com). Outros endereços do mesmo site, como m.youtube.com, e abas que já
      estavam abertas podem continuar funcionando.
    </Text>
  </Frame>
);
