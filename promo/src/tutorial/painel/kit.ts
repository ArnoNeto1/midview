import type { TileItem } from "../../components/app/AppWindow";
import type { BottomBarProps } from "../../components/app/BottomBar";
import type { Box, ButtonId, GroupId, Point } from "../../components/app/geometry";
import { IDLE_TILE, type TileState } from "../../components/app/MachineTile";
import {
  P_FIT,
  PAINEL_PCS,
  pSliderValue,
  pTileBox,
  pTileScreenCenter,
  painelButton,
  pCtrl,
  type PTileLayout,
} from "../../components/app/painel/geometry";
import type { PainelTitle } from "../../components/app/painel/TitleBar";
import type { ToolbarProps } from "../../components/app/Toolbar";
import { mix } from "../../lib/motion";
import { dlgTop, focus, groupFocus as painelGroupFocus, inside } from "../../painel/kit";
import { win } from "../kit";

/**
 * Ajudantes das cenas do tutorial no visual Painel: a grade de 15 máquinas
 * (ALUNO-01 a ALUNO-15), a barra, a faixa de baixo, os alvos do cursor e a
 * câmera, nas medidas de components/app/painel/geometry.ts.
 */

export { dlgTop, focus, inside };

/** ALUNO-14 e ALUNO-15 começam desligadas; "Ligar" (t15) acorda as duas. */
export const OFF_START = [13, 14];
/** Depois, a ALUNO-15 fica desligada (Pasta da turma) e sai da sala em "Remover da lista da sala" (t50). */
export const OFF_LATE = [14];

export const pname = (i: number) => PAINEL_PCS[i].name;

const lerpBox = (a: Box, b: Box, t: number): Box => ({ x: mix(a.x, b.x, t), y: mix(a.y, b.y, t), w: mix(a.w, b.w, t), h: mix(a.h, b.h, t) });

export type PGridOpts = {
  /** Máquinas desligadas ("Sem resposta"). */
  offline?: number[];
  /** Máquinas que saíram da lista da sala. */
  removed?: number[];
  selected?: (i: number) => number;
  /** "Só online" escondendo as desligadas (0–1). */
  onlineOnly?: number;
  layoutFrom?: PTileLayout;
  layoutTo?: PTileLayout;
  layoutT?: number;
  each?: (i: number) => Partial<TileState> & { box?: Box };
};

/** As 15 máquinas no estado de uma cena. */
export const pgrid = (o: PGridOpts = {}): TileItem[] => {
  const offline = o.offline ?? OFF_START;
  const removed = o.removed ?? [];
  const from = o.layoutFrom ?? P_FIT;
  const to = o.layoutTo ?? from;
  return PAINEL_PCS.map((_, i) => {
    const off = offline.includes(i);
    const { box, ...extra } = o.each ? o.each(i) : {};
    const state: TileState = {
      ...IDLE_TILE,
      online: off ? 0 : 1,
      status: off ? "Sem resposta" : "",
      appear: removed.includes(i) ? 0 : off ? 1 - (o.onlineOnly ?? 0) : 1,
      selected: o.selected ? o.selected(i) : 0,
      blockText: "3 bloqueados",
      ...extra,
    };
    return { index: i, box: box ?? lerpBox(pTileBox(i, from), pTileBox(i, to), o.layoutT ?? 0), state };
  });
};

/** A barra parada: "Ver a tela" e "Controlar" pedem uma máquina selecionada. */
export const ptoolbar = (extra: ToolbarProps["buttons"] = {}, oneSelected = false, spotlight: ToolbarProps["spotlight"] = null): ToolbarProps => ({
  buttons: { view: { disabled: !oneSelected }, control: { disabled: !oneSelected }, ...extra },
  spotlight,
});

/** O destaque do grupo durante o trecho inteiro. */
export const pspot = (group: GroupId, t: number, d: number) => ({ group, amount: win(t, 0, d - 4, 12, 12) });

export const P_SLIDER_FIT = pSliderValue(P_FIT.screenW);

export const pbottom = (extra: Partial<BottomBarProps> = {}): BottomBarProps => ({
  status: "",
  onlineOnly: 0,
  slider: P_SLIDER_FIT,
  fitHover: 0,
  fitPress: 0,
  onlineHover: 0,
  sliderGrab: 0,
  fitOn: 1,
  ...extra,
});

/** Os selos da barra de título: a aula ligada (Pasta da turma) e quantas estão ligadas. */
export const ptitle = (tiles: TileItem[], lesson?: string, lessonIn = 1): PainelTitle => {
  const inRoom = tiles.filter((t) => t.state.appear > 0.5);
  return { lesson, lessonIn, online: inRoom.filter((t) => t.state.online > 0.5).length, total: inRoom.length };
};

/* ---------- alvos e câmera ---------- */

export const pbutton = (id: ButtonId): Point => painelButton(id);
export const ptile = (i: number, layout: PTileLayout = P_FIT): Point => pTileScreenCenter(pTileBox(i, layout));
export const pmid = (dlg: Box, c: Box, dx = 0, dy = 0): Point => pCtrl(dlg, c, dx, dy);
export const pgroupFocus = (g: GroupId): Box => painelGroupFocus(g);
/** Uma janela de diálogo com folga em volta. */
export const pdialogFocus = (b: Box, pad = 60): Box => focus(b, pad, pad * 0.7);
