import React from "react";
import { MidviewLogo } from "../MidviewLogo";
import { useAppTheme } from "./appTheme";
import { BottomBar, type BottomBarProps } from "./BottomBar";
import { GRID_BOTTOM, GRID_TOP, WIN, type Box } from "./geometry";
import { GLYPH, Glyph } from "./Glyph";
import { MachineTile, type TileState } from "./MachineTile";
import { P_GRID_BOTTOM, P_GRID_TOP, PAINEL_PCS } from "./painel/geometry";
import { PainelTitleBar, type PainelTitle } from "./painel/TitleBar";
import { PCS, type Pc } from "./pcs";
import { Toolbar, type ToolbarProps } from "./Toolbar";

/** `pc`: a máquina do cartão; sem ela, a da lista do tema (PCS, ou PAINEL_PCS no Painel). */
export type TileItem = { index: number; box: Box; state: TileState; pc?: Pc };

type AppWindowProps = {
  /** Frame absoluto do vídeo (telas dos alunos ao vivo). */
  frame: number;
  tiles: TileItem[];
  toolbar: ToolbarProps;
  bottom: BottomBarProps;
  /** Painel: os selos da barra de título ("Aula: 7º A", "13 de 15 ligadas"). */
  title?: PainelTitle;
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
export const AppWindow: React.FC<AppWindowProps> = ({ frame, tiles, toolbar, bottom, title, children }) => {
  const theme = useAppTheme();
  if (theme.name === "painel") {
    return (
      <PainelWindow frame={frame} tiles={tiles} toolbar={toolbar} bottom={bottom} title={title}>
        {children}
      </PainelWindow>
    );
  }
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
            pc={t.pc ?? PCS[t.index]}
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

/** A janela da sala de aula no visual Painel (0.0.41): barra de título própria, grupos em painéis e cartões. */
const PainelWindow: React.FC<AppWindowProps> = ({ frame, tiles, toolbar, bottom, title, children }) => {
  const theme = useAppTheme();
  const online = tiles.filter((t) => t.state.online > 0.5).length;
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
      <PainelTitleBar title={title ?? { online, total: tiles.length }} />
      <Toolbar {...toolbar} />
      <div style={{ position: "absolute", left: 0, top: P_GRID_TOP, width: WIN.w, height: P_GRID_BOTTOM - P_GRID_TOP, overflow: "hidden" }}>
        {tiles.map((t) => (
          <MachineTile
            key={t.index}
            pc={t.pc ?? PAINEL_PCS[t.index]}
            index={t.index}
            box={{ ...t.box, y: t.box.y - P_GRID_TOP }}
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
