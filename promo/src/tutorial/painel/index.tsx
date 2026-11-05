import { Scene9Encerramento } from "../../scenes/Scene9Encerramento";
import { idlePainelApp } from "../../painel/world";
import { INICIO } from "../cenas/inicio";
import { makeTimeline } from "../engine";
import { FALAS, type Fala } from "../falas";
import type { Beat } from "../kit";
import { CONFIGURAR_P } from "./configurar";
import { ENERGIA_TELA_P } from "./energiaTela";
import { MENU_P } from "./menu";
import { PROGRAMAS_P } from "./programas";
import { SALA_P } from "./sala";
import { TURMA_P } from "./turma";

/**
 * O tutorial completo no visual Painel (0.0.41): as mesmas falas gravadas do
 * tutorial, com a janela e as janelas novas. Abrir site e Abrir programa
 * passaram para o grupo "Programas e sites" da barra, e "Instalar programa",
 * que ganhou botão na barra, entra num trecho sem fala (só legenda).
 */
const INSTALAR: Fala = { id: "p01", chapter: "programas", caption: "Instale programas na turma toda", say: "", silent: true };

export const FALAS_PAINEL: Fala[] = FALAS.flatMap((f) => {
  if (f.id === "t35" || f.id === "t36") {
    const moved = { ...f, chapter: "programas" as const };
    return f.id === "t36" ? [moved, INSTALAR] : [moved];
  }
  return [f];
});

export const BEATS_PAINEL: Record<string, Beat> = {
  // A abertura e o instalador não mudaram.
  t01: INICIO.t01,
  t02: INICIO.t02,
  t03: INICIO.t03,
  t04: INICIO.t04,
  t05: INICIO.t05,
  ...SALA_P,
  ...ENERGIA_TELA_P,
  ...TURMA_P,
  ...PROGRAMAS_P,
  ...CONFIGURAR_P,
  ...MENU_P,
  t56: { hideCursor: true, min: 250, draw: () => ({ custom: <Scene9Encerramento app={(abs) => idlePainelApp(abs, false)} startsAt={0} /> }) },
};

export const PAINEL_TUTORIAL = makeTimeline(FALAS_PAINEL, BEATS_PAINEL);
