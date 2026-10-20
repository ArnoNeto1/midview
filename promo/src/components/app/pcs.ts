import type { ScreenKind } from "./ScreenContent";

/** Uma máquina da sala: o nome que aparece no card e o que o aluno tem aberto. */
export type Pc = { name: string; kind: ScreenKind; variant: number };

export const PCS: Pc[] = [
  { name: "PC-01", kind: "code", variant: 0 },
  { name: "PC-02", kind: "browser", variant: 0 },
  { name: "PC-03", kind: "slides", variant: 0 },
  { name: "PC-04", kind: "sheet", variant: 0 },
  { name: "PC-05", kind: "blocks", variant: 0 },
  { name: "PC-06", kind: "doc", variant: 0 },
  { name: "PC-07", kind: "chart", variant: 0 },
  { name: "PC-08", kind: "terminal", variant: 0 },
  { name: "PC-09", kind: "paint", variant: 0 },
  { name: "PC-10", kind: "browser", variant: 1 },
  { name: "PC-11", kind: "code", variant: 1 },
  { name: "PC-12", kind: "chart", variant: 1 },
];

/** PC-11 e PC-12 começam desligadas; o "Ligar" da Energia acorda as duas. */
export const OFFLINE_AT_START = [10, 11];
