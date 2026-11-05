# Visual "Painel" no programa do professor (desenho)

Data: 2026-11-05. Vale para o `src/Midview.Master` inteiro (as 26 janelas).
Os mockups que o professor viu e escolheu estão guardados em
`C:\Users\Neto\AppData\Roaming\Claude\side-session-notes\local_cc643cd1-e6cc-4bb6-8c9f-1ede6a947289\redesign-mockups.html`.
Abra esse arquivo no navegador e clique em "C · Painel". As cores e o resumo
de cada direção estão em `redesign-mockups.md`, na mesma pasta.

## Intenção

O professor acha o programa "muito genérico". Nas palavras dele, a cor, a
"aba" e o que fica dentro das abas, ou seja, a barra de botões de cima e as
janelas que abrem a partir dela. Também acha feia a janela "Agendar ligar e
desligar". Ele quer que o Midview tenha cara própria.

Viu, lado a lado, o visual de hoje e oito direções: Lousa, Caderno, Painel,
Painel em Liquid Glass, Mural, Blocos, Laranja e Vidro. Escolheu o
**Painel**, um visual escuro de sala de controle. Nele, as telas dos alunos
se destacam e cada grupo da barra tem uma cor forte.

## Decidido pelo professor

- **Direção C · Painel**, escura. Não o Painel em Liquid Glass, nem as outras.
- **Só a versão escura.** A tela do professor fica no monitor e não vai para
  projetor, então não há versão clara nem botão para trocar.
- **Todas as 26 janelas de uma vez.** Cada uma ganha as cores novas e também
  a arrumação nova (cabeçalho com ícone, cartões, ação principal destacada),
  não só a troca de cores.
- A janela **"Agendar ligar e desligar"** é redesenhada como no mockup.

## Objetivos

### 1. Paleta e fonte (em `App.xaml`)

Os pincéis de hoje (`SlateBrush`, `CanvasBrush`, `CardBrush`, `InkBrush`...)
dão lugar a estes tokens. Os nomes ficam a critério de quem constrói.

| Uso | Cor |
|---|---|
| Fundo da janela, barra de título, barra de botões | `#0A111E` |
| Faixa de baixo, fundo das janelas de diálogo | `#0F1828` |
| Cartões, painéis de grupo, campos | `#141F33` |
| Linhas e bordas | `#22314D` |
| Texto | `#DCE6F5` |
| Texto secundário (dicas) | `#8394AE` |
| Destaque (seleção, foco, botão principal) | `#2DD4BF`, com texto `#04221D` sobre ele |
| Destaque em texto ou contorno sobre o escuro | `#7FF0DF` |
| Grupo Energia | `#FF6B6B` |
| Grupo Tela | `#4DA3FF` |
| Grupo Turma | `#FFC53D` |
| Grupo Programas e sites | `#3DDC97` |
| Grupo Configurar | `#A78BFA` |
| Erro (texto) | `#FF8A80` |
| Aviso (texto) | `#FFC53D` |
| Online / em dia | `#3DDC97` |
| Desligada / sem resposta | `#4A5872` |

- **Fonte:** Bahnschrift (vem com o Windows 10 1709 ou mais novo), com
  Segoe UI de reserva. Os ícones continuam em Segoe Fluent Icons, com Segoe
  MDL2 Assets de reserva, como hoje.
- Todo texto precisa de contraste AA (4,5:1) sobre o fundo em que aparece.
  Os pares da tabela foram escolhidos para isso. O vermelho de erro de hoje
  (`#B3261E`) fica ilegível no escuro e sai de todo o código.

### 2. Controles escuros (estilos implícitos em `App.xaml`)

Todo controle usado no programa do professor ganha um estilo escuro, para
nenhuma janela mostrar um pedaço branco do Windows:
`Button`, `ToggleButton`, `TextBox`, `PasswordBox`, `CheckBox`,
`RadioButton`, `ListBox`/`ListBoxItem`, `ScrollBar` (dentro de qualquer
`ScrollViewer`), `Slider`, `ContextMenu`/`MenuItem`/`Separator`, `ToolTip` e
`Hyperlink`. São os únicos usados hoje: não há `ComboBox`, `ProgressBar`,
`ListView` nem `DataGrid`.

Os botões têm três estilos:
- **normal:** cartão `#141F33` com borda `#22314D`;
- **principal:** turquesa `#2DD4BF` com texto escuro. Há um por janela, para
  a ação que a janela existe para fazer;
- **perigo:** contorno e texto vermelhos, para o que não tem volta. Exemplos:
  "RESET geral", "Limpar agora", "Tudo" (apagar downloads), "Apagar as
  pastas dos alunos", "Excluir" turma e "Forçar".

A caixa de seleção vira chave de liga/desliga só na janela Agendar. No resto
do programa ela continua sendo caixinha, agora escura.

### 3. Janela da sala de aula (`MainWindow.xaml`)

- **Barra de título própria** (só nesta janela). Mostra o símbolo do Midview,
  "Midview · Sala de aula", um selo "Aula: <turma>" quando há aula ligada na
  Pasta da turma, um selo "N de M ligadas" e os botões de minimizar,
  maximizar e fechar. Ela precisa continuar arrastando, maximizando com dois
  cliques, encaixando nas bordas da tela e redimensionando como hoje.
- **Barra de botões:** cada grupo (Energia, Tela, Turma, Programas e sites,
  Configurar) fica num painel arredondado `#141F33`. O ícone de cada botão
  tem a cor do grupo, o rótulo é claro e o nome do grupo aparece embaixo, em
  letras pequenas maiúsculas, com uma bolinha da cor do grupo. Não há mais
  traços separando os grupos.
- A barra **continua numa linha só numa tela de 1366 px** (a janela abre com
  1340 de largura). Numa janela mais estreita, os grupos inteiros descem para
  uma segunda linha, como hoje.
- **Telas dos alunos:** cada tela vira um cartão `#141F33` com cantos
  arredondados. Por baixo da imagem há uma linha com uma bolinha de estado
  (verde online, cinza desligada ou sem resposta, vermelha bloqueada), o nome
  da máquina e, à direita, o estado curto ("Sem resposta").
  - A tela bloqueada ganha um selo "🔒 Bloqueada" sobre a imagem.
  - Os avisos de hoje ("Saiu do bloqueio", bloqueio de programas) continuam
    debaixo do cartão, na cor de aviso ou de erro.
  - O nome sai de cima da imagem, onde fica hoje.
- **Seleção:** borda turquesa com brilho suave. O brilho aparece só nos
  cartões selecionados, nunca nas 31 telas ao mesmo tempo, para não pesar.
- **Faixa de baixo:** escura, com os mesmos botões ("Organizar sala",
  "Visualizar todos", "Só online"), o controle de tamanho e o texto de estado
  que existem hoje. Ligado, um botão fica com contorno e texto turquesa.
- **Menu do botão direito**, dicas (tooltips), aviso de sala vazia e aviso de
  "Só online" sem ninguém: todos escuros, com os mesmos textos e itens de hoje.

### 4. Janelas de diálogo: a arrumação comum

Toda janela que abre a partir da barra segue o mesmo esqueleto:

1. **Barra de título escura do Windows.** É a barra nativa, pintada de escuro
   pelo modo escuro do Windows 10/11. Não é desenhada à mão.
2. **Cabeçalho:** um quadrado arredondado com o ícone do botão que abriu a
   janela, na cor do grupo dele; o título em letra grande; e, embaixo, o
   texto que hoje vem em negrito no topo (por exemplo "Na turma toda (31
   máquinas)"). A tabela no fim deste documento diz o ícone e o grupo de
   cada janela.
3. **Conteúdo em cartões:** cada parte da janela fica num cartão `#141F33`
   com título curto. As dicas cinzas viram texto secundário dentro do cartão
   a que pertencem.
4. **Ação principal destacada:** o botão que faz a ação da janela usa o
   estilo principal. Quem constrói decide, janela por janela, se ele fica no
   rodapé à direita ou junto do formulário.
5. **Andamento e resultado:** o texto de andamento ("Enviando...") e a lista
   de resultado ficam num cartão próprio embaixo, que rola quando é comprido,
   como hoje.

Nada muda no que as janelas fazem: os mesmos campos, botões, textos,
atalhos (Enter, Esc) e o mesmo foco inicial.

Detalhes por janela:

- **Máquinas da sala (`MachinesWindow`)** ganha um **menu à esquerda** com
  cinco páginas: "Procurar e parear", "Atualizações" (alunos e professor,
  juntos), "Outro professor", "Senha de administrador" e "Avançado". Hoje são
  seções empilhadas numa janela de 740 de altura, que mal cabe numa tela de
  768. Na página "Procurar e parear":
  - cada máquina encontrada vira um cartão com caixinha, ícone, nome,
    endereço e um selo de estado;
  - a que espera conferência mostra o código em quatro quadrados grandes,
    com "Confere" (principal) e "Não confere".

  Quando a janela é aberta para atualizar (o "Atualizar agora" da pergunta de
  inicialização, ou "Atualizar os alunos" depois de o professor atualizar),
  ela já abre na página "Atualizações".
- **Pergunta de atualização (`QuestionWindow`):**
  - o cabeçalho tem ícone e uma frase curta ("Chegou uma versão nova");
  - as versões aparecem em dois selos, "0.0.40 → 0.0.41";
  - os três avisos ficam em linhas com ícone: fecha e abre sozinho, as telas
    bloqueadas são liberadas, o Windows pede permissão;
  - "Atualizar agora" é o botão principal, à direita.

  A `QuestionWindow` continua servindo para outras perguntas de sim ou não,
  que podem não ter selos nem linhas com ícone.
- **Agendar ligar e desligar (`ScheduleWindow`):**
  - os dias da semana viram sete botões que acendem;
  - "Ligar às" e "Desligar às" viram dois cartões lado a lado, cada um com
    uma chave de liga/desliga, o horário em números grandes (editável, no
    mesmo formato HH:mm de hoje) e a dica de cada um;
  - embaixo, uma **régua do dia** (0h, 6h, 12h, 18h e 24h) pinta o trecho
    entre a hora de ligar e a de desligar, com o texto "Ligado das 07:00 às
    18:00". Ela muda na hora quando o professor mexe nos horários ou nas
    chaves;
  - a última execução fica numa linha com ícone, no rodapé;
  - "Salvar agendamento" é o botão principal, à direita.

  Com só uma das chaves ligada, ou com a hora de desligar antes da de ligar,
  a régua mostra o que acontece de fato; como, fica a critério de quem
  constrói.
- **Painel do laboratório (`LabConfigWindow`):** os grupos ("Sequência de
  configuração ideal", "Bloqueios extras", "Manutenção", "Consulta") viram
  cartões. Os passos 1 a 5 ganham um selo com o número. "Limpar agora",
  "Tudo" e "RESET geral" usam o estilo de perigo.
- **Pasta da turma (`ClassFolderWindow`):** os mesmos três modos (lista de
  turmas, aula ligada, aula de outro PC), com a lista de turmas e a lista de
  computadores em cartões escuros. Os símbolos de estado (✓ ⟳ ○ ✗) ganham as
  cores de estado, e os avisos de espaço em disco usam a cor de aviso.
- **Ver e controlar (`ViewControlWindow`):** a barra de cima fica escura no
  padrão novo. "Controlando" aparece num selo de destaque e o aviso de "modo
  compatível" na cor de aviso. A área da tela do aluno continua preta.
- **Conversa e Bate-papo da turma (`ChatWindow`, `ClassChatWindow`):** a
  conversa continua como texto corrido numa caixa só leitura, agora escura,
  com cabeçalho e o campo de escrever com "Enviar" principal. Balões de
  conversa ficam fora desta entrega.
- **Abertura (`SplashWindow`):** já é preta com o logo animado e fica como
  está.

### 5. Caixas de mensagem do Windows

Hoje o programa abre caixas de mensagem do próprio Windows (`MessageBox.Show`)
em 13 lugares, por exemplo ao "Encerrar a aula", "Excluir turma", na senha de
administrador, nos avisos de atualização e ao fechar o programa com a aula
ligada. Essas caixas ficariam brancas no meio do programa escuro. Elas passam
a usar uma janela do próprio Midview no estilo novo, com os mesmos textos,
botões e respostas. A escolha padrão também continua a mesma: o que hoje é
Sim ou Não continua sendo.

As janelas do Windows para escolher arquivo ou pasta ("Escolher instalador",
"Escolher arquivo") **continuam do Windows** e podem aparecer claras. O
programa não controla o visual delas.

## Fora desta entrega

- O lado do aluno (Session e Agent): a tela de bloqueio, as mensagens e o
  bate-papo na tela do aluno não mudam.
- Versão clara, ou botão para trocar entre clara e escura.
- Efeito de vidro (Liquid Glass) e desfoque.
- Qualquer mudança de comportamento: os mesmos botões, a mesma ordem, os
  mesmos textos de resultado. Ficam de fora também funções novas, como
  atalhos de "dias úteis" no Agendar ou balões no bate-papo.
- O instalador e o site.

## Não pode acontecer

- Uma janela, ou parte dela, que ainda abra branca ou cinza-clara, a não ser
  as de escolher arquivo ou pasta, que são do Windows.
- Texto ilegível (abaixo de AA) no escuro, especialmente os vermelhos e os
  cinzas de hoje.
- A barra de botões quebrar em duas linhas numa tela de 1366 px.
- "Visualizar todos" deixar de caber todas as máquinas, ou o controle de
  tamanho mudar de comportamento, porque o cartão da tela mudou de tamanho.
  Em `MainWindow.TileLayout.cs`, `TileChromeWidth` precisa acompanhar a
  largura nova que o cartão soma à imagem. A altura extra já é medida.
- O programa ficar mais lento com 31 máquinas por causa de sombras ou
  brilhos.
- A janela da sala de aula perder o arrastar, o maximizar com dois cliques ou
  o encaixe nas bordas por causa da barra de título própria.

## Ambiente do professor (verificado em 2026-11-05)

- O PC do professor roda **Windows 11** (build 26300). A tela é 1920×1080 com
  escala de **125%**, o que dá 1536×864 pixels de trabalho para o WPF.
- No Windows 11, a barra de título nativa das janelas de diálogo pode ficar
  escura e ganhar a cor exata do fundo (`#0A111E` ou `#0F1828`), pelos
  atributos do DWM de modo escuro e de cor da barra.
- A barra de título própria da sala de aula precisa manter também o **menu de
  encaixe do Windows 11** (Snap Layouts), que aparece ao parar o mouse sobre o
  maximizar.
- Decisão de quem escreveu este documento: em PCs de colegas com Windows mais
  antigo, onde o modo escuro da barra de título não existe (antes do Windows 10
  1809), a barra das janelas de diálogo continua como o Windows a desenha.
  Isso é aceito. O conteúdo das janelas fica escuro do mesmo jeito. No
  Windows 10, a barra escura sai num cinza fixo do Windows, e isso também é
  aceito.
- A barra de botões continua numa linha numa tela de 1366 px a 100%, como
  hoje, e com folga no PC do professor (1536 px de trabalho).

## Restrições

- C#/.NET 8 WPF, sem pacotes NuGet novos. As fontes já vêm com o Windows.
- Os comentários do código ficam em inglês; os textos da tela, em português.
- Os testes existentes continuam passando. Lógica nova vem com testes: o
  trecho e o texto da régua do Agendar, o texto "N de M ligadas", a escolha
  da página inicial de "Máquinas da sala".
- Para ver as janelas, use só o roteiro seguro: um `new Application()` com
  os recursos do `App.xaml`, uma pasta de dados temporária e máquinas falsas
  (nota de memória `master-ui-check-harness`). Nunca o Master de verdade
  contra a sala do professor.
- O professor vê fotos de todas as janelas (1366×768) antes de qualquer
  versão ser publicada. Publicar segue o fluxo de versão de sempre, e só com
  o "pode" dele.

## A critério de quem constrói

- Os nomes dos tokens e estilos e a organização do `App.xaml` (um arquivo ou
  vários dicionários).
- Como a barra de título própria é feita (por exemplo `WindowChrome`) e como
  o modo escuro do Windows é ligado nas outras janelas.
- Tamanhos exatos, espaçamentos, raios e o desenho das barras de rolagem.
- Por janela, onde fica a ação principal e como os cartões se dividem, desde
  que siga a arrumação comum da seção 4.
- Se os itens do menu do botão direito ganham ícones.
- Como a régua do Agendar mostra os casos sem uma das chaves, ou com a hora
  de desligar antes da de ligar.

## Ícone e grupo de cada janela

O cabeçalho usa o ícone (Segoe Fluent Icons) e a cor do grupo do botão que
abre a janela. As janelas que não saem da barra entram no grupo mais próximo.

| Janela | Grupo | Ícone |
|---|---|---|
| `PowerWindow` (Desligar / Reiniciar) | Energia | `E7E8` / `E72C` |
| `ScheduleWindow` | Energia | `E787` |
| `ViewControlWindow` | Tela | `E890` / `E7C9` |
| `SendMessageWindow` | Turma | `E8BD` |
| `ChatWindow`, `ClassChatWindow` | Turma | `E8F2` |
| `SendFileWindow` | Turma | `E898` |
| `CollectFilesWindow` | Turma | `E896` |
| `ClassFolderWindow` | Turma | `E8F7` |
| `OpenWebsiteWindow` | Programas e sites | `E774` |
| `OpenProgramWindow` | Programas e sites | `E756` |
| `InstallWindow` | Programas e sites | `E7B8` |
| `UninstallWindow` | Programas e sites | `E74D` |
| `CloseAppsWindow` | Programas e sites | `E711` |
| `AppBlockWindow` | Programas e sites | `ECAA` |
| `SiteBlockWindow` | Programas e sites | `E774` |
| `LabConfigWindow` | Configurar | `E713` |
| `MachinesWindow` | Configurar | `E7F8` |
| `AddMachineWindow` (Ajustes avançados) | Configurar | `E713` |
| `GiveAccessWindow`, `JoinClassWindow` | Configurar | `E716` |
| `SetAdminPasswordWindow` | Configurar | `E72E` |
| `UpdateWindow` (Atualizar pela rede) | Configurar | `E895` |
| `QuestionWindow` e as caixas de mensagem | conforme a pergunta | conforme a pergunta |
