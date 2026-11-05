import { WIN, type Box, type ButtonId, type GroupId, type Point } from "../geometry";
import type { Pc } from "../pcs";
import { PG } from "./palette";

/**
 * Medidas da janela da sala de aula no visual Painel (MainWindow.xaml e
 * Themes/Painel.Main.xaml da versão 0.0.41), na mesma janela de 1530×820 do
 * vídeo: barra de título própria, grupos da barra de botões em painéis
 * arredondados, cartões das telas e a faixa de baixo.
 */
export const PAINEL = {
  titleBar: 32,
  /** Topo dos painéis dos grupos (barra de título + margem 2). */
  panelY: 34,
  panelH: 87,
  buttonW: 58,
  buttonH: 65,
  /** Onde a grade começa: painéis + margem 2 + respiro 6. */
  gridTop: 129,
  bottomBar: 36,
} as const;

export const P_GRID_TOP = PAINEL.gridTop;
export const P_GRID_BOTTOM = WIN.h - PAINEL.bottomBar;

type PButtonDef = { id: ButtonId; label: string; icon: string; badge?: string; options?: boolean; width?: number };
type PGroupDef = { id: GroupId; caption: string; buttons: PButtonDef[] };

/** Os botões da barra na ordem do XAML, com os ícones e as larguras de cada um. */
export const PAINEL_GROUPS: PGroupDef[] = [
  {
    id: "energia",
    caption: "Energia",
    buttons: [
      { id: "powerOff", label: "Desligar", icon: PG.power, options: true },
      { id: "restart", label: "Reiniciar", icon: PG.restart, options: true },
      { id: "wake", label: "Ligar", icon: PG.wake },
      { id: "schedule", label: "Agendar", icon: PG.calendar, options: true },
    ],
  },
  {
    id: "tela",
    caption: "Tela",
    buttons: [
      { id: "view", label: "Ver a tela", icon: PG.eye },
      { id: "control", label: "Controlar", icon: PG.control },
      { id: "lock", label: "Bloquear telas", icon: PG.monitor, badge: PG.lock, width: 72 },
    ],
  },
  {
    id: "turma",
    caption: "Turma",
    buttons: [
      { id: "message", label: "Mensagem", icon: PG.message, options: true },
      { id: "chat", label: "Bate-papo", icon: PG.chat },
      { id: "sendFile", label: "Enviar arquivo", icon: PG.upload, options: true },
      { id: "collect", label: "Recolher arquivos", icon: PG.download, options: true },
      { id: "classFolder", label: "Pasta da turma", icon: PG.syncFolder, options: true, width: 84 },
    ],
  },
  {
    id: "programas",
    caption: "Programas e sites",
    buttons: [
      { id: "openSite", label: "Abrir site", icon: PG.globe, options: true },
      { id: "openProgram", label: "Abrir programa", icon: PG.console, options: true },
      { id: "install", label: "Instalar programa", icon: PG.box, options: true },
      { id: "uninstall", label: "Desinstalar programa", icon: PG.trash, options: true },
      { id: "closeApps", label: "Fechar programas", icon: PG.cancel, options: true },
      { id: "blockApps", label: "Bloquear programas", icon: PG.apps, badge: PG.block, options: true },
      { id: "blockSites", label: "Bloquear sites", icon: PG.globe, badge: PG.block, options: true },
    ],
  },
  {
    id: "configurar",
    caption: "Configurar",
    buttons: [
      { id: "labPanel", label: "Painel do laboratório", icon: PG.settings, options: true },
      // "da sala" junto, para quebrar como no app: "Máquinas / da sala".
      { id: "machines", label: "Máquinas da sala", icon: PG.laptop, options: true },
    ],
  },
];

/** Caixas de cada botão e de cada painel de grupo (margem 2 em volta, borda 1 e respiro 2 por dentro). */
export const PAINEL_TOOLBAR = (() => {
  const buttons = {} as Record<ButtonId, Box>;
  const panels = {} as Record<GroupId, Box>;
  let x = 4;
  for (const group of PAINEL_GROUPS) {
    const start = x;
    let bx = start + 3;
    for (const b of group.buttons) {
      const w = b.width ?? PAINEL.buttonW;
      buttons[b.id] = { x: bx, y: PAINEL.panelY + 3, w, h: PAINEL.buttonH };
      bx += w;
    }
    const w = bx + 3 - start;
    panels[group.id] = { x: start, y: PAINEL.panelY, w, h: PAINEL.panelH };
    x = start + w + 4;
  }
  return { buttons, panels };
})();

/** Onde o cursor mira num botão da barra: o ícone. */
export const painelButton = (id: ButtonId): Point => {
  const b = PAINEL_TOOLBAR.buttons[id];
  return { x: b.x + b.w / 2 + 3, y: b.y + 21 };
};

/* ---------- a grade ---------- */

/** Quinze máquinas, como as fotos do app (ALUNO-01, ALUNO-02...), cada uma com o que o aluno tem aberto. */
export const PAINEL_PCS: Pc[] = [
  { name: "ALUNO-01", kind: "code", variant: 0 },
  { name: "ALUNO-02", kind: "browser", variant: 0 },
  { name: "ALUNO-03", kind: "slides", variant: 0 },
  { name: "ALUNO-04", kind: "sheet", variant: 0 },
  { name: "ALUNO-05", kind: "blocks", variant: 0 },
  { name: "ALUNO-06", kind: "doc", variant: 0 },
  { name: "ALUNO-07", kind: "chart", variant: 0 },
  { name: "ALUNO-08", kind: "terminal", variant: 0 },
  { name: "ALUNO-09", kind: "paint", variant: 0 },
  { name: "ALUNO-10", kind: "browser", variant: 1 },
  { name: "ALUNO-11", kind: "code", variant: 1 },
  { name: "ALUNO-12", kind: "chart", variant: 1 },
  { name: "ALUNO-13", kind: "blocks", variant: 2 },
  { name: "ALUNO-14", kind: "slides", variant: 1 },
  { name: "ALUNO-15", kind: "doc", variant: 1 },
];
export const P_TOTAL = PAINEL_PCS.length;

/** ALUNO-09 e ALUNO-14 começam desligadas; "Ligar" acorda as duas. */
export const P_OFFLINE = [8, 13];

/** Linha da máquina embaixo da tela (bolinha, nome, estado) e o aviso de bloqueio de programas. */
export const P_NAME_LINE = 21;
export const P_BADGE_LINE = 17;

/** Tamanho da imagem de cada tela; "Visualizar todos" cabe as 15 em 5 colunas. */
export type PTileLayout = { cols: number; screenW: number };
export const P_FIT: PTileLayout = { cols: 5, screenW: 284 };
export const P_SMALL: PTileLayout = { cols: 6, screenW: 222 };

export const pScreenH = (screenW: number) => Math.round((screenW * 9) / 16);

/** Cartão (borda 1 e respiro 4) com a tela e a linha da máquina; margem 4 entre cartões. */
export const pTileBox = (index: number, layout: PTileLayout = P_FIT): Box => {
  const col = index % layout.cols;
  const row = Math.floor(index / layout.cols);
  const w = layout.screenW + 10;
  const h = pScreenH(layout.screenW) + 10 + P_NAME_LINE;
  return { x: 14 + col * (w + 8), y: P_GRID_TOP + 14 + row * (h + 8), w, h };
};

/** Centro da tela (imagem) de um cartão. */
export const pTileScreenCenter = (b: Box): Point => ({ x: b.x + b.w / 2, y: b.y + 5 + pScreenH(b.w - 10) / 2 });

/* ---------- a faixa de baixo ---------- */

/** Botões da faixa de baixo, alinhados à direita (Padding 14,7), medidos nas fotos. */
export const P_BOTTOM = (() => {
  const y = WIN.h - PAINEL.bottomBar;
  const cy = y + PAINEL.bottomBar / 2;
  const right = WIN.w - 14;
  const box = (x: number, w: number): Box => ({ x, y: cy - 11, w, h: 22 });
  const larger = box(right - 23, 23);
  const slider: Box = { x: larger.x - 6 - 140, y: cy - 9, w: 140, h: 18 };
  const smaller = box(slider.x - 6 - 23, 23);
  const onlineOnly = box(smaller.x - 12 - 71, 71);
  const fitAll = box(onlineOnly.x - 6 - 104, 104);
  const organize = box(fitAll.x - 12 - 98, 98);
  return { y, cy, larger, slider, smaller, onlineOnly, fitAll, organize };
})();

/** O controle de tamanho vai de 160 a 640 px de largura. */
export const pSliderValue = (screenW: number) => (screenW - 160) / (640 - 160);
export const pSliderKnob = (value: number): Point => ({ x: P_BOTTOM.slider.x + 8 + value * (P_BOTTOM.slider.w - 16), y: P_BOTTOM.cy });

/* ---------- janelas ---------- */

/** Altura da barra de título escura do Windows sobre cada janela de diálogo. */
export const P_CAPTION = 32;

/** Janela de diálogo (área do conteúdo w×h, como nas fotos), centrada na janela principal. */
export const pDialog = (w: number, h: number): Box => {
  const total = h + P_CAPTION;
  return { x: Math.round((WIN.w - w) / 2), y: Math.max(8, Math.round((WIN.h - total) / 2) + 6), w, h: total };
};

/** Ponto no meio de um controle (caixa relativa ao conteúdo, abaixo da barra de título). */
export const pCtrl = (dlg: Box, c: Box, dx = 0, dy = 0): Point => ({
  x: dlg.x + c.x + c.w / 2 + dx,
  y: dlg.y + P_CAPTION + c.y + c.h / 2 + dy,
});
