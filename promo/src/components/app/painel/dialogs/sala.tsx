import React from "react";
import { clamp01 } from "../../../../lib/motion";
import { WIN, type Box } from "../../geometry";
import { P_GRID_BOTTOM, P_GRID_TOP, P_TOTAL, pDialog } from "../geometry";
import { Card, PBtn, PCheck, PDialog, PField, T, icon, typed } from "../kit";
import { GROUP_COLOR, P, PFONT, PG } from "../palette";

/* ---------- Sala vazia (MainWindow.xaml, foto 07) ---------- */

const EMPTY_CY = P_GRID_TOP + (P_GRID_BOTTOM - P_GRID_TOP) / 2;
/** Os dois botões do aviso de sala vazia, em coordenadas da janela. */
export const EMPTY_SEARCH: Box = { x: WIN.w / 2 - 182, y: EMPTY_CY + 44, w: 186, h: 30 };
export const EMPTY_JOIN: Box = { x: WIN.w / 2 + 12, y: EMPTY_CY + 44, w: 172, h: 30 };

/** O aviso no lugar da grade quando a sala não tem nenhuma máquina. */
export const EmptyRoom: React.FC<{ appear: number; hover?: number; press?: number; joinHover?: number }> = ({ appear, hover = 0, press = 0, joinHover = 0 }) =>
  appear > 0.01 ? (
    <div style={{ position: "absolute", left: 0, top: 0, width: WIN.w, height: WIN.h, opacity: clamp01(appear), fontFamily: PFONT }}>
      <div style={{ position: "absolute", left: 0, right: 0, top: EMPTY_CY - 92, textAlign: "center" }}>
        <span style={icon(40, P.muted)}>{PG.laptop}</span>
      </div>
      <div style={{ position: "absolute", left: 0, right: 0, top: EMPTY_CY - 34, textAlign: "center", fontSize: 14, lineHeight: "19px", color: P.muted }}>
        Nenhuma máquina na sala ainda.
        <br />
        Instale o Midview Aluno nas máquinas e procure-as na rede,
        <br />
        ou peça um código a um colega que já tem a sala.
      </div>
      <PBtn box={EMPTY_SEARCH} kind="primary" hover={hover} press={press}>
        Procurar máquinas na rede
      </PBtn>
      <PBtn box={EMPTY_JOIN} hover={joinHover}>
        Entrar na sala de um colega
      </PBtn>
    </div>
  ) : null;

/* ---------- Dar acesso a um colega (GiveAccessWindow.xaml, foto 20) ---------- */

export const GIVE = pDialog(486, 281);
export const INVITE_CODE = "K7QM-3XW9";

export const GiveAccessDialog: React.FC<{ s: { appear: number; joined: number } }> = ({ s }) => (
  <PDialog title="Dar acesso a um colega" box={GIVE} appear={s.appear} icon={PG.people} group="configurar" heading="Dar acesso a um colega">
    <T x={78} y={67} size={12}>
      Passe este código ao colega:
    </T>
    <Card box={{ x: 24, y: 98, w: 438, h: 68 }} />
    <div
      style={{
        position: "absolute",
        left: 24,
        top: 98,
        width: 438,
        height: 68,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        fontFamily: 'Consolas, "Cascadia Mono", monospace',
        fontSize: 34,
        fontWeight: 700,
        color: P.accentText,
        letterSpacing: "0.04em",
      }}
    >
      {INVITE_CODE}
    </div>
    <T x={24} y={178} muted size={11.5}>
      Vale até 15:42. {P_TOTAL - 1} máquinas serão liberadas quando o colega digitar o código.
    </T>
    {s.joined > 0.01 ? (
      <div style={{ position: "absolute", left: 24, top: 204, fontSize: 12, opacity: clamp01(s.joined) }}>PROF-02 entrou. Liberando as máquinas...</div>
    ) : null}
    <PBtn box={{ x: 24, y: 233, w: 68, h: 26 }}>Fechar</PBtn>
  </PDialog>
);

/* ---------- Entrar na sala de um colega (JoinClassWindow.xaml, fotos 21 e 23) ---------- */

export const JOIN = pDialog(486, 254);
export const JOIN_CTRL = { code: { x: 37, y: 161, w: 199, h: 30 }, enter: { x: 245, y: 161, w: 68, h: 30 } };

export const JoinClassDialog: React.FC<{ s: { appear: number; typed: number; enterHover?: number; enterPress?: number; done: number } }> = ({ s }) => (
  <PDialog
    title="Entrar na sala de um colega"
    box={JOIN}
    appear={s.appear}
    icon={PG.people}
    group="configurar"
    heading="Entrar na sala de um colega"
    hint={'Peça ao colega que já tem a sala para clicar em "Máquinas da sala" > "Dar acesso a um colega", e digite aqui o código que aparecer na tela dele.'}
  >
    <Card box={{ x: 24, y: 127, w: 438, h: 78 }} />
    <T x={37} y={141} size={12} weight={600}>
      Código do convite:
    </T>
    <PField box={JOIN_CTRL.code} text={typed(INVITE_CODE, s.typed)} caret={s.typed < 1} focus size={14} />
    <PBtn box={JOIN_CTRL.enter} kind="primary" hover={s.enterHover} press={s.enterPress}>
      Entrar
    </PBtn>
    {s.done > 0.01 ? (
      <div style={{ position: "absolute", left: 24, top: 216, fontSize: 12, opacity: clamp01(s.done) }}>Recebidas {P_TOTAL - 1} máquinas de PROF-02.</div>
    ) : null}
  </PDialog>
);

/* ---------- Máquinas da sala: Procurar e parear, com o código (foto 11) ---------- */

export type FoundCardP = {
  name: string;
  address: string;
  status: string;
  checked: number;
  /** Texto embaixo do nome ("Conectando...", "Pareada. Já está na sala."). */
  outcome?: string;
  /** O código de 6 dígitos esperando conferência (0–1). */
  code?: string;
  codeP?: number;
  confereHover?: number;
  conferePress?: number;
};

/** Altura de um cartão de máquina achada. */
export const foundCardH = (c: FoundCardP) => 52 + (c.outcome ? 22 : 0) + clamp01(c.codeP ?? 0) * 70;

/** Cartões das máquinas achadas, de cima para baixo a partir de y 153 (conteúdo da janela). */
export const foundCardY = (cards: FoundCardP[], i: number) => {
  let y = 153;
  for (let k = 0; k < i; k++) y += foundCardH(cards[k]) + 8;
  return y;
};

/** Onde fica o "Confere" no cartão `i`. */
export const confereBox = (cards: FoundCardP[], i: number): Box => {
  const y = foundCardY(cards, i);
  return { x: 520, y: y + 52 + (cards[i].outcome ? 22 : 0) + 26, w: 74, h: 30 };
};

const Chip: React.FC<{ x: number; y: number; children: React.ReactNode }> = ({ x, y, children }) => (
  <div
    style={{
      position: "absolute",
      right: x,
      top: y,
      padding: "1px 10px",
      borderRadius: 999,
      border: `1px solid ${P.line}`,
      background: P.bg,
      fontSize: 11,
      fontWeight: 600,
      whiteSpace: "nowrap",
    }}
  >
    {children}
  </div>
);

/** As máquinas achadas na rede, cada uma num cartão com caixinha, nome, endereço e estado. */
export const FoundCards: React.FC<{ cards: FoundCardP[]; p: number; dialogW: number }> = ({ cards, p, dialogW }) => (
  <>
    {cards.map((c, i) => {
      const y = foundCardY(cards, i);
      const show = clamp01((p - i * 0.1) * 2.5);
      if (show <= 0.01) return null;
      const codeP = clamp01(c.codeP ?? 0);
      const cb = confereBox(cards, i);
      return (
        <div key={c.name} style={{ opacity: show }}>
          <Card box={{ x: 224, y, w: 554, h: foundCardH(c) }} />
          <PCheck x={237} y={y + 18} on={c.checked}>
            {""}
          </PCheck>
          <span style={{ ...icon(20, GROUP_COLOR.configurar), position: "absolute", left: 263, top: y + 16 }}>{PG.monitor}</span>
          <T x={295} y={y + 11} weight={600} size={12}>
            {c.name}
          </T>
          <T x={295} y={y + 28} muted size={11.5}>
            {c.address}
          </T>
          <Chip x={dialogW - 764} y={y + 16}>
            {c.status}
          </Chip>
          {c.outcome ? (
            <T x={295} y={y + 52} size={12}>
              {c.outcome}
            </T>
          ) : null}
          {codeP > 0.01 ? (
            <div style={{ opacity: codeP }}>
              <T x={295} y={cb.y - 22} muted size={12}>
                Na tela do aluno aparece:
              </T>
              {c.code?.split("").map((d, k) =>
                d === " " ? null : (
                  <div
                    key={k}
                    style={{
                      position: "absolute",
                      left: 295 + k * 32 - (k > 3 ? 16 : 0),
                      top: cb.y - 3,
                      width: 27,
                      height: 36,
                      borderRadius: 6,
                      border: `1px solid ${P.accent}`,
                      background: P.bg,
                      display: "flex",
                      alignItems: "center",
                      justifyContent: "center",
                      fontFamily: "Consolas, monospace",
                      fontSize: 19,
                      fontWeight: 700,
                      color: P.accentText,
                    }}
                  >
                    {d}
                  </div>
                ),
              )}
              <PBtn box={cb} kind="primary" hover={c.confereHover} press={c.conferePress}>
                Confere
              </PBtn>
              <PBtn box={{ x: cb.x + 82, y: cb.y, w: 92, h: 30 }}>Não confere</PBtn>
            </div>
          ) : null}
        </div>
      );
    })}
  </>
);
