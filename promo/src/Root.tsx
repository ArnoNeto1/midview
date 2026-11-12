import "./index.css";
import "./fonts";
import React from "react";
import { AbsoluteFill, Composition, Folder } from "remotion";
import { COMERCIAL, MidviewComercial } from "./comercial/Comercial";
import { MidviewReels3min, REELS3 } from "./comercial/vertical/Reels3";
import { MidviewComercialVertical, VERTICAL } from "./comercial/vertical/Vertical";
import { AppThemeProvider } from "./components/app/appTheme";
import { Background } from "./components/Background";
import { MidviewPromo } from "./MidviewPromo";
import { MidviewPromoPainel, PAINEL_PROMO } from "./painel/MidviewPromoPainel";
import { MidviewReels, REELS_H, REELS_TOTAL, REELS_W } from "./reels/Reels";
import { Scene1Hook } from "./scenes/Scene1Hook";
import { Scene8Beneficios } from "./scenes/Scene8Beneficios";
import { Scene9Encerramento } from "./scenes/Scene9Encerramento";
import { Scene2Sala, Scene3Tela, Scene4Turma, Scene5Programas, Scene6Energia, Scene7Configurar } from "./scenes/TourScenes";
import { FPS, HEIGHT, SCENES, TOTAL_FRAMES, WIDTH } from "./timing";
import { COMPLETO, RESUMO } from "./tutorial/engine";
import { PAINEL_TUTORIAL } from "./tutorial/painel";
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
    {/* O mesmo app no visual Painel (0.0.41), com todos os botões da barra. */}
    <Composition
      id="MidviewPromoPainel"
      component={MidviewPromoPainel}
      durationInFrames={PAINEL_PROMO.total}
      fps={FPS}
      width={WIDTH}
      height={HEIGHT}
    />
    {/* O comercial de cerca de 54 s: tomadas reais do programa e ambiente de IA (src/comercial). */}
    <Composition id="MidviewComercial" component={MidviewComercial} durationInFrames={COMERCIAL.frames} fps={COMERCIAL.fps} width={WIDTH} height={HEIGHT} />
    {/* O comercial vertical, no estilo de explicação em neon (src/comercial/vertical). */}
    <Composition
      id="MidviewComercialVertical"
      component={MidviewComercialVertical}
      durationInFrames={VERTICAL.frames}
      fps={VERTICAL.fps}
      width={VERTICAL.largura}
      height={VERTICAL.altura}
    />
    {/* Reels de 3 minutos: um passeio pelo programa no visual do comercial vertical. */}
    <Composition
      id="MidviewReels3min"
      component={MidviewReels3min}
      durationInFrames={REELS3.frames}
      fps={REELS3.fps}
      width={REELS3.largura}
      height={REELS3.altura}
    />
    {/* Reels do Instagram (vertical), no visual Painel, sem narração. */}
    <Composition id="MidviewReels" component={MidviewReels} durationInFrames={REELS_TOTAL} fps={FPS} width={REELS_W} height={REELS_H} />
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
    {/* O tutorial completo no visual Painel (0.0.41): as mesmas falas, a janela e as janelas novas. */}
    <Composition
      id="MidviewTutorialPainel"
      component={Tutorial}
      defaultProps={{ theme: "painel" }}
      durationInFrames={PAINEL_TUTORIAL.TOTAL}
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
