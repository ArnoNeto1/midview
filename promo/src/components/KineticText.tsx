import React from "react";
import { interpolate, spring, useVideoConfig, type SpringConfig } from "remotion";
import { CLAMP, SPRING } from "../lib/motion";

type KineticTextProps = {
  /** Use "\n" para quebrar linha. */
  text: string;
  frame: number;
  /** Frame em que a primeira palavra entra. */
  start: number;
  /** Frames entre uma palavra e a próxima. */
  stagger?: number;
  /** "mask": a palavra sobe de trás de uma máscara. "rise": sobe com fade e desfoque. */
  mode?: "mask" | "rise";
  config?: Partial<SpringConfig>;
  className?: string;
  style?: React.CSSProperties;
  /** Estilo extra por palavra (ex.: gradiente nas palavras-chave). */
  wordStyle?: (word: string, index: number) => React.CSSProperties | undefined;
};

/**
 * O ponto final da Plus Jakarta Sans tem respiro lateral largo; com tracking negativo
 * ele parece solto da palavra. Aproxima a pontuação final (kerning óptico).
 */
export const tightPunctuation = (word: string): React.ReactNode => {
  const match = /^(.+?)([.,:;!?]+)$/.exec(word);
  if (!match) return word;
  return (
    <>
      {match[1]}
      <span style={{ marginLeft: "-0.1em" }}>{match[2]}</span>
    </>
  );
};

const MASK: React.CSSProperties = {
  display: "inline-block",
  overflow: "hidden",
  verticalAlign: "top",
  padding: "0.06em 0.08em 0.24em",
  margin: "-0.06em -0.08em -0.24em",
};

/** Tipografia cinética: cada palavra entra com spring(), em cascata. */
export const KineticText: React.FC<KineticTextProps> = ({
  text,
  frame,
  start,
  stagger = 3,
  mode = "mask",
  config = SPRING.soft,
  className,
  style,
  wordStyle,
}) => {
  const { fps } = useVideoConfig();
  let index = 0;
  return (
    <div className={className} style={style}>
      {text.split("\n").map((line, li) => (
        <div key={li}>
          {line.split(" ").map((word, wi, words) => {
            const i = index++;
            const p = spring({ frame: frame - start - i * stagger, fps, config });
            const motion: React.CSSProperties =
              mode === "mask"
                ? { transform: `translateY(${(1 - p) * 110}%)` }
                : {
                    transform: `translateY(${(1 - p) * 0.5}em)`,
                    opacity: interpolate(p, [0, 0.7], [0, 1], CLAMP),
                    filter: `blur(${Math.max(0, 1 - p) * 10}px)`,
                  };
            return (
              <React.Fragment key={wi}>
                <span style={mode === "mask" ? MASK : { display: "inline-block" }}>
                  <span style={{ display: "inline-block", ...motion, ...wordStyle?.(word, i) }}>
                    {tightPunctuation(word)}
                  </span>
                </span>
                {wi < words.length - 1 ? " " : null}
              </React.Fragment>
            );
          })}
        </div>
      ))}
    </div>
  );
};
