import React from "react";
import { clamp01, mix } from "../../../lib/motion";
import type { GroupId } from "../geometry";
import type { ToolbarProps } from "../Toolbar";
import { PAINEL, PAINEL_GROUPS, PAINEL_TOOLBAR } from "./geometry";
import { GROUP_COLOR, P, PFONT, PG, PICONS } from "./palette";

const icon = (size: number, color: string): React.CSSProperties => ({ fontFamily: PICONS, fontSize: size, lineHeight: 1, color });

/**
 * A barra de botões do Painel: cada grupo num painel arredondado, o ícone de
 * cada botão na cor do grupo, o rótulo claro e o nome do grupo embaixo, em
 * letras pequenas maiúsculas com uma bolinha da cor do grupo.
 */
export const PainelToolbar: React.FC<ToolbarProps> = ({ buttons, spotlight }) => (
  <div style={{ position: "absolute", left: 0, top: PAINEL.titleBar, width: "100%", height: PAINEL.gridTop - PAINEL.titleBar, background: P.bg }}>
    {PAINEL_GROUPS.map((group) => {
      const panel = PAINEL_TOOLBAR.panels[group.id];
      const color = GROUP_COLOR[group.id];
      return (
        <React.Fragment key={group.id}>
          <div
            style={{
              position: "absolute",
              left: panel.x,
              top: panel.y - PAINEL.titleBar,
              width: panel.w,
              height: panel.h,
              borderRadius: 12,
              background: P.card,
              border: `1px solid ${P.line}`,
              boxSizing: "border-box",
            }}
          />
          {group.buttons.map((b) => {
            const box = PAINEL_TOOLBAR.buttons[b.id];
            const v = buttons[b.id] ?? {};
            const hover = clamp01(v.hover ?? 0);
            const press = clamp01(v.press ?? 0);
            const disabled = Boolean(v.disabled);
            const chrome = press > 0.5 ? P.toolbarPressed : hover > 0.5 ? P.toolbarHover : P.card;
            const caption = v.caption ?? "";
            const badge = v.badge ?? b.badge;
            return (
              <div
                key={b.id}
                style={{
                  position: "absolute",
                  left: box.x,
                  top: box.y - PAINEL.titleBar,
                  width: box.w,
                  height: box.h,
                  fontFamily: PFONT,
                  color: disabled ? P.disabled : P.text,
                }}
              >
                <div style={{ position: "absolute", inset: 0, borderRadius: 8, background: P.toolbarHover, opacity: hover * (1 - press) }} />
                <div style={{ position: "absolute", inset: 0, borderRadius: 8, background: P.toolbarPressed, opacity: press }} />
                <div style={{ position: "relative", width: 26, height: 22, margin: "7px auto 0" }}>
                  <span style={{ ...icon(20, disabled ? P.disabled : color), position: "absolute", left: 0, right: 0, top: 1, textAlign: "center" }}>{b.icon}</span>
                  {badge ? (
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
                      <span style={icon(10, disabled ? P.disabled : P.text)}>{badge}</span>
                    </span>
                  ) : null}
                  {b.options ? (
                    <span style={{ ...icon(10, disabled ? P.disabled : P.muted), position: "absolute", right: -17, top: 6 }}>{PG.chevronDown}</span>
                  ) : null}
                </div>
                <div style={{ position: "relative", margin: "5px -3px 0", height: 28, fontSize: 11, lineHeight: "14px", textAlign: "center" }}>
                  <div style={{ whiteSpace: caption ? "nowrap" : "normal" }}>{v.label ?? b.label}</div>
                  {caption ? (
                    <div style={{ display: "flex", justifyContent: "center", marginTop: 1, opacity: clamp01(v.captionIn ?? 1) }}>
                      <span
                        style={{
                          padding: "0 6px",
                          borderRadius: 6,
                          background: GROUP_COLOR.turma,
                          color: "#1A1300",
                          fontSize: 10.5,
                          fontWeight: 600,
                          lineHeight: "12px",
                          transform: `scale(${mix(0.6, 1, clamp01(v.captionIn ?? 1))})`,
                        }}
                      >
                        {caption}
                      </span>
                    </div>
                  ) : null}
                </div>
              </div>
            );
          })}
          <div
            style={{
              position: "absolute",
              left: panel.x,
              width: panel.w,
              top: panel.y + panel.h - PAINEL.titleBar - 19,
              display: "flex",
              justifyContent: "center",
              alignItems: "center",
              fontFamily: PFONT,
              fontSize: 9.5,
              letterSpacing: "0.13em",
              textTransform: "uppercase",
              color: P.muted,
            }}
          >
            <span style={{ width: 6, height: 6, borderRadius: 3, background: color, marginRight: 5 }} />
            {group.caption}
          </div>
        </React.Fragment>
      );
    })}
    {spotlight && spotlight.amount > 0.01 ? <PainelSpotlight group={spotlight.group} amount={spotlight.amount} /> : null}
  </div>
);

/** Destaque do vídeo (não existe no app) em volta do grupo em uso, na cor do grupo. */
const PainelSpotlight: React.FC<{ group: GroupId; amount: number }> = ({ group, amount }) => {
  const panel = PAINEL_TOOLBAR.panels[group];
  const color = GROUP_COLOR[group];
  return (
    <div
      style={{
        position: "absolute",
        left: panel.x - 3,
        top: panel.y - PAINEL.titleBar - 3,
        width: panel.w + 6,
        height: panel.h + 6,
        borderRadius: 15,
        border: `2px solid ${color}`,
        boxShadow: `0 0 22px ${color}77, inset 0 0 16px ${color}22`,
        opacity: clamp01(amount),
      }}
    />
  );
};
