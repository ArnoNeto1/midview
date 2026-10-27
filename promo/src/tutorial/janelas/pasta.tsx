import React from "react";
import { useAppTheme } from "../../components/app/appTheme";
import { PCS } from "../../components/app/pcs";
import { Btn, Check, Field, Frame, Text, typed } from "../../components/app/ui";
import { clamp01 } from "../../lib/motion";
import { centered } from "../kit";

/** "Pasta da turma" (src/Midview.Master/ClassFolderWindow.xaml), antes e durante a aula. */
export const CLASS_FOLDER = centered(620, 660);
export const CLASS_FOLDER_CTRL = {
  newClass: { x: 16, y: 352, w: 130, h: 32 },
  nameField: { x: 16, y: 422, w: 360, h: 30 },
  create: { x: 386, y: 422, w: 100, h: 30 },
  row: (i: number) => ({ x: 16, y: 126 + i * 54, w: 588, h: 50 }),
  start: { x: 344, y: 604, w: 260, h: 38 },
  endKeep: { x: 34, y: 402, w: 200, h: 20 },
  end: { x: 424, y: 604, w: 180, h: 38 },
  takeHome: { x: 16, y: 112, w: 360, h: 32 },
};

export type ClassRow = { name: string; info: string };
export type PcStatus = "ok" | "copying" | "off" | "error";

export type ClassFolderState = {
  appear: number;
  mode: "list" | "lesson";
  classes: ClassRow[];
  selected: number;
  creating?: { p: number; typed: number };
  newHover?: number;
  newPress?: number;
  createHover?: number;
  createPress?: number;
  startHover?: number;
  startPress?: number;
  status?: string;
  /** Durante a aula. */
  pcs?: PcStatus[];
  endChoice?: 0 | 1 | 2;
  endHover?: number;
  endPress?: number;
  /** Outro PC de professor guarda as pastas (faixa azul). */
  remote?: number;
  takeHover?: number;
};

const MARK: Record<PcStatus, string> = { ok: "✓", copying: "⟳", off: "○", error: "✗" };

const ClassList: React.FC<{ s: ClassFolderState }> = ({ s }) => {
  const theme = useAppTheme();
  return (
    <>
      {s.classes.map((c, i) => {
        const r = CLASS_FOLDER_CTRL.row(i);
        const sel = s.selected === i;
        return (
          <div
            key={c.name}
            style={{
              position: "absolute",
              left: r.x,
              top: r.y,
              width: r.w,
              height: r.h,
              borderRadius: 6,
              border: `1px solid ${sel ? theme.selected.line : theme.dialog.line}`,
              background: sel ? theme.selected.fill : theme.dialog.panel,
              padding: "6px 12px",
            }}
          >
            <div style={{ fontSize: 14, fontWeight: 600 }}>{c.name}</div>
            <div style={{ fontSize: 12, color: theme.dialog.muted }}>{c.info}</div>
          </div>
        );
      })}
    </>
  );
};

export const ClassFolderWindow: React.FC<{ s: ClassFolderState }> = ({ s }) => {
  const theme = useAppTheme();
  const sel = s.classes[s.selected];
  const remote = clamp01(s.remote ?? 0);
  const top = remote > 0.01 ? 112 : 0;
  return (
    <Frame title="Pasta da turma" box={CLASS_FOLDER} appear={s.appear}>
      {remote > 0.01 ? (
        <div style={{ opacity: remote }}>
          <div
            style={{
              position: "absolute",
              left: 16,
              top: 44,
              width: 588,
              height: 60,
              borderRadius: 6,
              background: "#DBEAFE",
              border: "1px solid #93C5FD",
              color: "#1E3A8A",
              fontSize: 13,
              padding: "8px 12px",
            }}
          >
            As pastas das turmas ficam no PC PROF-1. O que você fizer aqui acontece nele.
          </div>
          <Btn box={{ ...CLASS_FOLDER_CTRL.takeHome, y: 112 }} hover={s.takeHover}>
            Guardar as pastas das turmas neste computador
          </Btn>
        </div>
      ) : null}
      {s.mode === "list" ? (
        <div style={{ position: "absolute", left: 0, top, width: CLASS_FOLDER.w, height: CLASS_FOLDER.h - top }}>
          <Text x={16} y={44} w={588}>
            Escolha a turma desta aula. Tudo o que os alunos baixarem ou salvarem na pasta da turma aparece em todos os computadores.
          </Text>
          <div style={{ position: "absolute", left: 0, top: 0 }}>
            <ClassList s={s} />
          </div>
          <Btn box={CLASS_FOLDER_CTRL.newClass} hover={s.newHover} press={s.newPress}>
            + Nova turma
          </Btn>
          <Btn box={{ x: 154, y: 352, w: 110, h: 32 }}>Renomear</Btn>
          <Btn box={{ x: 272, y: 352, w: 100, h: 32 }}>Excluir</Btn>
          {s.creating && s.creating.p > 0.01 ? (
            <div style={{ opacity: clamp01(s.creating.p * 1.5) }}>
              <Text x={16} y={398}>
                Nome da nova turma (por exemplo, 3º A):
              </Text>
              <Field box={CLASS_FOLDER_CTRL.nameField} text={typed("3º A", s.creating.typed)} caret={s.creating.typed < 1} />
              <Btn box={CLASS_FOLDER_CTRL.create} hover={s.createHover} press={s.createPress} strong>
                Criar
              </Btn>
              <Btn box={{ x: 494, y: 422, w: 110, h: 30 }}>Cancelar</Btn>
            </div>
          ) : null}
          {s.status ? (
            <Text x={16} y={480} w={588} muted>
              {s.status}
            </Text>
          ) : null}
          <Btn box={{ x: 16, y: CLASS_FOLDER_CTRL.start.y - top, w: 110, h: 38 }}>Fechar</Btn>
          <Btn box={{ ...CLASS_FOLDER_CTRL.start, y: CLASS_FOLDER_CTRL.start.y - top }} hover={s.startHover} press={s.startPress} strong>
            {sel ? `Começar a aula do ${sel.name}` : "Começar a aula"}
          </Btn>
        </div>
      ) : (
        <div style={{ position: "absolute", left: 0, top, width: CLASS_FOLDER.w, height: CLASS_FOLDER.h - top }}>
          <Text x={16} y={44} size={17} weight={600}>
            Pasta da turma: {sel?.name ?? "3º A"}
          </Text>
          <Text x={16} y={72} muted>
            Aula começou às 13:30 · 214 arquivos (1,2 GB) · disco deste PC com 180 GB livres
          </Text>
          <Text x={16} y={104} weight={600}>
            Computadores
          </Text>
          <div
            style={{
              position: "absolute",
              left: 16,
              top: 128,
              width: 588,
              height: 128,
              display: "grid",
              gridTemplateColumns: "repeat(4, 1fr)",
              rowGap: 8,
              fontSize: 13,
              padding: "10px 12px",
              borderRadius: 6,
              border: `1px solid ${theme.dialog.line}`,
              background: theme.dialog.panel,
            }}
          >
            {PCS.map((pc, i) => {
              const st = s.pcs?.[i] ?? "ok";
              const color = st === "ok" ? "#15803D" : st === "copying" ? "#B45309" : st === "error" ? theme.alert : theme.dialog.muted;
              return (
                <div key={pc.name}>
                  <span style={{ color, fontWeight: 700, marginRight: 6 }}>{MARK[st]}</span>
                  {pc.name}
                </div>
              );
            })}
          </div>
          <Text x={16} y={266} muted size={11.5}>
            ✓ em dia    ⟳ copiando    ○ desligado (recebe ao ligar)    ✗ com problema
          </Text>
          <Text x={16} y={300} weight={600}>
            Ao encerrar a aula
          </Text>
          <Check x={34} y={326} on={s.endChoice === 0 ? 1 : 0} radio>
            Manter os arquivos
          </Check>
          <Text x={58} y={346} muted size={11.5}>
            Voltam para todos os PCs na próxima aula do {sel?.name ?? "3º A"}.
          </Text>
          <Check x={34} y={372} on={s.endChoice === 1 ? 1 : 0} radio>
            Apagar só os de hoje
          </Check>
          <Text x={58} y={392} muted size={11.5}>
            Os 38 arquivos desta aula saem; os das aulas anteriores ficam.
          </Text>
          <Check x={34} y={418} on={s.endChoice === 2 ? 1 : 0} radio>
            Apagar tudo do {sel?.name ?? "3º A"}
          </Check>
          <Text x={58} y={438} muted size={11.5}>
            Os 214 arquivos saem do seu PC e de todos os PCs. Vão para a Lixeira deste PC.
          </Text>
          <Text x={16} y={480} muted>
            Pode fechar esta janela: a aula continua até você encerrá-la.
          </Text>
          <Btn box={{ x: 16, y: CLASS_FOLDER_CTRL.end.y - top, w: 180, h: 38 }}>Trocar de turma…</Btn>
          <Btn box={{ ...CLASS_FOLDER_CTRL.end, y: CLASS_FOLDER_CTRL.end.y - top }} hover={s.endHover} press={s.endPress} strong>
            Encerrar a aula
          </Btn>
        </div>
      )}
    </Frame>
  );
};
