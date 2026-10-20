export const FPS = 30;
export const WIDTH = 1920;
export const HEIGHT = 1080;
/** 1m30s. */
export const TOTAL_FRAMES = 2700;

/** Fronteiras do roteiro em frames absolutos. */
export const BOUNDARY = {
  hook: 0,
  sala: 180,
  tela: 480,
  turma: 840,
  programas: 1320,
  energia: 1680,
  configurar: 1980,
  beneficios: 2220,
  cta: 2460,
  end: 2700,
} as const;

/** Duração das transições (frames), centradas em cada fronteira. 0 = corte seco. */
export const TRANSITION = {
  hookToSala: 20,
  // Cenas 2 a 7 são o mesmo app em uso, com a mesma câmera: os cortes não aparecem.
  tour: 0,
  configurarToBeneficios: 20,
  beneficiosToCta: 20,
} as const;

const scene = (start: number, end: number, inFrames: number, outFrames: number) => ({
  from: start - inFrames / 2,
  durationInFrames: end - start + inFrames / 2 + outFrames / 2,
});

/**
 * Janela de cada cena em frames absolutos. Cada cena ganha metade da transição
 * de cada lado; somadas e descontadas as sobreposições, dão os 2700 frames.
 */
export const SCENES = {
  hook: scene(BOUNDARY.hook, BOUNDARY.sala, 0, TRANSITION.hookToSala),
  sala: scene(BOUNDARY.sala, BOUNDARY.tela, TRANSITION.hookToSala, TRANSITION.tour),
  tela: scene(BOUNDARY.tela, BOUNDARY.turma, TRANSITION.tour, TRANSITION.tour),
  turma: scene(BOUNDARY.turma, BOUNDARY.programas, TRANSITION.tour, TRANSITION.tour),
  programas: scene(BOUNDARY.programas, BOUNDARY.energia, TRANSITION.tour, TRANSITION.tour),
  energia: scene(BOUNDARY.energia, BOUNDARY.configurar, TRANSITION.tour, TRANSITION.tour),
  configurar: scene(BOUNDARY.configurar, BOUNDARY.beneficios, TRANSITION.tour, TRANSITION.configurarToBeneficios),
  beneficios: scene(BOUNDARY.beneficios, BOUNDARY.cta, TRANSITION.configurarToBeneficios, TRANSITION.beneficiosToCta),
  cta: scene(BOUNDARY.cta, BOUNDARY.end, TRANSITION.beneficiosToCta, 0),
} as const;
