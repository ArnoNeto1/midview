import {
  LAB,
  LAB_CTRL,
  LAB_SCROLL_MAX,
  LabPanelDialog,
  MACHINES,
  MACHINES_CTRL,
  MachinesDialog,
  UPDATE,
  UPDATE_CTRL,
  UpdateDialog,
  labLowerButton,
} from "../../components/app/painel/dialogs/configurar";
import { PAINEL_PCS } from "../../components/app/painel/geometry";
import { ramp } from "../../lib/motion";
import { hov, sp, tap, win, type Beat } from "../kit";
import { OFF_LATE, pbottom, pbutton, pdialogFocus, pgrid, pgroupFocus, pmid, pspot, ptoolbar } from "./kit";

const OFF = OFF_LATE;
const BROWSER_MSG = "O professor atualizou as configurações do navegador; o Chrome foi fechado para aplicar. Pode abri-lo de novo.";
const badges = () => pgrid({ offline: OFF, each: () => ({ blockBadge: 1 }) });
const bar = (t: number, d: number, extra: Parameters<typeof ptoolbar>[0] = {}) => ptoolbar(extra, false, pspot("configurar", t, d));
const MODO_VISITANTE = { x: 364, y: 621, w: 93, h: 24 };
const lower = (box: { x: number; y: number; w: number; h: number }, scroll: number) => pmid(LAB, { ...box, y: box.y - scroll });

/** As linhas da atualização pela rede: uma máquina primeiro, as outras depois que ela confirma. */
const updateLines = (t: number) =>
  PAINEL_PCS.filter((_, i) => !OFF.includes(i)).map((pc, i) => {
    if (i === 0) {
      if (t < 92) return { name: pc.name, text: "• verificando..." };
      if (t < 130) return { name: pc.name, text: `• enviando ${Math.min(100, Math.round(ramp(t, 92, 128) * 100))}%` };
      if (t < 150) return { name: pc.name, text: "• recebida; conferindo..." };
      if (t < 172) return { name: pc.name, text: "• reiniciando..." };
      return { name: pc.name, text: "✓ atualizada (0.0.41)", ok: true };
    }
    const start = 182 + i * 5;
    if (t < start) return { name: pc.name, text: "• verificando..." };
    if (t < start + 30) return { name: pc.name, text: `• enviando ${Math.round(ramp(t, start, start + 30) * 100)}%` };
    return { name: pc.name, text: "✓ atualizada (0.0.41)", ok: true };
  });

export const CONFIGURAR_P: Record<string, Beat> = {
  // "Painel do laboratório".
  t42: {
    moves: [{ at: 30, to: pbutton("labPanel"), arc: -30 }],
    clicks: [80],
    cams: [
      { at: 0, rect: pgroupFocus("configurar") },
      { at: 86, rect: pdialogFocus(LAB, 30) },
    ],
    draw: (t, d) => ({
      split: 1 - sp(t, 4, 18),
      student: { pc: 2, browser: { p: 1, url: "youtube.com", blocked: true } },
      app: {
        tiles: badges(),
        toolbar: bar(t, d, { labPanel: { hover: hov(t, 64, 92), press: tap(t, 80) } }),
        bottom: pbottom(),
        overlay: <LabPanelDialog s={{ appear: sp(t, 82, 12) }} />,
      },
    }),
  },

  // Os cinco passos.
  t43: {
    moves: [0, 1, 2, 3, 4].map((i) => ({ at: 30 + i * 52, to: pmid(LAB, LAB_CTRL.on(i)), arc: 10 })),
    clicks: [0, 1, 2, 3, 4].map((i) => 66 + i * 52),
    cams: [{ at: 0, rect: pdialogFocus(LAB, 30) }],
    draw: (t) => {
      const current = [0, 1, 2, 3, 4].findIndex((i) => t < 66 + i * 52 + 10);
      const step = current < 0 ? 4 : current;
      return {
        split: sp(t, 80, 18) * (1 - sp(t, 320, 18)),
        student: { pc: 2, apps: 1, msgbox: { p: win(t, 96, 300, 10, 10), text: BROWSER_MSG } },
        app: {
          tiles: badges(),
          toolbar: ptoolbar(),
          bottom: pbottom(),
          overlay: <LabPanelDialog s={{ appear: 1, hoverStep: step, hover: current < 0 ? 0 : hov(t, 66 + step * 52 - 10, 66 + step * 52 + 6), press: tap(t, 66 + step * 52) }} />,
        },
      };
    },
  },

  // Bloqueios extras e manutenção.
  t44: (() => {
    const SCROLL = LAB_SCROLL_MAX;
    const mid = 300;
    return {
      moves: [
        { at: 96, to: lower(MODO_VISITANTE, mid), arc: 20 },
        { at: 170, to: lower(labLowerButton("RESET geral"), SCROLL), arc: 20 },
      ],
      cams: [{ at: 0, rect: pdialogFocus(LAB, 30) }],
      draw: (t: number) => {
        const scroll = ramp(t, 20, 80) * mid + ramp(t, 140, 190) * (SCROLL - mid);
        return {
          app: {
            tiles: badges(),
            toolbar: ptoolbar(),
            bottom: pbottom(),
            overlay: (
              <LabPanelDialog
                s={{ appear: 1 - sp(t, 254, 10), scroll, hoverLower: t > 110 && t < 150 ? "Modo visitante" : t >= 196 && t < 250 ? "RESET geral" : null }}
              />
            ),
          },
        };
      },
    } satisfies Beat;
  })(),

  // "Máquinas da sala" > "Atualizações" > "Atualizar as máquinas pela rede...".
  t45: {
    moves: [
      { at: 10, to: pbutton("machines"), arc: -30 },
      { at: 70, to: pmid(MACHINES, MACHINES_CTRL.nav(1)), arc: 30 },
      { at: 120, to: pmid(MACHINES, MACHINES_CTRL.update), arc: 30 },
    ],
    clicks: [44, 96, 156],
    cams: [
      { at: 0, rect: pgroupFocus("configurar") },
      { at: 48, rect: pdialogFocus(MACHINES, 30) },
      { at: 164, rect: pdialogFocus(UPDATE, 40) },
    ],
    draw: (t, d) => ({
      app: {
        tiles: badges(),
        toolbar: bar(t, d, { machines: { hover: hov(t, 30, 56), press: tap(t, 44) } }),
        bottom: pbottom(),
        overlay: (
          <>
            <MachinesDialog s={{ appear: sp(t, 46, 12), page: t >= 97 ? 1 : 0, navHover: hov(t, 84, 100), updateHover: hov(t, 140, 166), updatePress: tap(t, 156) }} />
            <UpdateDialog s={{ appear: sp(t, 160, 12), progress: 0, step: "", lines: [] }} />
          </>
        ),
      },
    }),
  },

  // Uma máquina primeiro; as outras depois.
  t46: {
    moves: [{ at: 4, to: pmid(UPDATE, UPDATE_CTRL.run), arc: 30 }],
    clicks: [34],
    cams: [{ at: 0, rect: pdialogFocus(UPDATE, 40) }],
    draw: (t) => {
      const step =
        t < 36
          ? ""
          : t < 64
            ? "Verificando as versões..."
            : t < 88
              ? "Preparando o pacote..."
              : t < 176
                ? "Atualizando a primeira máquina..."
                : t < 262
                  ? "Atualizando as outras máquinas..."
                  : `Terminado. ${PAINEL_PCS.length - OFF.length} de ${PAINEL_PCS.length - OFF.length} máquina(s) nesta versão.`;
      return {
        app: {
          tiles: badges(),
          toolbar: ptoolbar(),
          bottom: pbottom(),
          overlay: (
            <>
              <MachinesDialog s={{ appear: 1, page: 1 }} />
              <UpdateDialog s={{ appear: 1, progress: 0, runHover: hov(t, 20, 46), runPress: tap(t, 34), step, lines: t < 40 ? [] : updateLines(t) }} />
            </>
          ),
        },
      };
    },
  },
};
