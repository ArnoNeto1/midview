"""
Áudio e tempos do comercial vertical (src/comercial/vertical).

    python scripts/comercial-vertical-audio.py

Lê src/comercial/vertical/roteiro.json e as falas de out/comercial/audio/original
(uma por cena). Grava:
  src/comercial/vertical/tempo.json            quando cada cena começa e quando
                                               cada palavra da legenda entra
  out/comercial/public/audio/vertical-mix.wav  a fala, a trilha e os efeitos

Cada cena dura a sua fala mais a folga. Não há reconhecimento de fala: as
pausas da voz dizem onde cada trecho de legenda começa quando há uma pausa por
trecho; senão o tempo é repartido pelo tamanho das palavras.
"""
import importlib.util
import json
import sys
import tempfile
from pathlib import Path

import numpy as np
from scipy.io import wavfile

PROMO = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("comum", PROMO / "scripts/comercial-audio.py")
comum = importlib.util.module_from_spec(spec)
spec.loader.exec_module(comum)
SR = comum.SR

ROTEIRO = json.loads((PROMO / "src/comercial/vertical/roteiro.json").read_text(encoding="utf-8"))
TEMPO = PROMO / "src/comercial/vertical/tempo.json"
SAIDA = PROMO / "out/comercial/public/audio/vertical-mix.wav"
ANTES_DA_FALA = 0.1  # a fala começa um instante depois da imagem


def falado(mono):
    """Os trechos com voz (início, fim) em segundos."""
    janela = int(SR * 0.02)
    quadros = mono[: len(mono) // janela * janela].reshape(-1, janela)
    com_voz = np.sqrt((quadros ** 2).mean(axis=1)) > comum.db(-38)
    trechos, inicio, silencio = [], None, 0
    for i, voz in enumerate(com_voz):
        if voz:
            inicio = i if inicio is None else inicio
            silencio = 0
        elif inicio is not None:
            silencio += 1
            if silencio * 0.02 >= 0.15:
                trechos.append((inicio * 0.02, (i - silencio + 1) * 0.02))
                inicio = None
    if inicio is not None:
        trechos.append((inicio * 0.02, len(com_voz) * 0.02))
    return trechos


def repartir(de, ate, pesos):
    """Corta [de, ate] em pedaços proporcionais aos pesos."""
    cortes = de + (ate - de) * np.concatenate([[0], np.cumsum(pesos)]) / sum(pesos)
    return list(zip(cortes[:-1], cortes[1:]))


def legendas(cena, trechos):
    """As falas da cena: quando cada trecho fica na tela e quando cada palavra entra."""
    grupos = cena["legendas"]
    if not grupos:
        return []
    tamanho = lambda palavra: len(palavra["texto"]) + 1
    if len(trechos) == len(grupos):
        janelas = trechos
    else:
        janelas = repartir(trechos[0][0], trechos[-1][1], [sum(map(tamanho, g)) for g in grupos])
    falas = []
    for i, (grupo, (de, ate)) in enumerate(zip(grupos, janelas)):
        entradas = repartir(de, ate, [tamanho(p) for p in grupo])
        falas.append({
            "de": 0 if i == 0 else round(ANTES_DA_FALA + de - 0.06, 3),
            "ate": None,
            "palavras": [
                {"texto": p["texto"], "em": round(ANTES_DA_FALA + inicio - 0.04, 3), "forte": bool(p.get("forte"))}
                for p, (inicio, _) in zip(grupo, entradas)
            ],
        })
    for fala, seguinte in zip(falas, falas[1:]):
        fala["ate"] = seguinte["de"]
    return falas


def trilha(som, impacto_no_filme, duracao):
    """A trilha: a parte tensa até o impacto, que cai onde a logo entra; depois o tema, baixo, sob a voz."""
    s = lambda segundos: int(round(segundos * SR))
    impacto = s(ROTEIRO["trilha"]["impactoNaTrilha"])
    tudo = som[impacto - s(impacto_no_filme):impacto + s(duracao - impacto_no_filme)].copy()
    tudo[: s(0.6)] *= comum.rampa(s(0.6))
    tudo[-s(1.2):] *= comum.rampa(s(1.2), False)
    t = impacto_no_filme
    pontos = np.array([[0, 0.25], [t - 0.05, 0.25], [t, 0.3], [t + 0.25, 0.3], [t + 0.6, 0.095], [duracao - 1.6, 0.095], [duracao - 1.2, 0.2]])
    return tudo * np.interp(np.arange(len(tudo)) / SR, pontos[:, 0], pontos[:, 1])[:, None]


def main():
    efeitos = {"clique": comum.clique(), "trava": comum.trava(), "varredura": comum.varredura()}
    cenas, vozes, agora = [], [], ROTEIRO["comeco"]
    with tempfile.TemporaryDirectory() as pasta:
        for cena in ROTEIRO["cenas"]:
            som = comum.ler(cena["arquivo"], pasta)
            trechos = falado(som.mean(axis=1))
            fim_da_fala = trechos[-1][1]
            duracao = round(ANTES_DA_FALA + fim_da_fala + cena["folga"], 3)
            falas = legendas(cena, trechos)
            if falas:
                falas[-1]["ate"] = duracao
            cenas.append({"id": cena["id"], "de": round(agora, 3), "duracao": duracao, "falas": falas})
            vozes.append((agora + ANTES_DA_FALA, comum.voz(som[: int((fim_da_fala + 0.08) * SR)]), cena))
            print(f"{cena['id']}: de {agora:.2f} a {agora + duracao:.2f} s; fala {fim_da_fala:.2f} s em {len(trechos)} trecho(s)")
            agora += duracao
        total = round(agora, 3)
        mix = np.zeros((int(round(total * SR)), 2))
        chamada = next(c for c in cenas if c["id"] == ROTEIRO["trilha"]["impactoNaCena"])
        comum.por(mix, trilha(comum.ler(ROTEIRO["trilha"]["arquivo"], pasta), chamada["de"], total), 0)
    for (em, som, cena), marcada in zip(vozes, cenas):
        comum.por(mix, som, em)
        for efeito in cena["efeitos"]:
            comum.por(mix, efeitos[efeito["tipo"]], marcada["de"] + efeito["em"])

    pico = np.abs(mix).max()
    if pico > comum.db(comum.PICO_DB):
        mix *= comum.db(comum.PICO_DB) / pico
    TEMPO.write_text(json.dumps({"fps": ROTEIRO["fps"], "duracao": total, "cenas": cenas}, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    SAIDA.parent.mkdir(parents=True, exist_ok=True)
    wavfile.write(SAIDA, SR, (mix * 32767).astype(np.int16))
    print(f"duração {total:.2f} s, pico {20 * np.log10(pico):.1f} dB")


if __name__ == "__main__":
    sys.exit(main())
