import { LAB, LAB_CTRL, LabPanelDialog, MACHINES, MACHINES_CTRL, MachinesDialog, UPDATE, UpdateDialog } from "../../components/app/painel/dialogs/configurar";
import { EASE, ramp } from "../../lib/motion";
import type { Beat } from "../engine";
import { FULL, btn, ctrl, dlgTop, focus, groupFocus, hov, tap, toolbarClick, win } from "../kit";
import { promoVoice } from "./comum";

/** Painel do laboratório: os passos do início da aula, um clique em cada "Ativar". */
const STEP_CLICKS = [38, 52, 66];
export const PAINEL_LAB: Beat = {
  id: "painelLab",
  dur: 95,
  group: "configurar",
  caption: "Prepare o laboratório num painel só.",
  voice: promoVoice("16", 6, 78, "Prepare o laboratório num painel só."),
  moves: [
    { at: 0, to: btn("labPanel"), arc: -40 },
    ...STEP_CLICKS.map((c, i) => ({ at: c - (i === 0 ? 14 : 10), to: ctrl(LAB, LAB_CTRL.on(i)), arc: i === 0 ? 30 : 0 })),
  ],
  clicks: [18, ...STEP_CLICKS],
  events: { appsReopen: 10 },
  cams: [
    { at: 0, rect: groupFocus("configurar") },
    { at: 22, rect: dlgTop(LAB, 540) },
  ],
  draw: (t) => {
    const step = STEP_CLICKS.findIndex((c) => t < c + 8);
    return {
      buttons: toolbarClick(t, "labPanel", 18),
      overlay: (
        <LabPanelDialog
          s={{
            appear: win(t, 20, 84),
            hoverStep: step < 0 ? 2 : step,
            hover: step < 0 ? 0 : hov(t, STEP_CLICKS[step] - 6, STEP_CLICKS[step] + 6),
            press: step < 0 ? 0 : tap(t, STEP_CLICKS[step]),
          }}
        />
      ),
    };
  },
};

/** Máquinas da sala: a página Atualizações e a atualização pela rede. */
export const MAQUINAS: Beat = {
  id: "maquinas",
  dur: 110,
  group: "configurar",
  caption: "Atualize todas as máquinas pela rede.",
  voice: promoVoice("17", 4, 100, "Atualize o Midview em todas as máquinas."),
  moves: [
    { at: 0, to: btn("machines"), arc: -40 },
    { at: 24, to: ctrl(MACHINES, MACHINES_CTRL.nav(1)), arc: 30 },
    { at: 40, to: ctrl(MACHINES, MACHINES_CTRL.update), arc: 20 },
  ],
  clicks: [18, 36, 54],
  cams: [
    { at: 0, rect: groupFocus("configurar") },
    { at: 22, rect: focus(MACHINES, 50, 30) },
    { at: 58, rect: focus(UPDATE, 50, 20) },
    { at: 96, rect: FULL },
  ],
  draw: (t) => ({
    buttons: toolbarClick(t, "machines", 18),
    overlay: (
      <>
        <MachinesDialog s={{ appear: win(t, 20, 98), page: t >= 37 ? 1 : 0, navHover: hov(t, 30, 40), updateHover: hov(t, 48, 60), updatePress: tap(t, 54) }} />
        <UpdateDialog s={{ appear: win(t, 56, 98), progress: ramp(t, 60, 92, EASE.inOut) }} />
      </>
    ),
  }),
};
