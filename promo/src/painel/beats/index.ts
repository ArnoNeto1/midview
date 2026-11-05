import type { Beat } from "../engine";
import { PAINEL_LAB, MAQUINAS } from "./configurar";
import { AGENDAR, DESLIGAR, LIGAR, SALA } from "./energia";
import { ABRIR_PROGRAMA, ABRIR_SITE, BLOQUEAR_PROGRAMAS, BLOQUEAR_SITES, FECHAR, INSTALAR } from "./programas";
import { BLOQUEAR, CONTROLAR, VER } from "./tela";
import { ARQUIVOS, BATEPAPO, MENSAGEM, PASTA } from "./turma";

/**
 * O tour do vídeo MidviewPromoPainel: a sala e todos os botões da barra, na
 * ordem dos grupos (Energia, Tela, Turma, Programas e sites, Configurar).
 */
export const PROMO_BEATS: Beat[] = [
  SALA,
  LIGAR,
  DESLIGAR,
  AGENDAR,
  VER,
  CONTROLAR,
  BLOQUEAR,
  MENSAGEM,
  BATEPAPO,
  ARQUIVOS,
  PASTA,
  ABRIR_SITE,
  ABRIR_PROGRAMA,
  INSTALAR,
  BLOQUEAR_PROGRAMAS,
  BLOQUEAR_SITES,
  FECHAR,
  PAINEL_LAB,
  MAQUINAS,
];
