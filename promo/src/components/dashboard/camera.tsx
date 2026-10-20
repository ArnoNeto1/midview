import React from "react";
import { interpolate, spring } from "remotion";
import { CLAMP, EASE, SPRING } from "../../lib/motion";
import { BOUNDARY, SCENES } from "../../timing";
import { STAGE, WINDOW, WINDOW_HEIGHT } from "./layout";

export type Camera = { x: number; y: number; scale: number; rotateX: number; rotateY: number; opacity: number };

/**
 * Câmera das Cenas 2 e 3, em frames absolutos. As duas cenas chamam a mesma função,
 * então o corte seco entre elas é invisível.
 */
export const dashboardCamera = (abs: number, fps: number): Camera => {
  // Entrada em perspectiva que assenta plana.
  const enter = spring({ frame: abs - (SCENES.dashboard.from + 6), fps, config: SPRING.soft, durationInFrames: 56 });
  // Zoom-in lento e contínuo.
  const push = interpolate(abs, [SCENES.dashboard.from + 40, BOUNDARY.actions + 30], [0, 1], { ...CLAMP, easing: EASE.inOut });
  // Cena 3: aproxima e desce o olhar para a barra de comandos.
  const focus = spring({ frame: abs - (BOUNDARY.actions + 2), fps, config: SPRING.smooth, durationInFrames: 50 });
  return {
    x: interpolate(enter, [0, 1], [90, 0]) + 10 * focus,
    y: interpolate(enter, [0, 1], [130, 0]) - 30 * focus,
    scale: interpolate(enter, [0, 1], [0.8, 1]) * (1 + 0.02 * push + 0.04 * focus),
    rotateX: interpolate(enter, [0, 1], [26, 0]),
    rotateY: interpolate(enter, [0, 1], [-22, 0]),
    opacity: interpolate(enter, [0, 0.3], [0, 1], CLAMP),
  };
};

/** Posiciona a janela do dashboard na tela e aplica a câmera. */
export const DashboardStage: React.FC<{ camera: Camera; children: React.ReactNode }> = ({ camera, children }) => (
  <div
    style={{
      position: "absolute",
      left: STAGE.x,
      top: STAGE.y,
      width: WINDOW.width,
      height: WINDOW_HEIGHT,
      opacity: camera.opacity,
      transformOrigin: "50% 85%",
      transform: `perspective(2200px) translate3d(${camera.x}px, ${camera.y}px, 0) rotateX(${camera.rotateX}deg) rotateY(${camera.rotateY}deg) scale(${camera.scale})`,
    }}
  >
    {children}
  </div>
);
