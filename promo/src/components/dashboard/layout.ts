/** Medidas do mock da janela do midview, em px (coordenadas internas da janela). */
export const WINDOW = {
  width: 1188,
  titleBar: 46,
  rail: 68,
  header: 76,
  padX: 24,
  footer: 92,
  radius: 18,
} as const;

/** Miniaturas 16:10 em grade 4×3. */
export const CARD = { w: 256, h: 160, gap: 16, cols: 4, rows: 3 } as const;

export const GRID = {
  left: WINDOW.rail + WINDOW.padX,
  top: WINDOW.titleBar + WINDOW.header,
  width: CARD.w * CARD.cols + CARD.gap * (CARD.cols - 1),
  height: CARD.h * CARD.rows + CARD.gap * (CARD.rows - 1),
} as const;

export const WINDOW_HEIGHT = GRID.top + GRID.height + WINDOW.footer;

/** Posição da janela na tela nas Cenas 2 e 3 (a coluna da esquerda fica para o texto). */
export const STAGE = { x: 668, y: (1080 - WINDOW_HEIGHT) / 2 } as const;

/** Coluna de texto à esquerda nas Cenas 2 e 3. */
export const CAPTION = { x: 100, width: 500 } as const;

export type Point = { x: number; y: number };
export type Rect = { x: number; y: number; w: number; h: number; cx: number; cy: number };

export const cardRect = (index: number): Rect => {
  const col = index % CARD.cols;
  const row = Math.floor(index / CARD.cols);
  const x = GRID.left + col * (CARD.w + CARD.gap);
  const y = GRID.top + row * (CARD.h + CARD.gap);
  return { x, y, w: CARD.w, h: CARD.h, cx: x + CARD.w / 2, cy: y + CARD.h / 2 };
};

export type CommandId = "lock" | "filter" | "cast";

const ORDER: CommandId[] = ["lock", "filter", "cast"];
const WIDTHS: Record<CommandId, number> = { lock: 190, filter: 158, cast: 196 };

/** Barra de comandos flutuante (Cena 3), centrada na faixa abaixo da grade. */
export const COMMAND_BAR = (() => {
  const pad = 8;
  const gap = 6;
  const height = 62;
  const iconW = 46;
  const iconCount = 2;
  const buttons = ORDER.reduce((sum, id) => sum + WIDTHS[id], 0) + gap * (ORDER.length - 1);
  const width = pad * 2 + buttons + gap + 1 + gap + iconW * iconCount + gap * (iconCount - 1);
  const cx = GRID.left + GRID.width / 2;
  const cy = GRID.top + GRID.height + WINDOW.footer / 2;
  const left = cx - width / 2;
  const center = {} as Record<CommandId, Point>;
  let x = left + pad;
  for (const id of ORDER) {
    center[id] = { x: x + WIDTHS[id] / 2, y: cy };
    x += WIDTHS[id] + gap;
  }
  return { left, top: cy - height / 2, width, height, pad, gap, iconW, cx, cy, order: ORDER, widths: WIDTHS, center };
})();

/** Prévia "Sua tela" que abre sobre o botão Transmitir Tela; a onda nasce no centro dela. */
export const POPOVER = (() => {
  const w = 236;
  const h = 182;
  const left = COMMAND_BAR.center.cast.x - w / 2;
  const top = COMMAND_BAR.top - 16 - h;
  return { w, h, left, top, center: { x: left + w / 2, y: top + h / 2 } };
})();
