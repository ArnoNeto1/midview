import React from "react";
import { AbsoluteFill, Audio, Sequence, staticFile, useCurrentFrame, useVideoConfig } from "remotion";
import { MidviewLogo } from "../../components/MidviewLogo";
import { Wordmark } from "../../components/Wordmark";
import { FONTS } from "../../fonts";
import { trecho, type Ponto } from "../pecas";
import { CorteDeLuz, Falas, FundoPainel, Letreiro, NomeDaParte, Painel, TelaPainel, VERDE, VERMELHO, type Fala } from "./hud";
import { SalaEmDesordem } from "./neon";
import tempo from "./reels3-tempo.json";

/**
 * O Reels de 3 minutos (1080x1920, 179,9 s): um passeio pelo Midview no
 * estilo de painel (hud.tsx). Um gancho só de texto e desenho, oito partes
 * com a gravação real (R1 a R6) na moldura, a fala em texto grande em cima e
 * a palavra-chave num letreiro embaixo, e o encerramento com o botão de
 * baixar. A interface nunca é redesenhada; a sala das tomadas é simulada e
 * nada aqui afirma velocidade.
 *
 * Tudo vem de reels3-tempo.json, que scripts/comercial-reels3-audio.py calcula
 * a partir de reels3-roteiro.json (cenas, falas, planos, câmeras, letreiros)
 * e da locução: mude o roteiro, rode o script, renderize.
 */

const CAIXA = { x: 50, y: 540, largura: 980, altura: 700 };

type Plano = { inicio: number; fim: number; tomada: string; de: number; taxa: number; pontos: number[][] };
type Linha = { inicio: number; fim: number; frases: number[]; tom: string; letreiros: string[] };
type Cena = {
  id: string;
  tipo: string;
  de: number;
  duracao: number;
  etiqueta: string;
  falas: Fala[];
  linhas: Record<string, Linha>;
  planos: Plano[];
  cartao?: { nome: string; sub: string };
  numero?: number;
  passos?: number;
};
const CENAS = tempo.cenas as unknown as Cena[];
export const REELS3 = { fps: tempo.fps, largura: 1080, altura: 1920, frames: Math.round(tempo.duracao * tempo.fps) };

const useAgoraDaCena = (): number => useCurrentFrame() / useVideoConfig().fps;
const comoPontos = (pontos: number[][]): Ponto[] => pontos.map(([t, x, y, z]) => ({ t, x, y, z }));

/** O letreiro que vale agora: o último que já entrou. Uma linha com vários tem um por frase dela. */
const LetreiroDaCena: React.FC<{ cena: Cena; agora: number }> = ({ cena, agora }) => {
  const todos = Object.values(cena.linhas).flatMap((linha) =>
    linha.letreiros.map((texto, i) => ({
      texto,
      tom: linha.tom,
      desde: linha.letreiros.length > 1 ? (linha.frases[i] ?? linha.inicio) + 0.1 : linha.inicio + 0.25,
    })),
  );
  const atual = todos.filter((l) => l.desde <= agora).pop();
  return atual ? <Letreiro key={atual.texto + atual.desde} agora={agora} texto={atual.texto} tom={atual.tom} desde={atual.desde} /> : null;
};

/** O gancho: a sala em desordem, em vermelho, e as dores em letreiro; em "o Midview resolve", a logo. */
const Gancho: React.FC<{ cena: Cena }> = ({ cena }) => {
  const agora = useAgoraDaCena();
  const logo = cena.linhas.r03.inicio + 1.2;
  const some = trecho(agora, logo - 0.3, logo + 0.1);
  return (
    <AbsoluteFill>
      <div style={{ position: "absolute", left: 0, top: -55, width: 1080, height: 1920 }}>
        <SalaEmDesordem some={some} cor={VERMELHO} />
      </div>
      <div
        style={{
          position: "absolute",
          left: 0,
          right: 0,
          top: 640,
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          gap: 26,
          opacity: some,
          transform: `scale(${0.85 + 0.15 * some})`,
        }}
      >
        <MidviewLogo size={260} progress={some} glow={0.7} />
        <Wordmark size={140} />
      </div>
      <Falas agora={agora} falas={cena.falas} />
      <LetreiroDaCena cena={cena} agora={agora} />
    </AbsoluteFill>
  );
};

const UmPlano: React.FC<{ plano: Plano }> = ({ plano }) => {
  const agora = useAgoraDaCena();
  return <TelaPainel agora={agora} nome={plano.tomada} de={plano.de} taxa={plano.taxa} pontos={comoPontos(plano.pontos)} caixa={CAIXA} />;
};

const UmaParte: React.FC<{ cena: Cena }> = ({ cena }) => {
  const agora = useAgoraDaCena();
  const { fps } = useVideoConfig();
  const f = (segundos: number) => Math.round(segundos * fps);
  return (
    <AbsoluteFill>
      {cena.planos.map((plano, i) => (
        <Sequence key={i} from={f(plano.inicio)} durationInFrames={Math.max(1, f(plano.fim) - f(plano.inicio))}>
          <UmPlano plano={plano} />
        </Sequence>
      ))}
      <Falas agora={agora} falas={cena.falas} />
      {cena.cartao ? <NomeDaParte numero={cena.numero ?? 0} de={cena.passos ?? 0} nome={cena.cartao.nome} /> : null}
      <LetreiroDaCena cena={cena} agora={agora} />
    </AbsoluteFill>
  );
};

/** O encerramento: a logo, a sala ao vivo, o endereço e o botão de baixar, clicado na frase "Baixe agora". */
const Fim: React.FC<{ cena: Cena }> = ({ cena }) => {
  const agora = useAgoraDaCena();
  const entra = (em: number) => trecho(agora, em, em + 0.35);
  const surge = (p: number): React.CSSProperties => ({ opacity: p, transform: `translateY(${(1 - p) * 20}px)` });
  const gratuito = cena.linhas.r37.inicio;
  const clique = cena.linhas.r38.inicio + 0.9;
  const clicado = agora >= clique;
  const chega = trecho(agora, clique - 0.9, clique - 0.1);
  const aperta = trecho(agora, clique, clique + 0.1) - trecho(agora, clique + 0.1, clique + 0.3);
  const anel = trecho(agora, clique, clique + 0.6);
  return (
    <AbsoluteFill>
      <div style={{ position: "absolute", left: 0, right: 0, top: 280, display: "flex", justifyContent: "center", alignItems: "center", gap: 26, ...surge(entra(0)) }}>
        <MidviewLogo size={128} progress={entra(0)} glow={0.6} />
        <Wordmark size={112} />
      </div>
      <TelaPainel agora={agora} nome="R1" de={0} pontos={[{ t: 0, x: 960, y: 540, z: 0.51 }]} caixa={{ x: 50, y: 452, largura: 980, altura: 551 }} aparece={entra(0.2)} />
      <div style={{ position: "absolute", left: 0, right: 0, top: 1056, textAlign: "center", fontFamily: FONTS.display, fontSize: 60, fontWeight: 800, color: "white", ...surge(entra(0.5)) }}>
        arnoneto1.github.io/midview
      </div>
      <div style={{ position: "absolute", left: 0, right: 0, top: 1184, display: "flex", justifyContent: "center", ...surge(entra(gratuito)) }}>
        <div
          style={{
            position: "relative",
            fontFamily: FONTS.display,
            fontSize: 54,
            fontWeight: 800,
            padding: "20px 54px 24px",
            borderRadius: 60,
            border: `3px solid ${VERDE}`,
            color: clicado ? "#03120F" : VERDE,
            backgroundColor: clicado ? VERDE : "rgba(3, 18, 15, 0.7)",
            boxShadow: `0 0 ${clicado ? 46 : 20}px ${VERDE}${clicado ? "" : "88"}`,
            transform: `scale(${1 - 0.06 * aperta})`,
          }}
        >
          {clicado ? "✓ Gratuito para escolas" : "Baixar o Midview"}
          <div
            style={{
              position: "absolute",
              left: "50%",
              top: "50%",
              width: 760 * anel,
              height: 760 * anel,
              marginLeft: -380 * anel,
              marginTop: -380 * anel,
              borderRadius: "50%",
              border: `2px solid ${VERDE}`,
              opacity: clicado ? 1 - anel : 0,
            }}
          />
        </div>
      </div>
      <div style={{ position: "absolute", left: 0, right: 0, top: 1356, textAlign: "center", fontFamily: FONTS.mono, fontSize: 28, letterSpacing: 2, color: "#8FD9CC", ...surge(entra(gratuito + 0.5)) }}>
        WINDOWS 10 E 11 · TUDO NA REDE DA ESCOLA
      </div>
      {/* O cursor que vem clicar no botão. */}
      <svg
        width={54}
        height={54}
        viewBox="0 0 24 24"
        style={{ position: "absolute", left: 880 - 250 * chega, top: 1480 - 215 * chega, opacity: trecho(agora, clique - 1, clique - 0.8) }}
      >
        <path d="M4 2 L4 19 L8.5 15 L11.5 21.5 L14 20.3 L11 14 L17 14 Z" fill="white" stroke="#03120F" strokeWidth={1.2} strokeLinejoin="round" />
      </svg>
    </AbsoluteFill>
  );
};

export const MidviewReels3min: React.FC = () => {
  const { fps } = useVideoConfig();
  const frame = useCurrentFrame();
  const f = (segundos: number) => Math.round(segundos * fps);
  const indice = Math.max(0, CENAS.findIndex((cena, i) => frame >= f(cena.de) && (i + 1 === CENAS.length || frame < f(CENAS[i + 1].de))));
  const agora = frame / fps;
  return (
    <AbsoluteFill style={{ backgroundColor: "#030807" }}>
      <FundoPainel />
      {CENAS.map((cena, i) => {
        const ate = i + 1 < CENAS.length ? f(CENAS[i + 1].de) : REELS3.frames;
        return (
          <Sequence key={cena.id} from={f(cena.de)} durationInFrames={ate - f(cena.de)}>
            {cena.tipo === "gancho" ? <Gancho cena={cena} /> : cena.tipo === "fim" ? <Fim cena={cena} /> : <UmaParte cena={cena} />}
          </Sequence>
        );
      })}
      {CENAS.slice(1).map((cena) => (
        <CorteDeLuz key={cena.id} agora={agora} em={cena.de} />
      ))}
      <Painel cena={indice + 1} cenas={CENAS.length} etiqueta={CENAS[indice].etiqueta} />
      <AbsoluteFill style={{ backgroundColor: "black", opacity: trecho(agora, tempo.duracao - 0.4, tempo.duracao) }} />
      <Audio src={staticFile("audio/reels3-mix.wav")} />
    </AbsoluteFill>
  );
};
