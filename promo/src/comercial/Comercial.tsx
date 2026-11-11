import React from "react";
import { AbsoluteFill, Audio, Sequence, staticFile, useVideoConfig } from "remotion";
import { MidviewLogo } from "../components/MidviewLogo";
import { Wordmark } from "../components/Wordmark";
import { BAHNSCHRIFT, CIANO, Camera, CenaIA, Fundo, Grao, Legenda, Moldura, Rotulo, Tomada, Varredura, trecho, useAgora } from "./pecas";
import tempo from "./tempo.json";

/**
 * O comercial de cerca de 54 s (promo/roteiro-comercial-ia.md): quatro cenas,
 * montadas em cima da locução (tempo.json; o áudio é mixado por
 * scripts/comercial-audio.py). Todo quadro em que o Midview aparece é uma das
 * tomadas reais R1 a R6; a IA só faz o ambiente (cenas 1, 2 e o fundo da 4).
 * A sala das tomadas é simulada: nada aqui afirma velocidade.
 */

export const COMERCIAL = { fps: tempo.fps, frames: Math.round(tempo.duracao * tempo.fps) };

/** Os cortes, em segundos do filme. */
const T = {
  virada: 10.87, // "O caos acabou": do caos para o pulso de luz
  janela: 14.72, // "Este é o Midview": a janela real entra
  acao: 21.72, // cena 3
  dividida: 26.42, // Fechar programas: professor e aluno lado a lado
  salto: 29.72, // os 30 segundos do aviso, pulados
  programas: 31.67, // Bloquear programas
  sites: 34.02, // "Bloqueados."
  fluido: 37.32, // a grade ao vivo
  fim: 41.32, // cena 4
  total: tempo.duracao,
};

/** Um trecho do filme, de `de` a `ate` segundos. */
const Trecho: React.FC<{ de: number; ate: number; solto?: boolean; children: React.ReactNode }> = ({ de, ate, solto = false, children }) => {
  const { fps } = useVideoConfig();
  const inicio = Math.round(de * fps);
  return (
    <Sequence from={inicio} durationInFrames={Math.round(ate * fps) - inicio} layout={solto ? "none" : "absolute-fill"}>
      {children}
    </Sequence>
  );
};

/** A janela real sobe de dentro das partículas, inclinada, e assenta de frente até ocupar o quadro. */
const JanelaQueSobe: React.FC = () => {
  const agora = useAgora(T.janela);
  const sobe = trecho(agora, T.janela, T.janela + 1.6);
  const assenta = trecho(agora, T.janela + 2.0, T.janela + 3.6);
  const escala = 0.62 + 0.38 * assenta;
  return (
    <AbsoluteFill style={{ perspective: 2400 }}>
      <div
        style={{
          width: 1920,
          height: 1080,
          overflow: "hidden",
          opacity: trecho(agora, T.janela, T.janela + 0.5),
          borderRadius: 18 * (1 - assenta),
          boxShadow: `0 0 0 ${1.5 * (1 - assenta)}px ${CIANO}AA, 0 40px 120px rgba(0, 0, 0, 0.7), 0 0 90px ${CIANO}33`,
          transform:
            `translateY(${(1 - sobe) * 620}px) scale(${escala}) ` +
            `rotateX(${(14 - 8 * sobe) * (1 - assenta)}deg) rotateY(${(-16 + 6 * sobe) * (1 - assenta)}deg)`,
        }}
      >
        <Tomada nome="R1" de={1} />
      </div>
    </AbsoluteFill>
  );
};

/** Fechar programas: à esquerda a janela do professor (R3), à direita a tela do aluno com o aviso (R4). */
const ProfessorEAluno: React.FC = () => {
  const dialogo = { x: 631, y: 117, largura: 658, altura: 839 };
  const aluno = { x: 440, y: 0, largura: 1040, altura: 840 };
  return (
    <AbsoluteFill>
      <Fundo duracao={T.programas - T.dividida} de={2} />
      <Legenda x={100} y={72}>
        NO PROFESSOR
      </Legenda>
      <Moldura recorte={dialogo} em={{ x: 100, y: 120 }}>
        <Tomada nome="R3" de={15.62} />
      </Moldura>
      <Legenda x={860} y={105}>
        NA TELA DO ALUNO
      </Legenda>
      <Moldura recorte={aluno} em={{ x: 860, y: 153 }} escala={0.92}>
        <Trecho de={0} ate={T.salto - T.dividida} solto>
          <Tomada nome="R4" de={14.5} />
        </Trecho>
        {/* 30 segundos depois: os programas fecham. */}
        <Trecho de={T.salto - T.dividida} ate={T.programas - T.dividida} solto>
          <Tomada nome="R4" de={44.3} />
        </Trecho>
        <Varredura inicio={T.dividida} de={T.salto - 0.3} ate={T.salto + 0.3} />
      </Moldura>
    </AbsoluteFill>
  );
};

/** Cena 4: a janela real em quadro cheio, depois na metade direita; a logo e o endereço na esquerda. */
const Encerramento: React.FC = () => {
  const agora = useAgora(T.fim);
  const recua = trecho(agora, T.fim + 1.8, T.fim + 3.0);
  const entra = (atraso: number) => trecho(agora, T.fim + 2.6 + atraso, T.fim + 3.3 + atraso);
  const surge = (p: number): React.CSSProperties => ({ opacity: p, transform: `translateY(${(1 - p) * 18}px)` });
  return (
    <AbsoluteFill>
      <Fundo duracao={T.total - T.fim} />
      <AbsoluteFill style={{ perspective: 2400 }}>
        <div
          style={{
            width: 1920,
            height: 1080,
            overflow: "hidden",
            borderRadius: 18 * recua,
            boxShadow: `0 0 0 ${1.5 * recua}px ${CIANO}AA, 0 40px 120px rgba(0, 0, 0, 0.7), 0 0 90px ${CIANO}33`,
            transform: `translate(${420 * recua}px, ${20 * recua}px) scale(${1 - 0.55 * recua}) rotateY(${-14 * recua}deg) rotateX(${3 * recua}deg)`,
          }}
        >
          <Tomada nome="R1" de={0} />
        </div>
      </AbsoluteFill>
      <div style={{ position: "absolute", left: 110, top: 250, width: 840, fontFamily: BAHNSCHRIFT, color: "white" }}>
        <div style={{ display: "flex", alignItems: "center", gap: 30, ...surge(entra(0)) }}>
          <MidviewLogo size={132} progress={entra(0)} />
          <Wordmark size={104} />
        </div>
        <div style={{ marginTop: 56, fontSize: 60, fontWeight: 600, letterSpacing: 0.5, ...surge(entra(0.35)) }}>
          arnoneto1.github.io/midview
        </div>
        <div style={{ marginTop: 14, width: 150, height: 4, borderRadius: 2, backgroundColor: CIANO, ...surge(entra(0.35)) }} />
        <div style={{ marginTop: 30, fontSize: 26, color: "#C4CEDC", letterSpacing: 0.5, whiteSpace: "nowrap", ...surge(entra(0.7)) }}>
          Gratuito para escolas · Windows 10 e 11 · Tudo na rede da escola
        </div>
      </div>
      <AbsoluteFill style={{ backgroundColor: "black", opacity: trecho(agora, T.total - 0.45, T.total) }} />
    </AbsoluteFill>
  );
};

export const MidviewComercial: React.FC = () => (
  <AbsoluteFill style={{ backgroundColor: "black" }}>
    {/* Cena 1: a dor (IA). */}
    <Trecho de={0} ate={T.virada}>
      <CenaIA id="cena1" duracao={T.virada} aviso="Cena 1 · o caos no laboratório" />
    </Trecho>

    {/* Cena 2: a virada (IA) e a janela real. */}
    <Trecho de={T.virada} ate={T.janela}>
      <CenaIA id="cena2" duracao={T.janela - T.virada} aviso="Cena 2 · o pulso de luz e os cadeados" />
    </Trecho>
    <Trecho de={T.janela} ate={T.acao}>
      <Fundo duracao={T.acao - T.janela} />
      <JanelaQueSobe />
    </Trecho>

    {/* Cena 3: a ação, toda em tela real. */}
    <Trecho de={T.acao} ate={T.dividida}>
      {/* "Um clique": perto do botão Bloquear telas; depois a grade inteira virando cadeado. */}
      <Camera
        inicio={T.acao}
        pontos={[
          { t: T.acao, x: 520, y: 193, z: 2.8 },
          { t: T.acao + 0.95, x: 520, y: 193, z: 2.8 },
          { t: T.acao + 1.7, x: 960, y: 540, z: 1 },
        ]}
      >
        <Tomada nome="R2" de={6.25} />
      </Camera>
      <Rotulo inicio={T.acao} de={T.acao + 1.9} ate={T.dividida - 0.1}>
        Bloquear telas
      </Rotulo>
    </Trecho>
    <Trecho de={T.dividida} ate={T.programas}>
      <ProfessorEAluno />
      <Rotulo inicio={T.dividida} de={T.dividida + 0.3} ate={T.programas - 0.1} y={985}>
        Fechar programas
      </Rotulo>
    </Trecho>
    <Trecho de={T.programas} ate={T.sites}>
      <Camera
        inicio={T.programas}
        pontos={[
          { t: T.programas, x: 860, y: 545, z: 1.12 },
          { t: T.sites, x: 860, y: 545, z: 1.2 },
        ]}
      >
        <Tomada nome="R5" de={21} />
      </Camera>
    </Trecho>
    <Trecho de={T.sites} ate={T.fluido}>
      <Camera
        inicio={T.sites}
        pontos={[
          { t: T.sites, x: 860, y: 537, z: 1.2 },
          { t: T.sites + 1.1, x: 860, y: 537, z: 1.24 },
          { t: T.sites + 2.1, x: 945, y: 330, z: 1.9 },
          { t: T.fluido, x: 945, y: 330, z: 1.95 },
        ]}
      >
        <Tomada nome="R6" de={11.55} />
      </Camera>
    </Trecho>
    {/* Sai antes de a câmera chegar perto da frase do diálogo de sites. */}
    <Trecho de={T.programas} ate={T.sites + 1.6}>
      <Rotulo inicio={T.programas} de={T.programas + 0.2} ate={T.sites + 1.5} y={940} naEsquerda>
        Bloquear programas e sites
      </Rotulo>
    </Trecho>
    <Trecho de={T.fluido} ate={T.fim}>
      {/* "Tudo fluido": a grade ao vivo, o controle de tamanho mexendo. */}
      <Camera
        inicio={T.fluido}
        pontos={[
          { t: T.fluido, x: 960, y: 540, z: 1 },
        ]}
      >
        <Tomada nome="R1" de={13.3} />
      </Camera>
    </Trecho>

    {/* Cena 4: encerramento. */}
    <Trecho de={T.fim} ate={T.total}>
      <Encerramento />
    </Trecho>

    <Grao />
    <Audio src={staticFile("audio/mix.wav")} />
  </AbsoluteFill>
);
