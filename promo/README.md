# midview — vídeo promocional

Vídeo de 1m30s (2700 frames, 30 fps, 1920×1080) feito com Remotion, React, TypeScript e Tailwind.
Mostra o app como ele é: a janela "Midview - Sala de aula" de `src/Midview.Master/MainWindow.xaml`,
com a barra de ferramentas em grupos (Tela, Turma, Programas e sites, Energia, Configurar),
a grade de telas, a barra de baixo e as janelas de cada função, com os textos reais.

Há dois vídeos com o mesmo roteiro:

| Composição | Janela do app |
| --- | --- |
| `MidviewPromo` | cores reais do app (fundo claro, barra cor de lousa, seleção amarela) |
| `MidviewPromoEscuro` | estilo escuro do vídeo (vidro, ciano) |

## Comandos

```bash
npm install
npx remotion studio
npx remotion render MidviewPromo out/midview-promo.mp4
npx remotion render MidviewPromoEscuro out/midview-promo-escuro.mp4
```

Renderize no Windows: a janela usa as fontes do sistema Segoe UI e Segoe Fluent Icons, como o app.
As demais fontes vêm do Google Fonts, então o render precisa de internet.

O Studio também lista cada cena isolada na pasta **Cenas**, para ajustar uma por vez.

## Logo

O símbolo padrão é um SVG vetorial redesenhado a partir de `../assets/midview-logo.jpg`
(`src/components/logoGeometry.ts`). Para usar a logo oficial, salve o arquivo em `public/`
como `logo.svg` (ou `midview-logo.svg` / `midview.svg`): ele substitui o símbolo em todo o vídeo.

## Estrutura

| Arquivo | O que faz |
| --- | --- |
| `src/timing.ts` | Fronteiras das cenas e transições (frames) |
| `src/MidviewPromo.tsx` | Composição principal com `TransitionSeries` e o tema |
| `src/scenes/` | As 9 cenas; as cenas 2 a 7 são o tour pelo app (`TourScenes.tsx`) |
| `src/components/app/tour.ts` | Roteiro do tour: cliques, cursor, câmera e o estado da janela a cada frame |
| `src/components/app/` | A janela do app: barra de ferramentas, cards, barra de baixo, diálogos e temas |
| `src/transitions/zoomBlur.tsx` | Transição personalizada entre cenas |
| `src/lib/motion.ts` | Presets de `spring()` e helpers de `interpolate()` |
