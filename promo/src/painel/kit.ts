import { WIN, type Box, type ButtonId, type GroupId, type Point } from "../components/app/geometry";
import { P_CAPTION, PAINEL_TOOLBAR, painelButton, pCtrl, pTileBox, pTileScreenCenter } from "../components/app/painel/geometry";
import type { ButtonVisual } from "../components/app/Toolbar";
import { hov, sp, tap, typing, win } from "../tutorial/kit";

/** Ajudantes de tempo (frames locais do trecho), os mesmos do tutorial. */
export { hov, sp, tap, typing, win };

/** Realce e aperto de um botão clicado em `at`. */
export const clickVisual = (t: number, at: number): ButtonVisual => ({ hover: hov(t, at - 8, at + 8), press: tap(t, at) });

/** Botão da barra clicado em `at`. */
export const toolbarClick = (t: number, id: ButtonId, at: number): Partial<Record<ButtonId, ButtonVisual>> => ({ [id]: clickVisual(t, at) });

/* ---------- câmera ---------- */

export const FULL: Box = { x: 0, y: 0, w: WIN.w, h: WIN.h };

/** Mantém o enquadramento dentro da janela. */
export const inside = (r: Box): Box => ({
  ...r,
  x: Math.min(Math.max(r.x, 0), Math.max(0, WIN.w - r.w)),
  y: Math.min(Math.max(r.y, 0), Math.max(0, WIN.h - r.h)),
});

/** A barra de botões em volta de um grupo. */
export const groupFocus = (g: GroupId, w = 1120, h = 600): Box => {
  const p = PAINEL_TOOLBAR.panels[g];
  return inside({ x: p.x + p.w / 2 - w / 2, y: 0, w, h });
};

/** Uma caixa com folga em volta. */
export const focus = (b: Box, padX = 70, padY = 50): Box => inside({ x: b.x - padX, y: b.y - padY, w: b.w + padX * 2, h: b.h + padY * 2 });

/** Parte de uma janela de diálogo: do topo (com a barra de título) até `to` px do conteúdo. */
export const dlgTop = (dlg: Box, to: number, padX = 60, padY = 40): Box =>
  focus({ x: dlg.x, y: dlg.y, w: dlg.w, h: Math.min(dlg.h, P_CAPTION + to) }, padX, padY);

/* ---------- alvos do cursor ---------- */

export const btn = (id: ButtonId): Point => painelButton(id);
export const ctrl = (dlg: Box, c: Box, dx = 0, dy = 0): Point => pCtrl(dlg, c, dx, dy);
export const tileAt = (i: number): Point => pTileScreenCenter(pTileBox(i));
/** Fechar (X) da barra de título de uma janela de diálogo. */
export const closeX = (dlg: Box): Point => ({ x: dlg.x + dlg.w - 23, y: dlg.y + P_CAPTION / 2 });
