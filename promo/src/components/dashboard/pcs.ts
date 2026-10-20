import type { ScreenKind } from "./ScreenContent";

export type Pc = { id: string; student: string; kind: ScreenKind; variant: number };

/** As 12 estações do laboratório, cada uma com um app diferente aberto. */
export const PCS: Pc[] = [
  { id: "PC-01", student: "Ana", kind: "code", variant: 0 },
  { id: "PC-02", student: "Bruno", kind: "browser", variant: 0 },
  { id: "PC-03", student: "Carla", kind: "slides", variant: 0 },
  { id: "PC-04", student: "Diego", kind: "sheet", variant: 0 },
  { id: "PC-05", student: "Elisa", kind: "blocks", variant: 0 },
  { id: "PC-06", student: "Felipe", kind: "doc", variant: 0 },
  { id: "PC-07", student: "Gabi", kind: "chart", variant: 0 },
  { id: "PC-08", student: "Heitor", kind: "terminal", variant: 0 },
  { id: "PC-09", student: "Isabela", kind: "paint", variant: 0 },
  { id: "PC-10", student: "João", kind: "browser", variant: 1 },
  { id: "PC-11", student: "Karina", kind: "code", variant: 1 },
  { id: "PC-12", student: "Lucas", kind: "chart", variant: 1 },
];
