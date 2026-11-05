# midview — vídeo promocional

Vídeo de 1m30s (2700 frames, 30 fps, 1920×1080) feito com Remotion, React, TypeScript e Tailwind.
Mostra o app como ele é: a janela "Midview - Sala de aula" de `src/Midview.Master/MainWindow.xaml`,
com a barra de ferramentas em grupos (Tela, Turma, Programas e sites, Energia, Configurar),
a grade de telas, a barra de baixo e as janelas de cada função, com os textos reais.
Uma narração em português explica cada função enquanto ela aparece.

Há dois vídeos com o mesmo roteiro:

| Composição | Janela do app |
| --- | --- |
| `MidviewPromo` | cores reais do app (fundo claro, barra cor de lousa, seleção amarela) |
| `MidviewPromoEscuro` | estilo escuro do vídeo (vidro, ciano) |

E dois vídeos no visual **Painel**, o tema escuro do programa do professor a partir da versão 0.0.41:

| Composição | O que é |
| --- | --- |
| `MidviewPromoPainel` | 1920×1080, cerca de 1m36s: abertura, tour por todos os botões da barra (Energia, Tela, Turma, Programas e sites, Configurar), cada um com a sua janela, e encerramento. Narração só com falas já gravadas (`public/narracao` e trechos de `public/narracao-tutorial`); "Instalar" e "Desinstalar" não têm fala. |
| `MidviewReels` | 1080×1920, 35 s, para o Instagram: as funções principais com legendas grandes dentro da área segura (sem texto nos 250 px de cima nem nos 350 px de baixo) e a logo com o convite no fim. Sem narração. |

## Comandos

```bash
npm install
npx remotion studio
npx remotion render MidviewPromo out/midview-promo.mp4
npx remotion render MidviewPromoEscuro out/midview-promo-escuro.mp4
npx remotion render MidviewPromoPainel out/midview-promo-painel.mp4
npx remotion render MidviewReels out/midview-reels.mp4
```

Renderize no Windows: a janela usa as fontes do sistema Segoe UI e Segoe Fluent Icons, como o app (e Bahnschrift, no visual Painel).
As demais fontes vêm do Google Fonts, então o render precisa de internet.

O Studio também lista cada cena isolada na pasta **Cenas**, para ajustar uma por vez.

## Narração

As falas ficam em `src/narration.ts` (texto e frame de início) e os áudios em `public/narracao/<id>.mp3`,
com a voz "Amanda" do ElevenLabs v4, gerada pelo Higgsfield. Para trocar uma fala, gere o áudio novo, junte os mp3
originais numa pasta e rode:

```bash
npm run narracao -- <pasta com os mp3 originais>
```

O script corta o silêncio das pontas, encurta as pausas longas e iguala o volume (`TEMPO` acelera a voz sem
mudar o tom, se faltar tempo). Ele mostra quantos frames cada fala dura: cada uma precisa terminar antes do início
da seguinte.

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
| `src/narration.ts` | Falas da narração e o frame em que cada uma começa |
| `scripts/narracao.mjs` | Prepara os áudios da narração |
| `src/components/app/` | A janela do app: barra de ferramentas, cards, barra de baixo, diálogos e temas |
| `src/transitions/zoomBlur.tsx` | Transição personalizada entre cenas |
| `src/components/app/painel/` | O visual Painel: cores, medidas, barra de título, barra de botões em grupos, cartões das telas, faixa de baixo e as janelas de diálogo (`dialogs/`), medidas nas fotos do app |
| `src/painel/` | Roteiro dos vídeos do Painel: trechos (`beats/`) com fala, legenda, cursor, câmera e janelas, e o estado da sala de um trecho para o outro (`world.ts`) |
| `src/reels/` | O Reels vertical |
| `src/lib/motion.ts` | Presets de `spring()` e helpers de `interpolate()` |
