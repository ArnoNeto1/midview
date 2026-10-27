/**
 * Medidas da janela "Midview - Sala de aula" (src/Midview.Master/MainWindow.xaml),
 * em px de tela a 100%: barra de ferramentas em grupos no topo, grade de telas
 * e barra de baixo. Tudo aqui é coordenada interna da janela.
 */
export const WIN = { w: 1530, h: 820, titleBar: 32, toolbar: 94, bottomBar: 42, radius: 8 } as const;

export const GRID_TOP = WIN.titleBar + WIN.toolbar;
export const GRID_BOTTOM = WIN.h - WIN.bottomBar;

export type Box = { x: number; y: number; w: number; h: number };
export type Point = { x: number; y: number };

export const center = (b: Box): Point => ({ x: b.x + b.w / 2, y: b.y + b.h / 2 });

export type ButtonId =
  | "view"
  | "control"
  | "lock"
  | "message"
  | "chat"
  | "sendFile"
  | "collect"
  | "classFolder"
  | "openSite"
  | "openProgram"
  | "closeApps"
  | "blockApps"
  | "blockSites"
  | "powerOff"
  | "restart"
  | "wake"
  | "schedule"
  | "labPanel"
  | "machines";

export type GroupId = "tela" | "turma" | "programas" | "energia" | "configurar";

type ButtonDef = { id: ButtonId; label: string; icon: string; badge?: string; options?: boolean; width?: number };
type GroupDef = { id: GroupId; caption: string; buttons: ButtonDef[] };

/** Os botões reais, na ordem do XAML (Energia primeiro), com os ícones do Segoe Fluent Icons. */
export const TOOLBAR_GROUPS: GroupDef[] = [
  {
    id: "energia",
    caption: "Energia",
    buttons: [
      { id: "powerOff", label: "Desligar", icon: "", options: true },
      { id: "restart", label: "Reiniciar", icon: "", options: true },
      { id: "wake", label: "Ligar", icon: "" },
      { id: "schedule", label: "Agendar", icon: "", options: true },
    ],
  },
  {
    id: "tela",
    caption: "Tela",
    buttons: [
      { id: "view", label: "Ver a tela", icon: "" },
      { id: "control", label: "Controlar", icon: "" },
      { id: "lock", label: "Bloquear telas", icon: "", badge: "", width: 84 },
    ],
  },
  {
    id: "turma",
    caption: "Turma",
    buttons: [
      { id: "message", label: "Mensagem", icon: "", options: true },
      { id: "chat", label: "Bate-papo", icon: "" },
      { id: "sendFile", label: "Enviar arquivo", icon: "", options: true },
      { id: "collect", label: "Recolher arquivos", icon: "", options: true },
      { id: "classFolder", label: "Pasta da turma", icon: "", options: true, width: 96 },
      { id: "openSite", label: "Abrir site", icon: "", options: true },
      { id: "openProgram", label: "Abrir programa", icon: "", options: true },
    ],
  },
  {
    id: "programas",
    caption: "Programas e sites",
    buttons: [
      { id: "closeApps", label: "Fechar programas", icon: "", options: true },
      { id: "blockApps", label: "Bloquear programas", icon: "", badge: "", options: true },
      { id: "blockSites", label: "Bloquear sites", icon: "", badge: "", options: true },
    ],
  },
  {
    id: "configurar",
    caption: "Configurar",
    buttons: [
      { id: "labPanel", label: "Painel do laboratório", icon: "", options: true },
      { id: "machines", label: "Máquinas da sala", icon: "", options: true },
    ],
  },
];

const TB = { padX: 8, padTop: 2, buttonW: 74, buttonH: 67, separator: 13, captionH: 23 } as const;

/** Caixas de cada botão, de cada grupo (com a legenda embaixo) e dos separadores. */
export const TOOLBAR_LAYOUT = (() => {
  const buttons = {} as Record<ButtonId, Box>;
  const groups = {} as Record<GroupId, Box & { captionY: number }>;
  const separators: number[] = [];
  const y = WIN.titleBar + TB.padTop;
  let x = TB.padX;
  TOOLBAR_GROUPS.forEach((group, gi) => {
    const start = x;
    for (const b of group.buttons) {
      const w = b.width ?? TB.buttonW;
      buttons[b.id] = { x, y, w, h: TB.buttonH };
      x += w;
    }
    groups[group.id] = { x: start, y, w: x - start, h: TB.buttonH + TB.captionH, captionY: y + TB.buttonH };
    if (gi < TOOLBAR_GROUPS.length - 1) {
      separators.push(x + 6);
      x += TB.separator;
    }
  });
  return { buttons, groups, separators, y };
})();

/** Onde o cursor mira num botão da barra: o ícone. */
export const buttonTarget = (id: ButtonId): Point => {
  const b = TOOLBAR_LAYOUT.buttons[id];
  return { x: b.x + b.w / 2 + 2, y: b.y + 24 };
};

/** Grade "Visualizar todos" (4 colunas, todas cabem) e grade com telas menores. */
export type TileLayout = { cols: number; w: number };
export const LAYOUT_FIT: TileLayout = { cols: 4, w: 330 };
export const LAYOUT_SMALL: TileLayout = { cols: 5, w: 262 };

/** Espaço para os avisos embaixo da tela do card ("Tela bloqueada", "3 bloqueados"). */
export const BADGE_H = 20;

export const screenHeight = (w: number) => Math.round((w * 9) / 16);

/** Card na grade (cards com margem 4), com as colunas centradas na janela. */
export const tileBox = (index: number, layout: TileLayout): Box => {
  const col = index % layout.cols;
  const row = Math.floor(index / layout.cols);
  const h = screenHeight(layout.w) + BADGE_H;
  const left = Math.max(14, Math.round((WIN.w - (layout.cols * (layout.w + 8) - 8)) / 2));
  return { x: left + col * (layout.w + 8), y: GRID_TOP + 14 + row * (h + 8), w: layout.w, h };
};

/** A barra de baixo, alinhada à direita como no XAML. */
export const BOTTOM = (() => {
  const y = WIN.h - WIN.bottomBar;
  const cy = y + WIN.bottomBar / 2;
  const right = WIN.w - 14;
  const larger: Box = { x: right - 26, y: cy - 12, w: 26, h: 24 };
  const slider: Box = { x: larger.x - 6 - 140, y: cy - 10, w: 140, h: 20 };
  const smaller: Box = { x: slider.x - 6 - 26, y: cy - 12, w: 26, h: 24 };
  const onlineOnly: Box = { x: smaller.x - 12 - 80, y: cy - 12, w: 80, h: 24 };
  const fitAll: Box = { x: onlineOnly.x - 6 - 112, y: cy - 12, w: 112, h: 24 };
  const organize: Box = { x: fitAll.x - 6 - 112, y: cy - 12, w: 112, h: 24 };
  // "Alinhar" só aparece com "Organizar sala" ligado.
  const align: Box = { x: organize.x - 6 - 70, y: cy - 12, w: 70, h: 24 };
  return { y, cy, larger, slider, smaller, onlineOnly, fitAll, organize, align };
})();

/** O controle de tamanho vai de 160 a 640 px de largura (TileSize). */
export const sliderValue = (tileWidth: number) => (tileWidth - 160) / (640 - 160);
export const sliderKnob = (value: number): Point => ({ x: BOTTOM.slider.x + 7 + value * (BOTTOM.slider.w - 14), y: BOTTOM.cy });
