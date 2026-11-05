# Visual Painel no programa do professor — plano de implementação

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deixar as 26 janelas do `src/Midview.Master` no visual escuro "Painel", com a arrumação nova, sem mudar o que nenhuma delas faz.

**Architecture:** O tema sai do `App.xaml` e vai para quatro dicionários em `src/Midview.Master/Themes/`. Toda janela passa a herdar de `MidviewWindow`, que dá fundo, fonte e barra de título escuros. Peças comuns (`DialogHeader`, cartões, botões principal e de perigo) servem as janelas de diálogo. A lógica nova (régua do Agendar, contagem de ligadas, conteúdo da pergunta de atualização, rótulos das caixas de mensagem, cor da barra de título) fica em classes puras com testes xUnit. Cada janela é refeita em XAML mantendo todos os `x:Name`, eventos e textos que o code-behind usa.

**Tech Stack:** C#/.NET 8 WPF, xUnit 2.9. Fontes Bahnschrift e Segoe Fluent Icons, que vêm com o Windows. P/Invoke em `dwmapi.dll`.

**Spec:** `docs/superpowers/specs/2026-11-05-visual-painel-design.md`, a ser lida junto com este plano. Mockups: `C:\Users\Neto\AppData\Roaming\Claude\side-session-notes\local_cc643cd1-e6cc-4bb6-8c9f-1ede6a947289\redesign-mockups.html`, opção "C · Painel".

## Global Constraints

- SDK: `C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe`. O `dotnet` do PATH não tem SDK. Hoje o build tem três avisos `xUnit1031`, que não são novos.
- Sem pacotes NuGet novos.
- Comentários de código em inglês; textos de tela em português. **Os textos de tela não mudam**, exceto a pergunta de atualização (Task 3) e os textos novos citados neste plano.
- Paleta (copiada da spec): fundo `#0A111E`; faixa de baixo e diálogos `#0F1828`; cartões, painéis e campos `#141F33`; linhas `#22314D`; texto `#DCE6F5`; texto secundário `#8394AE`; destaque `#2DD4BF` com texto `#04221D`; destaque em texto ou contorno `#7FF0DF`. Grupos: Energia `#FF6B6B`, Tela `#4DA3FF`, Turma `#FFC53D`, Programas e sites `#3DDC97`, Configurar `#A78BFA`. Erro `#FF8A80`; aviso `#FFC53D`; online `#3DDC97`; desligada `#4A5872`.
- Fonte `Bahnschrift, Segoe UI`; ícones `Segoe Fluent Icons, Segoe MDL2 Assets`.
- Todo texto com contraste AA (4,5:1) sobre o próprio fundo.
- A barra de botões cabe numa linha numa janela de 1340 px a 100% (tela de 1366).
- Nenhum `x:Name`, evento, atalho (`IsDefault`/`IsCancel`) ou foco inicial (`FocusManager.FocusedElement`) some ou muda de controle. O code-behind só muda onde uma tarefa diz.
- Nunca rode o Master de verdade. Para ver janelas, use o roteiro da Task 11 (nota de memória `master-ui-check-harness`): `new Application()`, recursos do tema, `MasterData.Folder` numa pasta temporária, nunca `new App()`.
- Os commits não levam a linha `Co-Authored-By`, como manda o `CLAUDE.md` do usuário.

## Review Focus

1. **Um `{StaticResource}` que não existe** só quebra quando a janela abre, não no build. Coberto pelo teste `EveryResourceKeyUsedInXamlIsDefined` (Task 1), que roda em toda tarefa.
2. **Uma janela esquecida, ainda clara**, por não herdar de `MidviewWindow`, por ter cor fixa clara ou por chamar `MessageBox.Show`. Coberto por `EveryWindowIsAMidviewWindow` (Task 1) e por `NoLightColorsOrWindowsMessageBoxesLeft` (Task 11).
3. **"Visualizar todos" e o controle de tamanho com o cartão novo.** `TileChromeWidth` precisa somar a borda, o padding e a margem do cartão novo, ou a última tela cai numa linha sozinha. É conferido no roteiro da Task 11 com 31 máquinas falsas, em "Visualizar todos", a 1340×760 e a 1536×864.
4. **Barra de título própria maximizada:** o conteúdo não pode ser cortado pelas bordas, e o maximizar com dois cliques, o encaixe e o menu do Windows 11 precisam continuar. É conferido à mão no roteiro da Task 11, com a lista da Task 4.
5. **Régua do Agendar nos casos de borda:** só ligar, só desligar, passando da meia-noite, horário inválido, horários iguais. Coberto pelos testes de `ScheduleTimeline` (Task 6).

---

## Estrutura de arquivos

| Arquivo | Responsabilidade |
|---|---|
| `src/Midview.Master/Themes/Painel.Colors.xaml` | Tokens de cor e fonte. Até a Task 11, também os nomes antigos (`SlateBrush`, `CanvasBrush`...) apontando para valores escuros. |
| `src/Midview.Master/Themes/Painel.Controls.xaml` | Estilos implícitos dos controles padrão e os estilos com chave `PrimaryButton`, `DangerButton`, `FilterToggleButton`, `SwitchCheckBox`, `DayPillCheckBox`, `TimeBox`, `NavRadioButton`. |
| `src/Midview.Master/Themes/Painel.Main.xaml` | Estilos da sala de aula: `ToolbarButton`, `ToolbarGroupPanel`, `ToolbarGroupCaption`, `MachineTileButton`, `TitleBarButton`, `TitleChip`. |
| `src/Midview.Master/Themes/Painel.Dialogs.xaml` | Modelo do `DialogHeader`; `Card`, `CardTitle`, `Hint`, `ResultCard`, `ErrorText`, `WarningText`, `StepBadge`, `CodeDigit`, `StatusChip`. |
| `src/Midview.Master/App.xaml` | Só `MergedDictionaries` com URIs `pack://application:,,,/Midview.Master;component/Themes/...`, para o roteiro conseguir carregar. |
| `src/Midview.Master/MidviewWindow.cs` | Base de toda janela: fundo, texto e fonte por `SetResourceReference`, barra de título escura no `OnSourceInitialized`. |
| `src/Midview.Master/DarkTitleBar.cs` | P/Invoke DWM: modo escuro (atributo 20, e 19 em builds 17763–18362) e, no build 22000 ou mais novo, cor da barra (atributo 35). |
| `src/Midview.Master/DialogHeader.cs` | `ContentControl` com `Icon`, `Heading` e `Accent`. O conteúdo é a linha de baixo, por exemplo o `TargetsTextBlock` de hoje. |
| `src/Midview.Master/QuestionContent.cs` | Os dados de uma pergunta: `QuestionContent`, `QuestionFact`. |
| `src/Midview.Master/MidviewMessage.cs` | Substituto do `MessageBox.Show` sobre a `QuestionWindow`. |
| `src/Midview.Master/MachineCountText.cs` | Texto do selo "N de M ligadas". |
| `src/Midview.Master/ScheduleTimeline.cs` | Trechos e texto da régua do Agendar. |
| `src/Midview.Master/MachinesPage.cs` | `enum MachinesPage`. |
| `src/Midview.Master/MainWindow.TitleBar.cs` | Barra de título própria: `WindowChrome`, gancho do Snap Layouts, margem quando maximizada, selos. |
| `tests/Midview.Master.Tests/ThemeXamlTests.cs` | Varredura do XAML e do código-fonte, e reflexão sobre as janelas. |
| `tests/Midview.Master.Tests/` `DarkTitleBarTests.cs`, `MidviewMessageTests.cs`, `MachineCountTextTests.cs`, `ScheduleTimelineTests.cs` | Testes da lógica nova. |

O roteiro de fotos (Task 11) fica na pasta de rascunho da sessão e não entra no repositório.

Comando de teste usado em todas as tarefas (na raiz do repositório):

```bash
"C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe" test Midview.sln
```

Passa quando todos os testes passam, só com os três avisos `xUnit1031` de hoje.

---

### Task 1: Fundação do tema

**Files:**
- Create: `src/Midview.Master/Themes/Painel.Colors.xaml`, `Painel.Controls.xaml`, `Painel.Main.xaml`, `Painel.Dialogs.xaml` (os três últimos ainda quase vazios)
- Create: `src/Midview.Master/MidviewWindow.cs`, `src/Midview.Master/DarkTitleBar.cs`
- Modify: `src/Midview.Master/App.xaml` (os estilos de hoje vão para `Painel.Main.xaml` e `Painel.Controls.xaml`, ainda sem mudar o desenho)
- Modify: as 26 janelas, só na raiz: `<local:MidviewWindow ...>` no XAML (com `xmlns:local` onde faltar) e `: MidviewWindow` no code-behind
- Test: `tests/Midview.Master.Tests/ThemeXamlTests.cs`, `tests/Midview.Master.Tests/DarkTitleBarTests.cs`

**Interfaces:**
- Produces:
  - Chaves de cor: `BackgroundBrush` (`#0A111E`), `SurfaceBrush` (`#0F1828`), `CardBrush` (`#141F33`), `LineBrush` (`#22314D`), `LineHoverBrush` (`#3A4D70`), `TextBrush` (`#DCE6F5`), `TextMutedBrush` (`#8394AE`), `TextDisabledBrush` (`#4A5872`), `AccentBrush` (`#2DD4BF`), `OnAccentBrush` (`#04221D`), `AccentTextBrush` (`#7FF0DF`), `AccentSoftBrush` (`#2DD4BF` com opacidade 0.14), `EnergiaBrush`, `TelaBrush`, `TurmaBrush`, `ProgramasBrush`, `ConfigurarBrush`, `ErrorBrush` (`#FF8A80`), `WarningBrush` (`#FFC53D`), `OnlineBrush` (`#3DDC97`), `OfflineBrush` (`#4A5872`), `DangerSoftBrush` (`#FF6B6B` com opacidade 0.16).
  - `FontFamily` com as chaves `UiFont` (`Bahnschrift, Segoe UI`) e `IconFont` (como hoje).
  - `public class MidviewWindow : Window`.
  - `internal static class DarkTitleBar { public static void Apply(Window window, Color caption); internal static uint ColorRef(Color color); }`.

- [ ] **Step 1: Escrever os testes que falham**

Em `ThemeXamlTests`:
- `EveryResourceKeyUsedInXamlIsDefined`. Varre `src/Midview.Master/**/*.xaml`. A raiz do repositório é a pasta acima de `AppContext.BaseDirectory` que contém `Midview.sln`. Junta cada chave usada em `{StaticResource X}`/`{DynamicResource X}` e confere que ela é um `x:Key` em `Themes/*.xaml` ou no mesmo arquivo, ou está na lista do que o código cria em tempo de execução: `TileWidth`, `TileHeight`. Chaves que começam com `{`, como `{x:Type Button}` e `{x:Static ...}`, ficam de fora. A falha lista `arquivo: chave`.
- `EveryWindowIsAMidviewWindow`. Todo tipo concreto em `typeof(MainWindow).Assembly` que deriva de `System.Windows.Window` também deriva de `MidviewWindow`, a não ser o próprio `MidviewWindow`. A falha lista os tipos.

Em `DarkTitleBarTests`:
- `ColorRef_IsBlueGreenRed`: `Assert.Equal(0x001E110Au, DarkTitleBar.ColorRef(Color.FromRgb(0x0A, 0x11, 0x1E)))`.

- [ ] **Step 2: Rodar os testes e ver que falham**

Rode o comando de teste. Deve falhar por compilação (`MidviewWindow`, `DarkTitleBar` não existem).

- [ ] **Step 3: Implementar**

- `MidviewWindow`: no construtor, `SetResourceReference` de `Background` para `SurfaceBrush`, de `Foreground` para `TextBrush` e de `FontFamily` para `UiFont`. Em `OnSourceInitialized`, `DarkTitleBar.Apply(this, (Background as SolidColorBrush)?.Color ?? #0F1828)`.
- `DarkTitleBar.Apply`: `DwmSetWindowAttribute(hwnd, 20, 1)` e, se falhar, o atributo 19. Com `Environment.OSVersion.Version.Build >= 22000`, também `DwmSetWindowAttribute(hwnd, 35, ColorRef(caption))`. Ignora erros: no Windows antigo, a barra fica como o Windows desenha, e a spec aceita isso.
- Os nomes antigos (`SlateBrush`, `SlateHoverBrush`, `SlatePressedBrush`, `SlateLineBrush`, `ChalkTextBrush`, `ChalkMutedBrush`, `ChalkDisabledBrush`, `ChalkYellowBrush`, `CanvasBrush`, `CardLineBrush`, `CardLineHoverBrush`, `InkBrush`, `InkMutedBrush`, `SelectedLineBrush`, `SelectedFillBrush`, `SelectionBandFillBrush`) ficam em `Painel.Colors.xaml` com valores escuros, para nada quebrar entre as tarefas:
  - Slate* → `#0A111E`/`#17233A`/`#1F2C45`/`#22314D`;
  - Chalk* → texto, secundário, desabilitado, destaque;
  - Canvas → `#0A111E`; CardLine* → linha e linha em foco;
  - Ink* → texto e secundário;
  - Selected* → `#2DD4BF` e `#132A33`; SelectionBandFill → `#2DD4BF` com opacidade 0.15.

  `CardBrush` passa a valer `#141F33`. A Task 11 remove os nomes antigos.
- Os estilos de hoje mudam de arquivo sem mudar o desenho: `ToolbarButton`, `ToolbarGroupCaption`, `ToolbarSeparator`, `MachineTileButton` e `FilterToggleButton` vão para `Painel.Main.xaml`; o `Button` implícito vai para `Painel.Controls.xaml`.
- Em cada janela, troque só a raiz e a classe base. Tire da raiz os `Background="{StaticResource CanvasBrush}"`, `CardBrush` e `FontFamily="{StaticResource UiFont}"`, que agora vêm da base. A `MainWindow` mantém `Background` próprio (`BackgroundBrush`). A `SplashWindow` mantém `Background="Black"`.

- [ ] **Step 4: Rodar os testes e ver que passam**

Rode o comando de teste. Tudo deve passar.

- [ ] **Step 5: Commit** `Move the teacher app's styles into Painel theme dictionaries and a dark window base`

---

### Task 2: Controles escuros

**Files:**
- Modify: `src/Midview.Master/Themes/Painel.Controls.xaml`
- Test: `ThemeXamlTests` (já cobre as chaves)

**Interfaces:**
- Consumes: as chaves de cor da Task 1.
- Produces: estilos implícitos para `Button`, `ToggleButton`, `TextBox`, `PasswordBox`, `CheckBox`, `RadioButton`, `ListBox`, `ListBoxItem`, `ScrollBar`, `Slider`, `ContextMenu`, `MenuItem`, `Separator`, `ToolTip` e `Hyperlink`. Também os estilos com chave:
  - `PrimaryButton`: fundo `AccentBrush`, texto `OnAccentBrush`, SemiBold.
  - `DangerButton`: contorno e texto `ErrorBrush`; no hover, fundo `DangerSoftBrush`.
  - `FilterToggleButton`: ligado com contorno `AccentBrush`, fundo `AccentSoftBrush` e texto `AccentTextBrush`.
  - `SwitchCheckBox`: trilho de 36×20 com bolinha; ligado em `AccentBrush`.
  - `DayPillCheckBox`: pílula de 46 de largura no mínimo; marcada em `AccentBrush` com texto `OnAccentBrush`.
  - `TimeBox`: `TextBox` com fonte `Cascadia Mono, Consolas` 26, contorno `AccentBrush` e texto `AccentTextBrush`.
  - `NavRadioButton`: item de menu lateral com ícone e texto (`Tag` = glifo); marcado com fundo `CardBrush` e barra de 3 px `AccentBrush` à esquerda.

- [ ] **Step 1:** Escreva os estilos. O `MenuItem` precisa de modelo próprio com papéis (`SubmenuHeader`, `SubmenuItem`), porque o modelo padrão do Windows desenha a lista dos submenus em cinza-claro. O menu "Abrir site" > "Outro site..." usa submenu. `ScrollBar` vertical e horizontal: polegar `LineHoverBrush` e trilho transparente. `ToolTip`: fundo `CardBrush`, borda `LineBrush`, largura máxima 360 com quebra de linha. `Hyperlink`: `AccentTextBrush`. Todos os estados (hover, pressionado, desabilitado, foco do teclado com contorno `AccentBrush`) têm contraste AA.
- [ ] **Step 2:** Rode o comando de teste; tudo passa.
- [ ] **Step 3:** Rode o roteiro da Task 11 (`--sheet`). Ele abre uma "folha de controles" com um exemplar de cada estilo, em todos os estados, e um menu de botão direito com submenu. Confira a foto: nada claro, tudo legível.
- [ ] **Step 4: Commit** `Dark Painel styles for every standard control the teacher app uses`

---

### Task 3: Peças das janelas de diálogo, caixas de mensagem e pergunta de atualização

**Files:**
- Create: `src/Midview.Master/DialogHeader.cs`, `QuestionContent.cs`, `MidviewMessage.cs`
- Modify: `src/Midview.Master/Themes/Painel.Dialogs.xaml`, `QuestionWindow.xaml`, `QuestionWindow.xaml.cs`, `TeacherUpdate.cs`, `MainWindow.TeacherUpdate.cs`
- Modify (`MessageBox.Show` → `MidviewMessage.Show`, mesmos argumentos): `App.xaml.cs`, `ClassFolderWindow.Classes.cs`, `ClassFolderWindow.Lesson.cs`, `ClassFolderWindow.TakeOver.cs`, `MachinesWindow.TeacherUpdate.cs` (2), `MachinesWindow.xaml.cs` (3), `MainWindow.ClassHome.cs` (2), `ViewControlWindow.xaml.cs` (2). São 13 chamadas.
- Test: `tests/Midview.Master.Tests/MidviewMessageTests.cs`, `tests/Midview.Master.Tests/TeacherUpdateTests.cs`

**Interfaces:**
- Produces:
  - `public sealed class DialogHeader : ContentControl` com as propriedades de dependência `string Icon`, `string Heading` e `Brush Accent`. O modelo fica em `Painel.Dialogs.xaml`: quadrado de 40×40 com cantos de 12 e fundo `Accent` a 0.18, o glifo em `Accent` 19 px, o `Heading` em 20 SemiBold, e o `Content` embaixo em `TextMutedBrush`.
  - Estilos `Card` (`Border`: `CardBrush`, borda `LineBrush`, cantos 10, padding 12), `CardTitle` (14 SemiBold), `Hint` (12, `TextMutedBrush`, com quebra), `ResultCard` (`Card` com `MaxHeight` livre, para a área de andamento e resultado), `ErrorText`, `WarningText`, `StepBadge` (círculo de 22 com o número), `CodeDigit` (30×36, fonte mono 20 Bold, contorno `AccentBrush`) e `StatusChip` (pílula 11 SemiBold).
  - `public sealed record QuestionFact(string Icon, string Text);`
  - `public sealed record QuestionContent(string Title, string Icon, string Heading, string Yes, string? No, string? Body = null, IReadOnlyList<QuestionFact>? Facts = null, (string From, string To)? Versions = null, bool NoIsDefault = false);`
  - `QuestionWindow(QuestionContent content)`, `static bool Ask(Window? owner, QuestionContent content)`. O `Ask(Window owner, string title, string question, string yes, string no)` de hoje continua, com `Heading = question` e `Icon = ""`.
  - `public static class MidviewMessage { public static MessageBoxResult Show(Window? owner, string text, string title, MessageBoxButton buttons, MessageBoxImage image, MessageBoxResult defaultResult = MessageBoxResult.None); public static MessageBoxResult Show(string text, string title, MessageBoxButton buttons, MessageBoxImage image); internal static (string Yes, string? No) Labels(MessageBoxButton buttons); internal static string IconFor(MessageBoxImage image); }`. A sobrecarga sem dono serve para `MachinesWindow.TeacherUpdate.cs:189`, que hoje chama o `MessageBox.Show` sem janela.
  - `public static QuestionContent TeacherUpdate.NewVersionContent(Version current, Version available)`, que substitui `NewVersionQuestion`.

- [ ] **Step 1: Escrever os testes que falham**

`MidviewMessageTests`:
- `Labels_OkIsOneButton`: `Labels(MessageBoxButton.OK) == ("OK", null)`.
- `Labels_YesNoIsSimNao`: `Labels(MessageBoxButton.YesNo) == ("Sim", "Não")`.
- `IconFor_EachImageHasAGlyph`: `Information` → `"\uE946"`, `Warning` → `"\uE7BA"`, `Error` → `"\uEA39"`, `Question` → `"\uE9CE"`.

Em `TeacherUpdateTests`, troque `NewVersionQuestion_SaysBothVersionsAndWhatHappens` por `NewVersionContent_SaysBothVersionsAndWhatHappens`, com `NewVersionContent(new Version(0,0,39), new Version(0,0,40))`:
- `Title == "Atualizar professor"`, `Heading == "Chegou uma versão nova do Midview"`, `Versions == ("0.0.39", "0.0.40")`, `Yes == "Atualizar agora"`, `No == "Agora não"`;
- `Facts` com os textos, nesta ordem:
  1. `"O Midview fecha e abre de novo sozinho, o que leva 1 ou 2 minutos."`
  2. `"Enquanto isso, as telas bloqueadas dos alunos são liberadas."`
  3. `"O Windows vai pedir permissão de administrador."`

- [ ] **Step 2:** Rode o comando de teste; deve falhar por compilação.
- [ ] **Step 3: Implementar**
  - `MidviewMessage.Show` monta um `QuestionContent`: o título é a legenda; o ícone vem de `IconFor(image)`; o `Heading` é o título; o `Body` é o texto, com as quebras de linha de hoje; os botões vêm de `Labels(buttons)`; `NoIsDefault` quando `defaultResult == MessageBoxResult.No`.
  - Ele devolve `Yes`/`No` quando os botões são `YesNo`, e `OK` quando são `OK`. Fechar a janela ou apertar Esc vale `No` em `YesNo` e `OK` em `OK`, como no `MessageBox`. Sem dono (`owner == null`), a janela abre no centro da tela.
  - A `QuestionWindow` mostra: o cabeçalho (ícone num quadrado `AccentBrush` a 0.18 e `Heading`); as `Versions` como dois `StatusChip` com `` entre eles; os `Facts` como linhas com ícone num quadrado de 30; o `Body`; e, no rodapé à direita, "No" e "Yes" (`PrimaryButton`). `IsDefault` e o foco ficam no botão de `NoIsDefault`.
  - Os glifos dos `Facts` de `NewVersionContent`, na ordem: `E72C`, `E785`, `EA18`.
  - `MainWindow.TeacherUpdate.cs` usa `QuestionWindow.Ask(this, TeacherUpdate.NewVersionContent(current, release.Version))`.
- [ ] **Step 4:** Rode o comando de teste; tudo passa. Confirme que não sobrou chamada: `grep -rn "MessageBox.Show" src/Midview.Master` não acha nada.
- [ ] **Step 5: Commit** `Dialog header and cards, Midview's own message boxes, and the new-version question in the Painel look`

---

### Task 4: Janela da sala de aula

**Files:**
- Create: `src/Midview.Master/MachineCountText.cs`, `src/Midview.Master/MainWindow.TitleBar.cs`
- Modify: `src/Midview.Master/ToolbarButton.cs` (propriedade `IconBrush`), `src/Midview.Master/MainWindow.xaml`, `MainWindow.xaml.cs` (só para chamar `UpdateTitleChips` onde `PublishPresence` é chamado e onde máquinas entram ou saem de `_tiles`), `MainWindow.ClassFolder.cs:61` (também chama `UpdateTitleChips`), `MainWindow.TileLayout.cs:28` (`TileChromeWidth`), `Themes/Painel.Main.xaml`
- Test: `tests/Midview.Master.Tests/MachineCountTextTests.cs`

**Interfaces:**
- Consumes: `TilePresence.Online` (`MachineTile.Presence`); `ClassFolderButton.Caption`, que tem o nome da turma com aula ligada.
- Produces: `public static class MachineCountText { public static string Online(int online, int total); }`, e `private void UpdateTitleChips()` em `MainWindow.TitleBar.cs`.

- [ ] **Step 1: Escrever o teste que falha.** `MachineCountTextTests`, um `[Theory]`:
  - `(0, 0) → ""`
  - `(1, 18) → "1 de 18 ligada"`
  - `(16, 18) → "16 de 18 ligadas"`
  - `(0, 31) → "0 de 31 ligadas"`
  - `(31, 31) → "31 de 31 ligadas"`
- [ ] **Step 2:** Rode o comando de teste e veja falhar.
- [ ] **Step 3: Implementar `MachineCountText.Online`** e a janela:
  - **Barra de título** (`MainWindow.TitleBar.cs` e uma linha nova no topo do `DockPanel`):
    - `WindowChrome` com `CaptionHeight=32`, `ResizeBorderThickness=6`, `GlassFrameThickness=0` e `UseAeroCaptionButtons=false`.
    - A linha tem: o símbolo (grade 2×2 de quadradinhos `#2DD4BF`, `#4DA3FF`, `#4DA3FF`, `#A78BFA`); "Midview" em SemiBold 15; "Sala de aula" em `TextMutedBrush`; o selo `TitleChip` "Aula: {turma}", visível só quando `ClassFolderButton.Caption` não é vazio, com contorno `AccentBrush` a 0.5 e texto `AccentTextBrush`; e o selo "N de M ligadas", visível quando o texto não é vazio, com bolinha `OnlineBrush`.
    - Os três botões `TitleBarButton` de 46 px usam `SystemCommands`. O de fechar fica vermelho no hover.
    - Gancho `HwndSource.AddHook`: `WM_NCHITTEST` (0x0084) sobre o botão maximizar devolve `HTMAXBUTTON` (9), que faz o Windows 11 mostrar o menu de encaixe. O hover desse botão é tratado com `WM_NCMOUSEMOVE`/`WM_NCMOUSELEAVE`, e o clique sai pelo `WM_NCLBUTTONUP` em cima dele.
    - Maximizada, a janela ganha `Margin` igual a `SystemParameters.WindowResizeBorderThickness` mais 2.
    - O selo da turma e o "N de M ligadas" são atualizados por `UpdateTitleChips()`.
  - **Barra de botões:**
    - Cada grupo vira um `Border` `ToolbarGroupPanel`: `CardBrush`, borda `LineBrush`, cantos 12, padding `2,2,2,0`. O nome do grupo é um `ToolbarGroupCaption` em 9,5 px maiúsculo, com espaçamento 0,13 em e bolinha da cor do grupo.
    - Os separadores saem; o `WrapPanel` usa margens de 4 entre grupos.
    - Cada `ToolbarButton` recebe a cor do grupo em `Foreground` do ícone, por uma propriedade nova `Brush IconBrush` em `ToolbarButton.cs`. O rótulo fica em `TextBrush`, 11 px. Hover `#1F2C45`, pressionado `#22314D`, foco com contorno `AccentBrush`, desabilitado com ícone e texto `TextDisabledBrush`.
    - O selo da Pasta da turma (`Caption`) usa `TurmaBrush` com texto `#1A1300`.
    - As larguras de hoje (58, e 72 e 84 nos dois botões largos) ficam.
  - **Telas dos alunos** (`MachineTileButton` e o `DataTemplate`):
    - Cartão `CardBrush`, borda `LineBrush`, cantos 12, padding 4. A imagem ganha cantos 8.
    - O nome sai de cima da imagem. Por baixo dela entra uma linha com bolinha 8 px (`OnlineBrush` quando online; `OfflineBrush` quando `Connecting`/`Offline`; `EnergiaBrush` quando a tela está bloqueada), o `Name` e, à direita, o `Status` curto.
    - O selo "🔒 Bloqueada" fica sobre a imagem, no canto de cima à esquerda, quando `ScreenLockBadge` não é vazio e `ScreenLockAlert` é falso. Os avisos (`ScreenLockAlert`, `BlockBadge`) continuam debaixo do cartão, em `ErrorBrush`.
    - Selecionado: fundo `#132A33`, anel de 3 px `AccentBrush` e um `DropShadowEffect` `#2DD4BF` de raio 18 e opacidade 0.35, só no anel do cartão selecionado.
    - `TileChromeWidth` passa de 10 para 18 (margem 4+4, borda 1+1, padding 4+4). Atualize o comentário dele.
  - **Faixa de baixo:** `SurfaceBrush`, linha de cima `LineBrush`. Os botões ligados usam `FilterToggleButton`, o controle de tamanho usa o `Slider` escuro e o texto de estado fica em `TextMutedBrush`.
  - **Avisos de sala vazia e "Só online":** o ícone `E7F8` em 40 px `TextMutedBrush` acima do texto. "Procurar máquinas na rede" usa `PrimaryButton`.
  - O menu de botão direito fica com os estilos da Task 2 e os mesmos itens.
- [ ] **Step 4:** Rode o comando de teste; tudo passa.
- [ ] **Step 5:** Rode o roteiro da Task 11 (`--main`) com 31 máquinas falsas, uma bloqueada e duas sem resposta. Confira:
  - a 1340 px, a barra cabe numa linha;
  - em "Visualizar todos", as 31 telas cabem, nenhuma cai sozinha numa linha, a 1340×760 e a 1536×864;
  - à mão, maximize com dois cliques, arraste a janela para a borda e veja o menu de encaixe ao parar o mouse no maximizar.
- [ ] **Step 6: Commit** `Classroom window in the Painel look: own title bar, grouped toolbar, dark tiles`

---

### Task 5: Máquinas da sala

**Files:**
- Create: `src/Midview.Master/MachinesPage.cs`
- Modify: `src/Midview.Master/MachinesWindow.xaml`, `MachinesWindow.xaml.cs`, `MachinesWindow.TeacherUpdate.cs:103`

**Interfaces:**
- Produces: `public enum MachinesPage { Search, Updates, OtherTeacher, AdminPassword, Advanced }` e `public void ShowPage(MachinesPage page)` em `MachinesWindow`.

- [ ] **Step 1: Implementar.** A janela muda para 820×600:
  - **Menu à esquerda** de 196 px em `BackgroundBrush`, com cinco `NavRadioButton`:
    - "Procurar e parear", ícone `E721`;
    - "Atualizações", `E895`;
    - "Outro professor", `E716`;
    - "Senha de administrador", `E72E`;
    - "Avançado", `E713`.
  - **À direita**, um painel por página, cada um com seu `DialogHeader` (ícone e título iguais aos do menu, `Accent` = `ConfigurarBrush`, e a dica de hoje como conteúdo) e os mesmos controles, com os mesmos `x:Name`.
  - **Procurar e parear:**
    - "Procurar na rede" usa `PrimaryButton`;
    - cada máquina encontrada vira um `Card` com: caixinha; ícone `E7F4`; `Name` em SemiBold, com o `Address` embaixo; à direita, um `StatusChip` com o `Status` e o `Outcome` (`ErrorBrush` quando `OutcomeIsError`);
    - com `AwaitingConfirmation`, aparece uma linha a mais: "Na tela do aluno aparece:", cada caractere de `CodeText` num `CodeDigit` (por um `ItemsControl` sobre a string), "Confere" (`PrimaryButton`) e "Não confere";
    - "Um colega já tem? Entrar na sala de um colega..." vai para o pé da página.
  - **Atualizações:** "Atualizar as máquinas" e "Atualizar professor", cada um num `Card`.
  - **Outro professor**, **Senha de administrador** e **Avançado:** um `Card` cada um, com o conteúdo de hoje. "Remover" a senha usa `DangerButton`.
  - `ShowPage` marca o item do menu e mostra o painel. A janela abre em `Search`. `OpenStudentUpdate()` e `StartTeacherUpdate(...)` chamam `ShowPage(MachinesPage.Updates)` antes do resto.
- [ ] **Step 2:** Rode o comando de teste; tudo passa.
- [ ] **Step 3:** Roteiro `--dialogs`: confira a foto de cada página, uma máquina esperando código e outra com erro.
- [ ] **Step 4: Commit** `Máquinas da sala with a side menu of pages in the Painel look`

---

### Task 6: Agendar ligar e desligar

**Files:**
- Create: `src/Midview.Master/ScheduleTimeline.cs`
- Modify: `src/Midview.Master/ScheduleWindow.xaml`, `ScheduleWindow.xaml.cs`
- Test: `tests/Midview.Master.Tests/ScheduleTimelineTests.cs`

**Interfaces:**
- Consumes: `PowerSchedule.TryParseTime`, `PowerSchedule.FormatTime` (`Midview.Protocol`); minutos desde a meia-noite (0–1439).
- Produces:
  - `public sealed record TimelineSegment(double Start, double End);`, frações do dia de 0 a 1;
  - `public static class ScheduleTimeline { public static (IReadOnlyList<TimelineSegment> Segments, string Text) For(int? wakeMinute, int? shutdownMinute); }`.

- [ ] **Step 1: Escrever os testes que falham** (`ScheduleTimelineTests`):
  - `BothInOrder`: `For(420, 1080)` → um trecho `(0.29166…, 0.75)`, texto `"Ligado das 07:00 às 18:00"`.
  - `PastMidnight`: `For(1320, 360)` → trechos `(0, 0.25)` e `(0.91666…, 1)`, texto `"Ligado das 22:00 às 06:00 do dia seguinte"`.
  - `WakeOnly`: `For(420, null)` → trecho `(0.29166…, 1)`, texto `"Liga às 07:00 e fica ligado até alguém desligar"`.
  - `ShutdownOnly`: `For(null, 1080)` → nenhum trecho, texto `"Desliga às 18:00"`.
  - `Nothing`: `For(null, null)` → nenhum trecho, texto `"Nada agendado"`.
  - `SameTime`: `For(420, 420)` → nenhum trecho, texto `"Os horários de ligar e de desligar são iguais."` (a mesma frase do `ReadForm`).

  Compare as frações com tolerância de 1e-9.
- [ ] **Step 2:** Rode o comando de teste e veja falhar.
- [ ] **Step 3: Implementar** `ScheduleTimeline.For` e a janela. A janela fica com 600×640.
  - **Cabeçalho:** `DialogHeader` (`E787`, "Agendar ligar e desligar", `EnergiaBrush`) com o `TargetsTextBlock` como conteúdo.
  - **Dias:** "Nos dias" e os sete `CheckBox` de hoje, com o estilo `DayPillCheckBox` e os mesmos nomes.
  - **Dois `Card` lado a lado**, cada um com: ícone (`E945` em ligar, `E7E8` em desligar); "Ligar às" ou "Desligar às"; o `WakeCheckBox`/`ShutdownCheckBox` com `SwitchCheckBox` e sem texto próprio (o rótulo fica ao lado); o `WakeTimeTextBox`/`ShutdownTimeTextBox` com `TimeBox`; e a dica de hoje com `Hint`.
  - **Régua:**
    - `TimelineTrack`: `Grid` de 30 px, `CardBrush`, cantos 8;
    - dentro dela, até dois `Border` `AccentSoftBrush` com contorno `AccentBrush`. Eles são posicionados por `UpdateTimeline()` a partir de `ActualWidth` × `Start`/`End`;
    - o `Text` fica centrado sobre a régua, em `AccentTextBrush`;
    - embaixo, as marcas 0h, 6h, 12h, 18h e 24h.
  - **Rodapé:** `LastRunTextBlock` numa linha com o ícone `E73E`; `ProgressTextBlock` e `ResultTextBlock` num `ResultCard`; "Salvar agendamento" (`SaveButton`) com `PrimaryButton`, à direita.
  - `UpdateTimeline()` lê cada horário só se a chave estiver ligada e `PowerSchedule.TryParseTime` aceitar o texto. Um horário inválido conta como `null`, e o erro continua aparecendo ao salvar, como hoje. Ela roda no construtor e em `Checked`/`Unchecked` das chaves, em `TextChanged` dos horários e em `SizeChanged` da régua.
- [ ] **Step 4:** Rode o comando de teste; tudo passa.
- [ ] **Step 5:** Roteiro `--dialogs`: foto com seg–sex, 07:00 e 18:00 ligados; outra só com ligar.
- [ ] **Step 6: Commit** `Agendar ligar e desligar with day pills, on/off cards and a day timeline`

---

### Task 7: Janelas de Energia e Turma

**Files:**
- Modify: `PowerWindow.xaml`, `SendMessageWindow.xaml`, `SendFileWindow.xaml`, `CollectFilesWindow.xaml`, `ChatWindow.xaml`, `ClassChatWindow.xaml` (os `.xaml.cs` só se a tarefa mandar)

A regra de cada janela é a mesma:
- o topo vira `DialogHeader` (ícone e grupo da tabela da spec) com o `TargetsTextBlock` (ou `StatusTextBlock` nas conversas) como conteúdo;
- as dicas cinza viram `Hint` dentro de `Card`;
- a ação da janela usa `PrimaryButton`;
- `ProgressTextBlock` e `ResultTextBlock` ficam num `ResultCard`;
- `Foreground="Gray"`, `#FAFAFA`, `#E3E3E3` e `#B3261E` saem.

- [ ] **Step 1:** Faça cada janela:
  - **`PowerWindow`:** `E7E8`, Energia. Em código, o `Icon` do cabeçalho troca para `E72C` quando é reinício, na mesma linha em que hoje o `Title` muda (`PowerWindow.xaml.cs:48`).
    - Ação e espera em dois `Card`.
    - A caixa das atualizações do Windows num `Card`.
    - `RunButton` usa `DangerButton`; `CancelShutdownButton` é normal.
  - **`SendMessageWindow`:** `E8BD`, Turma; `SendButton` principal.
  - **`SendFileWindow`:** `E898`, Turma; o arquivo e as opções em `Card`; `SendButton` principal.
  - **`CollectFilesWindow`:** `E896`, Turma.
    - Card "Recolher", com `CollectButton` principal e `OpenFolderButton`.
    - Card "Para começar a próxima aula", com `ClearStudentsButton` e `ClearCollectedButton` em `DangerButton`.
  - **`ChatWindow`:** `E8F2`, Turma. A conversa num `Card` que ocupa o meio; o campo e `SendButton` (principal) no rodapé.
  - **`ClassChatWindow`:** igual à `ChatWindow`.
- [ ] **Step 2:** Rode o comando de teste; tudo passa.
- [ ] **Step 3:** Roteiro `--dialogs`: fotos das seis.
- [ ] **Step 4: Commit** `Energia and Turma windows in the Painel look`

---

### Task 8: Janelas de Programas e sites

**Files:**
- Modify: `OpenWebsiteWindow.xaml`, `OpenProgramWindow.xaml`, `InstallWindow.xaml`, `UninstallWindow.xaml`, `AppBlockWindow.xaml`, `SiteBlockWindow.xaml`, `CloseAppsWindow.xaml`

A regra é a mesma da Task 7, com `ProgramasBrush` e os ícones da tabela da spec.

- [ ] **Step 1:** Faça cada janela:
  - **`OpenWebsiteWindow` e `OpenProgramWindow`:** `OpenButton` principal; "Salvar no menu" e o nome num `Card`; `ProblemTextBlock` com `ErrorText`.
  - **`InstallWindow`:** `InstallButton` principal.
  - **`UninstallWindow`:** a lista (`ProgramsList`) num `Card` com `CheckBox` escuros; `UninstallButton` com `DangerButton`.
  - **`AppBlockWindow`:**
    - os dois modos são `RadioButton`, cada um dentro de um `Card`;
    - "Programas abertos" e a lista num `Card`;
    - `ApplyButton` principal; `LiftButton` normal.
  - **`SiteBlockWindow`:**
    - card "Internet inteira", com "Bloquear a internet" em `DangerButton` e "Liberar a internet" normal;
    - card "Um site", com "Bloquear o site" em `DangerButton` e "Liberar o site" normal.
  - **`CloseAppsWindow`:**
    - os dois modos em `Card`, com "Forçar" no texto `WarningText`;
    - a espera num `Card`;
    - `CloseButton` principal.
- [ ] **Step 2:** Rode o comando de teste; tudo passa.
- [ ] **Step 3:** Roteiro `--dialogs`: fotos das sete. Desinstalar e Bloquear programas com listas de 12 itens.
- [ ] **Step 4: Commit** `Programas e sites windows in the Painel look`

---

### Task 9: Painel do laboratório e Pasta da turma

**Files:**
- Modify: `LabConfigWindow.xaml`, `ClassFolderWindow.xaml`

- [ ] **Step 1:** Faça as duas janelas:
  - **`LabConfigWindow`:** `E713`, Configurar.
    - Os `Style` locais (`SectionHeader`, `SectionNote`, `FunctionName`, `FunctionHint`, `Row`) passam a usar os tokens. `#2D2D30`, `Gray` e `#E0E0E0` saem.
    - Cada seção vira um `Card` com `CardTitle`, e as linhas dentro dele são separadas por `LineBrush`.
    - Os passos 1 a 5 ganham `StepBadge` com o número. O texto continua "Passo N — ...".
    - "Ativar" é normal. "Limpar agora", "Tudo" e "RESET geral" usam `DangerButton`.
  - **`ClassFolderWindow`:** `E8F7`, Turma, com `DialogHeader` acima dos três modos.
    - O `PrimaryButton` local sai; vale o global.
    - Os avisos:
      - `RemoteBorder`: `AccentSoftBrush` com borda `AccentBrush`;
      - `LowSpaceBorder`: `DangerSoftBrush` com texto `ErrorBrush`;
      - `AsideBorder`: `Card`.
    - `ClassesList` e o `Border` dos computadores ficam em `Card`.
    - Nos `DataTrigger` dos computadores, `IsFailed` usa `ErrorBrush` e `IsOffline` usa `TextMutedBrush`.
    - "Excluir" usa `DangerButton`.
    - As três escolhas de "Ao encerrar a aula" são `RadioButton` dentro de um `Card`.
- [ ] **Step 2:** Rode o comando de teste; tudo passa.
- [ ] **Step 3:** Roteiro `--dialogs`: fotos do painel inteiro, rolado, e da pasta da turma nos três modos.
- [ ] **Step 4: Commit** `Painel do laboratório and Pasta da turma in the Painel look`

---

### Task 10: Janelas de Configurar e Ver/controlar

**Files:**
- Modify: `AddMachineWindow.xaml`, `GiveAccessWindow.xaml`, `JoinClassWindow.xaml`, `JoinClassWindow.xaml.cs:14,87`, `SetAdminPasswordWindow.xaml`, `UpdateWindow.xaml`, `ViewControlWindow.xaml`, `ViewControlWindow.xaml.cs` (só o `ModeTextBlock`, se precisar de cor)

- [ ] **Step 1:** Faça cada janela:
  - **`AddMachineWindow`:** `E713`, cabeçalho "Ajustes avançados". As duas partes ("Este PC de professor" e "Adicionar ou editar uma máquina à mão") viram dois `Card`; o `Separator` sai. `SaveButton` principal.
  - **`GiveAccessWindow`:** `E716`. O código (`CodeTextBlock`) fica em `AccentTextBrush`, num `Card`.
  - **`JoinClassWindow`:** `E716`. `JoinButton` principal. No `.xaml.cs`, o `ErrorBrush` fixo `#B3261E` sai: `StatusTextBlock.Foreground = (Brush)FindResource(isError ? "ErrorBrush" : "TextBrush")`.
  - **`SetAdminPasswordWindow`:** `E72E`. "Salvar" principal; `ErrorTextBlock` com `ErrorText`.
  - **`UpdateWindow`:** `E895`. `UpdateButton` principal; `StopButton` com `DangerButton`; `WarningTextBlock` com `WarningText`.
  - **`ViewControlWindow`:**
    - a barra de cima fica `SurfaceBrush` com borda de baixo `LineBrush`, e os botões são os normais;
    - o `ModeTextBlock` vira um `StatusChip`: fundo `AccentSoftBrush` e texto `AccentTextBrush` controlando, `TextMutedBrush` só vendo;
    - `CompatibilityNote` fica com fundo `#FFC53D` a 0.16 e texto `WarningBrush`;
    - a área da tela continua `Black`.
- [ ] **Step 2:** Rode o comando de teste; tudo passa.
- [ ] **Step 3:** Roteiro `--dialogs`: fotos das seis.
- [ ] **Step 4: Commit** `Configurar and view/control windows in the Painel look`

---

### Task 11: Limpeza, guarda e fotos para o professor

**Files:**
- Modify: `src/Midview.Master/Themes/Painel.Colors.xaml` (sem os nomes antigos), e todo `.xaml` que ainda os use
- Test: `tests/Midview.Master.Tests/ThemeXamlTests.cs`
- Create (fora do repositório): o roteiro em `<pasta de rascunho da sessão>\painel-harness\`, a partir de `C:\Users\Neto\AppData\Roaming\Claude\side-session-notes\local_c1b56ff1-e97e-4802-bc8f-f2b1d9768915\tilecheck-harness\`

**Interfaces:**
- Consumes: tudo das Tasks 1–10.

- [ ] **Step 1: Escrever o teste que falha.** `NoLightColorsOrWindowsMessageBoxesLeft`:
  - Nenhum `.xaml` de `src/Midview.Master` pode conter:
    - `#B3261E`, `#FAFAFA`, `#E0E0E0`, `#E3E3E3`, `#FDECEA`, `#F2B8B5`, `#2D2D30`, `#5A4A1E`;
    - `Foreground="Gray"`, `Foreground="White"`, `Background="White"`;
    - os nomes antigos `SlateBrush`, `CanvasBrush`, `InkBrush`, `InkMutedBrush`, `ChalkYellowBrush`, `SelectedFillBrush`, `CardLineBrush`.
  - Nenhum `.cs` de `src/Midview.Master` pode conter `MessageBox.Show(`.
  - A falha lista `arquivo: trecho`.
- [ ] **Step 2:** Rode o comando de teste. Ele falha enquanto sobrar nome antigo.
- [ ] **Step 3:** Troque cada uso restante pelo token novo e tire os nomes antigos de `Painel.Colors.xaml`.
- [ ] **Step 4:** Rode o comando de teste; tudo passa.
- [ ] **Step 5: Roteiro de fotos.** Um programa WPF de console. Ele:
  - referencia `src/Midview.Master` e cria um `new Application()`;
  - carrega os quatro dicionários de `Themes` por `pack://`;
  - aponta `MasterData.Folder` para uma pasta temporária com `known-machines.json` falso e sobe `SessionServer`s em 127.0.0.2 a 127.0.0.32 (`ThreadPool.SetMinThreads(256, 256)`);
  - abre cada janela com dados falsos, sem rede real;
  - salva PNGs a 1366×768 com `RenderTargetBitmap`.

  Modos: `--sheet` (folha de controles da Task 2), `--main` (sala de aula) e `--dialogs` (as outras 25 janelas e uma `MidviewMessage` de cada tipo). Saída em `<pasta de rascunho>\painel-fotos\`.
- [ ] **Step 6:** Rode os três modos e confira todas as fotos contra a seção "Não pode acontecer" da spec. Mostre as fotos ao professor numa página. **Não publique nenhuma versão sem o "pode" dele.**
- [ ] **Step 7: Commit** `Remove the old light-theme names and guard against light colours and Windows message boxes`
