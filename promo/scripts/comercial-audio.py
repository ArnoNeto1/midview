"""
Mixa o áudio do comercial (src/comercial): a locução de cada cena no seu
instante, a trilha cortada para a virada cair onde o filme pede, e os efeitos
(clique, trava, varredura), que são sintetizados aqui.

    python scripts/comercial-audio.py

Lê src/comercial/tempo.json e os mp3 de out/comercial/audio/original; grava
out/comercial/public/audio/mix.wav (48 kHz, estéreo, a duração do filme).
Precisa de numpy e scipy; o ffmpeg é o do Remotion.
"""
import json
import subprocess
import sys
import tempfile
from pathlib import Path

import numpy as np
from scipy.io import wavfile
from scipy.signal import butter, sosfilt

SR = 48000
PROMO = Path(__file__).resolve().parent.parent
FFMPEG = PROMO / "node_modules/@remotion/compositor-win32-x64-msvc/ffmpeg.exe"
ORIGINAIS = PROMO / "out/comercial/audio/original"
SAIDA = PROMO / "out/comercial/public/audio/mix.wav"
TEMPO = json.loads((PROMO / "src/comercial/tempo.json").read_text(encoding="utf-8"))

VOZ_RMS_DB = -19.0  # volume da voz, medido só onde há fala
PICO_DB = -1.5


def db(valor):
    return 10 ** (valor / 20)


def ler(nome, pasta):
    """O mp3 como matriz estéreo de -1 a 1."""
    wav = Path(pasta) / f"{nome}.wav"
    subprocess.run(
        [str(FFMPEG), "-hide_banner", "-loglevel", "error", "-y", "-i", str(ORIGINAIS / f"{nome}.mp3"),
         "-ac", "2", "-ar", str(SR), str(wav)],
        check=True,
    )
    _, dados = wavfile.read(wav)
    return dados.astype(np.float64) / 32768


def rampa(n, sobe=True):
    curva = np.linspace(0, 1, n)[:, None]
    return curva if sobe else 1 - curva


def voz(som):
    """Mono no centro, com o volume igualado pela parte falada."""
    mono = som.mean(axis=1)
    janela = int(SR * 0.05)
    quadros = mono[: len(mono) // janela * janela].reshape(-1, janela)
    rms = np.sqrt((quadros ** 2).mean(axis=1))
    falado = rms[rms > db(-45)]
    ganho = db(VOZ_RMS_DB) / np.sqrt((falado ** 2).mean())
    mono = np.tanh(mono * ganho / db(PICO_DB)) * db(PICO_DB)  # limita os picos sem estalo
    borda = int(SR * 0.01)
    mono[:borda] *= np.linspace(0, 1, borda)
    mono[-borda:] *= np.linspace(1, 0, borda)
    return np.stack([mono, mono], axis=1)


def trilha(som):
    """A trilha no tempo do filme: a virada dela cai em impactoNoFilme, e um
    trecho do tema se repete para ela durar até o fim."""
    t = TEMPO["trilha"]
    s = lambda segundos: int(round(segundos * SR))
    impacto, no_filme = s(t["impactoNaTrilha"]), s(t["impactoNoFilme"])
    emenda = s(0.04)
    volta = s(t["repetirDe"])
    ate = volta + s(t["repetirBatidas"] * t["batida"])
    fim = min(s(t["fimNaTrilha"]), len(som))

    antes = som[impacto - no_filme:impacto].copy()
    antes[: s(1.2)] *= rampa(s(1.2))
    # Toca até `ate`, volta para `volta` (mesmo ponto do compasso) e segue até o fim.
    primeira = som[impacto:ate].copy()
    segunda = som[volta - emenda:fim].copy()
    primeira[-emenda:] = primeira[-emenda:] * rampa(emenda, False) + segunda[:emenda] * rampa(emenda)
    depois = np.concatenate([primeira, segunda[emenda:]])
    depois[-s(0.3):] *= rampa(s(0.3), False)
    tudo = np.concatenate([antes, depois])

    pontos = np.array(t["volume"])
    tempo = np.arange(len(tudo)) / SR
    return tudo * np.interp(tempo, pontos[:, 0], pontos[:, 1])[:, None]


def clique():
    n = int(SR * 0.035)
    t = np.arange(n) / SR
    rng = np.random.default_rng(7)
    som = (rng.standard_normal(n) * 0.6 + np.sin(2 * np.pi * 2600 * t)) * np.exp(-t * 190)
    return som * db(-15) / np.abs(som).max()


def trava():
    n = int(SR * 0.16)
    t = np.arange(n) / SR
    som = np.sin(2 * np.pi * (140 - 260 * t) * t) * np.exp(-t * 26)
    som[: int(SR * 0.02)] += clique()[: int(SR * 0.02)] * 2
    return som * db(-13) / np.abs(som).max()


def varredura():
    n = int(SR * 0.55)
    t = np.arange(n) / SR
    rng = np.random.default_rng(11)
    ruido = sosfilt(butter(2, [900, 5200], "bandpass", fs=SR, output="sos"), rng.standard_normal(n))
    som = ruido * np.sin(np.pi * t / t[-1]) ** 2
    return som * db(-20) / np.abs(som).max()


def por(mix, som, em):
    """Soma `som` (mono ou estéreo) ao mix a partir do segundo `em`."""
    if som.ndim == 1:
        som = np.stack([som, som], axis=1)
    inicio = int(round(em * SR))
    som = som[: max(0, len(mix) - inicio)]
    mix[inicio:inicio + len(som)] += som


def main():
    mix = np.zeros((int(round(TEMPO["duracao"] * SR)), 2))
    with tempfile.TemporaryDirectory() as pasta:
        por(mix, trilha(ler(TEMPO["trilha"]["arquivo"], pasta)), 0)
        for fala in TEMPO["locucao"]:
            som = ler(fala["arquivo"], pasta)
            fim = len(som) if fala["ate"] is None else int(fala["ate"] * SR)
            trecho = voz(som[int(fala["de"] * SR):fim])
            por(mix, trecho, fala["em"])
            print(f"{fala['id']}: de {fala['em']:.2f} a {fala['em'] + len(trecho) / SR:.2f} s")
    efeitos = {"clique": clique(), "trava": trava(), "varredura": varredura()}
    for efeito in TEMPO["efeitos"]:
        por(mix, efeitos[efeito["tipo"]], efeito["em"])

    pico = np.abs(mix).max()
    if pico > db(PICO_DB):
        mix *= db(PICO_DB) / pico
    print(f"pico {20 * np.log10(pico):.1f} dB, duração {len(mix) / SR:.2f} s")
    SAIDA.parent.mkdir(parents=True, exist_ok=True)
    wavfile.write(SAIDA, SR, (mix * 32767).astype(np.int16))
    print(SAIDA)


if __name__ == "__main__":
    sys.exit(main())
