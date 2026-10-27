import React from "react";
import { ClickRipple, Cursor, cursorPosition, type CursorMove } from "../../components/Cursor";
import type { Point } from "../../components/app/geometry";
import { ramp } from "../../lib/motion";
import { Scene1Hook } from "../../scenes/Scene1Hook";
import { Scene9Encerramento } from "../../scenes/Scene9Encerramento";
import { FPS } from "../../timing";
import { hov, sp, tap, typing, win, type Beat } from "../kit";
import { SilentInstall, WIZARD_CTRL, Wizard, WindowsDesktop, wizardPoint, type WizardState } from "../janelas/instalador";

/** Cursor próprio das cenas fora do app (instalador), em coordenadas da tela 1920×1080. */
const ScreenCursor: React.FC<{ t: number; start: Point; moves: CursorMove[]; clicks: number[] }> = ({ t, start, moves, clicks }) => {
  const p = cursorPosition(t, FPS, start, moves);
  const press = Math.max(0, ...clicks.map((c) => tap(t, c)));
  return (
    <>
      {clicks.map((c) => (t >= c && t <= c + 16 ? <ClickRipple key={c} {...cursorPosition(c, FPS, start, moves)} progress={ramp(t, c, c + 16)} /> : null))}
      <div style={{ position: "absolute", left: 0, top: 0, transformOrigin: "0 0", transform: "scale(1.4)" }}>
        <Cursor x={p.x / 1.4} y={p.y / 1.4} press={press} opacity={1} />
      </div>
    </>
  );
};

const NEXT = wizardPoint(WIZARD_CTRL.next);
const ACCEPT = wizardPoint(WIZARD_CTRL.accept);
const PROFESSOR = wizardPoint(WIZARD_CTRL.professor);

export const INICIO: Record<string, Beat> = {
  t01: { hideCursor: true, draw: () => ({ custom: <Scene1Hook /> }) },

  // Boas-vindas do instalador.
  t02: {
    draw: (t) => {
      const s: WizardState = { page: "welcome", appear: sp(t, 20, 16), nextHover: hov(t, 150, 172), nextPress: tap(t, 160) };
      const moves: CursorMove[] = [{ at: 90, to: NEXT, arc: 40 }];
      return {
        custom: (
          <>
            <WindowsDesktop shortcut={0} glow={0} />
            <Wizard s={s} />
            <ScreenCursor t={t} start={{ x: 1300, y: 900 }} moves={moves} clicks={[160]} />
          </>
        ),
      };
    },
  },

  // Licença aceita, tipo Professor e a cópia dos arquivos.
  t03: {
    draw: (t) => {
      const page = t < 70 ? "license" : t < 170 ? "tipo" : "installing";
      const s: WizardState = {
        page,
        appear: 1,
        accepted: t >= 38,
        tipo: t >= 120 ? "professor" : null,
        progress: ramp(t, 175, 280),
        nextHover: hov(t, 54, 74) + hov(t, 150, 172),
        nextPress: tap(t, 64) + tap(t, 160),
      };
      const moves: CursorMove[] = [
        { at: 4, to: ACCEPT, arc: 30 },
        { at: 44, to: NEXT, arc: 30 },
        { at: 86, to: PROFESSOR, arc: -30 },
        { at: 128, to: NEXT, arc: 30 },
      ];
      return {
        custom: (
          <>
            <WindowsDesktop shortcut={0} glow={0} />
            <Wizard s={s} />
            <ScreenCursor t={t} start={NEXT} moves={moves} clicks={[36, 64, 118, 160]} />
          </>
        ),
      };
    },
  },

  // Concluir e o atalho na Área de Trabalho.
  t04: {
    draw: (t) => {
      const s: WizardState = { page: t < 30 ? "installing" : "finished", appear: 1 - sp(t, 96, 14), progress: 1, nextHover: hov(t, 70, 96), nextPress: tap(t, 84) };
      const moves: CursorMove[] = [
        { at: 40, to: NEXT, arc: 20 },
        { at: 120, to: { x: 100, y: 268 }, arc: -60 },
      ];
      return {
        custom: (
          <>
            <WindowsDesktop shortcut={sp(t, 104, 16)} glow={win(t, 130, 400, 14, 14)} />
            {s.appear > 0.01 ? <Wizard s={s} /> : null}
            <ScreenCursor t={t} start={NEXT} moves={moves} clicks={[84]} />
          </>
        ),
      };
    },
  },

  // Instalação silenciosa em muitas máquinas.
  t05: {
    draw: (t) => ({
      custom: <SilentInstall typed={typing(t, 10, 80)} done={Array.from({ length: 12 }, (_, i) => sp(t, 100 + i * 9, 6))} />,
    }),
  },

  t56: { hideCursor: true, min: 250, draw: () => ({ custom: <Scene9Encerramento /> }) },
};
