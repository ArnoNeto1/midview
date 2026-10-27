import { spring } from "remotion";
import duracoes from "../../public/narracao-tutorial/duracoes.json";
import { cursorPosition, type CursorMove } from "../components/Cursor";
import type { Box, Point } from "../components/app/geometry";
import { SPRING, mix } from "../lib/motion";
import { FPS } from "../timing";
import { BEATS } from "./cenas";
import { FALAS, FALAS_RESUMO, type Fala } from "./falas";
import { FULL, tiles, toolbar, bottom, type Beat, type CamKey } from "./kit";

/** Frames entre o começo do trecho e a fala, e de silêncio depois dela. */
export const LEAD = 12;
export const GAP = 18;

export type Slot = { fala: Fala; index: number; from: number; dur: number; speech: number; beat: Beat };

/** Trecho ainda sem cena: a janela parada. */
const IDLE_BEAT: Beat = { draw: () => ({ app: { tiles: tiles(), toolbar: toolbar(), bottom: bottom() } }) };

/** A linha do tempo de uma lista de falas, com cursor, cliques e câmera em frames absolutos. */
export const makeTimeline = (falas: Fala[]) => {
  const frames = duracoes as Record<string, number>;
  let from = 0;
  // Cada fala abre um trecho do tamanho dela (ou maior, se a cena pedir).
  const SLOTS: Slot[] = falas.map((fala, index) => {
    const speech = frames[fala.id] ?? 120;
    const beat = BEATS[fala.beat ?? fala.id] ?? IDLE_BEAT;
    const dur = Math.max(beat.min ?? 0, LEAD + speech + GAP);
    const slot = { fala, index, from, dur, speech, beat };
    from += dur;
    return slot;
  });
  const TOTAL = SLOTS.reduce((sum, s) => sum + s.dur, 0);

  const slotAt = (frame: number): Slot => {
    for (let i = SLOTS.length - 1; i >= 0; i--) if (frame >= SLOTS[i].from) return SLOTS[i];
    return SLOTS[0];
  };

  const MOVES: CursorMove[] = SLOTS.flatMap((s) => (s.beat.moves ?? []).map((m) => ({ ...m, at: m.at + s.from })));
  const CLICKS: number[] = SLOTS.flatMap((s) => (s.beat.clicks ?? []).map((c) => c + s.from));
  const CAMS: CamKey[] = [{ at: 0, rect: FULL }, ...SLOTS.flatMap((s) => (s.beat.cams ?? []).map((k) => ({ at: k.at + s.from, rect: k.rect })))];

  const cursorAt = (frame: number): Point => cursorPosition(frame, FPS, CURSOR_START, MOVES);

  /** Aperto do cursor nos cliques. */
  const pressAt = (frame: number) => {
    let p = 0;
    for (const c of CLICKS) if (frame >= c - 1 && frame < c + 10) p = Math.max(p, frame < c + 2 ? (frame - c + 1) / 3 : 1 - (frame - c - 2) / 8);
    return Math.max(0, Math.min(1, p));
  };

  /** Região da janela enquadrada: cada chave parte de onde a anterior estava, numa mola de 1 s. */
  const cameraAt = (frame: number): Box => {
    let rect = CAMS[0].rect;
    for (let i = 1; i < CAMS.length; i++) {
      const key = CAMS[i];
      if (frame < key.at) break;
      const next = CAMS[i + 1];
      const until = next && next.at <= frame ? next.at : frame;
      rect = lerpBox(rect, key.rect, spring({ frame: until - key.at, fps: FPS, config: SPRING.smooth, durationInFrames: 30 }));
    }
    return rect;
  };

  return { SLOTS, TOTAL, slotAt, CLICKS, cursorAt, pressAt, cameraAt };
};

export type Timeline = ReturnType<typeof makeTimeline>;

const CURSOR_START: Point = { x: 760, y: 560 };

const lerpBox = (a: Box, b: Box, t: number): Box => ({ x: mix(a.x, b.x, t), y: mix(a.y, b.y, t), w: mix(a.w, b.w, t), h: mix(a.h, b.h, t) });

export const COMPLETO = makeTimeline(FALAS);
export const RESUMO = makeTimeline(FALAS_RESUMO);
