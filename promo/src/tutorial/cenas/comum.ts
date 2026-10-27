import { spring } from "remotion";
import type { TileItem } from "../../components/app/AppWindow";
import { LAYOUT_FIT, tileBox, type Box, type Point, type TileLayout } from "../../components/app/geometry";
import type { TileState } from "../../components/app/MachineTile";
import { PCS } from "../../components/app/pcs";
import { mix } from "../../lib/motion";
import { FPS } from "../../timing";
import { tiles } from "../kit";

/** Máquinas desligadas até "Ligar" (PC-11 e PC-12). */
export const OFFLINE = [10, 11];

export const lerpBox = (a: Box, b: Box, t: number): Box => ({ x: mix(a.x, b.x, t), y: mix(a.y, b.y, t), w: mix(a.w, b.w, t), h: mix(a.h, b.h, t) });

/** O mesmo movimento de mola do cursor (components/Cursor.tsx), para coisas arrastadas junto com ele. */
export const follow = (t: number, at: number) => spring({ frame: t - at, fps: FPS, config: { damping: 18, stiffness: 120, mass: 0.85 } });

export type GridOpts = {
  /** Máquinas ainda desligadas ("Tela indisponível"). */
  offline?: number[];
  /** Seleção (0–1) de cada máquina. */
  selected?: (i: number) => number;
  /** "Só online" escondendo as desligadas (0–1). */
  onlineOnly?: number;
  /** Mistura entre duas grades (0 = from, 1 = to). */
  layoutFrom?: TileLayout;
  layoutTo?: TileLayout;
  layoutT?: number;
  /** Ajustes por máquina, depois do resto. */
  each?: (i: number) => Partial<TileState> & { box?: Box };
};

/** A grade com as 12 máquinas no estado de uma cena. */
export const grid = (o: GridOpts = {}): TileItem[] => {
  const offline = o.offline ?? OFFLINE;
  const from = o.layoutFrom ?? LAYOUT_FIT;
  const to = o.layoutTo ?? from;
  return tiles((i) => {
    const off = offline.includes(i);
    const extra = o.each ? o.each(i) : {};
    return {
      box: lerpBox(tileBox(i, from), tileBox(i, to), o.layoutT ?? 0),
      online: off ? 0 : 1,
      status: off ? "Tela indisponível" : "",
      appear: off ? 1 - (o.onlineOnly ?? 0) : 1,
      selected: o.selected ? o.selected(i) : 0,
      ...extra,
    };
  });
};

export const ALL = PCS.map((_, i) => i);
export const live = (i: number, offline: number[] = OFFLINE) => (offline.includes(i) ? 0 : 1);

/** Último texto de uma lista [frame, texto] que já começou. */
export const statusAt = (t: number, list: [number, string][]) => {
  let s = "";
  for (const [at, text] of list) if (t >= at) s = text;
  return s;
};

export const point = (x: number, y: number): Point => ({ x, y });
