import { Audio } from "@remotion/media";
import React from "react";
import { AbsoluteFill, Sequence, staticFile, useCurrentFrame } from "remotion";
import { Background } from "../components/Background";
import { ClickRipple, Cursor } from "../components/Cursor";
import { Eyebrow } from "../components/Eyebrow";
import { KineticText } from "../components/KineticText";
import { AppThemeProvider, type AppThemeName } from "../components/app/appTheme";
import { AppWindow } from "../components/app/AppWindow";
import { GRID_BOTTOM, GRID_TOP, WIN } from "../components/app/geometry";
import { CLAMP, clamp01, mix, ramp } from "../lib/motion";
import { COLORS } from "../theme";
import { interpolate } from "remotion";
import { CHAPTERS, type ChapterId } from "./falas";
import { COMPLETO, LEAD, RESUMO, type Timeline } from "./engine";
import { StudentScreen } from "./StudentScreen";
import type { BeatFrame } from "./kit";

/** Onde a janela do app fica na tela: inteira, ou à esquerda com a tela do aluno ao lado. */
const VIEW_FULL = { x: 40, y: 196, w: 1840, h: 856 };
const VIEW_SPLIT = { x: 28, y: 196, w: 1120, h: 856 };
const MONITOR = { x: 1176, y: 300, w: 712 };

const HEADLINE = "font-display text-[52px] leading-[1.1] font-bold tracking-[-0.03em] text-white";

/** Legenda do trecho: capítulo na pílula e a frase do trecho, trocando junto com a fala. */
const Caption: React.FC<{ frame: number; tl: Timeline }> = ({ frame, tl }) => {
  const { SLOTS } = tl;
  const slot = tl.slotAt(frame);
  const caption = slot.fala.caption;
  if (!caption) return null;
  const prev = SLOTS[slot.index - 1];
  const t = frame - slot.from;
  const changed = !prev || prev.fala.caption !== caption;
  const eyebrowChanged = !prev || !prev.fala.caption || prev.fala.chapter !== slot.fala.chapter;
  const swap = changed ? ramp(t, 0, 10) : 1;
  const eyebrowIn = eyebrowChanged ? ramp(t, 0, 12) : 1;
  const next = SLOTS[slot.index + 1];
  const out = next && !next.fala.caption ? ramp(t, slot.dur - 12, slot.dur) : 0;
  return (
    <div style={{ opacity: 1 - out }}>
      <div
        style={{
          position: "absolute",
          left: 0,
          top: 0,
          width: 1920,
          height: 250,
          background: "linear-gradient(180deg, rgba(9,13,22,0.94) 0%, rgba(9,13,22,0.8) 55%, rgba(9,13,22,0) 100%)",
          opacity: eyebrowChanged && !prev?.fala.caption ? ramp(t, 0, 12) : 1,
        }}
      />
      <div className="absolute inset-x-0 flex justify-center" style={{ top: 34 }}>
        <div style={{ opacity: eyebrowIn, transform: `translateY(${(1 - eyebrowIn) * 10}px)` }}>
          <Eyebrow dot="#22D3EE" frame={frame}>
            {CHAPTERS[slot.fala.chapter]}
          </Eyebrow>
        </div>
      </div>
      <div className="absolute inset-x-0 text-center" style={{ top: 90 }}>
        {prev?.fala.caption && changed && swap < 1 ? (
          <div className="absolute inset-x-0" style={{ opacity: 1 - swap, transform: `translateY(${-swap * 24}px)` }}>
            <div className={HEADLINE}>{prev.fala.caption}</div>
          </div>
        ) : null}
        {changed ? (
          <KineticText key={slot.fala.id} text={caption} frame={t} start={4} stagger={2} className={HEADLINE} />
        ) : (
          <div className={HEADLINE}>{caption}</div>
        )}
      </div>
    </div>
  );
};

/** Os capítulos numa barra fina no rodapé, com o atual aceso e o avanço dentro dele. */
const CHAPTER_ORDER: ChapterId[] = ["instalacao", "pareamento", "sala", "energia", "tela", "turma", "programas", "configurar", "botaoDireito", "doisProfessores"];
const chapterSpans = (tl: Timeline) =>
  CHAPTER_ORDER.flatMap((id) => {
    const s = tl.SLOTS.filter((x) => x.fala.chapter === id);
    return s.length ? [{ id, from: s[0].from, to: s[s.length - 1].from + s[s.length - 1].dur }] : [];
  });
const SPANS = new Map<Timeline, ReturnType<typeof chapterSpans>>([
  [COMPLETO, chapterSpans(COMPLETO)],
  [RESUMO, chapterSpans(RESUMO)],
]);
const ChapterBar: React.FC<{ frame: number; tl: Timeline }> = ({ frame, tl }) => {
  const CHAPTER_SPANS = SPANS.get(tl) ?? chapterSpans(tl);
  const first = CHAPTER_SPANS[0].from;
  const last = CHAPTER_SPANS[CHAPTER_SPANS.length - 1].to;
  const show = interpolate(frame, [first - 10, first + 10, last - 10, last + 10], [0, 1, 1, 0], CLAMP);
  if (show <= 0.01) return null;
  const total = last - first;
  return (
    <div style={{ position: "absolute", left: 60, right: 60, bottom: 14, height: 4, display: "flex", gap: 6, opacity: show }}>
      {CHAPTER_SPANS.map((c) => {
        const p = clamp01((frame - c.from) / (c.to - c.from));
        const active = frame >= c.from && frame < c.to;
        return (
          <div key={c.id} style={{ flex: (c.to - c.from) / total, height: 4, borderRadius: 2, background: "rgba(255,255,255,0.12)", overflow: "hidden" }}>
            <div style={{ width: `${p * 100}%`, height: "100%", background: active ? "#22D3EE" : "rgba(34,211,238,0.55)" }} />
          </div>
        );
      })}
    </div>
  );
};

/** A janela do app com câmera, cursor e cliques, e a tela do aluno ao lado quando o trecho pede. */
const AppLayer: React.FC<{ frame: number; f: BeatFrame; hideCursor: boolean; t: number; tl: Timeline }> = ({ frame, f, hideCursor, t, tl }) => {
  const { CLICKS, cameraAt, cursorAt, pressAt } = tl;
  if (!f.app) return null;
  const split = clamp01(f.split ?? 0);
  const view = {
    x: mix(VIEW_FULL.x, VIEW_SPLIT.x, split),
    y: VIEW_FULL.y,
    w: mix(VIEW_FULL.w, VIEW_SPLIT.w, split),
    h: VIEW_FULL.h,
  };
  const rect = cameraAt(frame);
  const scale = Math.min(1.9, view.w / rect.w, view.h / rect.h);
  const tx = view.x + view.w / 2 - (rect.x + rect.w / 2) * scale;
  const ty = view.y + view.h / 2 - (rect.y + rect.h / 2) * scale;
  const cursor = cursorAt(frame);
  const cursorOpacity = hideCursor ? 1 - ramp(t, 0, 8) : 1;
  // Lado a lado, a janela não passa para cima da tela do aluno.
  const clipRight = mix(1920, VIEW_SPLIT.x + VIEW_SPLIT.w + 10, split);
  return (
    <>
      <div style={{ position: "absolute", left: 0, top: 0, width: clipRight, height: 1080, overflow: "hidden" }}>
      <div style={{ position: "absolute", left: 0, top: 0, width: WIN.w, height: WIN.h, transformOrigin: "0 0", transform: `translate(${tx}px, ${ty}px) scale(${scale})` }}>
        <AppWindow frame={frame} tiles={f.app.tiles} toolbar={f.app.toolbar} bottom={f.app.bottom}>
          {f.app.grid ? (
            <div style={{ position: "absolute", left: 0, top: GRID_TOP, width: WIN.w, height: GRID_BOTTOM - GRID_TOP }}>{f.app.grid}</div>
          ) : null}
          {f.app.overlay}
          {CLICKS.map((at) => {
            if (frame < at || frame > at + 16) return null;
            const p = cursorAt(at);
            return <ClickRipple key={at} x={p.x} y={p.y} progress={ramp(frame, at, at + 16)} />;
          })}
          <Cursor x={cursor.x} y={cursor.y} press={pressAt(frame)} opacity={cursorOpacity} />
        </AppWindow>
      </div>
      </div>
      {split > 0.01 && f.student ? (
        <div
          style={{
            position: "absolute",
            left: MONITOR.x + (1 - split) * 260,
            top: MONITOR.y,
            opacity: clamp01(split * 1.4),
          }}
        >
          <StudentScreen s={f.student} frame={frame} width={MONITOR.w} />
        </div>
      ) : null}
    </>
  );
};

export type TutorialProps = { theme: AppThemeName; cut?: "completo" | "resumo" };

/** Tutorial do Midview (completo ou resumo): cada fala da narração é um trecho, e a cena se ajusta ao tempo dela. */
export const Tutorial: React.FC<TutorialProps> = ({ theme, cut = "completo" }) => {
  const frame = useCurrentFrame();
  const tl = cut === "resumo" ? RESUMO : COMPLETO;
  const { SLOTS } = tl;
  const slot = tl.slotAt(frame);
  const t = frame - slot.from;
  const f = slot.beat.draw(t, slot.dur);
  const prev = SLOTS[slot.index - 1];
  const kindChanged = prev ? Boolean(prev.beat.draw(prev.dur - 1, prev.dur).custom) !== Boolean(f.custom) : true;
  const fadeIn = kindChanged ? ramp(t, 0, 14) : 1;
  return (
    <AppThemeProvider name={theme}>
      <AbsoluteFill className="font-sans text-white" style={{ backgroundColor: COLORS.bg }}>
        <Background />
        <AbsoluteFill style={{ opacity: fadeIn }}>
          {f.custom ? (
            // Cenas próprias (abertura, encerramento) leem o frame do trecho, não o do vídeo.
            <Sequence from={slot.from} durationInFrames={slot.dur} layout="none">
              {f.custom}
            </Sequence>
          ) : (
            <AppLayer frame={frame} f={f} hideCursor={Boolean(slot.beat.hideCursor)} t={t} tl={tl} />
          )}
        </AbsoluteFill>
        <Caption frame={frame} tl={tl} />
        <ChapterBar frame={frame} tl={tl} />
        {SLOTS.map((s) => (
          <Audio key={s.fala.id} src={staticFile(`narracao-tutorial/${s.fala.id}.mp3`)} from={s.from + LEAD} name={`Fala ${s.fala.id}`} />
        ))}
      </AbsoluteFill>
    </AppThemeProvider>
  );
};
