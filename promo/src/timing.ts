export const FPS = 30;
export const WIDTH = 1920;
export const HEIGHT = 1080;
export const TOTAL_FRAMES = 900;

/** Fronteiras do roteiro em frames absolutos: 0s · 5s · 12s · 20s · 25s · 30s. */
export const BOUNDARY = {
  hook: 0,
  dashboard: 150,
  actions: 360,
  benefits: 600,
  cta: 750,
  end: 900,
} as const;

/** Duração das transições (frames), centradas em cada fronteira. 0 = corte seco. */
export const TRANSITION = {
  hookToDashboard: 20,
  // As Cenas 2 e 3 usam a mesma câmera e o mesmo dashboard: o corte é invisível.
  dashboardToActions: 0,
  actionsToBenefits: 20,
  benefitsToCta: 20,
} as const;

const scene = (start: number, end: number, inFrames: number, outFrames: number) => ({
  from: start - inFrames / 2,
  durationInFrames: end - start + inFrames / 2 + outFrames / 2,
});

/**
 * Janela de cada cena em frames absolutos. Cada cena ganha metade da transição
 * de cada lado; somadas e descontadas as sobreposições, dão os 900 frames.
 */
export const SCENES = {
  hook: scene(BOUNDARY.hook, BOUNDARY.dashboard, 0, TRANSITION.hookToDashboard),
  dashboard: scene(BOUNDARY.dashboard, BOUNDARY.actions, TRANSITION.hookToDashboard, TRANSITION.dashboardToActions),
  actions: scene(BOUNDARY.actions, BOUNDARY.benefits, TRANSITION.dashboardToActions, TRANSITION.actionsToBenefits),
  benefits: scene(BOUNDARY.benefits, BOUNDARY.cta, TRANSITION.actionsToBenefits, TRANSITION.benefitsToCta),
  cta: scene(BOUNDARY.cta, BOUNDARY.end, TRANSITION.benefitsToCta, 0),
} as const;
