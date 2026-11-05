# Plano de implementação: onde salvar durante a Pasta da turma

> **Para agentes:** SUB-SKILL OBRIGATÓRIA: use superpowers:subagent-driven-development (recomendada) ou superpowers:executing-plans para implementar este plano tarefa por tarefa. Os passos usam caixas (`- [ ]`) para acompanhar o andamento.

**Objetivo:** com a Pasta da turma de uma aula ligada, os Downloads do aluno, a pasta padrão de salvar do Word, do Excel e do PowerPoint e a "última pasta usada" do Bloco de Notas e do Paint apontam para ela. Quando a aula termina, ou o Midview é desinstalado, tudo volta ao que era.

**Arquitetura:**
- A Sessão, como o aluno, aplica e desfaz no mesmo timer de 5 s de `ClassFolder.Follow`.
- Antes de gravar, ela anota cada valor que muda ("antes" e "gravado") num arquivo de estado em `%LOCALAPPDATA%\Midview`.
- A regra para desfazer, a lista de valores permitidos, o arquivo de estado e o formato de `LastVisitedPidlMRU` ficam no Midview.Protocol. Assim, o `UninstallCleanup` do Agent (como SYSTEM, no registro carregado do aluno) roda exatamente o mesmo desfazer.
- As chamadas do shell (`SHSetKnownFolderPath`, `ILCreateFromPathW`) ficam atrás de `IShellFolders`, falsificada nos testes.

**Tecnologias:** .NET 8 (`net8.0-windows`), C#, xUnit, `Microsoft.Win32.Registry`, P/Invoke para o shell32.

**Desenho:** `docs/superpowers/specs/2026-11-04-class-folder-save-locations-design.md`

## Restrições gerais

- Comentários explicam o porquê, em inglês. O texto que o professor ou o aluno lê fica em português.
- Arquivos com menos de 500 linhas, no estilo do repositório (veja `ClassFolder.Follow.cs` e `DownloadFolderPolicy.cs`).
- Os testes de registro rodam só em `HKCU\Software\MidviewTest\<guid>`, no lugar da chave `Software` do aluno, apagada no `Dispose`.
- Nenhum teste muda os Downloads, o Office ou as caixas de diálogo reais do desenvolvedor. A única conferência com as funções reais (Tarefa 1) devolve tudo num `finally`.
- Nada é testado nos computadores do laboratório; o professor testa no ALUNO-25 (seção "Conferência no laboratório" do desenho).
- Nunca citar o programa proibido no código, na documentação ou nos commits.
- Compilar e testar com `"C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe"` (o `dotnet` do PATH não tem SDK). Três avisos `xUnit1031` que já existiam não são falhas novas.
- Sem commit por tarefa: commit só quando o professor pedir (fluxo de versão, "Publicar X.Y.Z").

## Decisões além do desenho (de quem programa, seção "Como" do desenho)

- **Troca de aula** (outra turma, ou a mesma turma com outro id de aula): primeiro desfaz, depois aplica. O valor devolvido vira o novo "antes". O resultado é o mesmo do "aponta de novo sem sobrescrever o antes" do desenho.
- **Entrada de `LastVisitedPidlMRU`:** só conta como ainda do Midview quando os bytes são exatamente os que o Midview gravou. A lista de IDs não é decodificada, para o Agent poder fazer a mesma conferência.
  - Se o aluno salvou de novo na Pasta da turma e o Windows regravou a entrada, ela fica apontando para uma pasta que não existe mais.
  - Nesse caso, a caixa de diálogo volta ao padrão dela.
- **Uma vez por aula:** cada alvo é aplicado uma vez por aula, quando a pasta dela passa a existir como pasta de verdade. O arquivo de estado lembra dessa aula, então um reinício da Sessão ou um novo login na mesma aula não aplicam de novo.
- **Anotar antes de gravar:** o estado é salvo *antes* de qualquer gravação. Se não der para salvar, nada é gravado e o próximo ciclo tenta de novo.
- **Segurança:** o arquivo de estado é do aluno, que pode editá-lo.
  - Só entradas da lista de permitidos (chave, nome do valor, tipo) são devolvidas. Assim, um arquivo alterado não faz o Agent, como SYSTEM, gravar em `Software\Policies`, por exemplo.
  - O Agent nunca grava nem apaga o arquivo.
  - O Agent só o lê quando nem o arquivo nem as pastas abaixo do perfil são vínculos, e só até 64 KB.
- **Tipo inesperado:** um alvo cujo valor atual tem um tipo inesperado fica como está (não é aplicado nem anotado).

## Foco da revisão

- Um arquivo de estado alterado, com entradas fora da lista de permitidos, não pode gravar nada. Exemplos: `Policies\...`, um nome MRU que não é número, um tipo errado, uma entrada MRU de outro programa. Testes na Tarefa 3 (`IsAllowed`) e na Tarefa 7 (desinstalação).
- O arquivo da aula não pode ser lido no meio da aula (`agentLesson` devolve null): nada pode ser desfeito nem aplicado. Teste na Tarefa 5.
- O aluno muda um valor durante a aula (Downloads para `D:\Meus downloads`): a mudança tem de ficar no fim. Testes nas Tarefas 4 e 5.
- `MRUListEx` malformado: Bloco de Notas e Paint ficam como estão, e Downloads e Office são aplicados mesmo assim. Teste na Tarefa 5.
- A Sessão reinicia no meio da aula, ou o aluno entra depois de a aula terminar: nada é aplicado duas vezes, e o desfazer ainda acontece. Teste na Tarefa 5.

---

### Tarefa 1: Conferir neste PC os valores do Office e a função da pasta Downloads (sem código do produto)

A tabela do Office no desenho vem de fontes da comunidade e precisa ser confirmada (desenho, §2). Este PC tem o Office Home & Student 2019 Clique para Executar, que usa o `16.0`. Os scripts vão para o scratchpad, nunca para o repositório.

**Arquivos:**
- Alterar: `docs/superpowers/specs/2026-11-04-class-folder-save-locations-design.md`, tabela da §2 (anotar o que foi confirmado)

- [ ] **Passo 1: Word.**
  - Com PowerShell e COM (`Word.Application`), ler `Options.DefaultFilePath(0)` (wdDocumentsPath).
  - Defini-lo para uma pasta temporária nova (`InvokeMember("DefaultFilePath", SetProperty, …, @(0, caminho))`) e chamar `Quit()`.
  - Ler `HKCU\Software\Microsoft\Office\16.0\Word\Options` `DOC-PATH`: nome, tipo e texto.
  - No `finally`, devolver o valor antigo do mesmo jeito.
- [ ] **Passo 2: Excel.** O mesmo, com `Excel.Application.DefaultFilePath` e `HKCU\…\16.0\Excel\Options` `DefaultPath`.
- [ ] **Passo 3: PowerPoint.** O PowerPoint não tem propriedade no modelo de objetos.
  - Ler `HKCU\…\16.0\PowerPoint\RecentFolderList` `Default`.
  - Se não existir, pedir ao professor que defina uma vez neste PC Arquivo > Opções > Salvar > "Local padrão dos arquivos". Depois, ler e devolver.
  - Se ele não quiser, manter a linha do desenho e marcá-la como "só na conferência do laboratório".
- [ ] **Passo 4: um programa aberto sobrescreve o valor?**
  - Abrir o Word, gravar `DOC-PATH` de fora para uma pasta temporária, fechar o Word e ler `DOC-PATH` de novo.
  - Repetir com o Excel.
  - Se o programa gravou o valor antigo de volta ao sair, anotar no desenho, em "Limites conhecidos".
  - Devolver tudo no `finally`.
- [ ] **Passo 5: Downloads.** Usar P/Invoke via `Add-Type` do PowerShell: `SHSetKnownFolderPath(FOLDERID_Downloads, KF_FLAG_DONT_UNEXPAND = 0x2000, IntPtr.Zero, caminho)`. Em `try`/`finally`:
  - Ler o valor bruto de `User Shell Folders` `{374DE290-123F-4565-9164-39C4925E467B}` e o tipo dele.
  - Apontá-lo para uma pasta temporária nova e ler o valor bruto e o tipo.
  - Ver se apareceu um `desktop.ini` nessa pasta.
  - Voltar para o texto bruto antigo (por exemplo `%USERPROFILE%\Downloads`) com a mesma flag e ler de novo.
  - Esperado: o texto gravado é exatamente o que foi passado, nas duas vezes, como `REG_EXPAND_SZ`.
  - Se aparecer um `desktop.ini` que renomeia a pasta, parar e avisar: a Pasta da turma apareceria como "Downloads" na Área de Trabalho.
- [ ] **Passo 6: Anotar os resultados.** Atualizar a tabela da §2 do desenho com nome, tipo e chave confirmados, e acrescentar o que os Passos 4 e 5 mostraram. Ajustar as constantes da Tarefa 3 por ela.

---

### Tarefa 2: `LastVisitedMru`, o formato da "última pasta usada" da caixa de diálogo

**Arquivos:**
- Criar: `src/Midview.Protocol/LastVisitedMru.cs`
- Teste: `tests/Midview.Protocol.Tests/LastVisitedMruTests.cs`

**Interfaces:**
- Produz:
  - `public static class LastVisitedMru`
  - `public const string OrderName = "MRUListEx";`
  - `public static List<int>? ParseOrder(byte[]? data)`: os números, do mais recente para o mais antigo. Um `data` null (sem valor) dá lista vazia. O resultado é null quando:
    - o tamanho não é múltiplo de 4;
    - não há o fim `0xFFFFFFFF`;
    - um número se repete;
    - um número passa de 9999;
    - há algo depois do fim.
  - `public static byte[] FormatOrder(IEnumerable<int> order)`: DWORDs little-endian e depois `0xFFFFFFFF`.
  - `public static string? ProgramOf(byte[] entry)`: o nome UTF-16LE antes do primeiro nulo UTF-16. Null quando não há nulo nos primeiros 260 caracteres.
  - `public static byte[] Entry(string program, byte[] idList)`: o nome do programa em UTF-16LE, um nulo UTF-16 e o `idList` sem mudança.

- [ ] **Passo 1: Escrever os testes, que devem falhar.**
  - `ParseOrder_ReadsNewestFirst`: `[2,0,0,0, 0,0,0,0, 1,0,0,0, 0xFF,0xFF,0xFF,0xFF]` dá `[2, 0, 1]`.
  - `ParseOrder_NoValue_IsEmpty`: `null` dá `[]`.
  - `ParseOrder_Malformed_IsNull` (Theory): tamanho 6; sem fim; `[1,1,fim]`; `[10000,fim]`; `[fim, 3]`.
  - `FormatOrder_RoundTrips`: `ParseOrder(FormatOrder([5,0])) == [5,0]`, e os quatro últimos bytes são `FF FF FF FF`.
  - `Entry_ThenProgramOf`: `ProgramOf(Entry("notepad.exe", [1,2,0,0])) == "notepad.exe"`, e a entrada termina em `[1,2,0,0]`.
  - `ProgramOf_NoNull_IsNull`.
- [ ] **Passo 2: Rodar e ver falhar.** `"C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe" test tests/Midview.Protocol.Tests --filter LastVisitedMruTests`. Esperado: erro de compilação, porque `LastVisitedMru` não existe.
- [ ] **Passo 3: Implementar `LastVisitedMru`** com as assinaturas acima, usando `BinaryPrimitives` e `Encoding.Unicode`.
- [ ] **Passo 4: Rodar de novo.** Esperado: PASSA.

---

### Tarefa 3: `ClassSaveTargets` e `ClassSaveLocationsFile`, a lista de permitidos e o arquivo de estado

**Arquivos:**
- Criar: `src/Midview.Protocol/ClassSaveTargets.cs`
- Criar: `src/Midview.Protocol/ClassSaveLocationsFile.cs`
- Teste: `tests/Midview.Protocol.Tests/ClassSaveTargetsTests.cs`
- Teste: `tests/Midview.Protocol.Tests/ClassSaveLocationsFileTests.cs`

**Interfaces:**
- Usa: `LastVisitedMru.ProgramOf` (Tarefa 2), `ClassFolderRules.IsLink(string)`.
- Produz:
  - `public sealed record SavedSetting(string Key, string Name, RegistryValueKind Kind, string? Before, string Written)`.
    - Os caminhos são relativos à chave `Software` do aluno.
    - `Kind` é o tipo com que o `Before` é devolvido; quando `Before` é null, é o tipo gravado.
    - Os valores ficam como texto: strings como são, DWORD em decimal, binário em base64.
  - `public static class ClassSaveTargets`, com estes membros (a Tarefa 1 pode corrigir nomes e tipos do Office):
    - `DownloadsKey = @"Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders"`
    - `DownloadsName = "{374DE290-123F-4565-9164-39C4925E467B}"`
    - `OfficeRoot = @"Microsoft\Office\16.0"`
    - `OfficeFolders`: `(OfficeRoot + @"\Word\Options", "DOC-PATH", ExpandString)`, `(… @"\Excel\Options", "DefaultPath", String)` e `(… @"\PowerPoint\RecentFolderList", "Default", String)`
    - `PreferCloudKey = OfficeRoot + @"\Common\General"` e `PreferCloudName = "PreferCloudSaveLocations"`, gravado como DWORD `0`
    - `MruKey = @"Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU"`
    - `MruPrograms = { "notepad.exe", "mspaint.exe" }`
  - `public static bool IsAllowed(SavedSetting s)`:
    - Downloads e as pastas do Office: `Kind` é `String` ou `ExpandString`.
    - PreferCloud: `DWord`.
    - MRU: nome de 1 a 4 dígitos decimais, `Kind` `Binary`, e `Written` decodifica para uma entrada cujo `ProgramOf` está em `MruPrograms` (sem diferenciar maiúsculas). Se `Before` não for null, tem de ser entrada desse mesmo programa.
    - Todo valor codificado tem de decodificar.
    - Chave e nome são comparados sem diferenciar maiúsculas.
  - `public static string? ReadEncoded(RegistryKey software, string key, string name, out RegistryValueKind kind)`: null quando a chave ou o valor não existem.
  - `public static void WriteEncoded(RegistryKey software, string key, string name, RegistryValueKind kind, string? encoded)`: null apaga o valor. A chave é criada quando precisa.
  - `public static bool SameValue(string? current, RegistryValueKind currentKind, SavedSetting s)`:
    - Alvos de texto aceitam qualquer um dos dois tipos de string, ignorando `\` no fim e maiúsculas.
    - DWORD e binário comparam exatamente.
    - Null nunca é igual.
  - `public sealed record ClassSaveLocationsState(Guid LessonId, string Folder, IReadOnlyList<SavedSetting> Settings)`, com `public static readonly ClassSaveLocationsState None` (`Guid.Empty`, `""`, vazio).
  - `public static class ClassSaveLocationsFile`:
    - `FileName = "class-save-locations.json"`
    - `MaxBytes = 65536`
    - `public static ClassSaveLocationsState Read(string path, out string? problem, string? linkFreeBelow = null)`:
      - Nunca lança exceção.
      - Arquivo ausente dá `None`, sem problema.
      - Arquivo ilegível, maior que `MaxBytes` ou com JSON malformado dá `None` com problema.
      - Com `linkFreeBelow` definido, um vínculo no arquivo ou em qualquer pasta entre `linkFreeBelow` (exclusive) e o arquivo dá `None` com problema.
      - Configurações que não passam em `IsAllowed` são descartadas.
    - `public static void Save(string path, ClassSaveLocationsState state)`: arquivo temporário movido por cima do antigo, como `ClassFolder.SaveState`, criando a pasta. Erros de IO chegam a quem chamou.

- [ ] **Passo 1: Escrever os testes, que devem falhar.**
  - `IsAllowed_TheTargets` (Theory com as linhas permitidas): verdadeiro.
  - `IsAllowed_RefusesOthers` (Theory): falso para:
    - `Policies\Microsoft\Windows\System`
    - Downloads com `Binary`
    - nome MRU `"x1"`
    - nome MRU `"12345"`
    - entrada MRU de `"cmd.exe"`
    - `Before` MRU de outro programa
    - base64 que não decodifica
  - `WriteThenRead_EachKind`: ida e volta na chave descartável para `String`, `ExpandString`, `DWord` e `Binary`.
  - `SameValue_StringsIgnoreCaseAndTrailingSlash`: `@"C:\X\Pasta da turma – 7A\"` como `String` é igual a `Written` `@"c:\x\pasta da turma – 7a"` com `ExpandString`.
  - `SameValue_BinaryExact`: um byte diferente dá falso.
  - `Read_Missing_IsNoneWithoutProblem`.
  - `Read_Malformed_IsNoneWithProblem`: `"{"`.
  - `Read_TooBig_IsNoneWithProblem`: 65537 bytes.
  - `Read_DropsDisallowed`: um arquivo com uma configuração de Downloads e uma de `Policies` volta só com a de Downloads.
  - `Save_ThenRead_RoundTrips`.
  - `Read_LinkBelowRoot_IsRefused`: uma junção de pasta `Midview` → outra pasta temporária com o arquivo (criada com `cmd /c mklink /J`). Com `linkFreeBelow` = raiz temporária do perfil, dá `None` com problema; com `linkFreeBelow` null, lê.
- [ ] **Passo 2: Rodar e ver falhar.** `… test tests/Midview.Protocol.Tests --filter "ClassSaveTargetsTests|ClassSaveLocationsFileTests"`. Esperado: erro de compilação.
- [ ] **Passo 3: Implementar os dois arquivos** com as assinaturas acima, usando `System.Text.Json` como `ClassFolder.Follow.cs`. `RegistryValueKind` é serializado como número.
- [ ] **Passo 4: Rodar de novo.** Esperado: PASSA.

---

### Tarefa 4: `ClassSaveLocationsUndo`, a regra única para desfazer, da Sessão e do Agent

**Arquivos:**
- Criar: `src/Midview.Protocol/ClassSaveLocationsUndo.cs`
- Teste: `tests/Midview.Protocol.Tests/ClassSaveLocationsUndoTests.cs`

**Interfaces:**
- Usa: `ClassSaveTargets.*` e `SavedSetting` (Tarefa 3), `LastVisitedMru` (Tarefa 2).
- Produz: `public static IReadOnlyList<string> Run(RegistryKey software, IEnumerable<SavedSetting> settings, Action<string>? setDownloads = null)`. Para cada configuração que passa em `IsAllowed` e cujo valor atual é `SameValue` com `Written`:
  - Com `Before` null, o valor é apagado. Se for uma entrada MRU, o número dela também sai de `MRUListEx`, mas só quando `MRUListEx` é lido sem erro; senão, `MRUListEx` fica como está.
  - Com `Before` definido, o valor volta com `Kind`. O valor de Downloads volta por `setDownloads(Before)` quando ele é passado, e pelo registro bruto quando não é.
  - Qualquer outra configuração fica como está.
  - Nunca lança exceção. Cada configuração que falha gera uma linha de problema em português e não impede as outras.

- [ ] **Passo 1: Escrever os testes, que devem falhar.** Usam a chave descartável e a Pasta da turma `C:\U\Desktop\Pasta da turma – 7A`.
  - `PutsBackWhatWasThere`: Downloads com `Before = "%USERPROFILE%\Downloads"` (`ExpandString`), e o valor atual é a Pasta da turma. Depois, o valor é `%USERPROFILE%\Downloads` com tipo `ExpandString`.
  - `DeletesWhatWasNotThere`: Excel com `Before = null` faz o valor sumir.
  - `LeavesWhatTheStudentChanged`: o Downloads atual é `D:\Meus downloads` e fica assim.
  - `SetDownloads_IsUsedWhenGiven`: o callback recebe `"%USERPROFILE%\Downloads"`, e o `Run` não grava o valor bruto.
  - `MruNewEntry_LeavesTheOrder`: o valor `"3"` foi gravado com `Before` null e `MRUListEx = [3,0,fim]`. Depois, `"3"` sumiu e `MRUListEx == [0,fim]`.
  - `MruExistingEntry_PutsBytesBack`: a ordem não muda.
  - `MruMalformedOrder_StillDeletesEntry_LeavesOrder`.
  - `DisallowedSetting_IsSkipped`: uma configuração `Policies\…` cujo valor atual é igual a `Written` fica como está.
  - `OneFailure_DoesNotStopTheOthers`: `setDownloads` lança exceção. Há um problema, e o Excel volta mesmo assim.
- [ ] **Passo 2: Rodar e ver falhar.** `… --filter ClassSaveLocationsUndoTests`. Esperado: erro de compilação.
- [ ] **Passo 3: Implementar `ClassSaveLocationsUndo.Run`.**
- [ ] **Passo 4: Rodar de novo.** Esperado: PASSA.

---

### Tarefa 5: `ClassSaveLocations` na Sessão, aplicando e desfazendo conforme a aula

**Arquivos:**
- Criar: `src/Midview.Session/IShellFolders.cs`
- Criar: `src/Midview.Session/ClassSaveLocations.cs`
- Teste: `tests/Midview.Session.Tests/ClassSaveLocationsTests.cs`

**Interfaces:**
- Usa: Tarefas 2 a 4; `ClassLesson` (`IsOn`, `FolderName`); `FileNames.Safe`; `ClassFolderRules.IsLink`.
- Produz:
  - `public interface IShellFolders`:
    - `void SetDownloads(string path)`: gravado exatamente como foi passado; lança exceção ao falhar.
    - `byte[]? IdListOf(string folder)`: a lista de IDs absoluta, com o fim de dois bytes zero; null quando o shell não consegue montá-la.
  - `public sealed class ClassSaveLocations(string desktopDirectory, Func<ClassLesson?> agentLesson, string statePath, RegistryKey software, IShellFolders shell)`, com `public void Follow()`, que nunca lança exceção. Cada problema vai para o log uma vez só (`Console.WriteLine`, com um `HashSet` como em `ClassFolder.Problem`).

`Follow`, nesta ordem:
1. Se `agentLesson()` é null, retorna.
2. Lê o estado uma vez (`ClassSaveLocationsFile.Read(statePath, out problem)`) e guarda em memória.
3. Se o `LessonId` do estado não é `Guid.Empty` e não é o id da aula ligada (ou não há aula ligada):
   - roda `ClassSaveLocationsUndo.Run(software, state.Settings, shell.SetDownloads)` e registra os problemas;
   - salva `None` e mantém `None` em memória, mesmo se salvar falhar.
4. Se há aula ligada, o `LessonId` do estado não é o id dela, e a pasta `Path.Combine(desktop, lesson.FolderName)` passa nestas condições:
   - a Área de Trabalho não é vazia;
   - `FileNames.Safe(nome) == nome`;
   - a pasta existe;
   - nem a pasta nem a Área de Trabalho são vínculo (`IsLink`); um vínculo vai para o log uma vez.

   Então, monta as configurações:
   - Downloads: `Written` = pasta.
   - Office, só quando `software.OpenSubKey(OfficeRoot)` existe: as três pastas e depois PreferCloud `"0"`.
   - MRU, só quando `MRUListEx` é lido sem erro e `IdListOf(pasta)` não é null: para cada programa, a entrada numerada que ele já tiver (por `ProgramOf`) ou o menor número livre ≥ 0.

   Um alvo cujo tipo atual não é permitido é pulado.

   Depois, salva o estado `(lesson.LessonId, pasta, configurações)`. Se salvar lançar exceção, registra no log e não grava nada.

   Por fim, grava cada configuração:
   - Downloads por `shell.SetDownloads`, as outras por `WriteEncoded`.
   - Depois de gravar uma entrada MRU, o número dela vai para a frente de `MRUListEx`.
   - Cada gravação fica no próprio try/catch.

- [ ] **Passo 1: Escrever os testes, que devem falhar.**
  - Preparação:
    - uma Área de Trabalho temporária com `Pasta da turma – 7A`;
    - a chave descartável `software`, com a subchave `Microsoft\Office\16.0` e Downloads = `%USERPROFILE%\Downloads` (`ExpandString`);
    - um `FakeShellFolders`: `SetDownloads` grava o valor de Downloads em `software` e anota a chamada; `IdListOf` devolve `Encoding.Unicode.GetBytes(pasta)` seguido de `0,0`;
    - aulas `new ClassLesson(1, id, "7A", 1, Ended: false)`; "terminada" é `ClassLesson.None(2)`.
  - `LessonOn_PointsEverythingAtTheClassFolder`:
    - Downloads, `DOC-PATH`, `DefaultPath` e `Default` são a pasta;
    - `PreferCloudSaveLocations == 0`;
    - os valores `0` e `1` são `Entry("notepad.exe", …)` e `Entry("mspaint.exe", …)`;
    - `MRUListEx` começa com `[1, 0]` (o último gravado primeiro);
    - o arquivo de estado tem 7 configurações.
  - `LessonEnded_PutsEverythingBack`:
    - Downloads volta a `%USERPROFILE%\Downloads`, por `SetDownloads`;
    - os valores do Office e as entradas MRU que não existiam somem;
    - o estado é `None`.
  - `FolderNotThereYet_DoesNothing_ThenAppliesWhenItIs`.
  - `FolderIsALink_DoesNothing`: junção criada com `mklink /J`.
  - `UnknownLesson_DoesNothing`: `agentLesson` devolve null no meio da aula, e nada é desfeito.
  - `AppliedOncePerLesson`: o aluno põe Downloads em `D:\Meus downloads` no meio da aula. Os `Follow` seguintes mantêm, e no fim continua `D:\Meus downloads`.
  - `NewInstance_SameLesson_DoesNotApplyAgain` e `NewInstance_LessonEnded_StillUndoes` (estado lido do disco).
  - `OtherTurma_RePointsWithTheOriginalBefore`: aula do 7A, depois aula do 8B com a pasta dela. Tudo aponta para o 8B, e terminar o 8B devolve `%USERPROFILE%\Downloads`.
  - `NoOffice_SkipsOffice`: sem a chave `16.0`, nenhum valor do Office é criado.
  - `MalformedMruOrder_SkipsNotepadAndPaint_AppliesTheRest`.
  - `ExistingNotepadEntry_KeepsItsNumber_AndPutsItBack`.
  - `StateCannotBeSaved_WritesNothing`: existe uma pasta no lugar de `statePath`.
- [ ] **Passo 2: Rodar e ver falhar.** `… test tests/Midview.Session.Tests --filter ClassSaveLocationsTests`. Esperado: erro de compilação.
- [ ] **Passo 3: Implementar `IShellFolders` e `ClassSaveLocations`.** Incluir um comentário de cabeçalho que aponta para o desenho, como `ClassFolder.cs` faz.
- [ ] **Passo 4: Rodar de novo.** Esperado: PASSA.

---

### Tarefa 6: As chamadas reais do shell, ligadas na Sessão

**Arquivos:**
- Criar: `src/Midview.Session/WindowsShellFolders.cs`
- Alterar: `src/Midview.Session/Program.cs:70-90` (ao lado de `classFolder` e do timer dele)
- Teste: `tests/Midview.Session.Tests/WindowsShellFoldersTests.cs`

**Interfaces:**
- Usa: `IShellFolders` e `ClassSaveLocations` (Tarefa 5).
- Produz: `public sealed class WindowsShellFolders : IShellFolders`.
  - `SetDownloads` chama `SHSetKnownFolderPath(FOLDERID_Downloads, 0x2000 /* KF_FLAG_DONT_UNEXPAND */, IntPtr.Zero, path)` e lança `COMException` quando o HRESULT indica falha.
  - `IdListOf` chama `ILCreateFromPathW`, copia `ILGetSize` bytes e chama `ILFree` num `finally`. Um ponteiro null devolve null.

- [ ] **Passo 1: Escrever o teste `IdListOf_ATempFolder_EndsInZeroAndDecodesBack`, que deve falhar.**
  - Os bytes terminam em `0,0`.
  - Decodificados com `SHGetPathFromIDListW` (bytes fixados na memória, com um P/Invoke no próprio teste), dão a mesma pasta.
  - Não testar `SetDownloads`: mudaria os Downloads reais, e a Tarefa 1 já conferiu isso à mão.
- [ ] **Passo 2: Rodar e ver falhar.** `… --filter WindowsShellFoldersTests`. Esperado: erro de compilação.
- [ ] **Passo 3: Implementar `WindowsShellFolders`.**
- [ ] **Passo 4: Ligar em `Program.cs`.**
  - Abrir `Registry.CurrentUser.OpenSubKey("Software", writable: true)` com `using`.
  - Montar `new ClassSaveLocations(<a mesma Área de Trabalho>, () => ClassLessonFile.Read(ClassLessonFile.DefaultPath), Path.Combine(localAppData, "Midview", ClassSaveLocationsFile.FileName), software, new WindowsShellFolders())`.
  - Trocar o callback do timer para `_ => { classFolder.Follow(lessonNotice); saveLocations.Follow(); }`.
  - Ampliar o comentário de cima no mesmo estilo.
- [ ] **Passo 5: Rodar o teste e a compilação.** Esperado: o teste passa, e `"C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe" build Midview.sln` não tem avisos novos.

---

### Tarefa 7: Desfazer na desinstalação, no Agent

**Arquivos:**
- Alterar: `src/Midview.Agent/UninstallCleanup.cs:45-55` (dentro de `if (openUserSoftware is not null)`) e o comentário das linhas 22-28
- Teste: `tests/Midview.Agent.Tests/UninstallCleanupTests.cs`

**Interfaces:**
- Usa: `ClassSaveLocationsFile.Read(path, out problem, linkFreeBelow)` (Tarefa 3) e `ClassSaveLocationsUndo.Run(software, settings)` (Tarefa 4, sem `setDownloads`).
- Produz: `internal static string SaveLocationsStatePath(string desktop)`, que devolve `<pasta acima da Área de Trabalho>\AppData\Local\Midview\class-save-locations.json` (`AllStudentDesktops` dá `<perfil>\Desktop`).
  - O passo novo é `Step(failures, $"devolver as pastas de salvar do aluno de {desktop}", …)`.
  - Ele lê com `linkFreeBelow` = a pasta do perfil. Um problema de leitura vira uma linha de falha.
  - Ele roda o desfazer, e cada problema devolvido vira uma linha de falha.
  - Ele nunca grava nem apaga o arquivo de estado.

- [ ] **Passo 1: Escrever os testes, que devem falhar.** Os dois usam um `<perfil>\Desktop` temporário, mais `<perfil>\AppData\Local\Midview\class-save-locations.json` gravado com `ClassSaveLocationsFile.Save`, e a chave descartável como `openUserSoftware`.
  - `TheSaveLocations_ArePutBack`: Downloads com `Before` `%USERPROFILE%\Downloads` e valor atual = Pasta da turma. Depois de `UninstallCleanup.Run`, Downloads voltou e não há falhas. O arquivo de estado continua lá, sem mudança.
  - `ACraftedStateFile_WritesNothingOutsideTheAllowlist`: o arquivo tem uma configuração `Policies\Microsoft\Windows\System` cujo `Written` é igual ao valor atual e cujo `Before` é diferente. Depois do `Run`, o valor não mudou.
- [ ] **Passo 2: Rodar e ver falhar.** `… test tests/Midview.Agent.Tests --filter UninstallCleanupTests`. Esperado: `TheSaveLocations_ArePutBack` falha.
- [ ] **Passo 3: Implementar o passo e `SaveLocationsStatePath`.**
- [ ] **Passo 4: Rodar de novo.** Esperado: PASSA.

---

### Tarefa 8: Solução inteira e ajuste no desenho

**Arquivos:**
- Alterar: `docs/superpowers/specs/2026-11-04-class-folder-save-locations-design.md` ("Conferência no laboratório": novos passos 5 e 6)

- [ ] **Passo 1: Rodar todos os testes.** `"C:/Users/Neto/AppData/Local/Microsoft/dotnet/dotnet.exe" test Midview.sln`. Esperado: tudo passa, e os únicos avisos são os três `xUnit1031` que já existiam.
- [ ] **Passo 2: Ampliar a conferência no laboratório.**
  - Passo 5: na Área de Trabalho, a Pasta da turma continua aparecendo como "Pasta da turma – 7A", não como "Downloads".
  - Passo 6: um aluno que escolher outra pasta de Downloads no meio da aula mantém essa pasta depois da aula.
- [ ] **Passo 3: Avisar o professor, em português.** Dizer o que mudou e o que só a conferência no ALUNO-25 confirma: Bloco de Notas e Paint do Windows 10, o Office 2016 do laboratório e o nome na Área de Trabalho. Não publicar até o professor dizer "Publicar".
