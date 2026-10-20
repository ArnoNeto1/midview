import React from "react";
import { AbsoluteFill, useCurrentFrame } from "remotion";
import { AppStage } from "../components/app/AppStage";
import { TourCaption } from "../components/TourCaption";
import { SCENES } from "../timing";

type TourSceneKey = "sala" | "tela" | "turma" | "programas" | "energia" | "configurar";

/**
 * Cenas 2 a 7: o mesmo app em uso, cada uma num trecho do roteiro (components/app/tour.ts).
 * Como tudo é função do frame absoluto, os cortes entre elas não aparecem.
 */
const tourScene = (key: TourSceneKey): React.FC => {
  const TourScene: React.FC = () => {
    const abs = SCENES[key].from + useCurrentFrame();
    return (
      <AbsoluteFill>
        <AppStage abs={abs} />
        <TourCaption abs={abs} />
      </AbsoluteFill>
    );
  };
  return TourScene;
};

/** Cena 2 (6–16 s): a sala ao vivo, tamanho das telas e "Só online". */
export const Scene2Sala = tourScene("sala");
/** Cena 3 (16–28 s): grupo Tela — ver, controlar e bloquear telas. */
export const Scene3Tela = tourScene("tela");
/** Cena 4 (28–44 s): grupo Turma — mensagem, bate-papo, arquivos e site. */
export const Scene4Turma = tourScene("turma");
/** Cena 5 (44–56 s): grupo Programas e sites — bloquear programas e sites, fechar programas. */
export const Scene5Programas = tourScene("programas");
/** Cena 6 (56–66 s): grupo Energia — ligar pela rede e agendar. */
export const Scene6Energia = tourScene("energia");
/** Cena 7 (66–76 s): grupo Configurar e o menu do botão direito. */
export const Scene7Configurar = tourScene("configurar");
