import React from "react";
import { interpolate } from "remotion";
import { CLAMP, ramp } from "../lib/motion";
import { Eyebrow } from "./Eyebrow";
import { KineticText } from "./KineticText";

/** Legendas do tour (frames absolutos). O rótulo é o nome do grupo na barra do app. */
export const TOUR_CAPTIONS = [
  { at: 184, eyebrow: "Sala de aula", text: "Monitoramento em tempo real de todas as máquinas" },
  { at: 300, eyebrow: "Sala de aula", text: "Ajuste o tamanho das telas e veja só as ligadas." },
  { at: 484, eyebrow: "Tela", text: "Veja a tela de qualquer aluno." },
  { at: 616, eyebrow: "Tela", text: "Assuma o controle quando precisar." },
  { at: 712, eyebrow: "Tela", text: "Bloqueie todas as telas com um clique." },
  { at: 844, eyebrow: "Turma", text: "Mande um recado para a turma toda." },
  { at: 966, eyebrow: "Turma", text: "Converse com a turma no bate-papo." },
  { at: 1066, eyebrow: "Turma", text: "Envie e recolha arquivos." },
  { at: 1248, eyebrow: "Turma", text: "Abra o mesmo site em todas as máquinas." },
  { at: 1324, eyebrow: "Programas e sites", text: "Bloqueie os programas que tiram o foco." },
  { at: 1432, eyebrow: "Programas e sites", text: "Bloqueie a internet inteira ou só alguns sites." },
  { at: 1520, eyebrow: "Programas e sites", text: "Feche os programas, com aviso aos alunos." },
  { at: 1684, eyebrow: "Energia", text: "Ligue as máquinas pela rede." },
  { at: 1790, eyebrow: "Energia", text: "Agende quando o laboratório liga e desliga." },
  { at: 1984, eyebrow: "Configurar", text: "Prepare o laboratório num painel só." },
  { at: 2066, eyebrow: "Configurar", text: "Atualize todas as máquinas pela rede." },
  { at: 2168, eyebrow: "Configurar", text: "E tudo isso por máquina, no botão direito." },
];

const HEADLINE = "font-display text-[54px] leading-[1.1] font-bold tracking-[-0.03em] text-white";

/** Legenda no topo durante o tour; a mesma função em todas as cenas do app, sem emenda nos cortes. */
export const TourCaption: React.FC<{ abs: number }> = ({ abs }) => {
  let c = -1;
  TOUR_CAPTIONS.forEach((line, i) => {
    if (abs >= line.at) c = i;
  });
  if (c < 0) return null;
  const current = TOUR_CAPTIONS[c];
  const previous = c > 0 ? TOUR_CAPTIONS[c - 1] : null;
  const swap = ramp(abs, current.at, current.at + 10);
  const eyebrowChanged = !previous || previous.eyebrow !== current.eyebrow;
  const eyebrowIn = eyebrowChanged ? ramp(abs, current.at, current.at + 12) : 1;
  const band = interpolate(abs, [TOUR_CAPTIONS[0].at - 6, TOUR_CAPTIONS[0].at + 10], [0, 1], CLAMP);

  return (
    <>
      <div
        style={{
          position: "absolute",
          left: 0,
          top: 0,
          width: 1920,
          height: 260,
          background: "linear-gradient(180deg, rgba(9,13,22,0.94) 0%, rgba(9,13,22,0.8) 55%, rgba(9,13,22,0) 100%)",
          opacity: band,
        }}
      />
      <div className="absolute inset-x-0 flex justify-center" style={{ top: 36 }}>
        {eyebrowChanged && previous ? (
          <div className="absolute" style={{ opacity: 1 - eyebrowIn }}>
            <Eyebrow dot="#22D3EE" frame={abs}>
              {previous.eyebrow}
            </Eyebrow>
          </div>
        ) : null}
        <div style={{ opacity: eyebrowIn, transform: `translateY(${(1 - eyebrowIn) * 10}px)` }}>
          <Eyebrow dot="#22D3EE" frame={abs}>
            {current.eyebrow}
          </Eyebrow>
        </div>
      </div>
      <div className="absolute inset-x-0 text-center" style={{ top: 92 }}>
        {previous && swap < 1 ? (
          <div className="absolute inset-x-0" style={{ opacity: 1 - swap, transform: `translateY(${-swap * 24}px)` }}>
            <div className={HEADLINE}>{previous.text}</div>
          </div>
        ) : null}
        <KineticText key={current.at} text={current.text} frame={abs} start={current.at + 4} stagger={2} className={HEADLINE} />
      </div>
    </>
  );
};
