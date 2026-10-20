import React from "react";
import { clamp01 } from "../../lib/motion";
import { useAppTheme } from "./appTheme";
import { TOOLBAR_GROUPS, TOOLBAR_LAYOUT, WIN, type ButtonId, type GroupId } from "./geometry";
import { GLYPH, Glyph } from "./Glyph";

export type ButtonVisual = { hover?: number; press?: number; disabled?: boolean; label?: string };

export type ToolbarProps = {
  buttons: Partial<Record<ButtonId, ButtonVisual>>;
  /** Destaque do vídeo em volta do grupo em uso. */
  spotlight?: { group: GroupId; amount: number } | null;
};

/** A barra de ferramentas real: grupos de botões com ícone sobre o texto e o nome do grupo embaixo. */
export const Toolbar: React.FC<ToolbarProps> = ({ buttons, spotlight }) => {
  const theme = useAppTheme();
  const t = theme.toolbar;
  return (
    <div style={{ position: "absolute", left: 0, top: WIN.titleBar, width: WIN.w, height: WIN.toolbar, background: t.bg }}>
      {TOOLBAR_GROUPS.map((group) => {
        const g = TOOLBAR_LAYOUT.groups[group.id];
        return (
          <React.Fragment key={group.id}>
            {group.buttons.map((b) => {
              const box = TOOLBAR_LAYOUT.buttons[b.id];
              const v = buttons[b.id] ?? {};
              const hover = clamp01(v.hover ?? 0);
              const press = clamp01(v.press ?? 0);
              const color = v.disabled ? t.disabled : t.text;
              const chrome = press > 0.01 ? t.pressed : hover > 0.01 ? t.hover : t.bg;
              return (
                <div
                  key={b.id}
                  style={{
                    position: "absolute",
                    left: box.x,
                    top: box.y - WIN.titleBar,
                    width: box.w,
                    height: box.h,
                    color,
                    fontFamily: theme.font,
                  }}
                >
                  <div style={{ position: "absolute", inset: 0, borderRadius: 4, background: t.hover, opacity: hover * (1 - press) }} />
                  <div style={{ position: "absolute", inset: 0, borderRadius: 4, background: t.pressed, opacity: press }} />
                  <div style={{ position: "relative", width: 26, height: 22, margin: "7px auto 0" }}>
                    <Glyph
                      code={b.icon}
                      size={20}
                      style={{ position: "absolute", left: 0, right: 0, top: 1, textAlign: "center" }}
                    />
                    {b.badge ? (
                      <span
                        style={{
                          position: "absolute",
                          right: -3,
                          bottom: -3,
                          width: 14,
                          height: 14,
                          borderRadius: 7,
                          background: chrome,
                          display: "flex",
                          alignItems: "center",
                          justifyContent: "center",
                        }}
                      >
                        <Glyph code={b.badge} size={10} color={v.disabled ? t.disabled : t.badge} />
                      </span>
                    ) : null}
                    {b.options ? (
                      <Glyph code={GLYPH.options} size={10} style={{ position: "absolute", right: -17, top: 6 }} />
                    ) : null}
                  </div>
                  <div
                    style={{
                      position: "relative",
                      margin: "5px 4px 0",
                      height: 28,
                      fontSize: 12,
                      lineHeight: "14px",
                      textAlign: "center",
                      overflow: "hidden",
                    }}
                  >
                    {v.label ?? b.label}
                  </div>
                </div>
              );
            })}
            <div
              style={{
                position: "absolute",
                left: g.x,
                width: g.w,
                top: g.captionY - WIN.titleBar + 2,
                textAlign: "center",
                fontFamily: theme.font,
                fontSize: 11,
                color: t.muted,
              }}
            >
              {group.caption}
            </div>
          </React.Fragment>
        );
      })}
      {TOOLBAR_LAYOUT.separators.map((x) => (
        <div key={x} style={{ position: "absolute", left: x, top: 10, width: 1, height: WIN.toolbar - 20, background: t.line }} />
      ))}
      {spotlight && spotlight.amount > 0.01 ? <Spotlight group={spotlight.group} amount={spotlight.amount} /> : null}
    </div>
  );
};

const Spotlight: React.FC<{ group: GroupId; amount: number }> = ({ group, amount }) => {
  const theme = useAppTheme();
  const g = TOOLBAR_LAYOUT.groups[group];
  return (
    <div
      style={{
        position: "absolute",
        left: g.x - 5,
        top: g.y - WIN.titleBar - 1,
        width: g.w + 10,
        height: WIN.toolbar - 2,
        borderRadius: 8,
        border: `2px solid ${theme.spotlight}`,
        boxShadow: `0 0 22px ${theme.spotlight}88, inset 0 0 18px ${theme.spotlight}33`,
        opacity: clamp01(amount),
      }}
    />
  );
};
