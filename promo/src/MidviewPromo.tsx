import { TransitionSeries, linearTiming } from "@remotion/transitions";
import React from "react";
import { AbsoluteFill } from "remotion";
import { Background } from "./components/Background";
import { EASE } from "./lib/motion";
import { Scene1Hook } from "./scenes/Scene1Hook";
import { Scene2Dashboard } from "./scenes/Scene2Dashboard";
import { Scene3Actions } from "./scenes/Scene3Actions";
import { Scene4Benefits } from "./scenes/Scene4Benefits";
import { Scene5CTA } from "./scenes/Scene5CTA";
import { COLORS } from "./theme";
import { SCENES, TRANSITION } from "./timing";
import { zoomBlur } from "./transitions/zoomBlur";

const timing = (durationInFrames: number) => linearTiming({ durationInFrames, easing: EASE.inOut });

/** Vídeo completo: 900 frames a 30 fps (1920×1080). O fundo fica fixo e as cenas transitam sobre ele. */
export const MidviewPromo: React.FC = () => (
  <AbsoluteFill className="font-sans text-white" style={{ backgroundColor: COLORS.bg }}>
    <Background />
    <TransitionSeries>
      <TransitionSeries.Sequence durationInFrames={SCENES.hook.durationInFrames} name="1 · Gancho + logo">
        <Scene1Hook />
      </TransitionSeries.Sequence>
      <TransitionSeries.Transition presentation={zoomBlur()} timing={timing(TRANSITION.hookToDashboard)} />
      <TransitionSeries.Sequence durationInFrames={SCENES.dashboard.durationInFrames} name="2 · Dashboard ao vivo">
        <Scene2Dashboard />
      </TransitionSeries.Sequence>
      {/* Corte seco: a Cena 3 começa exatamente no enquadramento final da Cena 2. */}
      <TransitionSeries.Sequence durationInFrames={SCENES.actions.durationInFrames} name="3 · Ações rápidas">
        <Scene3Actions />
      </TransitionSeries.Sequence>
      <TransitionSeries.Transition presentation={zoomBlur()} timing={timing(TRANSITION.actionsToBenefits)} />
      <TransitionSeries.Sequence durationInFrames={SCENES.benefits.durationInFrames} name="4 · Benefícios">
        <Scene4Benefits />
      </TransitionSeries.Sequence>
      <TransitionSeries.Transition presentation={zoomBlur()} timing={timing(TRANSITION.benefitsToCta)} />
      <TransitionSeries.Sequence durationInFrames={SCENES.cta.durationInFrames} name="5 · CTA">
        <Scene5CTA />
      </TransitionSeries.Sequence>
    </TransitionSeries>
  </AbsoluteFill>
);
