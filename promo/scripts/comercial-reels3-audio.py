"""
Áudio e tempos do Reels de 3 minutos (src/comercial/vertical/Reels3.tsx).

    python scripts/comercial-reels3-audio.py

Lê src/comercial/vertical/reels3-roteiro.json e as falas r01..r38 de
out/comercial/audio/original. Grava:
  src/comercial/vertical/reels3-tempo.json     cenas, planos e legendas, já com
                                               os instantes calculados
  out/comercial/public/audio/reels3-mix.wav    a fala, a trilha e os efeitos

O filme fecha na duração pedida (duracaoExata): cada cena dura as suas falas
mais as pausas, e a diferença para o total vai para o fim das cenas (ou sai
das pausas, se as falas passarem do tempo). Não há reconhecimento de fala: as
pausas da voz dizem onde cada frase começa quando há uma pausa por frase;
senão o tempo é repartido pelo tamanho das palavras.
"""
import importlib.util
import json
import re
import sys
import tempfile
from pathlib import Path

import numpy as np
from scipy.io import wavfile

PROMO = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location("vertical", PROMO / "scripts/comercial-vertical-audio.py")
vertical = importlib.util.module_from_spec(spec)
spec.loader.exec_module(vertical)
comum = vertical.comum
SR = comum.SR

ROTEIRO = json.loads((PROMO / "src/comercial/vertical/reels3-roteiro.json").read_text(encoding="utf-8"))
TEMPO = PROMO / "src/comercial/vertical/reels3-tempo.json"
SAIDA = PROMO / "out/comercial/public/audio/reels3-mix.wav"
TOMADAS = {"R1": 36.02, "R2": 15.65, "R3": 60.97, "R4": 56.87, "R5": 42.68, "R6": 29.07}  # segundos de cada tomada
ENTRADA = 0.15  # a primeira fala de uma cena entra um instante depois da imagem
PAUSA_MINIMA = 0.15


def frases(texto):
    """O texto cortado nas pontuações; um pedaço de uma palavra só junta-se ao seguinte."""
    partes = [p.strip() for p in re.findall(r"[^,.:?;!]+[,.:?;!]*", texto) if p.strip()]
    juntas = []
    for parte in partes:
        if juntas and len(juntas[-1].split()) < 2:
            juntas[-1] += " " + parte
        else:
            juntas.append(parte)
    return juntas


def em_grupos(palavras, maximo=4):
    """Até quatro palavras por vez na legenda, em grupos parecidos."""
    quantos = -(-len(palavras) // maximo)
    tamanho = -(-len(palavras) // quantos)
    return [palavras[i:i + tamanho] for i in range(0, len(palavras), tamanho)]


def legendas_da_linha(linha, trechos, inicio):
    """Os grupos de palavras da linha, com o instante (na cena) em que cada palavra entra."""
    peso = lambda texto: len(texto) + 1
    pedacos = frases(linha["texto"])
    if len(trechos) == len(pedacos):
        janelas = trechos
    else:
        janelas = vertical.repartir(trechos[0][0], trechos[-1][1], [peso(p) for p in pedacos])
    zero = trechos[0][0]
    fortes = set(linha.get("forte", []))
    falas = []
    for pedaco, (de, ate) in zip(pedacos, janelas):
        grupos = em_grupos(pedaco.split())
        for grupo, (g_de, g_ate) in zip(grupos, vertical.repartir(de, ate, [sum(map(peso, g)) for g in grupos])):
            entradas = vertical.repartir(g_de, g_ate, [peso(p) for p in grupo])
            falas.append({
                "de": round(inicio + g_de - zero - 0.05, 3),
                "ate": None,
                "tom": linha.get("tom", "ok"),
                "palavras": [
                    {"texto": p, "em": round(inicio + e - zero - 0.03, 3), "forte": p in fortes}
                    for p, (e, _) in zip(grupo, entradas)
                ],
            })
    return falas, [round(inicio + de - zero, 3) for de, _ in janelas]


def trilha(som, impacto_no_filme, duracao):
    """A parte tensa até o impacto; depois o tema, repetido em volta, baixo, sob a voz."""
    s = lambda segundos: int(round(segundos * SR))
    t = ROTEIRO["trilha"]
    impacto = s(t["impactoNaTrilha"])
    emenda = s(0.04)
    volta = s(t["voltaBatidas"] * t["batida"])  # o trecho que se repete
    fim_do_tema = impacto + s(48 * t["batida"])
    partes = [som[impacto - s(impacto_no_filme):fim_do_tema].copy()]
    partes[0][: s(0.6)] *= comum.rampa(s(0.6))
    tamanho = len(partes[0])
    while tamanho < s(duracao) + emenda:
        mais = som[fim_do_tema - volta - emenda:fim_do_tema].copy()
        partes[-1][-emenda:] = partes[-1][-emenda:] * comum.rampa(emenda, False) + mais[:emenda] * comum.rampa(emenda)
        partes.append(mais[emenda:])
        tamanho += len(mais) - emenda
    tudo = np.concatenate(partes)[: s(duracao)]
    tudo[-s(1.5):] *= comum.rampa(s(1.5), False)
    i = impacto_no_filme
    pontos = np.array([[0, 0.25], [i - 0.05, 0.25], [i, 0.3], [i + 0.25, 0.3], [i + 0.6, 0.095], [duracao - 2, 0.095], [duracao - 1.5, 0.2]])
    return tudo * np.interp(np.arange(len(tudo)) / SR, pontos[:, 0], pontos[:, 1])[:, None]


def main():
    padrao = ROTEIRO["pausaPadrao"]
    vozes = {}
    with tempfile.TemporaryDirectory() as pasta:
        for cena in ROTEIRO["cenas"]:
            for linha in cena["linhas"]:
                som = comum.ler(linha["id"], pasta)
                trechos = vertical.falado(som.mean(axis=1))
                de, ate = trechos[0][0], trechos[-1][1]
                vozes[linha["id"]] = (comum.voz(som[int(max(0, de - 0.03) * SR):int((ate + 0.08) * SR)]), trechos, ate - de)
        musica = comum.ler(ROTEIRO["trilha"]["arquivo"], pasta)

    # Quanto falta (ou sobra) para o filme fechar na duração pedida.
    pausas = lambda c: [l.get("pausa", padrao) for l in c["linhas"]]
    natural = ROTEIRO["comeco"] + sum(
        ENTRADA + sum(vozes[l["id"]][2] for l in c["linhas"]) + sum(pausas(c)) + c["folga"] for c in ROTEIRO["cenas"]
    )
    resto = ROTEIRO["duracaoExata"] - natural
    folgas = sum(c["folga"] for c in ROTEIRO["cenas"])
    elastico = folgas + sum(p - PAUSA_MINIMA for c in ROTEIRO["cenas"] for p in pausas(c))
    if resto < 0 and -resto > elastico * 0.7:
        sys.exit(f"As falas não cabem: faltam {-resto:.1f} s e só há {elastico:.1f} s de pausas para encolher.")
    encolhe = 1 if resto >= 0 else (elastico + resto) / elastico
    print(f"natural {natural:.2f} s; {'sobram' if resto >= 0 else 'faltam'} {abs(resto):.2f} s")

    cenas, efeitos, agora, numero = [], [], ROTEIRO["comeco"], 0
    passos = sum(1 for c in ROTEIRO["cenas"] if c["tipo"] == "passo")
    impacto = None
    for cena in ROTEIRO["cenas"]:
        folga = cena["folga"] * (1 + resto / folgas) if resto >= 0 else cena["folga"] * encolhe
        t, falas, linhas, planos, falados = ENTRADA, [], {}, [], []
        for linha in cena["linhas"]:
            voz, trechos, dura = vozes[linha["id"]]
            pausa = linha.get("pausa", padrao)
            pausa = pausa if resto >= 0 else PAUSA_MINIMA + (pausa - PAUSA_MINIMA) * encolhe
            novas, comecos = legendas_da_linha(linha, trechos, t)
            if falas:
                falas[-1]["ate"] = min(falas[-1]["ate"], novas[0]["de"])
            for fala, seguinte in zip(novas, novas[1:]):
                fala["ate"] = seguinte["de"]
            novas[-1]["ate"] = round(t + dura + 0.9, 3)
            falas += novas
            linhas[linha["id"]] = {
                "inicio": round(t, 3), "fim": round(t + dura, 3), "frases": comecos, "tom": linha.get("tom", "ok"),
                "letreiros": linha.get("letreiros") or ([linha["letreiro"]] if linha.get("letreiro") else []),
            }
            falados.append((agora + t, voz))
            if linha["id"] == ROTEIRO["trilha"]["impactoNaLinha"]:
                impacto = agora + t
            for plano in linha.get("planos", []):
                planos.append({**plano, "inicio": round(t + plano.get("atraso", 0), 3)})
            t += dura + pausa
        duracao = round(t + folga, 3)
        falas[-1]["ate"] = min(falas[-1]["ate"], duracao)
        prontos = []
        for i, plano in enumerate(planos):
            fim = planos[i + 1]["inicio"] if i + 1 < len(planos) else duracao
            taxa = plano.get("taxa", 1)
            de = plano["de"]
            if de == "segue":
                anterior = prontos[-1]
                de = round(anterior["de"] + (plano["inicio"] - anterior["inicio"]) * anterior["taxa"], 3)
            ate_na_tomada = de + (fim - plano["inicio"]) * taxa
            if ate_na_tomada > TOMADAS[plano["tomada"]]:
                print(f"  ATENÇÃO {cena['id']}: a {plano['tomada']} acaba aos {TOMADAS[plano['tomada']]} s e o plano vai até {ate_na_tomada:.2f} s")
            prontos.append({"inicio": plano["inicio"], "fim": round(fim, 3), "tomada": plano["tomada"], "de": de, "taxa": taxa, "pontos": plano["pontos"]})
            for tipo, lista in (("clique", plano.get("cliques", [])), ("trava", plano.get("travas", []))):
                for na_tomada in lista:
                    quando = plano["inicio"] + (na_tomada - de) / taxa
                    if plano["inicio"] <= quando < fim:
                        efeitos.append((tipo, agora + quando))
        pronta = {
            "id": cena["id"], "tipo": cena["tipo"], "de": round(agora, 3), "duracao": duracao, "etiqueta": cena.get("etiqueta", ""),
            "falas": falas, "linhas": linhas, "planos": prontos,
        }
        if cena["tipo"] == "passo":
            numero += 1
            pronta.update({"cartao": cena["cartao"], "numero": numero, "passos": passos})
        cenas.append(pronta)
        vozes_da_cena = falados
        for quando, voz in vozes_da_cena:
            efeitos.append(("voz", quando, voz))
        print(f"{cena['id']}: de {agora:.2f} a {agora + duracao:.2f} s ({len(prontos)} planos, {len(falas)} legendas)")
        agora += duracao

    # O último décimo: a soma arredondada das cenas fecha exatamente no total pedido.
    total = ROTEIRO["duracaoExata"]
    cenas[-1]["duracao"] = round(cenas[-1]["duracao"] + total - agora, 3)

    mix = np.zeros((int(round(total * SR)), 2))
    comum.por(mix, trilha(musica, impacto, total), 0)
    sons = {"clique": comum.clique(), "trava": comum.trava()}
    for efeito in efeitos:
        comum.por(mix, efeito[2] if efeito[0] == "voz" else sons[efeito[0]], efeito[1])
    pico = np.abs(mix).max()
    if pico > comum.db(comum.PICO_DB):
        mix *= comum.db(comum.PICO_DB) / pico

    TEMPO.write_text(json.dumps({"fps": ROTEIRO["fps"], "duracao": total, "cenas": cenas}, ensure_ascii=False, separators=(",", ":")) + "\n", encoding="utf-8")
    SAIDA.parent.mkdir(parents=True, exist_ok=True)
    wavfile.write(SAIDA, SR, (mix * 32767).astype(np.int16))
    print(f"duração {total:.2f} s ({len(mix) / SR:.3f} s de áudio), pico {20 * np.log10(pico):.1f} dB, impacto aos {impacto:.2f} s")


if __name__ == "__main__":
    sys.exit(main())
