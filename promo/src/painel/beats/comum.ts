import type { Box, Point } from "../../components/app/geometry";
import { P_CAPTION } from "../../components/app/painel/geometry";
import type { Voice } from "../engine";

/** Ponto do conteúdo de uma janela de diálogo (abaixo da barra de título). */
export const pt = (dlg: Box, x: number, y: number): Point => ({ x: dlg.x + x, y: dlg.y + P_CAPTION + y });

/** Centro da bolinha de uma opção, ou da caixinha de uma caixa de seleção, posta em (x, y). */
export const mark = (dlg: Box, x: number, y: number): Point => pt(dlg, x + 8, y + 8);

/** Uma fala da narração do vídeo promocional (public/narracao/<id>.mp3). */
export const promoVoice = (id: string, at: number, frames: number, text: string): Voice => ({ src: `narracao/${id}.mp3`, at, frames, text });

/** Uma fala da narração do tutorial (public/narracao-tutorial/<id>.mp3), inteira ou só um trecho. */
export const tutorialVoice = (id: string, at: number, frames: number, text: string, trim: { before?: number; after?: number } = {}): Voice => ({
  src: `narracao-tutorial/${id}.mp3`,
  at,
  frames,
  text,
  trimBefore: trim.before,
  trimAfter: trim.after,
});
