import React from "react";
import { AbsoluteFill, Audio, Sequence, staticFile, useCurrentFrame, useVideoConfig } from "remotion";
import { MidviewLogo } from "../../components/MidviewLogo";
import { Wordmark } from "../../components/Wordmark";
import { FONTS } from "../../fonts";
import { trecho, type Ponto } from "../pecas";
import { AZUL, Andamento, Cartao, Dores, FundoNeon, Legendas, SalaEmDesordem, TelaReal, type Fala } from "./neon";
import tempo from "./tempo.json";

/**
 * O comercial vertical (1080x1920), no jeito dos anúncios curtos de
 * explicação: um gancho com a dor, "olha como o Midview resolve" e cinco
 * passos, cada um com a gravação real do Midview (R1 a R6) numa moldura azul
 * e a legenda da fala embaixo. A interface nunca é redesenhada. Nada nos
 * 250 px de cima nem nos 350 px de baixo, que os aplicativos cobrem. A sala
 * das tomadas é simulada: nada aqui afirma velocidade.
 *
 * Os tempos vêm de tempo.json, que scripts/comercial-vertical-audio.py calcula
 * a partir da locução; os instantes dos cliques abaixo casam com os efeitos de
 * roteiro.json.
 */

const CAIXA = { x: 50, y: 350, largura: 980, altura: 700 };

type Cena = { id: string; de: number; duracao: number; falas: Fala[] };
const CENAS = tempo.cenas as Cena[];
export const VERTICAL = { fps: tempo.fps, largura: 1080, altura: 1920, frames: Math.round(tempo.duracao * tempo.fps) };

/** Uma tomada dentro do passo: vale até `ate` (segundos do passo); `de` é o segundo dela quando o passo começa. */
type Parte = { ate?: number; nome: string; de: number; pontos: Ponto[] };
type Passo = { numero: number; nome: string; sub: string; partes: Parte[] };

const PASSOS: Record<string, Passo> = {
  // O clique em Bloquear telas (6,85 s da R2) cai 0,75 s depois de o passo começar.
  p1: {
    numero: 1,
    nome: "BLOQUEAR TELAS",
    sub: "cadeado em todas as telas da turma",
    partes: [
      {
        nome: "R2",
        de: 6.1,
        pontos: [
          { t: 0, x: 520, y: 150, z: 2.1 },
          { t: 1.1, x: 520, y: 150, z: 2.1 },
          { t: 1.8, x: 560, y: 420, z: 0.92 },
          { t: 4, x: 560, y: 420, z: 0.98 },
        ],
      },
    ],
  },
  // O clique no botão Fechar programas (16,32 s da R3) aos 0,6 s; depois a tela do aluno (R4), onde o aviso entra aos 15,4 s dela.
  p2: {
    numero: 2,
    nome: "FECHAR PROGRAMAS",
    sub: "o aluno é avisado e tem tempo de salvar",
    partes: [
      {
        ate: 1.9,
        nome: "R3",
        de: 15.72,
        pontos: [
          { t: 0, x: 960, y: 410, z: 1.25 },
          { t: 0.8, x: 960, y: 410, z: 1.25 },
          { t: 1.5, x: 960, y: 600, z: 1.25 },
        ],
      },
      {
        nome: "R4",
        de: 13.1,
        pontos: [
          { t: 1.9, x: 960, y: 350, z: 1 },
          { t: 4.5, x: 960, y: 330, z: 1.06 },
        ],
      },
    ],
  },
  // O clique em Aplicar bloqueio (22,23 s da R5) aos 1,3 s.
  p3: {
    numero: 3,
    nome: "BLOQUEAR PROGRAMAS",
    sub: "o jogo não abre mais, na turma toda",
    partes: [
      {
        nome: "R5",
        de: 20.93,
        pontos: [
          { t: 0, x: 800, y: 590, z: 1.45 },
          { t: 1.25, x: 800, y: 590, z: 1.45 },
          { t: 1.9, x: 800, y: 770, z: 1.45 },
        ],
      },
    ],
  },
  // O clique em Bloquear a internet (11,70 s da R6) aos 1,45 s.
  p4: {
    numero: 4,
    nome: "BLOQUEAR SITES",
    sub: "continua valendo se a máquina reiniciar",
    partes: [
      {
        nome: "R6",
        de: 10.25,
        pontos: [
          { t: 0, x: 960, y: 390, z: 1.4 },
          { t: 1.5, x: 960, y: 390, z: 1.4 },
          { t: 2.1, x: 960, y: 610, z: 1.3 },
          { t: 2.6, x: 960, y: 610, z: 1.3 },
          { t: 3.3, x: 945, y: 300, z: 1.7 },
        ],
      },
    ],
  },
  p5: {
    numero: 5,
    nome: "VER A SALA",
    sub: "as telas de todos os alunos, ao vivo",
    partes: [
      {
        nome: "R1",
        de: 1,
        pontos: [
          { t: 0, x: 960, y: 540, z: 0.51 },
          { t: 1, x: 960, y: 540, z: 0.51 },
          { t: 3.4, x: 700, y: 420, z: 0.95 },
        ],
      },
    ],
  },
};

const DORES = [
  { texto: "um jogando", x: 90, y: 470 },
  { texto: "outro no site errado", x: 330, y: 690 },
  { texto: "ninguém te ouve", x: 150, y: 900 },
];

const useAgoraDaCena = (): number => useCurrentFrame() / useVideoConfig().fps;

/** O gancho: a sala em desordem e as três dores; cada etiqueta entra com a sua fala. */
const Gancho: React.FC<{ cena: Cena }> = ({ cena }) => {
  const agora = useAgoraDaCena();
  const some = trecho(agora, cena.duracao - 0.3, cena.duracao);
  return (
    <AbsoluteFill>
      <div style={{ position: "absolute", left: 0, top: -260, width: 1080, height: 1920 }}>
        <SalaEmDesordem some={some} />
      </div>
      <Dores agora={agora} itens={DORES.map((dor, i) => ({ ...dor, em: cena.falas[i]?.palavras[0].em ?? i }))} some={some} />
      <Legendas agora={agora} falas={cena.falas} />
    </AbsoluteFill>
  );
};

/** "Olha como o Midview resolve": a logo, grande, antes dos passos. */
const Chamada: React.FC<{ cena: Cena }> = ({ cena }) => {
  const agora = useAgoraDaCena();
  const entra = trecho(agora, 0, 0.4);
  const sai = trecho(agora, cena.duracao - 0.3, cena.duracao);
  return (
    <AbsoluteFill>
      <div
        style={{
          position: "absolute",
          left: 0,
          right: 0,
          top: 560,
          display: "flex",
          flexDirection: "column",
          alignItems: "center",
          gap: 26,
          opacity: entra * (1 - sai),
          transform: `scale(${0.85 + 0.15 * entra})`,
        }}
      >
        <MidviewLogo size={230} progress={entra} glow={0.6} />
        <Wordmark size={130} />
      </div>
      <Legendas agora={agora} falas={cena.falas} />
    </AbsoluteFill>
  );
};

const UmPasso: React.FC<{ cena: Cena; passo: Passo }> = ({ cena, passo }) => {
  const agora = useAgoraDaCena();
  const entra = trecho(agora, 0, 0.3);
  const indice = passo.partes.findIndex((parte) => parte.ate === undefined || agora < parte.ate);
  const parte = passo.partes[indice];
  const desde = indice === 0 ? 0 : (passo.partes[indice - 1].ate ?? 0);
  return (
    <AbsoluteFill>
      {/* A moldura pulsa de novo a cada troca de tomada: marca o salto de uma tela para a outra. */}
      <TelaReal key={parte.nome} agora={agora} desde={desde} nome={parte.nome} de={parte.de} pontos={parte.pontos} caixa={CAIXA} aparece={entra} />
      <Legendas agora={agora} falas={cena.falas} />
      <Cartao numero={passo.numero} de={Object.keys(PASSOS).length} nome={passo.nome} sub={passo.sub} resolvido={entra} />
    </AbsoluteFill>
  );
};

/** O encerramento: a logo, a sala ao vivo, o endereço e "Gratuito para escolas". */
const Fim: React.FC = () => {
  const agora = useAgoraDaCena();
  const entra = (em: number) => trecho(agora, em, em + 0.35);
  const surge = (p: number): React.CSSProperties => ({ opacity: p, transform: `translateY(${(1 - p) * 20}px)` });
  const pulso = 1 + 0.9 * (trecho(agora, 2.9, 3.2) - trecho(agora, 3.2, 4));
  return (
    <AbsoluteFill>
      <div style={{ position: "absolute", left: 0, right: 0, top: 350, display: "flex", justifyContent: "center", alignItems: "center", gap: 26, ...surge(entra(0)) }}>
        <MidviewLogo size={128} progress={entra(0)} glow={0.5} />
        <Wordmark size={112} />
      </div>
      <TelaReal
        agora={agora}
        nome="R1"
        de={0}
        pontos={[{ t: 0, x: 960, y: 540, z: 0.51 }]}
        caixa={{ x: 50, y: 540, largura: 980, altura: 551 }}
        aparece={entra(0.2)}
      />
      <div style={{ position: "absolute", left: 0, right: 0, top: 1150, display: "flex", justifyContent: "center", ...surge(entra(0.5)) }}>
        <div
          style={{
            fontFamily: FONTS.display,
            fontSize: 58,
            fontWeight: 800,
            color: "white",
            padding: "16px 34px 20px",
            borderRadius: 20,
            border: `3px solid ${AZUL}`,
            backgroundColor: "rgba(6, 12, 24, 0.8)",
            boxShadow: `0 0 ${24 * pulso}px ${AZUL}, 0 0 ${70 * pulso}px ${AZUL}55`,
          }}
        >
          arnoneto1.github.io/midview
        </div>
      </div>
      <div style={{ position: "absolute", left: 0, right: 0, top: 1300, textAlign: "center", fontFamily: FONTS.display, color: "white" }}>
        <div style={{ fontSize: 62, fontWeight: 800, ...surge(entra(1.1)) }}>Gratuito para escolas</div>
        <div style={{ marginTop: 12, fontSize: 32, color: "#AFC3DE", ...surge(entra(1.5)) }}>Windows 10 e 11 · Tudo na rede da escola</div>
      </div>
    </AbsoluteFill>
  );
};

export const MidviewComercialVertical: React.FC = () => {
  const { fps } = useVideoConfig();
  const frame = useCurrentFrame();
  const f = (segundos: number) => Math.round(segundos * fps);
  const noFim = CENAS[CENAS.length - 1].id === "fim" && frame >= f(CENAS[CENAS.length - 1].de);
  return (
    <AbsoluteFill style={{ backgroundColor: "#050A14" }}>
      <FundoNeon />
      <div style={{ position: "absolute", left: 70, top: 268, display: "flex", alignItems: "center", gap: 14, opacity: noFim ? 0 : 0.92 }}>
        <MidviewLogo size={46} />
        <Wordmark size={40} />
      </div>
      {CENAS.map((cena) => (
        <Sequence key={cena.id} from={f(cena.de)} durationInFrames={f(cena.de + cena.duracao) - f(cena.de)}>
          {cena.id === "gancho" ? (
            <Gancho cena={cena} />
          ) : cena.id === "chamada" ? (
            <Chamada cena={cena} />
          ) : cena.id === "fim" ? (
            <Fim />
          ) : (
            <UmPasso cena={cena} passo={PASSOS[cena.id]} />
          )}
        </Sequence>
      ))}
      <Andamento feito={frame / VERTICAL.frames} />
      <AbsoluteFill style={{ backgroundColor: "black", opacity: trecho(frame / fps, tempo.duracao - 0.4, tempo.duracao) }} />
      <Audio src={staticFile("audio/vertical-mix.wav")} />
    </AbsoluteFill>
  );
};
