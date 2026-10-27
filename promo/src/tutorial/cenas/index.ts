import type { Beat } from "../kit";
import { INICIO } from "./inicio";
import { ENERGIA_TELA } from "./energiaTela";
import { MENU_PROFESSORES } from "./menuProfessores";
import { PROGRAMAS_CONFIG } from "./programasConfig";
import { SALA } from "./sala";
import { TURMA } from "./turma";

/** As cenas de cada fala do tutorial (src/tutorial/falas.ts), por id. */
export const BEATS: Record<string, Beat> = { ...INICIO, ...SALA, ...ENERGIA_TELA, ...TURMA, ...PROGRAMAS_CONFIG, ...MENU_PROFESSORES };
