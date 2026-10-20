import { TransitionSeries, linearTiming } from "@remotion/transitions";
import React from "react";
import { AbsoluteFill } from "remotion";
import { AppThemeProvider, type AppThemeName } from "./components/app/appTheme";
import { Background } from "./components/Background";
import { EASE } from "./lib/motion";
import { Scene1Hook } from "./scenes/Scene1Hook";
import { Scene8Beneficios } from "./scenes/Scene8Beneficios";
import { Scene9Encerramento } from "./scenes/Scene9Encerramento";
import { Scene2Sala, Scene3Tela, Scene4Turma, Scene5Programas, Scene6Energia, Scene7Configurar } from "./scenes/TourScenes";
import { COLORS } from "./theme";
import { SCENES, TRANSITION } from "./timing";
import { zoomBlur } from "./transitions/zoomBlur";

const timing = (durationInFrames: number) => linearTiming({ durationInFrames, easing: EASE.inOut });

export type MidviewPromoProps = {
  /** "real": as cores do app (fundo claro, barra cor de lousa). "escuro": o estilo escuro do vídeo. */
  theme: AppThemeName;
};

/** Vídeo completo: 2700 frames (1m30s) a 30 fps, 1920×1080. O fundo fica fixo e as cenas passam sobre ele. */
export const MidviewPromo: React.FC<MidviewPromoProps> = ({ theme }) => (
  <AppThemeProvider name={theme}>
    <AbsoluteFill className="font-sans text-white" style={{ backgroundColor: COLORS.bg }}>
      <Background />
      <TransitionSeries>
        <TransitionSeries.Sequence durationInFrames={SCENES.hook.durationInFrames} name="1 · Gancho + logo">
          <Scene1Hook />
        </TransitionSeries.Sequence>
        <TransitionSeries.Transition presentation={zoomBlur()} timing={timing(TRANSITION.hookToSala)} />
        <TransitionSeries.Sequence durationInFrames={SCENES.sala.durationInFrames} name="2 · Sala de aula">
          <Scene2Sala />
        </TransitionSeries.Sequence>
        {/* Cenas 2 a 7: cortes secos, a mesma janela segue em uso. */}
        <TransitionSeries.Sequence durationInFrames={SCENES.tela.durationInFrames} name="3 · Tela">
          <Scene3Tela />
        </TransitionSeries.Sequence>
        <TransitionSeries.Sequence durationInFrames={SCENES.turma.durationInFrames} name="4 · Turma">
          <Scene4Turma />
        </TransitionSeries.Sequence>
        <TransitionSeries.Sequence durationInFrames={SCENES.programas.durationInFrames} name="5 · Programas e sites">
          <Scene5Programas />
        </TransitionSeries.Sequence>
        <TransitionSeries.Sequence durationInFrames={SCENES.energia.durationInFrames} name="6 · Energia">
          <Scene6Energia />
        </TransitionSeries.Sequence>
        <TransitionSeries.Sequence durationInFrames={SCENES.configurar.durationInFrames} name="7 · Configurar">
          <Scene7Configurar />
        </TransitionSeries.Sequence>
        <TransitionSeries.Transition presentation={zoomBlur()} timing={timing(TRANSITION.configurarToBeneficios)} />
        <TransitionSeries.Sequence durationInFrames={SCENES.beneficios.durationInFrames} name="8 · Benefícios">
          <Scene8Beneficios />
        </TransitionSeries.Sequence>
        <TransitionSeries.Transition presentation={zoomBlur()} timing={timing(TRANSITION.beneficiosToCta)} />
        <TransitionSeries.Sequence durationInFrames={SCENES.cta.durationInFrames} name="9 · Encerramento">
          <Scene9Encerramento />
        </TransitionSeries.Sequence>
      </TransitionSeries>
    </AbsoluteFill>
  </AppThemeProvider>
);
