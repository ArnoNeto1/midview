# midview — vídeo promocional

Vídeo de 30 s (900 frames, 30 fps, 1920×1080) feito com Remotion, React, TypeScript e Tailwind.

## Comandos

```bash
npm install
npx remotion studio
npx remotion render MidviewPromo out/midview-promo.mp4
```

O Studio também lista cada cena isolada na pasta **Cenas**, para ajustar uma por vez.

## Logo

O símbolo padrão é um SVG vetorial redesenhado a partir de `../assets/midview-logo.jpg`
(`src/components/logoGeometry.ts`). Para usar a logo oficial, salve o arquivo em `public/`
como `logo.svg` (ou `midview-logo.svg` / `midview.svg`): ele substitui o símbolo em todo o vídeo.

## Estrutura

| Arquivo | O que faz |
| --- | --- |
| `src/timing.ts` | Fronteiras das cenas e transições (frames) |
| `src/MidviewPromo.tsx` | Composição principal com `TransitionSeries` |
| `src/scenes/` | As 5 cenas |
| `src/components/dashboard/` | Mock do app: janela, cards dos PCs, barra de comandos, cursor, câmera |
| `src/transitions/zoomBlur.tsx` | Transição personalizada entre cenas |
| `src/lib/motion.ts` | Presets de `spring()` e helpers de `interpolate()` |
