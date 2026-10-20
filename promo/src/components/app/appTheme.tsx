import React, { createContext, useContext } from "react";
import { FONTS } from "../../fonts";

export type AppThemeName = "real" | "escuro";

/** Cores e fontes da janela do Midview no vídeo. */
export type AppTheme = {
  name: AppThemeName;
  font: string;
  iconFont: string;
  window: { bg: string; line: string; shadow: string };
  titleBar: { bg: string; text: string; muted: string };
  toolbar: { bg: string; hover: string; pressed: string; line: string; text: string; muted: string; disabled: string; badge: string };
  canvas: string;
  card: { bg: string; line: string; hover: string; screen: string };
  ink: string;
  inkMuted: string;
  selected: { line: string; fill: string };
  alert: string;
  /** Texto de status sobre a tela escura do card ("Conectando..."). */
  chalk: string;
  bar: { bg: string; line: string };
  button: { bg: string; line: string; text: string; hover: string; pressed: string };
  toggleOn: { bg: string; line: string };
  slider: { track: string; thumb: string; thumbLine: string };
  dialog: { bg: string; panel: string; title: string; text: string; muted: string; line: string; input: string; check: string };
  menu: { bg: string; line: string; text: string; hover: string; muted: string };
  /** Destaque do vídeo (não existe no app) para apontar o grupo da barra em uso. */
  spotlight: string;
};

const SEGOE = '"Segoe UI Variable Text", "Segoe UI", system-ui, sans-serif';
const ICONS = '"Segoe Fluent Icons", "Segoe MDL2 Assets"';

export const APP_THEMES: Record<AppThemeName, AppTheme> = {
  // As cores de src/Midview.Master/App.xaml: barra cor de lousa, amarelo-giz na
  // seleção e um fundo claro para as telas dos alunos aparecerem.
  real: {
    name: "real",
    font: SEGOE,
    iconFont: ICONS,
    window: { bg: "#EEF1F0", line: "rgba(255,255,255,0.14)", shadow: "0 50px 120px -30px rgba(0,0,0,0.85)" },
    titleBar: { bg: "#F7F9F8", text: "#1F2A2E", muted: "#5B6B6F" },
    toolbar: {
      bg: "#1F2A2E",
      hover: "#2C3A3F",
      pressed: "#364850",
      line: "#33434A",
      text: "#E6ECEA",
      muted: "#93A3A6",
      disabled: "#5F6E72",
      badge: "#F2C14E",
    },
    canvas: "#EEF1F0",
    card: { bg: "#FFFFFF", line: "#D3DBDA", hover: "#9BAAAD", screen: "#111A1D" },
    ink: "#1F2A2E",
    inkMuted: "#5B6B6F",
    selected: { line: "#D99A1E", fill: "#FFF6DC" },
    alert: "#B3261E",
    chalk: "#E6ECEA",
    bar: { bg: "#FFFFFF", line: "#D3DBDA" },
    button: { bg: "#FFFFFF", line: "#D3DBDA", text: "#1F2A2E", hover: "#EEF1F0", pressed: "#D3DBDA" },
    toggleOn: { bg: "#FFF6DC", line: "#D99A1E" },
    slider: { track: "#C9D2D0", thumb: "#FFFFFF", thumbLine: "#9BAAAD" },
    dialog: {
      bg: "#EEF1F0",
      panel: "#FFFFFF",
      title: "#F7F9F8",
      text: "#1F2A2E",
      muted: "#5B6B6F",
      line: "#D3DBDA",
      input: "#FFFFFF",
      check: "#1F2A2E",
    },
    menu: { bg: "#FFFFFF", line: "#D3DBDA", text: "#1F2A2E", hover: "#EEF1F0", muted: "#93A3A6" },
    spotlight: "#F2C14E",
  },
  // O estilo escuro do vídeo (vidro, ciano), com a mesma estrutura do app.
  escuro: {
    name: "escuro",
    font: FONTS.ui,
    iconFont: ICONS,
    window: { bg: "#0B101C", line: "rgba(255,255,255,0.1)", shadow: "0 50px 120px -30px rgba(0,0,0,0.9)" },
    titleBar: { bg: "#0E1422", text: "#CBD5E1", muted: "#64748B" },
    toolbar: {
      bg: "#111A2C",
      hover: "#1A2540",
      pressed: "#22304F",
      line: "rgba(255,255,255,0.08)",
      text: "#E2E8F0",
      muted: "#64748B",
      disabled: "#475569",
      badge: "#22D3EE",
    },
    canvas: "#0B101C",
    card: { bg: "#0F172A", line: "rgba(255,255,255,0.1)", hover: "rgba(255,255,255,0.3)", screen: "#0A0F1C" },
    ink: "#E2E8F0",
    inkMuted: "#94A3B8",
    selected: { line: "#22D3EE", fill: "rgba(34,211,238,0.12)" },
    alert: "#F87171",
    chalk: "#CBD5E1",
    bar: { bg: "#0E1422", line: "rgba(255,255,255,0.08)" },
    button: {
      bg: "rgba(255,255,255,0.04)",
      line: "rgba(255,255,255,0.14)",
      text: "#E2E8F0",
      hover: "rgba(255,255,255,0.09)",
      pressed: "rgba(255,255,255,0.15)",
    },
    toggleOn: { bg: "rgba(34,211,238,0.15)", line: "#22D3EE" },
    slider: { track: "rgba(255,255,255,0.16)", thumb: "#E2E8F0", thumbLine: "rgba(255,255,255,0.4)" },
    dialog: {
      bg: "#0E1422",
      panel: "#131D31",
      title: "#0B111E",
      text: "#E2E8F0",
      muted: "#94A3B8",
      line: "rgba(255,255,255,0.1)",
      input: "#0B111E",
      check: "#22D3EE",
    },
    menu: { bg: "#111827", line: "rgba(255,255,255,0.1)", text: "#E2E8F0", hover: "rgba(255,255,255,0.07)", muted: "#64748B" },
    spotlight: "#22D3EE",
  },
};

const AppThemeContext = createContext<AppTheme>(APP_THEMES.real);

export const AppThemeProvider: React.FC<{ name: AppThemeName; children: React.ReactNode }> = ({ name, children }) => (
  <AppThemeContext.Provider value={APP_THEMES[name]}>{children}</AppThemeContext.Provider>
);

export const useAppTheme = () => useContext(AppThemeContext);
