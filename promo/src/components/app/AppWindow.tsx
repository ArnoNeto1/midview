import React from "react";
import { MidviewLogo } from "../MidviewLogo";
import { useAppTheme } from "./appTheme";
import { BottomBar, type BottomBarProps } from "./BottomBar";
import { GRID_BOTTOM, GRID_TOP, WIN, type Box } from "./geometry";
import { GLYPH, Glyph } from "./Glyph";
import { MachineTile, type TileState } from "./MachineTile";
import { PCS } from "./pcs";
import { Toolbar, type ToolbarProps } from "./Toolbar";

export type TileItem = { index: number; box: Box; state: TileState };

type AppWindowProps = {
  /** Frame absoluto do vídeo (telas dos alunos ao vivo). */
  frame: number;
  tiles: TileItem[];
  toolbar: ToolbarProps;
  bottom: BottomBarProps;
  /** Janelas, menus e cursor, em coordenadas da janela. */
  children?: React.ReactNode;
};

const TitleBar: React.FC = () => {
  const theme = useAppTheme();
  return (
    <div
      style={{
        position: "absolute",
        left: 0,
        top: 0,
        width: WIN.w,
        height: WIN.titleBar,
        background: theme.titleBar.bg,
        display: "flex",
        alignItems: "center",
        gap: 9,
        paddingLeft: 11,
        fontFamily: theme.font,
        fontSize: 12,
        color: theme.titleBar.text,
      }}
    >
      <MidviewLogo size={16} />
      Midview - Sala de aula
      <div style={{ position: "absolute", right: 0, top: 0, height: WIN.titleBar, display: "flex" }}>
        {[GLYPH.minimize, GLYPH.maximize, GLYPH.close].map((code) => (
          <div key={code} style={{ width: 46, display: "flex", alignItems: "center", justifyContent: "center" }}>
            <Glyph code={code} size={10} color={theme.titleBar.text} />
          </div>
        ))}
      </div>
    </div>
  );
};

/** A janela "Midview - Sala de aula", fiel a MainWindow.xaml. */
export const AppWindow: React.FC<AppWindowProps> = ({ frame, tiles, toolbar, bottom, children }) => {
  const theme = useAppTheme();
  return (
    <div
      style={{
        position: "relative",
        width: WIN.w,
        height: WIN.h,
        borderRadius: WIN.radius,
        overflow: "hidden",
        background: theme.canvas,
        boxShadow: `${theme.window.shadow}, 0 0 0 1px ${theme.window.line}`,
      }}
    >
      <TitleBar />
      <Toolbar {...toolbar} />
      <div style={{ position: "absolute", left: 0, top: GRID_TOP, width: WIN.w, height: GRID_BOTTOM - GRID_TOP, overflow: "hidden" }}>
        {tiles.map((t) => (
          <MachineTile
            key={t.index}
            pc={PCS[t.index]}
            index={t.index}
            box={{ ...t.box, y: t.box.y - GRID_TOP }}
            state={t.state}
            frame={frame}
          />
        ))}
      </div>
      <BottomBar {...bottom} />
      {children}
    </div>
  );
};
