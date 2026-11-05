import type { GroupId } from "../geometry";

/**
 * O visual "Painel" do programa do professor (versão 0.0.41), com as cores de
 * src/Midview.Master/Themes/Painel.Colors.xaml e Painel.Main.xaml.
 */
export const P = {
  bg: "#0A111E",
  surface: "#0F1828",
  card: "#141F33",
  line: "#22314D",
  lineHover: "#3A4D70",
  text: "#DCE6F5",
  muted: "#8394AE",
  disabled: "#4A5872",
  accent: "#2DD4BF",
  onAccent: "#04221D",
  accentText: "#7FF0DF",
  accentSoft: "rgba(45,212,191,0.14)",
  error: "#FF8A80",
  warning: "#FFC53D",
  online: "#3DDC97",
  offline: "#4A5872",
  dangerSoft: "rgba(255,107,107,0.16)",
  toolbarHover: "#1F2C45",
  toolbarPressed: "#22314D",
  tileSelected: "#132A33",
  closeHover: "#C42B1C",
} as const;

/** Uma cor por grupo da barra de botões. */
export const GROUP_COLOR: Record<GroupId, string> = {
  energia: "#FF6B6B",
  tela: "#4DA3FF",
  turma: "#FFC53D",
  programas: "#3DDC97",
  configurar: "#A78BFA",
};

/** Bahnschrift vem com o Windows 10 1709 ou mais novo; Segoe UI de reserva, como no app. */
export const PFONT = 'Bahnschrift, "Segoe UI", system-ui, sans-serif';
export const PICONS = '"Segoe Fluent Icons", "Segoe MDL2 Assets"';

/** Glifos do Segoe Fluent Icons usados nas janelas do Painel. */
export const PG = {
  power: "",
  restart: "",
  wake: "",
  calendar: "",
  eye: "",
  control: "",
  monitor: "",
  lock: "",
  unlock: "",
  message: "",
  chat: "",
  upload: "",
  download: "",
  syncFolder: "",
  globe: "",
  console: "",
  box: "",
  trash: "",
  cancel: "",
  apps: "",
  block: "",
  settings: "",
  laptop: "",
  sync: "",
  chevronDown: "",
  minimize: "",
  maximize: "",
  close: "",
  zoomOut: "",
  zoomIn: "",
  check: "",
  search: "",
  people: "",
  folder: "",
} as const;
