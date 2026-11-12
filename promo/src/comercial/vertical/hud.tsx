import React from "react";
import { AbsoluteFill, random, useCurrentFrame, useVideoConfig } from "remotion";
import { FONTS } from "../../fonts";
import { Tomada, trecho, type Ponto } from "../pecas";

/**
 * Peças do Reels no estilo de painel: fundo escuro com grade e grão, moldura
 * com contador de cena e "REC", a fala em texto grande que entra palavra por
 * palavra, a palavra-chave num letreiro neon e a gravação real do Midview
 * numa moldura com cantoneiras. Verde-água (a cor dos botões do programa)
 * para o que resolve, vermelho para a dor. A interface nunca é redesenhada.
 */

export const VERDE = "#2DD4BF";
export const VERMELHO = "#FF3B4E";
const BRANCO = "#F2FFFB";
export const corDoTom = (tom: string): string => (tom === "dor" ? VERMELHO : VERDE);
const neon = (cor: string, forca = 1): string => `0 0 ${10 * forca}px ${cor}, 0 0 ${34 * forca}px ${cor}AA, 0 0 ${80 * forca}px ${cor}55`;

/** O desvio de cores de uma falha de imagem, que dura um instante depois de `desde`. */
const falha = (agora: number, desde: number, dura = 0.28): number => {
  const p = (agora - desde) / dura;
  return p < 0 || p > 1 ? 0 : 1 - p;
};

/** Fundo quase preto com um clarão verde-água, grade fina e grão. */
export const FundoPainel: React.FC = () => {
  const frame = useCurrentFrame();
  const grao =
    "url(\"data:image/svg+xml;utf8,<svg xmlns='http://www.w3.org/2000/svg' width='220' height='220'><filter id='g'><feTurbulence type='fractalNoise' baseFrequency='0.9' numOctaves='1'/><feColorMatrix type='saturate' values='0'/></filter><rect width='220' height='220' filter='url(%23g)'/></svg>\")";
  return (
    <AbsoluteFill style={{ background: "radial-gradient(760px 900px at 50% 44%, rgba(26, 120, 104, 0.36), transparent 72%), #030807" }}>
      <AbsoluteFill
        style={{
          opacity: 0.09,
          backgroundImage:
            "linear-gradient(rgba(120, 255, 225, 0.7) 1px, transparent 1px), linear-gradient(90deg, rgba(120, 255, 225, 0.7) 1px, transparent 1px)",
          backgroundSize: "54px 54px",
        }}
      />
      <AbsoluteFill
        style={{
          opacity: 0.16,
          mixBlendMode: "overlay",
          backgroundImage: grao,
          backgroundPosition: `${Math.floor(random(`gx${frame}`) * 220)}px ${Math.floor(random(`gy${frame}`) * 220)}px`,
        }}
      />
      <AbsoluteFill style={{ background: "radial-gradient(900px 1300px at 50% 50%, transparent 55%, rgba(0, 0, 0, 0.6))" }} />
    </AbsoluteFill>
  );
};

/** A moldura do painel: cruzes nos cantos, contador de cena, "REC" com o tempo do filme e a etiqueta da cena. */
export const Painel: React.FC<{ cena: number; cenas: number; etiqueta: string }> = ({ cena, cenas, etiqueta }) => {
  const frame = useCurrentFrame();
  const { fps } = useVideoConfig();
  const dois = (n: number) => String(Math.floor(n)).padStart(2, "0");
  const relogio = `${dois(frame / fps / 60)}:${dois((frame / fps) % 60)}:${dois(frame % fps)}`;
  const texto: React.CSSProperties = { position: "absolute", fontFamily: FONTS.mono, fontSize: 22, letterSpacing: 3, color: "#8FD9CC" };
  return (
    <AbsoluteFill>
      {[
        [26, 26],
        [1034, 26],
        [26, 1874],
        [1034, 1874],
      ].map(([x, y]) => (
        <div key={`${x}-${y}`} style={{ position: "absolute", left: x, top: y, width: 20, height: 20, opacity: 0.55 }}>
          <div style={{ position: "absolute", left: 9, top: 0, width: 2, height: 20, backgroundColor: "#8FD9CC" }} />
          <div style={{ position: "absolute", left: 0, top: 9, width: 20, height: 2, backgroundColor: "#8FD9CC" }} />
        </div>
      ))}
      <div style={{ ...texto, left: 64, top: 62 }}>
        MIDVIEW / {dois(cena)}
        <span style={{ opacity: 0.45 }}> DE {dois(cenas)}</span>
      </div>
      <div style={{ ...texto, right: 64, top: 62, display: "flex", alignItems: "center", gap: 12 }}>
        <span style={{ width: 12, height: 12, borderRadius: 6, backgroundColor: VERMELHO, opacity: frame % fps < fps / 2 ? 1 : 0.25 }} />
        REC {relogio}
      </div>
      <div style={{ ...texto, left: 64, top: 1836 }}>{etiqueta}</div>
    </AbsoluteFill>
  );
};

export type Fala = { de: number; ate: number; tom: string; palavras: { texto: string; em: number; forte: boolean }[] };

/** A fala em texto grande, em cima da imagem: cada palavra entra no seu instante, a palavra-chave na cor do tom. */
export const Falas: React.FC<{ agora: number; falas: Fala[] }> = ({ agora, falas }) => {
  const fala = falas.find((f) => agora >= f.de && agora < f.ate);
  if (!fala) {
    return null;
  }
  return (
    <div
      style={{
        position: "absolute",
        left: 64,
        right: 64,
        top: 268,
        height: 250,
        display: "flex",
        flexWrap: "wrap",
        alignContent: "flex-end",
        justifyContent: "center",
        columnGap: 18,
        fontFamily: FONTS.display,
        fontSize: 68,
        fontWeight: 800,
        letterSpacing: -1,
        lineHeight: 1.16,
        color: BRANCO,
      }}
    >
      {fala.palavras.map((palavra, i) => {
        const p = trecho(agora, palavra.em, palavra.em + 0.16);
        const cor = corDoTom(fala.tom);
        return (
          <span
            key={i}
            style={{
              opacity: p,
              filter: `blur(${(1 - p) * 10}px)`,
              transform: `translateY(${(1 - p) * 12}px)`,
              ...(palavra.forte ? { color: cor, textShadow: neon(cor, 0.5) } : {}),
            }}
          >
            {palavra.texto}
          </span>
        );
      })}
    </div>
  );
};

/** O letreiro: a palavra-chave enorme, em neon, com um risco de luz embaixo; entra com uma falha de imagem. */
export const Letreiro: React.FC<{ agora: number; texto: string; tom: string; desde: number; y?: number }> = ({ agora, texto, tom, desde, y = 1300 }) => {
  const cor = corDoTom(tom);
  const p = trecho(agora, desde, desde + 0.22);
  const f = falha(agora, desde, 0.34);
  const tamanho = Math.min(150, Math.floor(960 / (texto.length * 0.62)));
  return (
    <div style={{ position: "absolute", left: 40, right: 40, top: y, height: 230, display: "flex", flexDirection: "column", alignItems: "center", justifyContent: "center" }}>
      <div
        style={{
          fontFamily: FONTS.display,
          fontWeight: 800,
          fontSize: tamanho,
          letterSpacing: -2,
          lineHeight: 1,
          whiteSpace: "nowrap",
          color: cor,
          opacity: p,
          filter: `blur(${(1 - p) * 14}px)`,
          transform: `scale(${1.22 - 0.22 * p}) translateX(${f * (random(`l${Math.floor(agora * 30)}`) - 0.5) * 26}px)`,
          textShadow: `${neon(cor)}, ${-7 * f}px 0 ${VERMELHO}, ${7 * f}px 0 #7CF7FF`,
        }}
      >
        {texto}
      </div>
      <div
        style={{
          marginTop: 16,
          height: 4,
          borderRadius: 2,
          width: `${p * Math.min(92, texto.length * 6.2 + 14)}%`,
          background: `linear-gradient(90deg, transparent, ${cor} 20%, #FFFFFF 96%, transparent)`,
          boxShadow: neon(cor, 0.6),
        }}
      />
    </div>
  );
};

/** A gravação real do Midview numa moldura de painel, com cantoneiras; cada troca de tomada entra com uma falha. */
export const TelaPainel: React.FC<{
  agora: number;
  nome: string;
  de: number;
  taxa?: number;
  pontos: Ponto[];
  caixa: { x: number; y: number; largura: number; altura: number };
  aparece?: number;
}> = ({ agora, nome, de, taxa = 1, pontos, caixa, aparece = 1 }) => {
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
  const f = falha(agora, 0, 0.2);
  const canto = (estilo: React.CSSProperties): React.CSSProperties => ({ position: "absolute", width: 44, height: 44, borderColor: VERDE, borderStyle: "solid", ...estilo });
  return (
    <div
      style={{
        position: "absolute",
        left: caixa.x,
        top: caixa.y,
        width: caixa.largura,
        height: caixa.altura,
        opacity: aparece,
        transform: `translateX(${f * (random(`t${Math.floor(agora * 30)}`) - 0.5) * 30}px)`,
      }}
    >
      <div
        style={{
          position: "absolute",
          inset: 0,
          overflow: "hidden",
          backgroundColor: "#0A111E",
          border: `1.5px solid ${VERDE}88`,
          boxShadow: `0 0 34px ${VERDE}44, 0 0 120px ${VERDE}22`,
          filter: `brightness(${1 + f * 0.8})`,
        }}
      >
        <div style={{ width: 1920, height: 1080, transformOrigin: "0 0", transform: `translate(${caixa.largura / 2 - x * z}px, ${caixa.altura / 2 - y * z}px) scale(${z})` }}>
          <Tomada nome={nome} de={de} taxa={taxa} />
        </div>
      </div>
      <div style={canto({ left: -10, top: -10, borderWidth: "4px 0 0 4px" })} />
      <div style={canto({ right: -10, top: -10, borderWidth: "4px 4px 0 0" })} />
      <div style={canto({ left: -10, bottom: -10, borderWidth: "0 0 4px 4px" })} />
      <div style={canto({ right: -10, bottom: -10, borderWidth: "0 4px 4px 0" })} />
    </div>
  );
};

/** A linha pequena embaixo da moldura: o número da parte e o nome da função. */
export const NomeDaParte: React.FC<{ numero: number; de: number; nome: string; y?: number }> = ({ numero, de, nome, y = 1262 }) => (
  <div style={{ position: "absolute", left: 0, right: 0, top: y, textAlign: "center", fontFamily: FONTS.mono, fontSize: 24, letterSpacing: 4, color: VERDE }}>
    {String(numero).padStart(2, "0")} / {String(de).padStart(2, "0")}
    <span style={{ opacity: 0.5 }}> · </span>
    {nome}
  </div>
);

/** Uma faixa de luz que cruza a tela na troca de cena. */
export const CorteDeLuz: React.FC<{ agora: number; em: number }> = ({ agora, em }) => {
  const p = (agora - em + 0.12) / 0.3;
  if (p <= 0 || p >= 1) {
    return null;
  }
  return (
    <AbsoluteFill style={{ overflow: "hidden" }}>
      <div
        style={{
          position: "absolute",
          left: 0,
          right: 0,
          height: 160,
          top: -160 + p * 2080,
          background: `linear-gradient(180deg, transparent, ${VERDE}DD 50%, transparent)`,
          filter: "blur(6px)",
        }}
      />
      <AbsoluteFill style={{ backgroundColor: VERDE, opacity: 0.16 * (1 - Math.abs(p - 0.5) * 2) }} />
    </AbsoluteFill>
  );
};
