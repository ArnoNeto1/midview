import React from "react";
import { AbsoluteFill, interpolate, random, useCurrentFrame } from "remotion";
import { FONTS } from "../../fonts";
import { Tomada, trecho, type Ponto } from "../pecas";

/**
 * Peças do comercial vertical (Vertical.tsx), no estilo de explicação em
 * neon: o problema é um desenho laranja, a solução é a gravação real do
 * Midview dentro de uma moldura azul. A interface nunca é redesenhada.
 */

export const LARANJA = "#FF6A2B";
export const AZUL = "#2FB7FF";
export const brilho = (cor: string, forca = 1): string =>
  `drop-shadow(0 0 ${5 * forca}px ${cor}) drop-shadow(0 0 ${16 * forca}px ${cor}AA)`;

/** Mistura duas cores hexadecimais (#RRGGBB), de `a` (p = 0) a `b` (p = 1). */
export const misturar = (a: string, b: string, p: number): string => {
  const canal = (cor: string, i: number) => parseInt(cor.slice(1 + i * 2, 3 + i * 2), 16);
  const meio = [0, 1, 2].map((i) => Math.round(canal(a, i) + (canal(b, i) - canal(a, i)) * p));
  return `rgb(${meio.join(", ")})`;
};

/** Fundo azul-marinho com névoa e uma grade fraca. */
export const FundoNeon: React.FC = () => (
  <AbsoluteFill
    style={{
      background:
        "radial-gradient(900px 700px at 50% 52%, rgba(34, 96, 190, 0.34), transparent 70%), " +
        "radial-gradient(700px 500px at 82% 12%, rgba(120, 150, 190, 0.16), transparent 70%), #050A14",
    }}
  >
    <AbsoluteFill
      style={{
        opacity: 0.1,
        backgroundImage:
          "linear-gradient(rgba(120, 180, 255, 0.6) 1px, transparent 1px), linear-gradient(90deg, rgba(120, 180, 255, 0.6) 1px, transparent 1px)",
        backgroundSize: "90px 90px",
        maskImage: "radial-gradient(700px 800px at 50% 52%, black, transparent 78%)",
      }}
    />
  </AbsoluteFill>
);

/** Um trecho de legenda: fica na tela de `de` a `ate`; cada palavra entra no seu instante (segundos da cena). */
export type Fala = { de: number; ate: number; palavras: { texto: string; em: number; forte?: boolean }[] };

/** A legenda da fala, embaixo da imagem: poucas palavras por vez, a palavra-chave em itálico numa caixa azul. */
export const Legendas: React.FC<{ agora: number; falas: Fala[]; y?: number }> = ({ agora, falas, y = 1100 }) => {
  const fala = falas.find((f) => agora >= f.de && agora < f.ate);
  if (!fala) {
    return null;
  }
  return (
    <div
      style={{
        position: "absolute",
        left: 70,
        right: 70,
        top: y,
        height: 200,
        display: "flex",
        flexWrap: "wrap",
        alignContent: "center",
        justifyContent: "center",
        columnGap: 18,
        rowGap: 6,
        fontFamily: FONTS.display,
        fontSize: 66,
        fontWeight: 700,
        color: "#F4F8FF",
        lineHeight: 1.2,
      }}
    >
      {fala.palavras.map((palavra, i) => {
        const p = trecho(agora, palavra.em, palavra.em + 0.14);
        return (
          <span
            key={i}
            style={{
              opacity: p,
              transform: `translateY(${(1 - p) * 14}px)`,
              ...(palavra.forte
                ? {
                    fontFamily: "Georgia, 'Times New Roman', serif",
                    fontStyle: "italic",
                    fontWeight: 700,
                    padding: "0 16px 4px",
                    borderRadius: 10,
                    backgroundColor: "#2A6FE8",
                    boxShadow: `0 0 22px ${AZUL}88`,
                  }
                : {}),
            }}
          >
            {palavra.texto}
          </span>
        );
      })}
    </div>
  );
};

/** As dores do gancho: etiquetas em neon laranja que entram uma a uma sobre o desenho da sala. */
export const Dores: React.FC<{ agora: number; itens: { texto: string; em: number; x: number; y: number }[]; some: number }> = ({ agora, itens, some }) => (
  <>
    {itens.map((item) => {
      const p = trecho(agora, item.em, item.em + 0.22);
      return (
        <div
          key={item.texto}
          style={{
            position: "absolute",
            left: item.x,
            top: item.y,
            padding: "10px 26px 12px",
            borderRadius: 16,
            border: `3px solid ${LARANJA}`,
            backgroundColor: "rgba(20, 8, 4, 0.86)",
            boxShadow: `0 0 20px ${LARANJA}`,
            fontFamily: FONTS.display,
            fontSize: 44,
            fontWeight: 800,
            color: "#FFE3D6",
            opacity: p * (1 - some),
            transform: `scale(${0.8 + 0.2 * p}) rotate(${(1 - p) * -6}deg)`,
          }}
        >
          {item.texto}
        </div>
      );
    })}
  </>
);

/** A gravação real numa moldura azul brilhante; a câmera passa pelos pontos (tempos contados do início da solução). */
export const TelaReal: React.FC<{
  agora: number;
  nome: string;
  de: number;
  pontos: Ponto[];
  caixa: { x: number; y: number; largura: number; altura: number };
  aparece: number;
  /** Quando esta tomada entrou (segundos da cena): a moldura pulsa a partir daí. */
  desde?: number;
  taxa?: number;
}> = ({ agora, nome, de, pontos, caixa, aparece, desde = 0, taxa = 1 }) => {
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
  const pulso = 1 + 0.9 * (1 - trecho(agora, desde, desde + 0.6));
  return (
    <div
      style={{
        position: "absolute",
        left: caixa.x,
        top: caixa.y,
        width: caixa.largura,
        height: caixa.altura,
        borderRadius: 26,
        overflow: "hidden",
        border: `3px solid ${AZUL}`,
        boxShadow: `0 0 ${26 * pulso}px ${AZUL}, 0 0 ${80 * pulso}px ${AZUL}66, inset 0 0 22px ${AZUL}55`,
        opacity: aparece,
        transform: `scale(${0.9 + 0.1 * aparece})`,
        backgroundColor: "#0A111E",
      }}
    >
      <div
        style={{
          width: 1920,
          height: 1080,
          transformOrigin: "0 0",
          transform: `translate(${caixa.largura / 2 - x * z}px, ${caixa.altura / 2 - y * z}px) scale(${z})`,
        }}
      >
        <Tomada nome={nome} de={de} taxa={taxa} />
      </div>
    </div>
  );
};

/** O cartão de baixo: o número do item e o nome da função; a borda vai do laranja ao azul quando a solução chega. */
export const Cartao: React.FC<{ numero: number; de: number; nome: string; sub: string; resolvido: number; rotulo?: string }> = ({
  numero,
  de,
  nome,
  sub,
  resolvido,
  rotulo = "PASSO",
}) => {
  const cor = misturar(LARANJA, AZUL, resolvido);
  return (
    <div
      style={{
        position: "absolute",
        left: 70,
        right: 70,
        top: 1354,
        height: 192,
        boxSizing: "border-box",
        borderRadius: 22,
        border: `3px solid ${cor}`,
        boxShadow: `0 0 22px ${cor}, inset 0 0 18px rgba(47, 183, 255, 0.12)`,
        backgroundColor: "rgba(6, 12, 24, 0.72)",
        padding: "18px 30px",
        color: "white",
      }}
    >
      <div style={{ display: "flex", justifyContent: "space-between", fontFamily: FONTS.mono, fontSize: 22, letterSpacing: 3, color: cor }}>
        <span>MIDVIEW</span>
        <span>
          {rotulo} {numero} DE {de}
        </span>
      </div>
      <div style={{ marginTop: 8, fontFamily: FONTS.display, fontSize: 48, fontWeight: 800, letterSpacing: 1, lineHeight: 1.15 }}>{nome}</div>
      <div style={{ marginTop: 4, fontFamily: FONTS.display, fontSize: 28, lineHeight: 1.2, color: "#AFC3DE", opacity: resolvido }}>{sub}</div>
    </div>
  );
};

/** A linha de andamento do filme, embaixo do cartão. */
export const Andamento: React.FC<{ feito: number }> = ({ feito }) => (
  <div style={{ position: "absolute", left: 70, right: 70, top: 1560, height: 5, borderRadius: 3, backgroundColor: "rgba(120, 160, 220, 0.2)" }}>
    <div style={{ width: `${feito * 100}%`, height: "100%", borderRadius: 3, backgroundColor: LARANJA, boxShadow: `0 0 10px ${LARANJA}` }} />
  </div>
);

/** Problema 1: a sala em desordem. Seis monitores em neon (laranja, ou a cor dada), cada um piscando com uma coisa diferente. */
export const SalaEmDesordem: React.FC<{ some: number; cor?: string }> = ({ some, cor = LARANJA }) => {
  const frame = useCurrentFrame();
  const cores = ["#FF4D6D", "#FFD23F", "#7CFF6B", "#B388FF", "#FF9F1C", "#4DD0FF"];
  return (
    <svg
      width={980}
      height={730}
      viewBox="0 0 980 730"
      style={{ position: "absolute", left: 50, top: 610, opacity: 1 - some, transform: `scale(${1 - 0.12 * some})`, filter: brilho(cor) }}
    >
      {Array.from({ length: 6 }, (_, i) => {
        const cx = 170 + (i % 3) * 320;
        const cy = 190 + Math.floor(i / 3) * 340;
        const treme = Math.sin(frame / 2.2 + i * 1.7) * 3.2;
        const passo = Math.floor(frame / 4);
        return (
          <g key={i} transform={`translate(${cx}, ${cy}) rotate(${treme})`}>
            <rect x={-125} y={-90} width={250} height={170} rx={16} fill="rgba(255, 106, 43, 0.06)" stroke={cor} strokeWidth={5} />
            <path d="M -34 80 L -44 118 L 44 118 L 34 80" fill="none" stroke={cor} strokeWidth={5} strokeLinejoin="round" />
            {Array.from({ length: 5 }, (_, k) => {
              const s = `${i}-${k}-${passo}`;
              const largura = 30 + random(`w${s}`) * 90;
              return (
                <rect
                  key={k}
                  x={-108 + random(`x${s}`) * (216 - largura)}
                  y={-74 + k * 28}
                  width={largura}
                  height={18}
                  rx={6}
                  fill={cores[Math.floor(random(`c${s}`) * cores.length)]}
                  opacity={0.9}
                  style={{ filter: "none" }}
                />
              );
            })}
          </g>
        );
      })}
      {["?!", "!!", "zzz"].map((sinal, i) => {
        const sobe = (frame * 2.4 + i * 70) % 150;
        return (
          <text
            key={sinal}
            x={300 + i * 200}
            y={380 - sobe * 0.4}
            fontSize={58}
            fontWeight={800}
            fontFamily={FONTS.display}
            fill={cor}
            opacity={interpolate(sobe, [0, 30, 120, 150], [0, 1, 1, 0])}
            textAnchor="middle"
          >
            {sinal}
          </text>
        );
      })}
    </svg>
  );
};
