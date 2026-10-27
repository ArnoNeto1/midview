import React from "react";
import { useAppTheme } from "../../components/app/appTheme";
import { BOTTOM, LAYOUT_FIT, LAYOUT_SMALL, WIN, center, sliderKnob, sliderValue, tileBox, type Box } from "../../components/app/geometry";
import { PCS } from "../../components/app/pcs";
import { mix, ramp } from "../../lib/motion";
import { EMPTY_SEARCH, EmptyRoom } from "../janelas/outras";
import { MACHINES, MACHINES_CTRL, MachinesWindow, type FoundCard } from "../janelas/configurar";
import { FULL, SLIDER_FIT, bottom, hov, inside, mid, sp, tap, tileCenter, toolbar, win, type Beat } from "../kit";
import { follow, grid, lerpBox, statusAt } from "./comum";

const MACHINES_FOCUS = inside({ x: MACHINES.x - 30, y: MACHINES.y - 16, w: 840, h: 480 });
const CODE = "482 913";

const card = (i: number, status: string, checked: number, extra: Partial<FoundCard> = {}): FoundCard => ({
  name: PCS[i].name,
  address: `192.168.0.${101 + i}`,
  status,
  checked,
  ...extra,
});

/** O botão "Confere" no card 0, com o código aberto. */
const confere = () => {
  const c = MACHINES_CTRL.card(0);
  return { x: MACHINES.x + c.x + 118 + 84 + 6 + 31, y: MACHINES.y + c.y + 15 + 13 };
};

const SLIDER_SMALL = sliderValue(LAYOUT_SMALL.w);

/** Retângulo de seleção arrastado na grade. */
const SelectionRect: React.FC<{ box: Box; o: number }> = ({ box, o }) => {
  const theme = useAppTheme();
  return o > 0.01 ? (
    <div
      style={{
        position: "absolute",
        left: box.x,
        top: box.y,
        width: box.w,
        height: box.h,
        border: `1.5px dashed ${theme.selected.line}`,
        background: `${theme.selected.line}22`,
        opacity: o,
      }}
    />
  ) : null;
};

/** Plaquinha "Ctrl" perto do cursor. */
const KeyChip: React.FC<{ at: { x: number; y: number }; o: number }> = ({ at, o }) =>
  o > 0.01 ? (
    <div
      style={{
        position: "absolute",
        left: at.x + 26,
        top: at.y + 30,
        padding: "3px 9px",
        borderRadius: 6,
        background: "#0F172A",
        border: "1px solid rgba(255,255,255,0.3)",
        color: "#FFFFFF",
        fontFamily: '"Segoe UI", sans-serif',
        fontSize: 15,
        fontWeight: 700,
        opacity: o,
        boxShadow: "0 6px 16px rgba(0,0,0,0.4)",
      }}
    >
      Ctrl
    </div>
  ) : null;

const machinesBase = { appear: 1, status: "12 máquinas encontradas.", cards: [] as FoundCard[], cardsP: 1 };

export const SALA: Record<string, Beat> = {
  // Sala vazia: "Procurar máquinas na rede".
  t06: {
    moves: [{ at: 40, to: center(EMPTY_SEARCH), arc: 40 }],
    clicks: [124],
    cams: [{ at: 132, rect: MACHINES_FOCUS }],
    draw: (t) => ({
      app: {
        tiles: [],
        grid: <EmptyRoom appear={1} hover={hov(t, 108, 134)} press={tap(t, 124)} />,
        toolbar: toolbar(),
        bottom: bottom({ fitOn: 1 }),
        overlay: <MachinesWindow s={{ ...machinesBase, appear: sp(t, 130, 14), status: "Procurando...", cards: [], cardsP: 0, searchPress: 0 }} />,
      },
    }),
  },

  // Máquinas encontradas: marcar e "Parear as selecionadas".
  t07: {
    moves: [
      ...[0, 1, 2, 3, 4, 5].map((i) => ({ at: 96 + i * 18, to: mid(MACHINES, MACHINES_CTRL.check(i)), arc: 10 })),
      { at: 214, to: mid(MACHINES, MACHINES_CTRL.pair), arc: 30 },
    ],
    clicks: [...[0, 1, 2, 3, 4, 5].map((i) => 110 + i * 18), 248],
    draw: (t) => ({
      app: {
        tiles: [],
        grid: <EmptyRoom appear={1} />,
        toolbar: toolbar(),
        bottom: bottom(),
        overlay: (
          <MachinesWindow
            s={{
              ...machinesBase,
              status: t < 34 ? "Procurando..." : "12 máquinas encontradas.",
              cardsP: ramp(t, 34, 80),
              cards: [0, 1, 2, 3, 4, 5].map((i) => card(i, "Sem professor", t >= 110 + i * 18 ? 1 : 0)),
              pairHover: hov(t, 236, 260),
              pairPress: tap(t, 248),
            }}
          />
        ),
      },
    }),
  },

  // O código na tela do professor e na do aluno: "Confere".
  t08: {
    moves: [{ at: 150, to: confere(), arc: 20 }],
    clicks: [210],
    draw: (t) => {
      const codeP = sp(t, 22, 10) * (1 - sp(t, 214, 8));
      const first = t < 20 ? "Conectando..." : t < 216 ? "" : t < 240 ? "Confirmando..." : "Pareada. Já está na sala.";
      return {
        split: sp(t, 30, 18) * (1 - sp(t, 262, 18)),
        student: { pc: 0, pairing: { p: sp(t, 40, 12) * (1 - sp(t, 216, 10)), code: CODE } },
        app: {
          tiles: [],
          grid: <EmptyRoom appear={1} />,
          toolbar: toolbar(),
          bottom: bottom(),
          overlay: (
            <MachinesWindow
              s={{
                ...machinesBase,
                cards: [card(0, first, 1, { code: CODE, codeP, confereHover: hov(t, 196, 220) }), ...[1, 2, 3, 4, 5].map((i) => card(i, "Sem professor", 1))],
              }}
            />
          ),
        },
      };
    },
  },

  // As outras pareadas; a janela fecha e a sala aparece.
  t09: {
    cams: [{ at: 168, rect: FULL }],
    draw: (t) => {
      const closing = sp(t, 160, 14);
      return {
        app: {
          tiles: closing > 0.01 ? grid({ each: (i) => ({ appear: sp(t, 176 + i * 3, 12) }) }) : [],
          grid: closing < 0.99 ? <EmptyRoom appear={1 - closing} /> : undefined,
          toolbar: toolbar(),
          bottom: bottom(),
          overlay: (
            <MachinesWindow
              s={{
                ...machinesBase,
                appear: 1 - closing,
                cards: [0, 1, 2, 3, 4, 5].map((i) => card(i, i === 0 || t >= 20 + i * 22 ? "Pareada. Já está na sala." : "Confirmando...", 1)),
              }}
            />
          ),
        },
      };
    },
  },

  // A grade ao vivo.
  t10: {
    moves: [{ at: 60, to: tileCenter(5), arc: 60 }],
    cams: [
      { at: 40, rect: inside({ x: 120, y: 110, w: 980, h: 560 }) },
      { at: 150, rect: FULL },
    ],
    draw: () => ({ app: { tiles: grid(), toolbar: toolbar(), bottom: bottom() } }),
  },

  // Nome de cada máquina e "Tela indisponível".
  t11: {
    moves: [{ at: 70, to: tileCenter(10), arc: -40 }],
    cams: [
      { at: 50, rect: inside({ x: 380, y: 430, w: 980, h: 400 }) },
      { at: 180, rect: FULL },
    ],
    draw: () => ({
      app: {
        tiles: grid(),
        toolbar: toolbar(),
        bottom: bottom(),
      },
    }),
  },

  // Clique, Ctrl+clique e retângulo.
  t12: (() => {
    const b8 = tileBox(8, LAYOUT_FIT);
    const b9 = tileBox(9, LAYOUT_FIT);
    const p0 = { x: b8.x - 6, y: b8.y - 4 };
    const p1 = { x: b9.x + b9.w + 6, y: b9.y + b9.h + 4 };
    return {
      moves: [
        { at: 14, to: tileCenter(2), arc: 40 },
        { at: 62, to: tileCenter(5), arc: 30 },
        { at: 118, to: p0, arc: 30 },
        { at: 156, to: p1, arc: 0 },
      ],
      clicks: [44, 92],
      draw: (t: number) => {
        const drag = follow(t, 156);
        const rectBox: Box = { x: p0.x, y: p0.y, w: (p1.x - p0.x) * drag, h: (p1.y - p0.y) * drag };
        const dragging = t >= 150 && t < 214;
        const sel = (i: number) => {
          if (t >= 212) return i === 8 || i === 9 ? 1 : 0;
          if (i === 2) return sp(t, 44, 6);
          if (i === 5) return sp(t, 92, 6);
          return 0;
        };
        return {
          app: {
            tiles: grid({ selected: sel }),
            toolbar: toolbar({}, t >= 44 && t < 92),
            bottom: bottom({ status: statusAt(t, [[44, "Selecionada: PC-03"], [92, "Selecionadas: 2"], [212, "Selecionadas: 2"]]) }),
            overlay: (
              <>
                <SelectionRect box={rectBox} o={dragging ? 1 : 1 - ramp(t, 214, 222)} />
                <KeyChip at={tileCenter(5)} o={win(t, 74, 106, 6, 8)} />
              </>
            ),
          },
        };
      },
    } satisfies Beat;
  })(),

  // Tamanho das telas, "Visualizar todos" e "Só online".
  t13: {
    moves: [
      { at: 10, to: sliderKnob(SLIDER_FIT), arc: 40 },
      { at: 46, to: sliderKnob(SLIDER_SMALL), arc: 0 },
      { at: 112, to: center(BOTTOM.fitAll), arc: -20 },
      { at: 176, to: center(BOTTOM.onlineOnly), arc: -14 },
    ],
    clicks: [140, 206, 270],
    cams: [
      { at: 0, rect: { x: 0, y: 110, w: WIN.w, h: 710 } },
      { at: 290, rect: FULL },
    ],
    draw: (t) => {
      const small = sp(t, 46, 24) * (1 - sp(t, 142, 24));
      const onlineOnly = sp(t, 208, 10) * (1 - sp(t, 272, 10));
      return {
        app: {
          tiles: grid({ layoutFrom: LAYOUT_FIT, layoutTo: LAYOUT_SMALL, layoutT: small, onlineOnly }),
          toolbar: toolbar(),
          bottom: bottom({
            slider: mix(SLIDER_FIT, SLIDER_SMALL, small),
            sliderGrab: win(t, 40, 76, 4, 6),
            fitOn: t < 46 || t >= 142 ? 1 : 0,
            fitHover: hov(t, 128, 150),
            fitPress: tap(t, 140),
            onlineOnly,
            onlineHover: hov(t, 194, 280),
          }),
        },
      };
    },
  },

  // "Organizar sala": arrastar uma tela e "Alinhar".
  t14: (() => {
    const from = tileCenter(0);
    const to = { x: from.x + 120, y: from.y + 70 };
    return {
      moves: [
        { at: 8, to: center(BOTTOM.organize), arc: 30 },
        { at: 56, to: from, arc: 40 },
        { at: 92, to, arc: 0 },
        { at: 150, to: center(BOTTOM.align), arc: -30 },
        { at: 214, to: center(BOTTOM.organize), arc: 0 },
      ],
      clicks: [34, 182, 240],
      cams: [
        { at: 0, rect: inside({ x: 0, y: 140, w: WIN.w, h: 680 }) },
        { at: 250, rect: FULL },
      ],
      draw: (t: number) => {
        const organize = sp(t, 36, 8) * (1 - sp(t, 242, 8));
        const drag = follow(t, 92) * (1 - sp(t, 184, 16));
        return {
          app: {
            tiles: grid({
              each: (i) => {
                if (i !== 0) return {};
                const b = tileBox(0, LAYOUT_FIT);
                return { box: lerpBox(b, { ...b, x: b.x + 120, y: b.y + 70 }, drag), selected: win(t, 80, 182, 6, 8) };
              },
            }),
            toolbar: toolbar(),
            bottom: bottom({
              organize,
              organizeHover: hov(t, 22, 46) + hov(t, 228, 252),
              alignHover: hov(t, 168, 194),
              alignPress: tap(t, 182),
              status:
                organize > 0.5
                  ? "Arraste as telas para ficarem como as máquinas estão na sala. Arrastar uma tela selecionada leva junto as outras selecionadas."
                  : "",
            }),
          },
        };
      },
    } satisfies Beat;
  })(),
};

