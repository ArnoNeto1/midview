import { Audio } from "@remotion/media";
import { TransitionSeries, linearTiming } from "@remotion/transitions";
import React from "react";
import { AbsoluteFill, staticFile, useCurrentFrame } from "remotion";
import { AppThemeProvider } from "../components/app/appTheme";
import { WIN } from "../components/app/geometry";
import { Background } from "../components/Background";
import { EASE } from "../lib/motion";
import { Scene1Hook } from "../scenes/Scene1Hook";
import { Scene9Encerramento } from "../scenes/Scene9Encerramento";
import { COLORS } from "../theme";
import { zoomBlur } from "../transitions/zoomBlur";
import { PROMO_BEATS } from "./beats";
import { makeTimeline, type Timeline } from "./engine";
import { FULL } from "./kit";
import { PainelCaption } from "./PainelCaption";
import { PainelStage } from "./PainelStage";
import { idlePainelApp } from "./world";

/** Partes do vídeo (frames) e a transição entre elas. */
export const PAINEL_PROMO = (() => {
  const hook = 180;
  const cta = 200;
  const transition = 20;
  const tour = makeTimeline(PROMO_BEATS, { cursor: { x: WIN.w + 80, y: WIN.h + 70 }, cam: FULL });
  const tourFrom = hook - transition;
  const ctaFrom = tourFrom + tour.TOTAL - transition;
  return { hook, cta, transition, tour, tourFrom, ctaFrom, total: ctaFrom + cta };
})();

/** Área da tela onde a câmera enquadra a janela, abaixo da legenda. */
const VIEWPORT = { x: 40, y: 196, w: 1840, h: 864 };

/** As falas de um roteiro, cada uma no seu trecho (mp3 já gravados, cortados quando é só uma frase). */
export const BeatVoices: React.FC<{ tl: Timeline; offset?: number; volume?: number }> = ({ tl, offset = 0, volume = 1 }) => (
  <>
    {tl.VOICES.map(({ id, from, voice }) => (
      <Audio
        key={id}
        src={staticFile(voice.src)}
        from={offset + from}
        trimBefore={voice.trimBefore}
        trimAfter={voice.trimAfter}
        volume={volume}
        name={`Fala ${id}`}
      />
    ))}
  </>
);

const Tour: React.FC = () => {
  const frame = useCurrentFrame();
  const tl = PAINEL_PROMO.tour;
  return (
    <AbsoluteFill>
      <PainelStage tl={tl} frame={frame} viewport={VIEWPORT} enterAt={4} />
      <PainelCaption tl={tl} frame={frame} fadeInAt={6} />
    </AbsoluteFill>
  );
};

const timing = (durationInFrames: number) => linearTiming({ durationInFrames, easing: EASE.inOut });

/**
 * Vídeo promocional no visual Painel (versão 0.0.41): a abertura com a logo, o
 * tour por todos os botões da barra, cada um com a sua janela, e o encerramento.
 * A narração é a mesma voz dos outros vídeos, só com falas já gravadas.
 */
export const MidviewPromoPainel: React.FC = () => (
  <AppThemeProvider name="painel">
    <AbsoluteFill className="font-sans text-white" style={{ backgroundColor: COLORS.bg }}>
      <Background />
      <TransitionSeries>
        <TransitionSeries.Sequence durationInFrames={PAINEL_PROMO.hook} name="Abertura">
          <Scene1Hook />
        </TransitionSeries.Sequence>
        <TransitionSeries.Transition presentation={zoomBlur()} timing={timing(PAINEL_PROMO.transition)} />
        <TransitionSeries.Sequence durationInFrames={PAINEL_PROMO.tour.TOTAL} name="Tour pelo Painel">
          <Tour />
        </TransitionSeries.Sequence>
        <TransitionSeries.Transition presentation={zoomBlur()} timing={timing(PAINEL_PROMO.transition)} />
        <TransitionSeries.Sequence durationInFrames={PAINEL_PROMO.cta} name="Encerramento">
          <Scene9Encerramento app={(abs) => idlePainelApp(abs)} startsAt={PAINEL_PROMO.ctaFrom} />
        </TransitionSeries.Sequence>
      </TransitionSeries>
      {/* Narração em frames do vídeo, fora das cenas, para nenhuma fala ser cortada numa transição. */}
      <Audio src={staticFile("narracao/01.mp3")} from={36} name="Fala 01" />
      <BeatVoices tl={PAINEL_PROMO.tour} offset={PAINEL_PROMO.tourFrom} />
      <Audio src={staticFile("narracao/20.mp3")} from={PAINEL_PROMO.ctaFrom + 24} name="Fala 20" />
    </AbsoluteFill>
  </AppThemeProvider>
);
