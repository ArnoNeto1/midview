import React from "react";
import { Eyebrow } from "../components/Eyebrow";
import { KineticText } from "../components/KineticText";
import { PAINEL_GROUPS } from "../components/app/painel/geometry";
import { GROUP_COLOR, P } from "../components/app/painel/palette";
import { ramp } from "../lib/motion";
import type { Beat, Timeline } from "./engine";

const HEADLINE = "font-display text-[54px] leading-[1.1] font-bold tracking-[-0.03em] text-white";

/** Rótulo da legenda: o nome do grupo na barra, com a bolinha na cor do grupo. */
export const eyebrowOf = (beat: Beat) => ({
  text: beat.eyebrow ?? PAINEL_GROUPS.find((g) => g.id === beat.group)?.caption ?? "",
  color: beat.group ? GROUP_COLOR[beat.group] : P.accent,
});

/** Legenda no topo durante o tour: troca junto com o trecho, sem emenda nos cortes. */
export const PainelCaption: React.FC<{ tl: Timeline; frame: number; fadeInAt?: number }> = ({ tl, frame, fadeInAt = 0 }) => {
  const slot = tl.slotAt(frame);
  const beat = slot.beat;
  if (!beat.caption) return null;
  let p = slot.index - 1;
  while (p >= 0 && !tl.SLOTS[p].beat.caption) p--;
  const prev = p >= 0 ? tl.SLOTS[p].beat : null;
  const t = frame - slot.from;
  const changed = !prev || prev.caption !== beat.caption;
  const swap = changed ? ramp(t, 0, 10) : 1;
  const eb = eyebrowOf(beat);
  const prevEb = prev ? eyebrowOf(prev) : null;
  const eyebrowChanged = !prevEb || prevEb.text !== eb.text;
  const eyebrowIn = eyebrowChanged ? ramp(t, 0, 12) : 1;
  const band = ramp(frame, fadeInAt, fadeInAt + 16);
  return (
    <div style={{ opacity: band }}>
      <div
        style={{
          position: "absolute",
          left: 0,
          top: 0,
          width: 1920,
          height: 260,
          background: "linear-gradient(180deg, rgba(9,13,22,0.94) 0%, rgba(9,13,22,0.8) 55%, rgba(9,13,22,0) 100%)",
        }}
      />
      <div className="absolute inset-x-0 flex justify-center" style={{ top: 36 }}>
        {eyebrowChanged && prevEb ? (
          <div className="absolute" style={{ opacity: 1 - eyebrowIn }}>
            <Eyebrow dot={prevEb.color} frame={frame}>
              {prevEb.text}
            </Eyebrow>
          </div>
        ) : null}
        <div style={{ opacity: eyebrowIn, transform: `translateY(${(1 - eyebrowIn) * 10}px)` }}>
          <Eyebrow dot={eb.color} frame={frame}>
            {eb.text}
          </Eyebrow>
        </div>
      </div>
      <div className="absolute inset-x-0 text-center" style={{ top: 92 }}>
        {prev && changed && swap < 1 ? (
          <div className="absolute inset-x-0" style={{ opacity: 1 - swap, transform: `translateY(${-swap * 24}px)` }}>
            <div className={HEADLINE}>{prev.caption}</div>
          </div>
        ) : null}
        {changed ? (
          <KineticText key={beat.id} text={beat.caption} frame={t} start={4} stagger={2} className={HEADLINE} />
        ) : (
          <div className={HEADLINE}>{beat.caption}</div>
        )}
      </div>
    </div>
  );
};
