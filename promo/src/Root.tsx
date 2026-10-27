import "./index.css";
import "./fonts";
import React from "react";
import { AbsoluteFill, Composition, Folder } from "remotion";
import { AppThemeProvider } from "./components/app/appTheme";
import { Background } from "./components/Background";
import { MidviewPromo } from "./MidviewPromo";
import { Scene1Hook } from "./scenes/Scene1Hook";
import { Scene8Beneficios } from "./scenes/Scene8Beneficios";
import { Scene9Encerramento } from "./scenes/Scene9Encerramento";
import { Scene2Sala, Scene3Tela, Scene4Turma, Scene5Programas, Scene6Energia, Scene7Configurar } from "./scenes/TourScenes";
import { FPS, HEIGHT, SCENES, TOTAL_FRAMES, WIDTH } from "./timing";
import { COMPLETO, RESUMO } from "./tutorial/engine";
import { Tutorial } from "./tutorial/Tutorial";

/** Cena isolada sobre o fundo, para ajustar uma cena por vez no Studio. */
const preview = (Scene: React.FC): React.FC => {
  const ScenePreview: React.FC = () => (
    <AppThemeProvider name="real">
      <AbsoluteFill className="font-sans text-white">
        <Background />
        <Scene />
      </AbsoluteFill>
    </AppThemeProvider>
  );
  return ScenePreview;
};

const SCENE_PREVIEWS = [
  { id: "Cena1-Gancho", component: preview(Scene1Hook), durationInFrames: SCENES.hook.durationInFrames },
  { id: "Cena2-Sala", component: preview(Scene2Sala), durationInFrames: SCENES.sala.durationInFrames },
  { id: "Cena3-Tela", component: preview(Scene3Tela), durationInFrames: SCENES.tela.durationInFrames },
  { id: "Cena4-Turma", component: preview(Scene4Turma), durationInFrames: SCENES.turma.durationInFrames },
  { id: "Cena5-Programas", component: preview(Scene5Programas), durationInFrames: SCENES.programas.durationInFrames },
  { id: "Cena6-Energia", component: preview(Scene6Energia), durationInFrames: SCENES.energia.durationInFrames },
  { id: "Cena7-Configurar", component: preview(Scene7Configurar), durationInFrames: SCENES.configurar.durationInFrames },
  { id: "Cena8-Beneficios", component: preview(Scene8Beneficios), durationInFrames: SCENES.beneficios.durationInFrames },
  { id: "Cena9-Encerramento", component: preview(Scene9Encerramento), durationInFrames: SCENES.cta.durationInFrames },
];

export const RemotionRoot: React.FC = () => (
  <>
    {/* O mesmo vídeo em dois temas: as cores reais do app e o estilo escuro. */}
    <Composition
      id="MidviewPromo"
      component={MidviewPromo}
      defaultProps={{ theme: "real" }}
      durationInFrames={TOTAL_FRAMES}
      fps={FPS}
      width={WIDTH}
      height={HEIGHT}
    />
    <Composition
      id="MidviewPromoEscuro"
      component={MidviewPromo}
      defaultProps={{ theme: "escuro" }}
      durationInFrames={TOTAL_FRAMES}
      fps={FPS}
      width={WIDTH}
      height={HEIGHT}
    />
    {/* O tutorial completo, com todas as funções e narração; dura o que a narração pede. */}
    <Composition
      id="MidviewTutorial"
      component={Tutorial}
      defaultProps={{ theme: "real" }}
      durationInFrames={COMPLETO.TOTAL}
      fps={FPS}
      width={WIDTH}
      height={HEIGHT}
    />
    <Composition
      id="MidviewTutorialEscuro"
      component={Tutorial}
      defaultProps={{ theme: "escuro" }}
      durationInFrames={COMPLETO.TOTAL}
      fps={FPS}
      width={WIDTH}
      height={HEIGHT}
    />
    {/* O resumo de até 3 minutos, só com as funções mais usadas. */}
    <Composition
      id="MidviewResumo"
      component={Tutorial}
      defaultProps={{ theme: "real", cut: "resumo" }}
      durationInFrames={RESUMO.TOTAL}
      fps={FPS}
      width={WIDTH}
      height={HEIGHT}
    />
    <Composition
      id="MidviewResumoEscuro"
      component={Tutorial}
      defaultProps={{ theme: "escuro", cut: "resumo" }}
      durationInFrames={RESUMO.TOTAL}
      fps={FPS}
      width={WIDTH}
      height={HEIGHT}
    />
    <Folder name="Cenas">
      {SCENE_PREVIEWS.map((scene) => (
        <Composition
          key={scene.id}
          id={scene.id}
          component={scene.component}
          durationInFrames={scene.durationInFrames}
          fps={FPS}
          width={WIDTH}
          height={HEIGHT}
        />
      ))}
    </Folder>
  </>
);
