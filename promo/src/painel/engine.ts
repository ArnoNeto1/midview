import type React from "react";
import { spring } from "remotion";
import { cursorPosition, type CursorMove } from "../components/Cursor";
import type { BottomBarProps } from "../components/app/BottomBar";
import type { Box, ButtonId, GroupId, Point } from "../components/app/geometry";
import type { TileState } from "../components/app/MachineTile";
import type { PainelTitle } from "../components/app/painel/TitleBar";
import type { ButtonVisual } from "../components/app/Toolbar";
import { SPRING, mix } from "../lib/motion";
import { FPS } from "../timing";

/**
 * Roteiro dos vídeos do visual Painel: uma lista de trechos (beats), cada um
 * com a sua duração, a fala (um mp3 já gravado da narração), a legenda, os
 * movimentos do cursor, os cliques, a câmera e o que a janela mostra. Os
 * frames de cada trecho são locais; a linha do tempo os soma.
 */

/** Uma fala já gravada (public/narracao ou public/narracao-tutorial), com o trecho usado. */
export type Voice = {
  src: string;
  /** Frame local em que a fala começa. */
  at: number;
  /** Frames cortados do começo e onde a fala acaba (frames do arquivo). */
  trimBefore?: number;
  trimAfter?: number;
  /** Duração em frames do trecho usado (para conferir que não encosta na próxima). */
  frames: number;
  text: string;
};

export type CamKey = { at: number; rect: Box };

/** O que um trecho muda na janela, num frame local `t`. */
export type BeatOut = {
  tiles?: (i: number, base: TileState) => Partial<TileState>;
  buttons?: Partial<Record<ButtonId, ButtonVisual>>;
  bottom?: Partial<BottomBarProps>;
  title?: Partial<PainelTitle>;
  /** Janelas de diálogo e menus, em coordenadas da janela, sob o cursor. */
  overlay?: React.ReactNode;
  /** Destaque do grupo do trecho (0–1). */
  spot?: number;
  /** Seleção das máquinas (0–1 por máquina). */
  selected?: (i: number) => number;
};

export type Beat = {
  id: string;
  dur: number;
  group?: GroupId;
  /** Rótulo da legenda (padrão: o nome do grupo na barra). */
  eyebrow?: string;
  caption?: string;
  voice?: Voice;
  moves?: CursorMove[];
  clicks?: number[];
  cams?: CamKey[];
  /** Acontecimentos que valem depois do trecho (frame local), por nome. */
  events?: Record<string, number>;
  /** O que a janela mostra no frame local `t` (`abs`: frame da linha do tempo, para as telas ao vivo). */
  draw: (t: number, abs: number) => BeatOut;
};

export type Slot = { beat: Beat; index: number; from: number };

export const makeTimeline = (beats: Beat[], start: { cursor: Point; cam: Box }) => {
  let from = 0;
  const SLOTS: Slot[] = beats.map((beat, index) => {
    const slot = { beat, index, from };
    from += beat.dur;
    return slot;
  });
  const TOTAL = from;
  const EVENTS: Record<string, number> = {};
  for (const s of SLOTS) for (const [name, at] of Object.entries(s.beat.events ?? {})) EVENTS[name] = s.from + at;
  const MOVES: CursorMove[] = SLOTS.flatMap((s) => (s.beat.moves ?? []).map((m) => ({ ...m, at: m.at + s.from })));
  const CLICKS: number[] = SLOTS.flatMap((s) => (s.beat.clicks ?? []).map((c) => c + s.from));
  const CAMS: CamKey[] = [{ at: 0, rect: start.cam }, ...SLOTS.flatMap((s) => (s.beat.cams ?? []).map((k) => ({ at: k.at + s.from, rect: k.rect })))];

  const slotAt = (frame: number): Slot => {
    for (let i = SLOTS.length - 1; i >= 0; i--) if (frame >= SLOTS[i].from) return SLOTS[i];
    return SLOTS[0];
  };
  const cursorAt = (frame: number): Point => cursorPosition(frame, FPS, start.cursor, MOVES);
  const pressAt = (frame: number) => {
    let p = 0;
    for (const c of CLICKS) if (frame >= c - 1 && frame < c + 10) p = Math.max(p, frame < c + 2 ? (frame - c + 1) / 3 : 1 - (frame - c - 2) / 8);
    return Math.max(0, Math.min(1, p));
  };
  /** Região da janela enquadrada; cada chave parte de onde a anterior estava, numa mola de 1 s. */
  const cameraAt = (frame: number): Box => {
    let rect = CAMS[0].rect;
    for (let i = 1; i < CAMS.length; i++) {
      const key = CAMS[i];
      if (frame < key.at) break;
      const next = CAMS[i + 1];
      const until = next && next.at <= frame ? next.at : frame;
      const p = spring({ frame: until - key.at, fps: FPS, config: SPRING.smooth, durationInFrames: 30 });
      rect = { x: mix(rect.x, key.rect.x, p), y: mix(rect.y, key.rect.y, p), w: mix(rect.w, key.rect.w, p), h: mix(rect.h, key.rect.h, p) };
    }
    return rect;
  };
  /** As falas na linha do tempo, para conferir que nenhuma encosta na seguinte. */
  const VOICES = SLOTS.filter((s) => s.beat.voice).map((s) => ({ id: s.beat.id, from: s.from + s.beat.voice!.at, to: s.from + s.beat.voice!.at + s.beat.voice!.frames, voice: s.beat.voice! }));
  return { SLOTS, TOTAL, EVENTS, CLICKS, slotAt, cursorAt, pressAt, cameraAt, VOICES };
};

export type Timeline = ReturnType<typeof makeTimeline>;
