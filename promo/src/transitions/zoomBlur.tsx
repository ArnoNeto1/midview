import type { TransitionPresentation, TransitionPresentationComponentProps } from "@remotion/transitions";
import React from "react";
import { AbsoluteFill, interpolate } from "remotion";
import { CLAMP } from "../lib/motion";

type ZoomBlurProps = {
  /** Escala final da cena que sai (vem na direção da câmera). */
  exitScale?: number;
  /** Escala inicial da cena que entra. */
  enterScale?: number;
  /** Desfoque máximo, em px. */
  blur?: number;
};

const ZoomBlurPresentation: React.FC<TransitionPresentationComponentProps<ZoomBlurProps>> = ({
  children,
  presentationDirection,
  presentationProgress: p,
  passedProps,
}) => {
  const { exitScale = 1.08, enterScale = 0.94, blur = 10 } = passedProps;
  const entering = presentationDirection === "entering";
  // As cenas são transparentes sobre o fundo contínuo: as duas precisam de fade.
  const opacity = entering ? interpolate(p, [0.2, 1], [0, 1], CLAMP) : interpolate(p, [0, 0.8], [1, 0], CLAMP);
  const scale = entering ? interpolate(p, [0, 1], [enterScale, 1]) : interpolate(p, [0, 1], [1, exitScale]);
  const amount = (entering ? 1 - p : p) * blur;
  return (
    <AbsoluteFill
      style={{
        opacity,
        transform: `scale(${scale})`,
        filter: amount > 0.05 ? `blur(${amount}px)` : undefined,
      }}
    >
      {children}
    </AbsoluteFill>
  );
};

/** Transição "zoom através": a cena que sai avança e desfoca, a que entra vem do fundo. */
export const zoomBlur = (props: ZoomBlurProps = {}): TransitionPresentation<ZoomBlurProps> => ({
  component: ZoomBlurPresentation,
  props,
});
