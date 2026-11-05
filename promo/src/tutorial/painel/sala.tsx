import { WIN, center, type Box } from "../../components/app/geometry";
import { MACHINES, MACHINES_CTRL, MachinesDialog } from "../../components/app/painel/dialogs/configurar";
import { EMPTY_SEARCH, EmptyRoom, confereBox, type FoundCardP } from "../../components/app/painel/dialogs/sala";
import { P_BOTTOM, P_FIT, P_SMALL, pSliderKnob, pSliderValue, pTileBox } from "../../components/app/painel/geometry";
import { mix, ramp } from "../../lib/motion";
import { KeyChip, SelectionRect } from "../cenas/sala";
import { follow, lerpBox, statusAt } from "../cenas/comum";
import { FULL, hov, sp, tap, win, type Beat } from "../kit";
import { P_SLIDER_FIT, inside, pbottom, pgrid, pmid, pname, ptile, ptitle, ptoolbar } from "./kit";

const MACHINES_FOCUS = inside({ x: MACHINES.x - 40, y: MACHINES.y - 20, w: MACHINES.w + 80, h: MACHINES.h + 40 });
const CODE = "482 913";
const SLIDER_SMALL = pSliderValue(P_SMALL.screenW);

const card = (i: number, checked: number, extra: Partial<FoundCardP> = {}): FoundCardP => ({
  name: pname(i),
  address: `192.168.0.${101 + i}`,
  status: "Sem professor",
  checked,
  ...extra,
});
const FIVE = [0, 1, 2, 3, 4];
const machines = { appear: 1, page: 0 as const, status: "15 máquinas encontradas.", found: [] as FoundCardP[], foundP: 1, pairEnabled: true };

/** A sala vazia: sem máquinas, sem selo de "ligadas". */
const empty = () => ({ tiles: [], toolbar: ptoolbar(), bottom: pbottom(), title: { online: 0, total: 0 } });

export const SALA_P: Record<string, Beat> = {
  // Sala vazia: "Procurar máquinas na rede".
  t06: {
    moves: [{ at: 40, to: center(EMPTY_SEARCH), arc: 40 }],
    clicks: [124],
    cams: [{ at: 132, rect: MACHINES_FOCUS }],
    draw: (t) => ({
      app: {
        ...empty(),
        grid: <EmptyRoom appear={1} hover={hov(t, 108, 134)} press={tap(t, 124)} />,
        overlay: <MachinesDialog s={{ ...machines, appear: sp(t, 130, 14), status: "Procurando...", found: [], pairEnabled: false, searchPress: 0 }} />,
      },
    }),
  },

  // As máquinas achadas, já marcadas: "Parear as selecionadas".
  t07: {
    moves: [
      { at: 90, to: pmid(MACHINES, { x: 237, y: 171, w: 16, h: 16 }), arc: 20 },
      { at: 130, to: pmid(MACHINES, { x: 237, y: 171 + 4 * 60, w: 16, h: 16 }), arc: 10 },
      { at: 200, to: pmid(MACHINES, MACHINES_CTRL.pair), arc: 30 },
    ],
    clicks: [236],
    draw: (t) => ({
      app: {
        ...empty(),
        grid: <EmptyRoom appear={1} />,
        overlay: (
          <MachinesDialog
            s={{
              ...machines,
              status: t < 34 ? "Procurando..." : "15 máquinas encontradas.",
              foundP: ramp(t, 34, 80),
              found: FIVE.map((i) => card(i, 1)),
              pairHover: hov(t, 224, 248),
              pairPress: tap(t, 236),
            }}
          />
        ),
      },
    }),
  },

  // O código na tela do professor e na do aluno: "Confere".
  t08: (() => {
    const shown = (codeP: number, outcome: string) => [card(0, 1, { code: CODE, codeP, outcome }), ...[1, 2, 3, 4].map((i) => card(i, 1))];
    const target = confereBox(shown(1, " "), 0);
    return {
      moves: [{ at: 150, to: pmid(MACHINES, target), arc: 20 }],
      clicks: [210],
      draw: (t: number) => {
        const codeP = sp(t, 22, 10) * (1 - sp(t, 214, 8));
        const outcome = t < 20 ? "Conectando..." : t < 216 ? " " : t < 240 ? "Confirmando..." : "Pareada. Já está na sala.";
        const cards = shown(codeP, outcome);
        cards[0].confereHover = hov(t, 196, 220);
        cards[0].conferePress = tap(t, 210);
        return {
          split: sp(t, 30, 18) * (1 - sp(t, 262, 18)),
          student: { pc: 0, pairing: { p: sp(t, 40, 12) * (1 - sp(t, 216, 10)), code: CODE } },
          app: { ...empty(), grid: <EmptyRoom appear={1} />, overlay: <MachinesDialog s={{ ...machines, found: cards }} /> },
        };
      },
    } satisfies Beat;
  })(),

  // As outras pareadas; a janela fecha e a sala aparece.
  t09: {
    cams: [{ at: 168, rect: FULL }],
    draw: (t) => {
      const closing = sp(t, 160, 14);
      const tiles = pgrid({ each: (i) => ({ appear: sp(t, 176 + i * 3, 12) }) });
      return {
        app: {
          tiles: closing > 0.01 ? tiles : [],
          grid: closing < 0.99 ? <EmptyRoom appear={1 - closing} /> : undefined,
          toolbar: ptoolbar(),
          bottom: pbottom(),
          title: closing > 0.01 ? ptitle(pgrid()) : { online: 0, total: 0 },
          overlay: (
            <MachinesDialog
              s={{
                ...machines,
                appear: 1 - closing,
                found: FIVE.map((i) => card(i, 1, { outcome: i === 0 || t >= 20 + i * 22 ? "Pareada. Já está na sala." : "Confirmando..." })),
              }}
            />
          ),
        },
      };
    },
  },

  // A grade ao vivo.
  t10: {
    moves: [{ at: 60, to: ptile(6), arc: 60 }],
    cams: [
      { at: 40, rect: inside({ x: 0, y: 110, w: 1000, h: 470 }) },
      { at: 150, rect: FULL },
    ],
    draw: () => ({ app: { tiles: pgrid(), toolbar: ptoolbar(), bottom: pbottom() } }),
  },

  // Nome de cada máquina e as desligadas.
  t11: {
    moves: [{ at: 70, to: ptile(13), arc: -40 }],
    cams: [
      { at: 50, rect: inside({ x: 560, y: 470, w: 970, h: 350 }) },
      { at: 180, rect: FULL },
    ],
    draw: () => ({ app: { tiles: pgrid(), toolbar: ptoolbar(), bottom: pbottom() } }),
  },

  // Clique, Ctrl+clique e retângulo.
  t12: (() => {
    const b8 = pTileBox(8);
    const b9 = pTileBox(9);
    const p0 = { x: b8.x - 6, y: b8.y - 4 };
    const p1 = { x: b9.x + b9.w + 6, y: b9.y + b9.h + 4 };
    return {
      moves: [
        { at: 14, to: ptile(2), arc: 40 },
        { at: 62, to: ptile(5), arc: 30 },
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
            tiles: pgrid({ selected: sel }),
            toolbar: ptoolbar({}, t >= 44 && t < 92),
            bottom: pbottom({ status: statusAt(t, [[44, `Selecionada: ${pname(2)}`], [92, "Selecionadas: 2"], [212, "Selecionadas: 2"]]) }),
            overlay: (
              <>
                <SelectionRect box={rectBox} o={dragging ? 1 : 1 - ramp(t, 214, 222)} />
                <KeyChip at={ptile(5)} o={win(t, 74, 106, 6, 8)} />
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
      { at: 10, to: pSliderKnob(P_SLIDER_FIT), arc: 40 },
      { at: 46, to: pSliderKnob(SLIDER_SMALL), arc: 0 },
      { at: 112, to: center(P_BOTTOM.fitAll), arc: -20 },
      { at: 176, to: center(P_BOTTOM.onlineOnly), arc: -14 },
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
          tiles: pgrid({ layoutFrom: P_FIT, layoutTo: P_SMALL, layoutT: small, onlineOnly }),
          toolbar: ptoolbar(),
          bottom: pbottom({
            slider: mix(P_SLIDER_FIT, SLIDER_SMALL, small),
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
    const from = ptile(0);
    const to = { x: from.x + 120, y: from.y + 70 };
    return {
      moves: [
        { at: 8, to: center(P_BOTTOM.organize), arc: 30 },
        { at: 56, to: from, arc: 40 },
        { at: 92, to, arc: 0 },
        { at: 150, to: center(P_BOTTOM.align), arc: -30 },
        { at: 214, to: center(P_BOTTOM.organize), arc: 0 },
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
            tiles: pgrid({
              each: (i) => {
                if (i !== 0) return {};
                const b = pTileBox(0);
                return { box: lerpBox(b, { ...b, x: b.x + 120, y: b.y + 70 }, drag), selected: win(t, 80, 182, 6, 8) };
              },
            }),
            toolbar: ptoolbar(),
            bottom: pbottom({
              organize,
              organizeHover: hov(t, 22, 46) + hov(t, 228, 252),
              alignHover: hov(t, 168, 194),
              alignPress: tap(t, 182),
              status:
                organize > 0.5 ? "Arraste as telas para ficarem como as máquinas estão na sala. Arrastar uma tela selecionada leva junto as outras selecionadas." : "",
            }),
          },
        };
      },
    } satisfies Beat;
  })(),
};
