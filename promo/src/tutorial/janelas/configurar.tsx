import React from "react";
import { useAppTheme } from "../../components/app/appTheme";
import { Btn, Frame, Text } from "../../components/app/ui";
import { clamp01 } from "../../lib/motion";
import { centered } from "../kit";

/* ---------- Painel do laboratório (LabConfigWindow.xaml) ---------- */

export const LAB = centered(640, 720);

type LabRow = { title: string; text: string; buttons: string[] };
type LabSection = { title: string; note?: string; rows: LabRow[] };

const ON_OFF = ["Ativar", "Desativar"];
export const LAB_SECTIONS: LabSection[] = [
  {
    title: "Sequência de configuração ideal (início da aula)",
    note: "Para preparar as máquinas no começo da aula, aplique os passos 1 a 5 nesta ordem.",
    rows: [
      { title: "Passo 1 — Bloqueador de anúncios", text: "Instala o bloqueador no Chrome; aparece sozinho na próxima vez que o aluno abrir o Chrome (vale no modo anônimo também).", buttons: ON_OFF },
      { title: "Passo 2 — Limpeza ao fechar", text: "O Chrome apaga histórico, cookies, senhas e downloads quando fecha.", buttons: ON_OFF },
      { title: "Passo 3 — Blindagem total", text: "Trava as configurações, a personalização, o modo anônimo e a página inicial do Chrome, e o papel de parede do Windows.", buttons: ON_OFF },
      { title: "Passo 4 — Bloquear o Microsoft Edge", text: "Fecha o Edge sempre que o aluno abrir, pelo mesmo bloqueio de programas do Midview.", buttons: ON_OFF },
      { title: "Passo 5 — Bloquear downloads", text: "Impede qualquer download pelo Chrome.", buttons: ON_OFF },
    ],
  },
  {
    title: "Bloqueios extras",
    rows: [
      { title: "Modo visitante", text: "O Chrome só abre como visitante, sem perfis, login nem histórico.", buttons: ON_OFF },
      { title: "Travar a barra de tarefas", text: "Impede o aluno de fixar ou desafixar ícones da barra de tarefas.", buttons: ON_OFF },
      { title: "Bloquear sites de distração", text: "Redes sociais, jogos e YouTube, pelo mesmo “Bloquear sites” do Midview (vale em qualquer navegador). “Liberar” desbloqueia esses endereços.", buttons: ["Bloquear", "Liberar"] },
    ],
  },
  {
    title: "Manutenção",
    rows: [
      { title: "Limpar perfis do Chrome", text: "Fecha o Chrome e apaga os perfis salvos de todos os usuários, deixando o navegador zerado para a próxima turma. Não dá para desfazer.", buttons: ["Limpar agora"] },
      { title: "Apagar os downloads dos alunos", text: "Apaga a pasta Downloads de cada aluno: só os arquivos de hoje, ou tudo. Não dá para desfazer.", buttons: ["De hoje", "Tudo"] },
      { title: "RESET geral", text: "Remove tudo o que este painel aplicou (e o que um script antigo tenha deixado) e devolve a máquina ao padrão.", buttons: ["RESET geral"] },
    ],
  },
  { title: "Consulta", rows: [{ title: "Ver o que está aplicado", text: "Lê de cada máquina o que está ligado agora.", buttons: ["Ver agora"] }] },
];

const ROW_H = 66;
const SECTION_H = 34;
const NOTE_H = 26;

/** Posição (no conteúdo rolável) de cada linha, para mirar os botões. */
export const LAB_ROWS: { section: number; row: number; y: number }[] = (() => {
  const out: { section: number; row: number; y: number }[] = [];
  let y = 70;
  LAB_SECTIONS.forEach((sec, si) => {
    y += SECTION_H + (sec.note ? NOTE_H : 0);
    sec.rows.forEach((_, ri) => {
      out.push({ section: si, row: ri, y });
      y += ROW_H;
    });
    y += 8;
  });
  return out;
})();
/** Botão "Ativar" (ou o primeiro) de uma linha, relativo à janela, com a rolagem aplicada. */
export const labButton = (index: number, scroll: number) => ({ x: 430, y: LAB_ROWS[index].y + 10 - scroll, w: 88, h: 28 });

export const LabWindow: React.FC<{ s: { appear: number; scroll: number; on: number[]; hover?: number; press?: number } }> = ({ s }) => {
  const theme = useAppTheme();
  let flat = -1;
  let y = 70;
  return (
    <Frame title="Painel do laboratório" box={LAB} appear={s.appear}>
      <div style={{ position: "absolute", left: 0, top: 32, width: LAB.w, height: LAB.h - 32, overflow: "hidden" }}>
        <div style={{ position: "absolute", left: 0, top: -32 - s.scroll, width: LAB.w }}>
          <Text x={16} y={42} w={608} muted size={11.5}>
            Cada função é aplicada na máquina do aluno pelo Midview e continua valendo se ela reiniciar. O Chrome do aluno é fechado ao
            aplicar, para a mudança valer na hora.
          </Text>
          {LAB_SECTIONS.map((sec) => {
            const top = y;
            y += SECTION_H + (sec.note ? NOTE_H : 0);
            const rows = sec.rows.map((row) => {
              flat += 1;
              const ry = y;
              y += ROW_H;
              const idx = flat;
              return (
                <div key={row.title} style={{ position: "absolute", left: 16, top: ry, width: 608, height: ROW_H - 6, borderRadius: 6, background: theme.dialog.panel, border: `1px solid ${theme.dialog.line}` }}>
                  <div style={{ position: "absolute", left: 12, top: 7, width: 390, fontSize: 13, fontWeight: 600 }}>{row.title}</div>
                  <div style={{ position: "absolute", left: 12, top: 26, width: 390, fontSize: 11, lineHeight: 1.3, color: theme.dialog.muted }}>{row.text}</div>
                  {row.buttons.map((b, bi) => (
                    <Btn
                      key={b}
                      box={{ x: 414 + bi * 96 - (row.buttons.length === 1 ? 0 : 0), y: 10, w: row.buttons.length === 1 ? 184 : 88, h: 28 }}
                      hover={bi === 0 && s.hover !== undefined && Math.round(s.hover) === idx ? 1 : 0}
                      press={bi === 0 && s.press !== undefined && Math.round(s.press) === idx ? 1 : 0}
                    >
                      {b}
                    </Btn>
                  ))}
                </div>
              );
            });
            y += 8;
            return (
              <React.Fragment key={sec.title}>
                <div style={{ position: "absolute", left: 16, top: top + 6, fontSize: 14, fontWeight: 700 }}>{sec.title}</div>
                {sec.note ? <div style={{ position: "absolute", left: 16, top: top + 28, fontSize: 11.5, color: theme.dialog.muted }}>{sec.note}</div> : null}
                {rows}
              </React.Fragment>
            );
          })}
        </div>
      </div>
    </Frame>
  );
};

/* ---------- Máquinas da sala (MachinesWindow.xaml) ---------- */

export const MACHINES = centered(780, 700);
export const MACHINES_CTRL = {
  search: { x: 16, y: 92, w: 160, h: 32 },
  pair: { x: 186, y: 92, w: 190, h: 32 },
  card: (i: number) => ({ x: 16 + (i % 2) * 378, y: 172 + Math.floor(i / 2) * 64, w: 370, h: 58 }),
  check: (i: number) => ({ x: 28 + (i % 2) * 378, y: 190 + Math.floor(i / 2) * 64, w: 16, h: 16 }),
  confere: { x: 0, y: 0, w: 90, h: 28 },
  update: { x: 16, y: 470, w: 280, h: 32 },
  giveAccess: { x: 16, y: 574, w: 210, h: 32 },
  allowNew: { x: 236, y: 574, w: 260, h: 32 },
  join: { x: 16, y: 136, w: 300, h: 18 },
};

export type FoundCard = { name: string; address: string; status: string; checked: number; code?: string; codeP?: number; confereHover?: number };

export type MachinesState = {
  appear: number;
  searchHover?: number;
  searchPress?: number;
  pairHover?: number;
  pairPress?: number;
  status: string;
  cards: FoundCard[];
  cardsP: number;
  updateHover?: number;
  updatePress?: number;
  giveHover?: number;
  givePress?: number;
  allowHover?: number;
  allowPress?: number;
  allowResult?: number;
  joinHover?: number;
};

const Section: React.FC<{ y: number; title: string; hint: string }> = ({ y, title, hint }) => (
  <>
    <Text x={16} y={y} size={14} weight={700}>
      {title}
    </Text>
    <Text x={16} y={y + 22} w={748} muted size={11.5}>
      {hint}
    </Text>
  </>
);

export const MachinesWindow: React.FC<{ s: MachinesState }> = ({ s }) => {
  const theme = useAppTheme();
  return (
    <Frame title="Máquinas da sala" box={MACHINES} appear={s.appear}>
      <Section
        y={42}
        title="Procurar e parear"
        hint="Procura as máquinas com o Midview Aluno nesta rede. Ao parear, cada máquina mostra um código na tela: confira se é o mesmo que aparece aqui."
      />
      <Btn box={MACHINES_CTRL.search} hover={s.searchHover} press={s.searchPress} strong>
        Procurar na rede
      </Btn>
      <Btn box={MACHINES_CTRL.pair} hover={s.pairHover} press={s.pairPress}>
        Parear as selecionadas
      </Btn>
      <Text x={392} y={99} muted>
        {s.status}
      </Text>
      <div style={{ position: "absolute", left: 16, top: 136, fontSize: 12, color: theme.dialog.muted }}>
        Um colega já tem estas máquinas? <span style={{ color: "#2563EB", textDecoration: s.joinHover ? "underline" : "none" }}>Entrar na sala de um colega...</span>
      </div>
      {s.cards.map((c, i) => {
        const b = MACHINES_CTRL.card(i);
        const p = clamp01((s.cardsP - i * 0.08) * 2);
        if (p <= 0.01) return null;
        const codeP = clamp01(c.codeP ?? 0);
        return (
          <div
            key={c.name}
            style={{
              position: "absolute",
              left: b.x,
              top: b.y,
              width: b.w,
              height: b.h,
              borderRadius: 6,
              border: `1px solid ${c.checked > 0.5 ? theme.selected.line : theme.dialog.line}`,
              background: theme.dialog.panel,
              opacity: p,
              transform: `translateY(${(1 - p) * 8}px)`,
            }}
          >
            <div style={{ position: "absolute", left: 11, top: 19, width: 15, height: 15, borderRadius: 3, border: `1px solid ${theme.dialog.muted}`, background: theme.dialog.input, display: "flex", alignItems: "center", justifyContent: "center", fontSize: 11 }}>
              {c.checked > 0.5 ? "✓" : ""}
            </div>
            <div style={{ position: "absolute", left: 36, top: 8, fontSize: 13.5, fontWeight: 700 }}>{c.name}</div>
            <div style={{ position: "absolute", left: 36, top: 28, fontSize: 11, color: theme.dialog.muted }}>{c.address}</div>
            {codeP > 0.01 ? (
              <div style={{ position: "absolute", left: 118, top: 15, opacity: codeP, display: "flex", alignItems: "center", gap: 6, whiteSpace: "nowrap" }}>
                <span style={{ fontFamily: "Consolas, monospace", fontSize: 19, fontWeight: 700, width: 84 }}>{c.code}</span>
                <div style={{ position: "relative", width: 62, height: 26 }}>
                  <Btn box={{ x: 0, y: 0, w: 62, h: 26 }} hover={c.confereHover} strong>
                    Confere
                  </Btn>
                </div>
                <div style={{ position: "relative", width: 86, height: 26 }}>
                  <Btn box={{ x: 0, y: 0, w: 86, h: 26 }}>Não confere</Btn>
                </div>
              </div>
            ) : (
              <div style={{ position: "absolute", right: 12, top: 19, fontSize: 12, color: c.status.startsWith("Pareada") ? "#15803D" : theme.dialog.muted }}>{c.status}</div>
            )}
          </div>
        );
      })}
      <Section y={420} title="Atualizar as máquinas" hint="Versão do programa do aluno neste PC: 2026-10-26 13:05 b1d0c70" />
      <Btn box={MACHINES_CTRL.update} hover={s.updateHover} press={s.updatePress}>
        Atualizar as máquinas pela rede...
      </Btn>
      <Section
        y={520}
        title="Outro professor"
        hint="Para um colega ter as máquinas desta sala: dê a ele um código (nada aparece na tela dos alunos), ou deixe as máquinas aceitarem o pareamento dele por 10 minutos."
      />
      <Btn box={MACHINES_CTRL.giveAccess} hover={s.giveHover} press={s.givePress}>
        Dar acesso a um colega...
      </Btn>
      <Btn box={MACHINES_CTRL.allowNew} hover={s.allowHover} press={s.allowPress}>
        Permitir novo professor (10 minutos)
      </Btn>
      <div style={{ opacity: clamp01(s.allowResult ?? 0) }}>
        <Text x={508} y={581} muted>
          11 de 11 máquinas liberadas por 10 minutos.
        </Text>
      </div>
      <Section y={622} title="Avançado" hint="A identidade deste computador e uma máquina adicionada à mão (em outra rede, ou com o endereço para corrigir)." />
    </Frame>
  );
};

/* ---------- Atualizar pela rede (UpdateWindow.xaml) ---------- */

export const UPDATE = centered(660, 620);
export const UPDATE_CTRL = { run: { x: 176, y: 250, w: 150, h: 34 } };

export type UpdateLine = { name: string; text: string; ok?: boolean };

export const UpdateWindow: React.FC<{ s: { appear: number; runHover?: number; runPress?: number; step: string; lines: UpdateLine[] } }> = ({ s }) => {
  const theme = useAppTheme();
  return (
    <Frame title="Atualizar pela rede" box={UPDATE} appear={s.appear}>
      <Text x={16} y={44} w={628} muted size={11.5}>
        Programa do aluno: C:\Program Files\Midview Professor\aluno
      </Text>
      <Text x={16} y={66} weight={600}>
        Versão do pacote: 26/10 13:05
      </Text>
      <Text x={16} y={94} w={628} muted size={11}>
        Atualiza o programa dos alunos pela rede: uma máquina primeiro, e as outras só depois que ela confirmar que a versão nova funciona; se
        não funcionar, ela volta sozinha para a anterior. Por 1 a 3 minutos você perde a visão e o controle de cada máquina; os alunos podem
        continuar usando o computador, mas mensagens e conversas abertas fecham e o teclado e o mouse são destravados. Não desligue as máquinas
        até terminar. Os professores que cada máquina aceita não mudam.
      </Text>
      <Btn box={{ x: 16, y: 250, w: 150, h: 34 }}>Verificar versões</Btn>
      <Btn box={UPDATE_CTRL.run} hover={s.runHover} press={s.runPress} strong>
        Atualizar
      </Btn>
      <Btn box={{ x: 336, y: 250, w: 110, h: 34 }}>Parar</Btn>
      <Text x={16} y={300} weight={600}>
        {s.step}
      </Text>
      <div
        style={{
          position: "absolute",
          left: 16,
          top: 328,
          width: 628,
          height: 270,
          borderRadius: 4,
          border: `1px solid ${theme.dialog.line}`,
          background: theme.dialog.input,
          padding: "8px 12px",
          fontSize: 12.5,
          lineHeight: 1.55,
          overflow: "hidden",
        }}
      >
        {s.lines.map((l) => (
          <div key={l.name} style={{ color: l.ok ? "#15803D" : theme.dialog.text }}>
            {l.name} {l.text}
          </div>
        ))}
      </div>
    </Frame>
  );
};
