# Pasta da turma: downloads e arquivos salvos caem nela (desenho)

Data: 2026-11-04. Amplia a Pasta da turma
(`docs/superpowers/specs/2026-10-20-class-folder-design.md`).

## Intenção

Com a Pasta da turma de uma aula ligada, o Chrome e o Edge já baixam para ela
(`DownloadFolderPolicy`, no Agent, como SYSTEM, no registro do aluno). Todo o
resto vai para onde o Windows e cada programa mandam, quase sempre
**Documentos**. O problema do professor, nas palavras dele: os alunos salvam
os trabalhos do Word e do Excel em Documentos, e a Pasta da turma nunca os
recolhe. Ele quer que, com a Pasta da turma ligada, "todo tipo de download ou
criação de arquivo novo" caia na Pasta da turma.

## Objetivos

Com uma aula ligada, no computador do aluno que estiver logado:

1. **Os downloads de qualquer programa** caem na Pasta da turma: a pasta
   conhecida Downloads do aluno (`FOLDERID_Downloads`) aponta para ela.
2. **Word, Excel e PowerPoint** (Office 2016 ou mais novo, `16.0`) abrem o
   Salvar / Salvar como na Pasta da turma. Essa é a versão do laboratório,
   confirmada no ALUNO-25 pelos atalhos `Word.lnk`/`Excel.lnk`/`PowerPoint.lnk`
   de uma instalação Clique para Executar ou MSI 2016+. O local padrão dos
   arquivos aponta para a Pasta da turma, e salvar no computador tem
   preferência sobre salvar na nuvem.
3. **Bloco de Notas e Paint** (o `notepad.exe` e o `mspaint.exe` clássicos do
   Windows 10) abrem o Salvar na Pasta da turma. A "última pasta usada por este
   programa" da caixa de diálogo comum (`ComDlg32\LastVisitedPidlMRU`) passa a
   ser a Pasta da turma para os dois.

Quando a aula termina, ou o aluno entra depois de ela ter terminado, cada um
desses volta ao que era antes, e só onde ainda está como o Midview deixou.

## Decidido pelo professor

- Opção A, não B: **Documentos e Imagens não são redirecionados.**
  Redirecioná-los poria pastas dos próprios programas, como os modelos do
  Office, dentro da Pasta da turma, e esconderia os documentos do aluno durante
  a aula.
- Bloco de Notas e Paint pela "última pasta usada", sabendo de duas coisas. Um
  aluno que salvar uma vez em outro lugar faz o programa lembrar desse outro
  lugar. E isso depende de um detalhe interno do Windows que não dá para
  testar no PC do desenvolvedor (Windows 11, com outro Bloco de Notas e outro
  Paint).
- O aluno ainda pode escolher outro lugar no Salvar como (um pen drive), e
  tudo bem.
- Programas que já estiverem abertos quando a aula começar podem continuar com
  a pasta antiga até serem reabertos. **O Midview nunca fecha o Word, o Excel,
  o PowerPoint, o Bloco de Notas ou o Paint** (trabalho não salvo).
- Os computadores dos alunos têm Windows 10 Pro Education 22H2 (build 19045).
- Tudo aponta **direto para a Pasta da turma**, não para uma subpasta por
  computador dentro dela: o aluno dá o nome ao arquivo. A Pasta da turma é
  compartilhada e sincronizada em todos os computadores (`ClassSyncPlanner`).
  O professor aceita o que vem disso:
  - Um aluno que mantiver um nome padrão ("Pasta1.xlsx",
    "Apresentação1.pptx", "Sem título.png") pode ver o arquivo renomeado para
    "X (2)" pela sincronização, quando um colega salvou um com o mesmo nome.
  - No Bloco de Notas e no Paint, salvar de novo com esse nome substitui a
    cópia do colega em todos os computadores. A cópia substituída vai para a
    Lixeira do professor, como a sincronização já faz.

  Nada neste desenho muda a sincronização.

## O que não é objetivo, e o que seria um fracasso

- Não é obrigar a salvar ali: nada impede salvar em outro lugar.
- Nenhuma mudança em Documentos, Imagens, Área de Trabalho ou em qualquer
  outra pasta conhecida.
- Nunca deixar os Downloads, o Office ou as caixas de diálogo de um aluno
  apontando para uma Pasta da turma depois de a aula acabar. Essa pasta vai
  para a Lixeira quando a aula termina, e Downloads apontando para ela
  quebraria os downloads.
- Nunca passar por cima de uma configuração que outra pessoa (o aluno, uma
  política da escola) mudou durante a aula: desfazer só o que ainda é como o
  Midview deixou.
- Nunca mexer em nada quando a Pasta da turma ainda não existe, não é uma
  pasta de verdade (é um vínculo) ou não está na Área de Trabalho deste aluno.

## Como (decidido aqui, a confirmar por quem programar)

### Quem faz: a Sessão, como o aluno

As três são configurações do próprio aluno (o `HKCU` dele, a pasta conhecida
dele), não políticas. O aluno poderia mudá-las de qualquer jeito, então não
há nada a proteger usando o SYSTEM. E agir como o aluno dá a identidade certa
para as funções do shell.

Por isso, isso fica no **Midview.Session**, ao lado de `ClassFolder.Follow`,
que já segue a aula a cada 5 s a partir de `ClassLessonFile`. Será uma classe
nova, por exemplo `ClassSaveLocations`, chamada no mesmo timer, com o próprio
arquivo de estado em `%LOCALAPPDATA%\Midview`, ao lado de
`class-folder-session.json`. O Agent não muda nada durante as aulas (veja
"Desinstalação durante uma aula" para a única exceção).

A pasta para onde apontar é `Path.Combine(<Área de Trabalho>,
lesson.FolderName)` da aula ligada agora. Ela só é usada depois que existe como
pasta de verdade, criada pelo Agent, e não como ponto de nova análise
(`ClassFolderRules.IsLink`).

### O que é o "antes", e a regra para desfazer

No primeiro momento de uma aula, o valor anterior de cada configuração (ou
"não estava definido") é anotado no arquivo de estado, e só depois a Pasta da
turma é gravada.

No fim, para cada configuração:
- Se o valor atual ainda é a Pasta da turma que o Midview gravou (comparando
  caminhos completos normalizados, sem diferenciar maiúsculas), o valor
  anotado volta, ou o valor é apagado se não estava definido.
- Senão, o valor fica como está e é só esquecido.

Uma aula nova enquanto uma está apontada (outra turma) aponta de novo sem
sobrescrever o "antes" anotado. O arquivo de estado sobrevive a reinícios da
Sessão e a saídas do usuário, então um fim de aula visto só no próximo login
ainda desfaz.

### 1. Downloads

A Sessão, como o aluno, usa `SHGetKnownFolderPath`/`SHSetKnownFolderPath(FOLDERID_Downloads,
0, NULL, path)`, que grava `User Shell Folders` e avisa o shell.

O "antes" é o valor não expandido de `User Shell Folders`
(`{374DE290-123F-4565-9164-39C4925E467B}`, normalmente
`%USERPROFILE%\Downloads`), anotado como texto bruto com o tipo dele.
Aplicar usa `SHSetKnownFolderPath` com `KF_FLAG_DONT_UNEXPAND` (`0x2000`), que
gravou um caminho normal exatamente como passado. Desfazer não pode depender
só da função, que expande `%USERPROFILE%`: a Sessão a chama com o "antes"
expandido (só para avisar o shell) e depois grava o texto bruto do "antes"
direto no registro; o Agent só grava o valor bruto (ver "Limites conhecidos").
Nenhum arquivo é movido: `SHSetKnownFolderPath` não move nada, e nada do tipo
`KF_REDIRECT_*` é usado.

### 2. Office 2016+ (`Software\Microsoft\Office\16.0`, HKCU)

| Programa | Chave | Valor |
| --- | --- | --- |
| Word | `Word\Options` | `DOC-PATH` (REG_EXPAND_SZ), instável: o Word o gravou em só uma de várias execuções |
| Excel | `Excel\Options` | `DefaultPath` (REG_EXPAND_SZ, confirmado; não é REG_SZ) |
| PowerPoint | `PowerPoint\RecentFolderList` | `Default` (REG_SZ), só na conferência do laboratório |
| todos | `Common\General` | `PreferCloudSaveLocations` = 0 (DWORD) |

Esses valores vêm de fontes da comunidade, não da documentação da Microsoft.
Conferidos num Office Home & Student 2019 Clique para Executar (`16.0`), pelo
modelo de objetos de cada programa:
- Word: `Options.DefaultFilePath(wdDocumentsPath)` gravou `DOC-PATH` como
  REG_EXPAND_SZ, com uma `\` no fim (`C:\...\pasta\`). Isso aconteceu numa
  execução; em várias execuções seguintes o Word aceitou o valor mas não
  gravou `DOC-PATH` no registro (nem 1 s nem 3 s depois, nem ao sair). Por isso
  o Agent e a Sessão devem tratar `DOC-PATH` ausente como normal e não podem
  contar com o Word gravando por conta própria. Reconferir no laboratório.
- Excel: `Application.DefaultFilePath` gravou `DefaultPath` na hora, como
  REG_EXPAND_SZ, sem `\` no fim. A tabela dizia REG_SZ: o Midview grava
  REG_EXPAND_SZ quando o valor ainda não existe, e mantém o tipo que já existe
  (REG_SZ ou REG_EXPAND_SZ) quando existe.
- PowerPoint: a chave `PowerPoint\RecentFolderList` não existe neste PC (o
  PowerPoint nunca teve o "Local padrão dos arquivos" definido). A linha fica
  como está, só na conferência do laboratório.
- Neste PC `DOC-PATH` e `DefaultPath` não existiam antes e foram apagados de
  novo no fim: "sem valor" é o estado comum e o "antes" tem de aceitar isso.

Só vale quando `Software\Microsoft\Office\16.0` existe, ou seja, o Office está
instalado e já foi aberto uma vez. Senão, a parte do Office é pulada em
silêncio (veja "Limites conhecidos"). A regra para desfazer é a mesma.

### 3. Bloco de Notas e Paint (`ComDlg32\LastVisitedPidlMRU`, HKCU)

O formato da chave
`Software\Microsoft\Windows\CurrentVersion\Explorer\ComDlg32\LastVisitedPidlMRU`:
- Valores chamados `0`, `1`, ..., cada um REG_BINARY: o nome do arquivo do
  programa em UTF-16LE terminado em nulo, seguido da lista de IDs absoluta da
  pasta, que termina num `USHORT` zero.
- `MRUListEx` (REG_BINARY) lista os números dos valores como `DWORD`s, o mais
  recente primeiro, terminando em `0xFFFFFFFF`.

Para `notepad.exe` e `mspaint.exe`:
- A lista de IDs é montada com `ILCreateFromPathW(classFolder)` (+ `ILGetSize`,
  `ILFree`).
- Ela é gravada na entrada que esse programa já tiver (mantendo o número) ou
  num número livre novo, e esse número vai para a frente de `MRUListEx`.
- O "antes" são os bytes anteriores da entrada, ou "sem entrada".

No fim, a Sessão decodifica a entrada atual (`SHGetPathFromIDListW`). Se ela é
do mesmo programa e ainda leva à Pasta da turma (caminhos completos
normalizados, sem diferenciar maiúsculas nem a `\` do fim), conta como a que o
Midview gravou, mesmo com outros bytes: salvar uma vez no Bloco de Notas ou no
Paint dentro da Pasta da turma faz o Windows regravar a entrada, porque a lista
de IDs guarda datas. Então os bytes anteriores voltam. Se não havia entrada
antes, a entrada e o número dela em `MRUListEx` são removidos. Uma entrada que
leva a outra pasta é do aluno e fica como está. O Agent, na desinstalação, não
decodifica (veja "Limites conhecidos").

A leitura é defensiva: dados malformados fazem a chave ficar como está, com
um registro no log uma vez só.

O apontamento acontece no máximo uma vez por aula e Sessão, não a cada 5 s.
Assim, o aluno que depois salvar em outro lugar mantém essa escolha, como foi
decidido.

### Desinstalação durante uma aula (decisão minha, não do professor)

Desinstalar o Midview para a Sessão, que então nunca vê a aula terminar.
`UninstallCleanup` (o Agent, como SYSTEM) já remove a política de download dos
navegadores onde o registro do aluno estiver carregado (`openUserSoftware`), e
faz o mesmo aqui. Para cada Área de Trabalho de aluno, ele lê o arquivo de
estado desse aluno e, no registro carregado dele, aplica a mesma regra para
desfazer aos valores brutos do registro que o arquivo anota:
- o texto de `User Shell Folders`
- os valores do Office
- a entrada de `LastVisitedPidlMRU` e o `MRUListEx`

Por isso, o arquivo de estado guarda valores brutos do registro, o suficiente
para devolver tudo sem nenhuma função do shell.

O arquivo é do próprio aluno, que pode escrever nele. Então o Agent:
- só grava aqueles valores com nome;
- só grava no registro desse mesmo aluno;
- ignora um arquivo que não consiga ler.

Um aluno que não estiver logado fica com os valores no registro dele, como
hoje com a política dos navegadores.

### Limites conhecidos (decisões minhas)

- Computadores com Office 2007 ou 2013: só o `16.0` é tratado. O laboratório
  tem 2016+ e o professor não quer o Office 2007.
- Um programa do Office aberto não sobrescreve o valor ao fechar (conferido
  com Word e Excel: gravei `DOC-PATH` e `DefaultPath` de fora com o programa
  aberto, fechei, e o valor gravado de fora continuou). Mas o programa aberto
  continuou lendo, pelo modelo de objetos, o valor que ele mesmo tinha
  definido antes (não o gravado de fora). Então um Word ou Excel já aberto no
  começo da aula pode não ver a pasta nova até ser aberto de novo, a conferir
  no laboratório. Para o Word, "não sobrescreve ao fechar" tem pouca base: ele
  gravou `DOC-PATH` em só uma de várias execuções (veja "2. Office 2016+"),
  então não ter gravado ao fechar pode ser só esse mesmo comportamento
  instável.
- O Office só é apontado se `Software\Microsoft\Office\16.0` existe no momento
  em que a aula é aplicada, e isso acontece uma vez só por aula. Num usuário em
  que o Word ou o Excel é aberto pela primeira vez durante a aula, a chave
  ainda não existia: o Office fica sem a Pasta da turma a aula inteira, e só
  passa a ser apontado na aula seguinte.
- `SHSetKnownFolderPath(FOLDERID_Downloads, KF_FLAG_DONT_UNEXPAND = 0x2000, ...)`
  grava o texto passado como REG_EXPAND_SZ, e para um caminho normal
  (`C:\...\pasta`) o texto bruto é exatamente o passado. Mas ao devolver
  `%USERPROFILE%\Downloads` a função gravou `C:\Users\Neto\Downloads`, já
  expandido. Por isso a função sozinha não devolve o texto bruto do "antes".
  Para desfazer, a Sessão faz nesta ordem:
  1. chama `SHSetKnownFolderPath` com o "antes" expandido
     (`Environment.ExpandEnvironmentVariables`, como o aluno), só para o shell
     ser avisado;
  2. DEPOIS dessa chamada, SEMPRE grava o texto bruto do "antes" direto em
     `User Shell Folders`, com o tipo dele, para o texto guardado ficar
     exatamente o original.

  A ordem importa: chamar a função depois da gravação direta sobrescreveria o
  texto bruto com o expandido de novo. O Agent (desinstalação) só grava o valor
  bruto direto no registro, sem chamar a função. Nenhum `desktop.ini` apareceu
  na pasta nova.
- O Agent (desinstalação durante uma aula) compara a entrada de
  `LastVisitedPidlMRU` byte a byte com a que o Midview gravou, sem decodificar:
  como SYSTEM, ele não usa o shell do aluno. Consequência: se o aluno salvou no
  Bloco de Notas ou no Paint dentro da Pasta da turma e o Midview for
  desinstalado antes de a aula terminar, a entrada desse programa fica
  apontando para a Pasta da turma (que vai para a Lixeira), e a pasta de antes
  da aula não volta para ele. Só a Sessão, no fim de uma aula, decodifica.
- Voltar para um Midview mais antigo durante uma aula: a Sessão antiga não
  conhece o arquivo de estado, então as configurações ficam apontadas até um
  Midview com esta função rodar de novo.

### Log e falhas

Nada sai do timer como exceção. Cada problema vai para o log uma vez só (o
`Console.WriteLine` da Sessão, como `ClassFolder` faz), e uma parte que falha
não impede as outras.

## Restrições

- .NET 8, C#, o estilo do repositório: comentários explicam o porquê, em
  inglês; o texto que o professor ou o aluno lê fica em português. Arquivos
  com menos de 500 linhas.
- TDD: o trabalho no registro fica atrás de pequenas costuras, para os testes
  rodarem numa subárvore descartável `HKCU\Software\MidviewTest\<guid>` (como
  fazem os `LabConfigCommandHandlerTests`). A chamada da pasta conhecida fica
  atrás de uma interface, falsificada nos testes. Um teste de verdade de
  `SHSetKnownFolderPath`, se houver, precisa devolver os Downloads reais do
  desenvolvedor num `finally`.
- Nada é testado nos computadores reais do laboratório por quem programa; o
  professor testa no ALUNO-25.
- Nunca citar o programa proibido no código, na documentação ou nos commits.

## Deixado para quem programar

- Nomes de classes e arquivos, o formato do arquivo de estado, e como o timer
  da Sessão o chama (dentro de `ClassFolder.Follow` ou ao lado).
- Se a parte do Office também deve cobrir o `15.0` (Office 2013): não é
  preciso para este laboratório.
- As assinaturas exatas de P/Invoke.

## Conferência no laboratório (pelo professor, antes de uma versão chegar a todos os computadores)

No ALUNO-25, com uma aula começada pelo Master do professor:
1. Baixar um arquivo no Firefox ou em qualquer programa que não seja o Chrome
   (ou usar "Salvar link como" no Chrome): ele cai na Pasta da turma.
2. Abrir o Word, o Excel e o PowerPoint depois de a aula começar: o Salvar
   como abre na Pasta da turma.
3. Bloco de Notas e Paint: o Salvar abre na Pasta da turma.
4. Terminar a aula, sair e entrar de novo no usuário: Downloads, Office e as
   caixas de diálogo voltam ao que eram, e a Pasta da turma antiga some, como
   antes. Durante a aula e depois dela, ler os valores brutos de
   `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\User Shell Folders`
   (por exemplo com `reg query`): `{374DE290-123F-4565-9164-39C4925E467B}` e
   `{7D83EE9B-2244-4E70-B1F5-5393042AF1E4}` (este é comum no Windows 10).
   Anotar os dois; depois da aula, os dois têm de estar como antes dela.
5. Na Área de Trabalho, durante a aula, a Pasta da turma continua aparecendo
   como "Pasta da turma – 7A" (o nome da aula), não como "Downloads", mesmo com
   a pasta conhecida Downloads apontando para ela.
6. Durante a aula, escolher outra pasta de Downloads (por exemplo
   `D:\Meus downloads`, nas propriedades da pasta Downloads). Terminar a aula,
   sair e entrar de novo: essa pasta continua como Downloads, e o Midview não a
   troca de volta ("desfazer só o que ainda é como o Midview deixou").
7. PowerPoint, no passo 2: o valor `Default` de `PowerPoint\RecentFolderList`
   só pode ser confirmado aqui, porque a chave não existe no computador do
   desenvolvedor (veja "2. Office 2016+", em "Como").
8. Bloco de Notas: durante a aula, salvar um arquivo uma vez na Pasta da
   turma. Terminar a aula e abrir o Salvar do Bloco de Notas: ele abre na pasta
   de antes da aula, não na Pasta da turma.
9. Num usuário em que o Word nunca foi aberto, começar a aula e só então abrir
   o Word pela primeira vez. Anotar se o Salvar como abre na Pasta da turma (o
   esperado é que não, veja "Limites conhecidos"), e que nada quebra. Na aula
   seguinte, o Salvar como abre na Pasta da turma.
