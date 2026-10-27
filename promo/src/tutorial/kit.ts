import type React from "react";
import { spring } from "remotion";
import type { CursorMove } from "../components/Cursor";
import type { TileItem } from "../components/app/AppWindow";
import type { BottomBarProps } from "../components/app/BottomBar";
import {
  LAYOUT_FIT,
  TOOLBAR_LAYOUT,
  WIN,
  sliderValue,
  tileBox,
  type Box,
  type GroupId,
  type Point,
  type TileLayout,
} from "../components/app/geometry";
import { IDLE_TILE, type TileState } from "../components/app/MachineTile";
import { PCS } from "../components/app/pcs";
import type { ToolbarProps } from "../components/app/Toolbar";
import { SPRING, clamp01, ramp } from "../lib/motion";
import { FPS } from "../timing";
import type { StudentScreenState } from "./StudentScreen";

/* ---------- tempo dentro de um trecho ---------- */

/** Mola de 0 a 1 que começa em `at` (frames locais do trecho). */
export const sp = (t: number, at: number, dur = 12) => spring({ frame: t - at, fps: FPS, config: SPRING.smooth, durationInFrames: dur });
/** Aparece em `on` e some em `off`. */
export const win = (t: number, on: number, off: number, inDur = 10, outDur = 10) => sp(t, on, inDur) * (1 - sp(t, off, outDur));
/** Realce do mouse sobre um controle entre `from` e `to`. */
export const hov = (t: number, from: number, to: number) => ramp(t, from, from + 5) * (1 - ramp(t, to, to + 5));
/** Aperto rápido de um clique em `at`. */
export const tap = (t: number, at: number) => clamp01(Math.min(ramp(t, at - 1, at + 2), 1 - ramp(t, at + 3, at + 10)));
/** Texto digitado de `from` a `to`. */
export const typing = (t: number, from: number, to: number) => clamp01((t - from) / Math.max(1, to - from));

/* ---------- caixas ---------- */

/** Janela centrada na janela principal (CenterOwner). */
export const centered = (w: number, h: number): Box => ({ x: Math.round((WIN.w - w) / 2), y: Math.round((WIN.h - h) / 2) + 14, w, h });
/** Ponto no meio de um controle, com a caixa do controle relativa à janela que o contém. */
export const mid = (dlg: Box, ctrl: Box, dx = 0, dy = 0): Point => ({ x: dlg.x + ctrl.x + ctrl.w / 2 + dx, y: dlg.y + ctrl.y + ctrl.h / 2 + dy });
/** Onde o cursor mira num botão da barra de ferramentas. */
export const button = (id: keyof typeof TOOLBAR_LAYOUT.buttons): Point => {
  const b = TOOLBAR_LAYOUT.buttons[id];
  return { x: b.x + b.w / 2 + 2, y: b.y + 24 };
};
/** Centro da tela de um card da grade. */
export const tileCenter = (index: number, layout: TileLayout = LAYOUT_FIT): Point => {
  const b = tileBox(index, layout);
  return { x: b.x + b.w / 2, y: b.y + (b.w * 9) / 32 };
};

/* ---------- câmera ---------- */

export const FULL: Box = { x: 0, y: 0, w: WIN.w, h: WIN.h };
/** Mantém o enquadramento dentro da janela. */
export const inside = (r: Box): Box => ({
  ...r,
  x: Math.min(Math.max(r.x, 0), Math.max(0, WIN.w - r.w)),
  y: Math.min(Math.max(r.y, 0), Math.max(0, WIN.h - r.h)),
});
export const groupFocus = (g: GroupId): Box => {
  const gb = TOOLBAR_LAYOUT.groups[g];
  return inside({ x: gb.x + gb.w / 2 - 560, y: 0, w: 1120, h: 600 });
};
export const dialogFocus = (b: Box, pad = 70): Box => inside({ x: b.x - pad, y: b.y - pad * 0.7, w: b.w + pad * 2, h: b.h + pad * 1.4 });
export const bottomFocus: Box = { x: 230, y: 160, w: 1300, h: 660 };

/* ---------- a janela no estado de sempre ---------- */

export type TileOverride = (i: number, base: TileState) => Partial<TileState> & { box?: Box };

/** Os 12 cards da grade; `override` muda cada card a partir do estado parado. */
export const tiles = (override?: TileOverride, layout: TileLayout = LAYOUT_FIT): TileItem[] =>
  PCS.map((_, i) => {
    const base: TileState = { ...IDLE_TILE };
    const o = override ? override(i, base) : {};
    const { box, ...state } = o;
    return { index: i, box: box ?? tileBox(i, layout), state: { ...base, ...state } };
  });

/** A barra de ferramentas parada: "Ver a tela" e "Controlar" pedem uma máquina selecionada. */
export const toolbar = (extra: ToolbarProps["buttons"] = {}, oneSelected = false, spotlight: ToolbarProps["spotlight"] = null): ToolbarProps => ({
  buttons: {
    view: { disabled: !oneSelected },
    control: { disabled: !oneSelected },
    ...extra,
  },
  spotlight,
});

export const SLIDER_FIT = sliderValue(LAYOUT_FIT.w);

export const bottom = (extra: Partial<BottomBarProps> = {}): BottomBarProps => ({
  status: "",
  onlineOnly: 0,
  slider: SLIDER_FIT,
  fitHover: 0,
  fitPress: 0,
  onlineHover: 0,
  sliderGrab: 0,
  fitOn: 1,
  ...extra,
});

/* ---------- trechos ---------- */

/** O que a janela mostra num frame. */
export type AppFrame = {
  tiles: TileItem[];
  toolbar: ToolbarProps;
  bottom: BottomBarProps;
  /** Janelas, menus e destaques, em coordenadas da janela (desenhados sob o cursor). */
  overlay?: React.ReactNode;
  /** A grade some (sala vazia) e este conteúdo fica no lugar. */
  grid?: React.ReactNode;
};

export type BeatFrame = {
  app?: AppFrame;
  /** A tela do aluno ao lado da janela (0 = escondida). */
  student?: StudentScreenState;
  /** Quanto a tela do aluno ocupa (0–1); a janela encolhe para a esquerda. */
  split?: number;
  /** Conteúdo próprio do trecho (instalador, abertura). */
  custom?: React.ReactNode;
};

export type CamKey = { at: number; rect: Box };

export type Beat = {
  /** Duração mínima (frames), além do que a fala pede. */
  min?: number;
  /** Movimentos do cursor (frames locais). */
  moves?: CursorMove[];
  /** Cliques do botão esquerdo (frames locais). */
  clicks?: number[];
  /** Enquadramentos (frames locais). */
  cams?: CamKey[];
  /** O cursor some neste trecho. */
  hideCursor?: boolean;
  draw: (t: number, d: number) => BeatFrame;
};
