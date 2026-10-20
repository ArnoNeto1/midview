import "./index.css";
import "./fonts";
import React from "react";
import { AbsoluteFill, Composition, Folder } from "remotion";
import { Background } from "./components/Background";
import { MidviewPromo } from "./MidviewPromo";
import { Scene1Hook } from "./scenes/Scene1Hook";
import { Scene2Dashboard } from "./scenes/Scene2Dashboard";
import { Scene3Actions } from "./scenes/Scene3Actions";
import { Scene4Benefits } from "./scenes/Scene4Benefits";
import { Scene5CTA } from "./scenes/Scene5CTA";
import { FPS, HEIGHT, SCENES, TOTAL_FRAMES, WIDTH } from "./timing";

/** Cena isolada sobre o fundo, para ajustar uma cena por vez no Studio. */
const preview = (Scene: React.FC): React.FC => {
  const ScenePreview: React.FC = () => (
    <AbsoluteFill className="font-sans text-white">
      <Background />
      <Scene />
    </AbsoluteFill>
  );
  return ScenePreview;
};

const SCENE_PREVIEWS = [
  { id: "Cena1-Gancho", component: preview(Scene1Hook), durationInFrames: SCENES.hook.durationInFrames },
  { id: "Cena2-Dashboard", component: preview(Scene2Dashboard), durationInFrames: SCENES.dashboard.durationInFrames },
  { id: "Cena3-Acoes", component: preview(Scene3Actions), durationInFrames: SCENES.actions.durationInFrames },
  { id: "Cena4-Beneficios", component: preview(Scene4Benefits), durationInFrames: SCENES.benefits.durationInFrames },
  { id: "Cena5-CTA", component: preview(Scene5CTA), durationInFrames: SCENES.cta.durationInFrames },
];

export const RemotionRoot: React.FC = () => (
  <>
    <Composition
      id="MidviewPromo"
      component={MidviewPromo}
      durationInFrames={TOTAL_FRAMES}
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
