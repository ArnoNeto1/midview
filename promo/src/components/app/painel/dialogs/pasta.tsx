import React from "react";
import { clamp01 } from "../../../../lib/motion";
import { PAINEL_PCS, pDialog } from "../geometry";
import { Card, PBtn, PDialog, PField, PRadio, T, typed } from "../kit";
import { P, PG } from "../palette";

/**
 * A Pasta da turma completa do tutorial (ClassFolderWindow.xaml, fotos 42 a 45):
 * a lista de turmas com "+ Nova turma", a aula ligada com os computadores e as
 * opções ao encerrar, e o aviso de que as pastas ficam em outro PC de professor.
 */
export const FOLDER = pDialog(586, 663);

export type FolderClass = { name: string; note: string };
export type FolderPc = "ok" | "copying" | "off";

export type FolderState = {
  appear: number;
  mode: "list" | "lesson";
  classes: FolderClass[];
  selected: number;
  /** Linha "Nome da nova turma" (0→1) e o nome digitado (0→1 de "3º A"). */
  creating?: { p: number; typed: number };
  newHover?: number;
  newPress?: number;
  createHover?: number;
  createPress?: number;
  startHover?: number;
  startPress?: number;
  /** Cartão de resultado embaixo (0→1) e o texto dele. */
  result?: { p: number; text: string };
  /** Aula ligada: a turma, os computadores e a opção ao encerrar. */
  turma?: string;
  pcs?: FolderPc[];
  endChoice?: 0 | 1 | 2;
  endHover?: number;
  endPress?: number;
  /** As pastas ficam no PC de outro professor (0→1), com "Guardar as pastas das turmas neste computador". */
  remote?: number;
  takeHover?: number;
};

const NEW_NAME = "3º A";
export const FOLDER_CTRL = {
  newClass: { x: 24, y: 464, w: 99, h: 27 },
  create: { x: 408, y: 521, w: 70, h: 30 },
  start: (turma: string | null) => ({ x: turma ? 380 : 438, y: 614, w: turma ? 182 : 124, h: 29 }),
  row: (i: number, offset = 0) => ({ x: 34, y: 132 + offset + i * 46, w: 518, h: 44 }),
  choice: (i: 0 | 1 | 2) => ({ x: 37, y: 424 + i * 40, w: 16, h: 16 }),
  end: { x: 444, y: 614, w: 118, h: 28 },
  takeOver: { x: 37, y: 113, w: 300, h: 26 },
};

const ListView: React.FC<{ s: FolderState }> = ({ s }) => {
  const remote = clamp01(s.remote ?? 0);
  const off = remote > 0.01 ? 82 : 0;
  const creating = clamp01(s.creating?.p ?? 0);
  const turma = s.selected >= 0 ? s.classes[s.selected]?.name ?? null : null;
  return (
    <>
      {remote > 0.01 ? (
        <div style={{ opacity: remote }}>
          <Card box={{ x: 24, y: 76, w: 538, h: 72 }} style={{ border: `1px solid ${P.accent}`, background: "rgba(45,212,191,0.08)" }} />
          <T x={37} y={88} w={512} size={12}>
            As pastas das turmas ficam no PC PROF-01. O que você fizer aqui acontece nele.
          </T>
          <PBtn box={FOLDER_CTRL.takeOver} hover={s.takeHover}>
            Guardar as pastas das turmas neste computador
          </PBtn>
        </div>
      ) : null}
      <T x={24} y={82 + off} w={538} muted size={12.5}>
        Escolha a turma desta aula. Tudo o que os alunos baixarem ou salvarem na pasta da turma aparece em todos os computadores.
      </T>
      <Card box={{ x: 24, y: 122 + off, w: 538, h: 330 - off }} />
      {s.classes.map((c, i) => {
        const r = FOLDER_CTRL.row(i, off);
        return (
          <React.Fragment key={c.name}>
            <div style={{ position: "absolute", left: r.x, top: r.y, width: r.w, height: r.h, borderRadius: 4, background: "#253552", opacity: i === s.selected ? 1 : 0 }} />
            <T x={r.x + 10} y={r.y + 7} weight={600} size={13}>
              {c.name}
            </T>
            <T x={r.x + 10} y={r.y + 25} muted size={11}>
              {c.note}
            </T>
          </React.Fragment>
        );
      })}
      <PBtn box={FOLDER_CTRL.newClass} hover={s.newHover} press={s.newPress}>
        + Nova turma
      </PBtn>
      <PBtn box={{ x: 130, y: 464, w: 82, h: 27 }}>Renomear</PBtn>
      <PBtn box={{ x: 220, y: 464, w: 63, h: 27 }} kind="danger">
        Excluir
      </PBtn>
      {creating > 0.01 ? (
        <div style={{ opacity: creating }}>
          <T x={24} y={502} muted size={12}>
            Nome da nova turma (por exemplo, 3º A):
          </T>
          <PField box={{ x: 24, y: 521, w: 376, h: 30 }} text={typed(NEW_NAME, s.creating?.typed ?? 0)} caret focus />
          <PBtn box={FOLDER_CTRL.create} hover={s.createHover} press={s.createPress}>
            Criar
          </PBtn>
          <PBtn box={{ x: 486, y: 521, w: 76, h: 30 }}>Cancelar</PBtn>
        </div>
      ) : null}
      {s.result && s.result.p > 0.01 ? (
        <div style={{ position: "absolute", left: 24, top: 566, fontSize: 12, color: P.text, opacity: clamp01(s.result.p) }}>{s.result.text}</div>
      ) : null}
      <PBtn box={{ x: 24, y: 614, w: 64, h: 29 }}>Fechar</PBtn>
      <PBtn box={FOLDER_CTRL.start(turma)} kind="primary" hover={s.startHover} press={s.startPress} disabled={!turma}>
        {turma ? `Começar a aula do ${turma}` : "Começar a aula"}
      </PBtn>
    </>
  );
};

const LessonView: React.FC<{ s: FolderState }> = ({ s }) => {
  const turma = s.turma ?? "3º A";
  const pcs = s.pcs ?? PAINEL_PCS.map(() => "ok" as FolderPc);
  const choice = s.endChoice ?? 0;
  return (
    <>
      <T x={24} y={84} size={15} weight={600}>
        Pasta da turma: {turma}
      </T>
      <T x={24} y={106} muted>
        Aula começou às 07:30 · 12 arquivos (18 MB) · disco deste PC com 180 GB livres
      </T>
      <Card box={{ x: 24, y: 132, w: 538, h: 238 }} />
      <T x={37} y={146} title>
        Computadores
      </T>
      <div style={{ position: "absolute", left: 37, top: 172, width: 512, display: "flex", flexWrap: "wrap", rowGap: 6, columnGap: 18, fontSize: 12.5 }}>
        {PAINEL_PCS.map((pc, i) => {
          const st = pcs[i] ?? "ok";
          const symbol = st === "off" ? "○" : st === "ok" ? "✓" : "⟳";
          const color = st === "off" ? P.muted : st === "ok" ? P.online : P.accentText;
          return (
            <span key={pc.name} style={{ width: 84, color, whiteSpace: "nowrap" }}>
              {symbol} {pc.name}
            </span>
          );
        })}
      </div>
      <T x={37} y={344} muted size={11}>
        ✓ em dia &nbsp;&nbsp; ⟳ copiando &nbsp;&nbsp; ○ desligado (recebe ao ligar) &nbsp;&nbsp; ✗ com problema
      </T>
      <Card box={{ x: 24, y: 381, w: 538, h: 221 }} />
      <T x={37} y={395} title>
        Ao encerrar a aula
      </T>
      {[
        { t: "Manter os arquivos", d: `Voltam para todos os PCs na próxima aula do ${turma}.` },
        { t: "Apagar só os de hoje", d: "Os 12 arquivos desta aula saem; os das aulas anteriores ficam." },
        { t: `Apagar tudo do ${turma}`, d: `Os 12 arquivos saem do seu PC e de todos os PCs. Vão para a Lixeira deste PC.` },
      ].map((o, i) => (
        <React.Fragment key={o.t}>
          <PRadio x={37} y={424 + i * 40} on={choice === i ? 1 : 0}>
            <span style={{ fontWeight: 600 }}>{o.t}</span>
          </PRadio>
          <T x={61} y={441 + i * 40} muted>
            {o.d}
          </T>
        </React.Fragment>
      ))}
      <T x={37} y={576} muted>
        Pode fechar esta janela: a aula continua até você encerrá-la.
      </T>
      <PBtn box={{ x: 24, y: 614, w: 119, h: 28 }}>Trocar de turma...</PBtn>
      <PBtn box={FOLDER_CTRL.end} kind="primary" hover={s.endHover} press={s.endPress}>
        Encerrar a aula
      </PBtn>
    </>
  );
};

export const FolderDialog: React.FC<{ s: FolderState }> = ({ s }) => (
  <PDialog title="Pasta da turma" box={FOLDER} appear={s.appear} icon={PG.syncFolder} group="turma" heading="Pasta da turma">
    {s.mode === "list" ? <ListView s={s} /> : <LessonView s={s} />}
  </PDialog>
);
