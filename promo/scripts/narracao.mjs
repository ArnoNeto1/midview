/**
 * Prepara as falas da narração para o vídeo: corta o silêncio do começo e do
 * fim, encurta as pausas longas e iguala o volume de todas.
 *
 *   node scripts/narracao.mjs <pasta com os mp3 originais>
 *
 * Grava public/narracao/<id>.mp3 e mostra quantos frames cada fala dura, para
 * conferir os inícios em src/narration.ts (uma fala não pode invadir a próxima).
 */
import { spawnSync } from "node:child_process";
import { mkdirSync, readdirSync } from "node:fs";
import { basename, join } from "node:path";
import { fileURLToPath } from "node:url";

const FPS = 30;
const SILENCE_DB = -42; // abaixo disso conta como silêncio
const MAX_PAUSE = 0.3; // pausa mais longa que fica entre duas frases (s)
const LEAD = 0.05; // silêncio mantido antes da primeira palavra (s)
const TAIL = 0.12; // e depois da última (s)
const TEMPO = 1; // 1 = ritmo original; acima de 1 acelera sem mudar o tom da voz
const TARGET_LUFS = -16;
const MAX_TRUE_PEAK = -1.5;

const source = process.argv[2];
if (!source) {
  console.error("Uso: node scripts/narracao.mjs <pasta com os mp3 originais>");
  process.exit(1);
}
const target = fileURLToPath(new URL("../public/narracao/", import.meta.url));
mkdirSync(target, { recursive: true });

const cli = fileURLToPath(new URL("../node_modules/@remotion/cli/remotion-cli.js", import.meta.url));
const ffmpeg = (args) => {
  const run = spawnSync(process.execPath, [cli, "ffmpeg", "-hide_banner", ...args], { encoding: "utf8" });
  if (run.status !== 0) throw new Error(run.stderr);
  return run.stderr;
};

/** Duração, trechos de silêncio e volume da fala original. */
const analyze = (file) => {
  // O loudnorm reamostra o áudio e alonga o fim, então a duração vem de uma passada sem ele.
  const log = ffmpeg(["-i", file, "-af", `silencedetect=noise=${SILENCE_DB}dB:d=0.1`, "-f", "null", "-"]);
  const all = (re) => [...log.matchAll(re)].map((m) => Number(m[1]));
  const [h, m, s] = [...log.matchAll(/time=(\d+):(\d+):([\d.]+)/g)].at(-1).slice(1).map(Number);
  const duration = h * 3600 + m * 60 + s;
  const ends = all(/silence_end: ([\d.]+)/g);
  const silences = all(/silence_start: ([\d.]+)/g).map((start, i) => [start, ends[i] ?? duration]);
  const loud = ffmpeg(["-i", file, "-af", "aformat=channel_layouts=stereo,loudnorm=print_format=json", "-f", "null", "-"]);
  const loudness = (key) => Number(loud.match(new RegExp(`"${key}" : "(-?[\\d.]+)"`))[1]);
  return { duration, silences, lufs: loudness("input_i"), peak: loudness("input_tp") };
};

/** Trechos com voz: o que sobra entre os silêncios. */
const voiced = (duration, silences) => {
  const parts = [];
  let at = 0;
  for (const [start, end] of silences) {
    if (start > at) parts.push([at, start]);
    at = end;
  }
  if (at < duration) parts.push([at, duration]);
  // Um respiro curto no começo, antes de um silêncio longo, sai junto com o silêncio.
  if (parts.length > 1 && parts[0][1] - parts[0][0] < 0.3 && parts[1][0] - parts[0][1] >= 0.5) parts.shift();
  return parts;
};

/** Trechos que ficam: a voz inteira, com cada pausa longa reduzida a MAX_PAUSE. */
const kept = (parts, duration) => {
  const ranges = [];
  let start = Math.max(0, parts[0][0] - LEAD);
  for (let i = 0; i < parts.length - 1; i++) {
    if (parts[i + 1][0] - parts[i][1] <= MAX_PAUSE) continue;
    ranges.push([start, parts[i][1] + MAX_PAUSE / 2]);
    start = parts[i + 1][0] - MAX_PAUSE / 2;
  }
  ranges.push([start, Math.min(duration, parts.at(-1)[1] + TAIL)]);
  return ranges;
};

for (const name of readdirSync(source).filter((n) => n.endsWith(".mp3")).sort()) {
  const id = basename(name, ".mp3");
  const { duration, silences, lufs, peak } = analyze(join(source, name));
  const ranges = kept(voiced(duration, silences), duration);
  const gain = Math.min(TARGET_LUFS - lufs, MAX_TRUE_PEAK - peak);
  const pieces = ranges.map(([a, b], i) => `[0:a]atrim=start=${a.toFixed(3)}:end=${b.toFixed(3)},asetpts=PTS-STARTPTS[p${i}]`);
  const joined = `${ranges.map((_, i) => `[p${i}]`).join("")}concat=n=${ranges.length}:v=0:a=1`;
  const filter = `${pieces.join(";")};${joined},atempo=${TEMPO},aformat=channel_layouts=stereo,volume=${gain.toFixed(2)}dB[out]`;
  ffmpeg(["-y", "-i", join(source, name), "-filter_complex", filter, "-map", "[out]", "-ar", "44100", "-c:a", "libmp3lame", "-b:a", "128k", join(target, `${id}.mp3`)]);
  const seconds = ranges.reduce((sum, [a, b]) => sum + b - a, 0) / TEMPO;
  console.log(`${id}  ${duration.toFixed(2)} s -> ${seconds.toFixed(2)} s  ${Math.ceil(seconds * FPS)} frames  ${gain >= 0 ? "+" : ""}${gain.toFixed(1)} dB`);
}
