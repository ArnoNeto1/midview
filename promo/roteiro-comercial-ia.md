# Roteiro de produção: comercial do Midview (ElevenLabs + Higgsfield)

Comercial de cerca de 50 segundos, 16:9, 1080p, em quatro cenas. A locução é
gerada no ElevenLabs e as cenas cinematográficas no Higgsfield; a interface do
Midview nunca é gerada por IA: entra por gravação de tela ou pelos renders do
Remotion desta pasta.

**Conceito: do caos ao comando.** O caos é quente, colorido e barulhento, sem
nenhum ciano. O Midview chega em preto, cinza e ciano elétrico, e a partir daí
o ciano é a única cor do filme.

| Cena | Tempo (estimado) | Imagem |
| --- | --- | --- |
| 1. A Dor | 0:00 a 0:11 | Higgsfield |
| 2. A Virada | 0:11 a 0:20 | Higgsfield |
| 3. A Ação | 0:20 a 0:39 | Tela real do Midview, com apoio do Higgsfield |
| 4. Call to Action | 0:39 a 0:50 | Higgsfield (fundo) + logo e endereço na edição |

Os tempos são estimativas pelo número de palavras. Gere a locução primeiro e
monte a imagem em cima dela, não o contrário.

## O que o roteiro afirma, e onde isso está no repositório

Cada frase da locução corresponde a algo que o programa faz hoje (versão 0.0.50).

| Ponto forte | Onde está | Como aparece no comercial |
| --- | --- | --- |
| **Bloquear telas** na hora: cadeado em todas as máquinas, teclado e mouse travados. O Ctrl+Alt+Del não é saída, e a máquina que reinicia é bloqueada de novo sozinha. | `docs/superpowers/specs/2026-10-05-screen-lock-design.md`, `src/Midview.Master/ScreenLockKeeper.cs`, `README.md` | Cenas 2 e 3 |
| **Fechar programas** com aviso de 30 segundos ou 1 minuto para o aluno salvar, ou na hora. A área de trabalho e a barra de tarefas nunca são fechadas. | `docs/superpowers/specs/2026-09-24-close-programs-design.md`, `src/Midview.Master/CloseAppsWindow.xaml` | Cena 3 |
| **Bloquear programas e sites**, valendo mesmo depois de reiniciar, porque quem aplica é o serviço do Windows, não o programa do aluno. | `docs/superpowers/specs/2026-09-27-app-blocking-design.md`, `src/Midview.Master/SiteBlockWindow.xaml` | Cena 3 |
| **Tela fluida**: só as partes da tela que mudaram trafegam; a captura dobra de ritmo quando há movimento (até cerca de 30 por segundo); numa rede lenta chegam menos quadros, mais novos, em vez de uma fila atrasada. | `src/Midview.Session/FramePipeline.cs`, `CaptureCadence.cs`, `FrameCredit.cs` | "Tudo fluido" (Cena 3) |
| **Uma máquina lenta ou desligada não atrasa as outras**: cada máquina tem a sua própria conexão e os comandos vão para todas ao mesmo tempo. | `src/Midview.Master/ThumbnailPoller.cs`, spec de Fechar programas | "uma máquina lenta não segura a turma" (Cena 3) |
| **A turma nunca fica presa**: se o computador do professor cair, as telas são liberadas em 15 segundos; se o programa do aluno cair, o serviço o reabre. | spec de Bloquear telas, `docs/superpowers/specs/2026-09-21-core-architecture-and-remote-screen-design.md` | Argumento de venda para o site e para a legenda do vídeo |
| **Tudo na rede da escola**, criptografado, sem servidor externo e sem telemetria. | `README.md` (Como funciona, Privacidade) | Texto na tela (Cena 4) |
| **Gratuito para escolas**, um instalador só, sem instalar o .NET. | `README.md`, `LICENSE` | Cena 4 |
| **Visual moderno**: o tema escuro "Painel". | `src/Midview.Master/Themes/Painel.Colors.xaml` | Toda a Cena 3 |
| **Mais de 2.000 testes automatizados**, conferidos a cada mudança, parte deles em Windows de verdade. | `tests/`, `.github/workflows/` | Opcional, como texto na tela |

**Sobre a comparação com a concorrência.** O roteiro não cita o Veyon nem diz
"não trava" como promessa absoluta: não há no repositório uma medição lado a
lado que sustente isso, e publicidade comparativa precisa ser comprovável
(CONAR, art. 32). No lugar, a locução afirma o que o código garante ("uma
máquina lenta não segura a turma") e a Cena 3 prova com uma tomada real, sem
cortes. Se você medir os dois programas no mesmo laboratório, a comparação
direta pode entrar com o número na tela.

## Parte 1: locução (ElevenLabs)

**Direção de voz.** Português do Brasil, tom de tecnologia premium. Cena 1:
cansada e próxima, como quem já viveu aquilo. A partir de "O caos acabou":
grave, calma e segura, sem gritar; a autoridade vem do ritmo lento. A voz
"Amanda", dos vídeos do site, mantém a identidade; para um tom mais imponente,
teste uma voz mais grave com a Cena 2 antes de gerar o resto.

**Como gerar.** Uma geração por cena (quatro arquivos), com duas a quatro
variações de cada: assim a virada de emoção entre as cenas 1 e 2 fica limpa e
dá para refazer uma cena sem mexer nas outras. Estabilidade mais baixa (perto
de 0,35) na Cena 1, para a frustração soar natural, e mais alta (perto de 0,6)
nas demais.

### Texto para colar

No Eleven v3, as etiquetas entre colchetes, em inglês, são direção de atuação
e não são lidas. O v4 do Higgsfield deve se comportar igual, mas não foi
testado aqui: gere a Cena 1 primeiro e confira se nenhuma etiqueta é falada.
Se for, apague as etiquetas e mantenha as reticências e os travessões, que já
seguram o ritmo.

Cena 1:

```text
[tired, frustrated] Trinta máquinas. Trinta distrações. [short pause] Você pede atenção... e ninguém ouve. [sighs] Cansado de não conseguir bloquear as telas, ou fechar os programas, na hora certa? [long pause]
```

Cena 2:

```text
[calm, authoritative] O CAOS acabou. [long pause] Este é o Midview. [short pause] O laboratório inteiro, ao vivo, na sua tela... e sob o seu comando.
```

Cena 3:

```text
[confident, steady] Um clique: [short pause] todas as telas bloqueadas. Teclado e mouse, travados. [short pause] Outro clique: os programas fecham — com aviso para o aluno salvar o trabalho. [short pause] Jogos e sites fora da aula? [firmly] Bloqueados. Mesmo depois de reiniciar. [short pause] Tudo fluido: uma máquina lenta não segura a turma.
```

Cena 4:

```text
[authoritative, with impact] Midview. [short pause] Controle total do seu laboratório. Sem complicação. [short pause] [warm] Gratuito para escolas. [long pause] [confident] Baixe agora. O endereço está na tela.
```

| Marcação do roteiro | Etiqueta no ElevenLabs |
| --- | --- |
| [pausa curta] | `[short pause]` |
| [pausa dramática] | `[long pause]` |
| (falar com frustração) | `[tired, frustrated]`, `[sighs]` |
| (falar com impacto) | `[authoritative, with impact]`, `[firmly]`, palavra em maiúsculas |

**O endereço na locução.** `arnoneto1.github.io/midview` é ruim de ouvir e
fácil de errar; por isso a locução manda olhar a tela, onde ele fica por pelo
menos 4 segundos. Se quiser que seja falado, troque a última frase por:
"Baixe agora em arno neto um, ponto guit-râb, ponto i ó, barra mid-viu."

## Parte 2: tabela de produção

Configuração comum no Higgsfield: Cinema Studio 4.0 (`cinematic_studio_video_4_0`),
texto para vídeo, 16:9, 1080p, **áudio desligado** (a trilha e a locução entram
na edição). Os modelos não têm campo de prompt negativo, então as proibições
("no readable text") vão no fim de cada prompt.

### Cena 1: A Dor (o caos no laboratório)

**Locução (ElevenLabs):** (falar com frustração) Trinta máquinas. Trinta
distrações. [pausa curta] Você pede atenção... e ninguém ouve. [pausa curta]
Cansado de não conseguir bloquear as telas, ou fechar os programas, na hora
certa? [pausa dramática]

**Prompt Higgsfield** (10 segundos):

```text
Cinematic wide shot of a school computer lab in chaos, seen from the back of the room near the teacher's desk. Four rows of desktop monitors glow with chaotic, mismatched content: fast-moving colorful video games, endless scrolling video feeds, flashing chat bubbles, all abstract and slightly blurred, no readable text, no recognizable logos. Students are seen only from behind, hunched toward their screens, a few turning to each other and laughing, nobody looking at the front. In the foreground, slightly out of focus, a tired teacher in their forties raises one hand asking for attention, is ignored, lowers the hand and exhales, shoulders dropping. Camera: handheld, slow uneasy push-in at eye level, 35mm lens, shallow depth of field, subtle focus breathing. Lighting: harsh flat overhead fluorescent tubes with a faint flicker and a sickly warm-green cast, clashing multicolored monitor light spilling on desks and shoulders, dust visible in the air. Color: desaturated muddy warm tones against oversaturated screen colors, absolutely no cyan or teal anywhere in the frame. Mood: noisy, overwhelming, out of control. Photorealistic, anamorphic film look, natural motion blur, fine film grain, 24 fps. No readable text, no logos, no user interface, no faces of students.
```

Plano de apoio opcional (5 segundos), para cortar em "e ninguém ouve":

```text
Extreme close-up of a teacher's hand hovering over a computer mouse, hesitating, then clicking twice with no result. Behind it, completely out of focus, rows of monitors flicker with chaotic multicolored light. Camera: static macro shot, 85mm lens, very shallow depth of field. Lighting: flat fluorescent top light, warm-green cast, colored flicker from the screens on the skin. Desaturated warm palette, no cyan. Photorealistic, film grain, tense and frustrated mood. No text, no logos.
```

### Cena 2: A Virada (apresentação do Midview)

**Locução:** (falar com impacto) O caos acabou. [pausa dramática] Este é o
Midview. [pausa curta] O laboratório inteiro, ao vivo, na sua tela... e sob o
seu comando.

**Prompt Higgsfield** (9 a 10 segundos; o pulso de luz deve cair em "acabou"):

```text
Extreme slow motion, one continuous shot. The same school computer lab, now frozen in time: the overhead lights cut out and the room drops into deep black and charcoal gray. From the teacher's desk in the foreground, a single pulse of electric cyan light, exact color #00BBD4, bursts outward as a thin shockwave ring that sweeps across the room. As the wave crosses each row, the chaotic colors on every monitor are wiped away one by one and replaced by a calm, uniform dark-gray screen showing one simple flat light-gray padlock icon in the center. The students are motionless dark silhouettes seen from behind. Thousands of fine glowing cyan particles and hair-thin threads of light trail behind the wave, linking monitor to monitor like a network, then settle and hang suspended in the air. Camera: perfectly smooth slow dolly forward down the center aisle with a gentle upward crane move, 24mm lens, deep parallax through the floating particles. Lighting: volumetric cyan beams cutting through light haze, soft cyan rim light on desks and chairs, glossy reflections on a dark polished floor, pitch-black shadows. Color palette: strictly black, charcoal gray, electric cyan #00BBD4 and light gray, no other hues. Style: Unreal Engine 5 cinematic render, ray-traced reflections, hyper-detailed, premium technology commercial, 120 fps slow motion. No readable text, no logos, no user interface.
```

O cadeado cinza-claro sobre cinza-escuro, sem texto, é exatamente a tela que o
aluno vê no **Bloquear telas** (`WinFormsScreenLock`): por não ter letras, a IA
consegue desenhá-la, e o corte para a interface real fica coerente.

Se a virada não sair de primeira: gere duas imagens paradas (o laboratório no
caos e o laboratório com os cadeados) e use-as como quadro inicial e final no
Cinema Studio 3.0 (`cinematic_studio_3_0`, `start_image` e `end_image`). Se o
slow motion não vier pelo prompt, o Cinema Studio (`cinematic_studio_video_v2`)
tem o controle `speedramp: slowmo`.

### Cena 3: A Ação (controle absoluto)

**Locução:** Um clique: [pausa curta] todas as telas bloqueadas. Teclado e
mouse, travados. [pausa curta] Outro clique: os programas fecham — com aviso
para o aluno salvar o trabalho. [pausa curta] Jogos e sites fora da aula?
(falar com impacto) Bloqueados. Mesmo depois de reiniciar. [pausa curta] Tudo
fluido: uma máquina lenta não segura a turma.

**Orientação de edição.** A regra da cena: a interface real é a causa, o
laboratório gerado por IA é a consequência. Todo texto que aparece vem do
programa de verdade.

Plano a plano:

| Fala | Imagem |
| --- | --- |
| "Um clique:" | **Real.** Aproximação no botão **Bloquear telas** da barra; o cursor clica. |
| "todas as telas bloqueadas. Teclado e mouse, travados." | **IA** (1 a 2 segundos, sobra da Cena 2: os monitores virando cadeado) e corte para o **real**: a grade com o cadeado em todas as telas, "🔒 Tela bloqueada" em cada uma e "Telas bloqueadas: 14 de 14." na linha de status. |
| "Outro clique: os programas fecham, com aviso para o aluno salvar o trabalho." | **Real**, tela dividida. À esquerda, a janela **Fechar programas** com "Avisar o aluno e esperar 30 segundos" marcado e o clique no botão. À direita, a tela do aluno com o aviso "O professor vai fechar seus programas em 30 segundos. Salve seu trabalho agora." |
| "Jogos e sites fora da aula? Bloqueados. Mesmo depois de reiniciar." | **Real**, dois cortes rápidos: **Bloquear programas** > **Aplicar bloqueio**, e **Bloquear sites** > **Bloquear a internet**. O corte cai na palavra "Bloqueados". |
| "Tudo fluido: uma máquina lenta não segura a turma." | **Real**, uma tomada sem cortes: a grade inteira ao vivo, o controle deslizante mudando o tamanho das telas, uma máquina "Sem resposta" no meio das outras, que seguem atualizando. |

Como juntar a IA com a tela real:

1. **De onde vem a tela real.** Duas fontes, as duas com o texto certo:
   - Gravação com o OBS em 1920×1080 a 60 quadros por segundo, do Midview
     0.0.50, num laboratório montado para a gravação, com máquinas e telas
     fictícias (como nos vídeos do site). Não grave alunos de verdade nem
     nomes reais de máquinas.
   - Render do Remotion desta pasta, que reproduz a janela "Midview - Sala de
     aula" no visual Painel com os textos reais e o cursor suave:
     `npx remotion render MidviewPromoPainel out/painel.mp4`. Os trechos
     `bloquear` (`src/painel/beats/tela.tsx`) e `fechar`, `bloquearProgramas`
     e `bloquearSites` (`src/painel/beats/programas.tsx`) são os desta cena.
2. **Troca de tela.** Gere no Higgsfield o plano abaixo, com um monitor de
   tela verde lisa, e encaixe a gravação nele com "corner pin" (After Effects)
   ou o rastreador planar do Fusion (DaVinci Resolve). Com a câmera parada
   basta marcar os quatro cantos, sem rastreamento. Por cima, um brilho ciano
   leve vazando da tela para a mesa e 10% de reflexo.
3. **Corte de causa e consequência.** O clique acontece na interface real, em
   close; o resultado aparece no laboratório gerado por IA. O cadeado é o
   elemento gráfico que liga os dois mundos.
4. **Janela flutuante.** Onde não houver monitor, coloque a gravação como uma
   janela inclinada em 3D (cantos arredondados, sombra, contorno ciano fino)
   sobre o fundo de partículas da Cena 4, com aproximações no botão clicado e
   um anel ciano em volta do cursor no instante do clique.
5. **Prova de velocidade.** Não acelere o bloqueio das telas. Mostre do clique
   até o último cadeado em tempo real, com o texto "Gravação real, sem
   cortes" no canto. Ensaie antes: a tomada precisa ser boa de verdade.
6. **Saltos de tempo.** Os 30 segundos do aviso de Fechar programas não cabem
   na cena: corte com uma varredura de luz ciano, sem fingir que foi
   instantâneo.
7. **Textos na tela.** Sempre na edição, nunca pela IA, na fonte Bahnschrift
   (a do programa, que já vem com o Windows): "Bloquear telas", "Fechar
   programas", "Bloquear programas e sites".
8. **Unidade de cor.** A interface real é azul-marinho `#0A111E` com destaque
   verde-água `#2DD4BF`; as cenas de IA usam preto e ciano `#00BBD4`. Na
   correção de cor, puxe os pretos das cenas de IA levemente para o azul e
   aplique o mesmo grão de filme (2 a 3%) sobre tudo, para o corte entre os
   dois não saltar.

Plano opcional para a troca de tela (5 segundos):

```text
Over-the-shoulder close-up of a teacher's hand resting on a computer mouse in front of a single modern frameless monitor on a dark desk. The monitor screen is a perfectly flat, evenly lit, solid chroma green rectangle with four sharp, fully visible corners, no reflections and no content. The index finger presses the mouse button once, decisively. In the background, softly out of focus, a dark computer lab with rows of monitors as blurred cyan bokeh. Camera: completely static tripod shot, 50mm lens, shallow depth of field, the monitor sharp and unobstructed. Lighting: low-key, electric cyan #00BBD4 rim light from the right, soft light-gray fill on the hand, deep black shadows. Color palette: black, charcoal gray, electric cyan and light gray. Photorealistic, premium technology commercial, clean and calm. No text, no logos.
```

### Cena 4: Call to Action (arnoneto1.github.io/midview)

**Locução:** (falar com impacto) Midview. [pausa curta] Controle total do seu
laboratório. Sem complicação. [pausa curta] Gratuito para escolas.
[pausa dramática] Baixe agora. O endereço está na tela.

**Prompt Higgsfield** (10 segundos, fundo de encerramento):

```text
Luxurious dark abstract background for a technology brand end card. Deep black and charcoal-gray space above a polished black glass floor. Fine particles drift slowly through volumetric light shafts descending from above: soft light-gray particles on the left half of the frame, electric cyan particles, exact color #00BBD4, on the right half, meeting and gently swirling at the center. Far in the background, softly out of focus, hair-thin cyan lines sketch a faint network of connected nodes. The center of the frame stays clean, dark and empty, reserved as negative space for a logo. Camera: very slow, perfectly smooth push-in with subtle parallax, 35mm lens, shallow depth of field, large soft bokeh. Lighting: volumetric cyan god rays through thin haze, gentle rim glow near the edges, soft vignette, subtle mirror reflections on the floor. Style: premium, minimal, Unreal Engine 5 cinematic render, ray tracing, ultra clean, seamless slow motion. No text, no letters, no logos, no people.
```

A divisão cinza à esquerda e ciano à direita repete a logo, que é metade de
cada cor.

Na edição, sobre o fundo:

- A logo: `../assets/midview-logo.jpg` tem fundo preto, então entra no modo de
  mesclagem "Tela" (Screen). `../assets/midview-animado.webp` é a versão
  animada, e a composição `Cena9-Encerramento` do Remotion é a vetorial.
- O endereço `arnoneto1.github.io/midview`, grande, em Bahnschrift, por pelo
  menos 4 segundos.
- Uma linha menor: "Gratuito para escolas · Windows 10 e 11 · Tudo na rede da
  escola".

## Trilha e efeitos

Uma só música para o filme todo, com a virada no segundo 11. Sugestão de
prompt para o ElevenLabs Music (também disponível no Higgsfield), 50 segundos,
instrumental:

```text
Instrumental score for a 50-second premium technology commercial. First 11 seconds: tense, cluttered and slightly dissonant, restless ticking percussion and detuned synths, rising unease. At 11 seconds: a sudden moment of silence, then one deep sub-bass impact. From there: confident, minimal, modern electronic theme at 100 BPM, warm deep bass, a clean pulsing synth arpeggio, wide cinematic pads, steadily building. Final 10 seconds: resolve into a bold, uplifting, memorable chord with a long clean tail. No vocals.
```

Efeitos pontuais: o burburinho de sala na Cena 1, cortado a seco em "O caos
acabou"; um grave no pulso de luz; um clique seco de mouse e um "trava" curto a
cada bloqueio da Cena 3.

Para Reels e Shorts, gere as cenas de novo em 9:16 em vez de recortar o 16:9;
a composição `MidviewReels` desta pasta já tem a interface no formato vertical.
