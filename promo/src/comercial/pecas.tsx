import React from "react";
import { AbsoluteFill, Easing, OffthreadVideo, interpolate, random, staticFile, useCurrentFrame, useVideoConfig } from "remotion";
import tempo from "./tempo.json";

/**
 * Peças do comercial (Comercial.tsx). As tomadas R1 a R6 são gravações da
 * tela do programa de verdade e entram como vídeo: nada da interface é
 * redesenhado aqui. A mídia fica fora do repositório, em
 * out/comercial/public (renderize com --public-dir=out/comercial/public).
 */

export const CIANO = "#00BBD4";
export const BAHNSCHRIFT = "Bahnschrift, 'DIN Alternate', 'Segoe UI', sans-serif";
const SUAVE = Easing.bezier(0.45, 0, 0.2, 1);

/** Quanto do caminho entre `de` e `ate` (segundos do filme) já passou, de 0 a 1, com arrancada e freada suaves. */
export const trecho = (agora: number, de: number, ate: number): number =>
  interpolate(agora, [de, ate], [0, 1], { extrapolateLeft: "clamp", extrapolateRight: "clamp", easing: SUAVE });

/** O segundo do filme, dentro de uma sequência que começa em `inicio`. */
export const useAgora = (inicio: number): number => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  return inicio + frame / fps;
};

/** Uma tomada real, a partir do segundo `de` dela; `taxa` abaixo de 1 a faz andar mais devagar. */
export const Tomada: React.FC<{ nome: string; de: number; taxa?: number }> = ({ nome, de, taxa = 1 }) => {
  const { fps } = useVideoConfig();
  return (
    <OffthreadVideo
      src={staticFile(`tomadas/${nome}.mp4`)}
      trimBefore={Math.round(de * fps)}
      playbackRate={taxa}
      muted
      style={{ width: 1920, height: 1080, maxWidth: "none", display: "block" }}
    />
  );
};

/** Ponto da câmera: no segundo `t` do filme, o ponto (x, y) da tomada fica no centro do quadro, ampliado `z` vezes. */
export type Ponto = { t: number; x: number; y: number; z: number };

/** A tomada em quadro cheio, com a câmera passando pelos pontos dados. */
export const Camera: React.FC<{ inicio: number; pontos: Ponto[]; children: React.ReactNode }> = ({ inicio, pontos, children }) => {
  const agora = useAgora(inicio);
  let a = pontos[0];
  let b = pontos[pontos.length - 1];
  for (let i = 0; i < pontos.length - 1; i++) {
    if (agora >= pontos[i].t) {
      a = pontos[i];
      b = pontos[i + 1];
    }
  }
  const p = a === b ? 0 : trecho(agora, a.t, b.t);
  const z = a.z + (b.z - a.z) * p;
  const x = a.x + (b.x - a.x) * p;
  const y = a.y + (b.y - a.y) * p;
  return (
    <AbsoluteFill style={{ overflow: "hidden", backgroundColor: "#0A111E" }}>
      <div style={{ transformOrigin: "0 0", transform: `translate(${960 - x * z}px, ${540 - y * z}px) scale(${z})` }}>{children}</div>
    </AbsoluteFill>
  );
};

/** Um recorte da tomada (em pixels dela) numa moldura: cantos arredondados, contorno ciano fino e sombra. */
export const Moldura: React.FC<{
  recorte: { x: number; y: number; largura: number; altura: number };
  em: { x: number; y: number };
  escala?: number;
  children: React.ReactNode;
}> = ({ recorte, em, escala = 1, children }) => (
  <div
    style={{
      position: "absolute",
      left: em.x,
      top: em.y,
      width: recorte.largura * escala,
      height: recorte.altura * escala,
      overflow: "hidden",
      borderRadius: 14,
      boxShadow: `0 0 0 1.5px ${CIANO}99, 0 30px 80px rgba(0, 0, 0, 0.65), 0 0 60px ${CIANO}22`,
    }}
  >
    <div
      style={{
        position: "relative",
        width: 1920,
        height: 1080,
        transformOrigin: "0 0",
        transform: `scale(${escala}) translate(${-recorte.x}px, ${-recorte.y}px)`,
      }}
    >
      {children}
    </div>
  </div>
);

type CenaDeIA = { arquivo: string; duracao: number } | null;
const IA = tempo.ia as Record<string, CenaDeIA>;

/** Uma cena de ambiente feita por IA; enquanto o vídeo dela não chega, um aviso no lugar. */
export const CenaIA: React.FC<{ id: string; duracao: number; de?: number; aviso: string; children?: React.ReactNode }> = ({
  id,
  duracao,
  de = 0,
  aviso,
  children,
}) => {
  const { fps } = useVideoConfig();
  const cena = IA[id];
  if (!cena) {
    return (
      <AbsoluteFill style={{ backgroundColor: "#04070D", alignItems: "center", justifyContent: "center" }}>
        {children}
        <div style={{ fontFamily: BAHNSCHRIFT, color: "#7C8CA3", fontSize: 34, letterSpacing: 1, textAlign: "center", lineHeight: 1.5 }}>
          {aviso}
          <div style={{ fontSize: 24, color: "#4B5A70" }}>cena de IA: esperando o vídeo</div>
        </div>
      </AbsoluteFill>
    );
  }
  // O vídeo mais curto que o trecho anda um pouco mais devagar para cobri-lo; o mais longo é cortado.
  const taxa = Math.min(1, (cena.duracao - de) / duracao);
  return (
    <AbsoluteFill style={{ backgroundColor: "black" }}>
      <OffthreadVideo
        src={staticFile(cena.arquivo)}
        trimBefore={Math.round(de * fps)}
        playbackRate={taxa}
        muted
        style={{ width: "100%", height: "100%", objectFit: "cover" }}
      />
    </AbsoluteFill>
  );
};

/** O fundo das janelas flutuantes e do encerramento: a cena de IA 4 ou, sem ela, um fundo desenhado. */
export const Fundo: React.FC<{ duracao: number; de?: number }> = ({ duracao, de = 0 }) => {
  const frame = useCurrentFrame();
  if (IA.cena4) {
    return <CenaIA id="cena4" duracao={duracao} de={de} aviso="" />;
  }
  return (
    <AbsoluteFill
      style={{
        background:
          "radial-gradient(900px 620px at 24% 44%, rgba(211, 217, 226, 0.10), transparent 70%), " +
          `radial-gradient(900px 620px at 76% 44%, ${CIANO}26, transparent 70%), #04070D`,
      }}
    >
      {Array.from({ length: 70 }, (_, i) => {
        const direita = random(`lado${i}`) > 0.5;
        const x = (direita ? 960 : 0) + random(`x${i}`) * 960 + Math.sin(frame / 90 + i) * 14;
        const y = (random(`y${i}`) * 1080 + 1080 - ((frame * (0.25 + random(`v${i}`) * 0.5)) % 1080)) % 1080;
        const tamanho = 1.5 + random(`t${i}`) * 3.5;
        return (
          <div
            key={i}
            style={{
              position: "absolute",
              left: x,
              top: y,
              width: tamanho,
              height: tamanho,
              borderRadius: "50%",
              backgroundColor: direita ? CIANO : "#D3D9E2",
              opacity: 0.15 + random(`o${i}`) * 0.45,
              filter: "blur(0.6px)",
            }}
          />
        );
      })}
    </AbsoluteFill>
  );
};

/** Texto posto na edição (nunca pela IA): o nome da função, embaixo, no centro. */
export const Rotulo: React.FC<{
  inicio: number;
  de: number;
  ate: number;
  y?: number;
  naEsquerda?: boolean;
  children: React.ReactNode;
}> = ({ inicio, de, ate, y = 930, naEsquerda = false, children }) => {
  const agora = useAgora(inicio);
  const visivel = trecho(agora, de, de + 0.35) * (1 - trecho(agora, ate - 0.3, ate));
  return (
    <div
      style={{
        position: "absolute",
        left: naEsquerda ? 36 : 0,
        right: 0,
        top: y,
        display: "flex",
        justifyContent: naEsquerda ? "flex-start" : "center",
        opacity: visivel,
      }}
    >
      <div
        style={{
          fontFamily: BAHNSCHRIFT,
          fontSize: naEsquerda ? 34 : 44,
          fontWeight: 600,
          color: "white",
          letterSpacing: 0.5,
          padding: naEsquerda ? "10px 22px 12px" : "12px 34px 14px",
          borderRadius: 12,
          backgroundColor: "rgba(4, 8, 15, 0.82)",
          borderLeft: `5px solid ${CIANO}`,
          transform: `translateY(${(1 - visivel) * 14}px)`,
        }}
      >
        {children}
      </div>
    </div>
  );
};

/** Legenda pequena em cima de uma moldura. */
export const Legenda: React.FC<{ x: number; y: number; children: React.ReactNode }> = ({ x, y, children }) => (
  <div style={{ position: "absolute", left: x, top: y, fontFamily: BAHNSCHRIFT, fontSize: 26, letterSpacing: 2, color: CIANO }}>{children}</div>
);

/** Varredura de luz ciano de um lado ao outro: marca um salto de tempo, sem fingir que foi instantâneo. */
export const Varredura: React.FC<{ inicio: number; de: number; ate: number }> = ({ inicio, de, ate }) => {
  const p = trecho(useAgora(inicio), de, ate);
  if (p <= 0 || p >= 1) {
    return null;
  }
  return (
    <AbsoluteFill style={{ overflow: "hidden" }}>
      <div
        style={{
          position: "absolute",
          top: 0,
          bottom: 0,
          width: "34%",
          left: `${-34 + p * 134}%`,
          background: `linear-gradient(90deg, transparent, ${CIANO}CC 50%, transparent)`,
          filter: "blur(10px)",
        }}
      />
    </AbsoluteFill>
  );
};

/** Grão de filme leve sobre tudo, para o corte entre a IA e a tela real não saltar. */
export const Grao: React.FC = () => {
  const frame = useCurrentFrame();
  return (
    <AbsoluteFill style={{ opacity: 0.07, mixBlendMode: "overlay", pointerEvents: "none" }}>
      <svg width="1920" height="1080">
        <filter id="grao">
          <feTurbulence type="fractalNoise" baseFrequency="0.85" numOctaves={1} seed={frame % 97} />
          <feColorMatrix type="saturate" values="0" />
        </filter>
        <rect width="1920" height="1080" filter="url(#grao)" />
      </svg>
    </AbsoluteFill>
  );
};
