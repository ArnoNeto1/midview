# Assistente "Preparar celulares" (desenho, revisão 2: pelo cabo USB)

Data: 2026-11-11. Mudanças no `src/Midview.Master` (programa do professor) e
no agente Android (`src/Midview.Android`). Continua o desenho
`2026-11-11-agente-android-design.md` e depende do plano 1 dele
(`docs/superpowers/plans/2026-11-11-agente-android-1-base.md`).

**Por que a revisão.** A primeira versão (aprovada) preparava o celular pelo
QR dos 6 toques, sem cabo. Em 2025 o Google passou a aceitar no QR só apps de
gerenciamento de uma lista aprovada por ele; o agente do Midview não está
nela, e nos celulares com Play Store o QR recusa instalar. O professor
escolheu o caminho pelo cabo USB ("Sim, pelo cabo"). O que não depende do QR
continua como estava: um passo por vez, prints reais, "Deu errado?", o
número de trás vira o nome, entrar na turma sem código, "Apagar e preparar de
novo".

## Intenção

Hoje, para pôr um celular da escola no Midview, alguém precisa saber de cor
uma sequência que nenhum programa faz por ele. Quem não conhece Android trava
em qualquer ponto.

O professor quer que **qualquer outro professor, mesmo quem não sabe nada de
celular, prepare os celulares da escola sozinho**, só com o Midview aberto no
PC do professor e um cabo USB, sem ajuda.

## Decidido pelo professor

- **Quem usa:** professores que podem não saber nada de celular nem de
  computador. Estão sozinhos na frente do Midview do PC do professor, sem
  vídeo, folha impressa ou alguém para perguntar.
- **Os celulares:** da escola, todos Xiaomi, modelos diferentes: Redmi 10C,
  Redmi Note 10S, Redmi Note 9 Pro ("até onde eu sei"). **Todos podem ser
  apagados.** Cada um tem **um número escrito atrás**. **Não têm chip.**
- **Um PC só:** os celulares ficam sempre com a mesma turma e o mesmo PC de
  professor.
- **Pelo cabo USB:** o professor faz à mão só o que a Xiaomi exige que seja
  feito no celular; o resto o Midview faz sozinho pelo cabo.
- **Conta Mi e chip só por alguns minutos:** a escola não tem conta Mi (só
  Google) e os celulares não têm chip. Testado pelo professor no Redmi 10C em
  2026-11-11: a chave "Depuração USB (configurações de segurança)" **só liga
  com chip no celular** e conta Mi entrada; depois de ligada, ela **continua
  ligada sem a conta Mi e sem o chip** (conferido pelo cabo:
  `persist.security.adbinput=1`, `gsm.sim.state=ABSENT`, nenhuma conta).
  Então: a conta Mi entrada pelo Google da escola e um chip emprestado (pode ser o do celular do próprio professor), usados em
  cada celular e tirados em seguida.
- **Formato:** um **assistente de um passo por vez** (mockup "A · Um passo por
  vez", em `.superpowers/brainstorm/2043-1794420649/content/passo-a-passo.html`),
  **bem detalhado**, "pois tem professor que não vai achar direito".
- **Cada passo** tem: título curto, os toques um por linha com os nomes exatos
  dos menus, a imagem da tela do celular com o lugar do toque contornado em
  vermelho, quanto tempo leva, o que aparece quando dá certo, e um **"Deu
  errado?"** com os problemas comuns daquele passo e a saída de cada um
  (mockup aprovado:
  `.superpowers/brainstorm/2043-1794420649/content/passos-com-prints.html`).
- **Imagens:** prints reais do Redmi 10C, tirados pelo `adb` sem tocar em
  nada, com nomes de rede e contas borrados. A tela de login da conta Mi
  bloqueia print e começa como **desenho**.
- **O número de trás vira o nome:** o assistente pergunta o número no início
  e o celular aparece no Midview como **"Celular 07"** sem precisar renomear.
- **Sem código de 6 dígitos:** o celular preparado pelo assistente **entra na
  turma sozinho**, já pareado com este PC.
- **Onde fica:** quando o filtro **Celulares** está vazio, o espaço vazio
  mostra um **botão grande "Preparar celulares"**. O link "Preparar
  celular..." em Máquinas da sala → Procurar e parear continua, para os
  celulares seguintes.
- **Anti-objetivo principal:** o professor **travar no meio e não saber o que
  fazer**, mesmo que no fim o celular fique pronto.
- **Preparar de novo um celular que já é da escola:** um **botão no Midview,
  "Apagar e preparar de novo"**, que só o PC em que o celular confia pode
  usar.
- **O Redmi 10C pode ser usado e apagado durante a construção** para uma
  preparação de verdade e para os prints que faltam.
- **Só o Redmi 10C é testado por ora.** O assistente é feito a partir dele;
  o Note 10S e o Note 9 Pro são conferidos na primeira preparação de verdade
  de cada um, e o que for diferente neles entra depois no "Deu errado?".

## Objetivos

### 1. Abrir o assistente

- Filtro **Celulares** vazio: em vez do texto de hoje, um botão grande
  **"Preparar celulares"** e uma frase curta ("Nenhum celular nesta sala
  ainda.").
- O link de Máquinas da sala → Procurar e parear abre o mesmo assistente.
- Enquanto o agente Android não estiver publicado, o botão e o link não
  aparecem.

### 2. Antes do primeiro celular (só uma vez)

- **Wi-Fi do laboratório:** se não há Wi-Fi guardado (`PhoneWifiFile`), pede
  o nome e a senha, com as mesmas regras de hoje e o aviso de que fica guardado
  só neste PC. Com Wi-Fi guardado, mostra discreto "Wi-Fi: <nome> · trocar".
  "Deu errado?": rede com usuário e senha (empresarial) ou com página de login
  não funciona.
- **O que separar:** uma tela "Antes de começar, separe:" com o cabo USB do
  celular, um chip qualquer (pode ser o do seu celular) e a agulha da gaveta
  do chip (ou um clipe), e o e-mail e a senha da conta Google da escola.
- **Conta Mi da escola:** a conta Mi entra **pelo Google** (botão do Google
  na tela de login da conta Mi), com a conta Google da escola; não é preciso
  criar outra conta (conferido no Redmi 10C pela sessão do agente Android). O
  Midview **não guarda** e-mail nem senha.

### 3. Os passos à mão, para cada celular

Um passo por tela, com **Voltar** e um botão de avançar escrito como o
professor confirma ("Já tirei, próximo passo"). Na ordem:

1. **Número do celular.** "Qual número está escrito atrás deste celular?" Se o
   número já existe na turma, avisa ("Já existe um Celular 07. Vai preparar
   de novo o 07?"); o antigo é trocado pelo novo quando este entrar.
2. **Ligar o celular.** Ligado, desbloqueado, com bateria, conectado ao Wi-Fi
   (para entrar na conta Mi). "Deu errado?": "Apareceu Bem-vindo" → o celular
   está apagado: escolha o idioma e o Wi-Fi e **pule** a conta Google e a
   conta Mi até chegar à tela inicial; "Pede um PIN que ninguém sabe" → ajuda
   "Celular travado com PIN".
3. **Tirar a conta Google.** Configurações → Contas e sincronização → Google →
   a conta → Mais → Remover conta. "Deu errado?": "Não aparece Google" → não
   há conta, siga; "Pede a senha e ninguém sabe" → ajuda "Celular com conta
   desconhecida".
4. **Ligar o modo de desenvolvedor.** Configurações → Sobre o telefone →
   tocar **7 vezes** em "Versão da MIUI" até aparecer "Você agora é um
   desenvolvedor". "Deu errado?": "Diz que já é desenvolvedor" → siga.
5. **Colocar o chip e entrar na conta Mi.** Abrir a gaveta com a agulha,
   colocar o chip, fechar. Configurações → no topo, "Conta Mi" → tocar no
   botão do Google e entrar com a conta Google da escola. "Deu errado?": "Já tem outra conta Mi" → sair dela (pede a
   senha dela; se ninguém sabe, ajuda "Celular com conta desconhecida");
   "Fica carregando no 'não sou robô'" → conferir data e hora automáticas e o
   Wi-Fi.
6. **Ligar as três chaves.** Configurações → Configurações adicionais →
   Opções do desenvolvedor → ligar **"Depuração USB"**, **"Instalar via
   USB"** e **"Depuração USB (configurações de segurança)"**. Cada uma mostra
   um aviso; esperar o contador e tocar em "Aceitar"/"Próximo". "Deu errado?":
   "A chave volta a desligar" → o chip não está bem colocado ou a conta Mi
   não entrou; "Pede para inserir um cartão SIM" → passo 5.
7. **Sair das contas e tirar o chip.** Configurações → Conta Mi → desligar
   "Encontrar dispositivo" se estiver ligado → Sair da conta. Depois, se o
   login pelo Google deixou uma conta Google no celular, tirá-la como no
   passo 3. Por fim abrir a gaveta e tirar o chip, sem desligar o celular. As
   chaves do passo 6 continuam ligadas.
8. **Ligar o cabo.** Ligar o celular no PC do professor pelo cabo USB. No
   celular aparece "Permitir depuração USB?": marcar "Sempre permitir deste
   computador" e tocar em **Permitir**. O assistente **avança sozinho** quando
   vê o celular no cabo. "Deu errado?": "Não aparece a pergunta" → trocar de
   porta USB ou de cabo (cabo só de carregar não serve), desbloquear a tela;
   "Toquei em Cancelar" → desligar e ligar o cabo de novo.

O assistente insiste nos passos 3 e 7: com qualquer conta no celular, o
Android não deixa o Midview terminar.

**Outras versões do MIUI.** *(Escolha minha.)* Os textos e prints são do MIUI
14. Em vez de perguntar o modelo, cada passo de menu traz no "Deu errado?" o
caminho do MIUI 12/13 e a dica de usar a busca das Configurações com a palavra
do item ("desenvolvedor", "depuração").

### 4. O Midview termina sozinho pelo cabo

Com o celular no cabo e autorizado, o assistente mostra uma lista que vai
sendo marcada, em linguagem simples, e leva cerca de 1 minuto:

1. **Conferindo o celular**: Android 10 ou mais novo, nenhuma conta, nenhum
   segundo usuário ("Segundo espaço" ou "Apps duplos" da MIUI), as chaves do
   passo 6 ligadas, ainda não é celular da escola. Cada falha vira uma frase
   que manda de volta ao passo certo ("Ainda há uma conta neste celular. Volte
   ao passo 3.").
2. **Instalando o Midview no celular.** A MIUI pergunta no celular se pode
   instalar: o assistente avisa para tocar em **Instalar**. Em seguida, ainda
   pelo cabo, libera o agente da limpeza da MIUI (`cmd appops set <pacote>
   10008 allow` e `cmd deviceidle whitelist +<pacote>`): o "Limpar tudo" dos
   recentes mata o agente, e isso o ajuda a voltar sozinho.
3. **Entregando o Wi-Fi do laboratório** (o agente o põe ao virar celular
   da escola).
4. **Dando o número e pareando com este PC** (sem código de 6 dígitos).
5. **Transformando em celular da escola.** É o último, porque a partir daqui o
   agente desliga a depuração pelo cabo (os alunos não conseguem religar).

Depois: **"Pronto. Pode tirar o cabo."** O celular mostra **"Pronto. Este é o
Celular 07 da escola."** e aparece no filtro Celulares quando entrar no Wi-Fi.
O assistente mostra **"Celular 07 pronto ✓ (3 de 11)"** com **"Preparar o
próximo"** (volta ao passo 1, pulando as telas do objetivo 2) e
**"Terminei"**. *(Escolha minha: o "de 11" é perguntado uma vez e é
opcional; sem ele, mostra "3 celulares na turma".)*

Se o celular não aparece no Midview em **5 minutos** *(escolha minha)* depois
de pronto, o assistente diz que é problema de rede (mesmo Wi-Fi do PC,
celular perto do roteador), não de preparação: o celular já confia neste PC.

### 5. Nunca deixar o professor travado

- Todo passo tem o "Deu errado?" com as saídas daquele passo.
- Se um item do objetivo 4 falha, o assistente diz o que aconteceu em
  português simples, para qual passo voltar, e oferece **"Tentar de novo"**.
  Nada fica pela metade de um jeito que impeça recomeçar: até o item 5 o
  celular ainda não é da escola.
- Ajudas fixas, abertas a partir dos "Deu errado?":
  - **Celular com conta desconhecida:** sem a senha da conta Mi ou Google,
    separe o celular e procure quem tem a senha (ou a assistência Xiaomi com
    a nota fiscal).
  - **Celular travado com PIN:** desligar; segurar Ligar + Volume para cima
    até aparecer o menu; Wipe Data → Wipe All Data → Confirm → Reboot to
    System. Só funciona se as contas já foram tiradas. Depois, passo 2
    ("Apareceu Bem-vindo").
- O professor sempre pode **fechar** o assistente; os celulares que já
  entraram continuam na turma, e reabrir começa pelo passo 1.

### 6. "Apagar e preparar de novo"

- No menu do quadradinho de um celular (e no passo 1, quando o número já
  existe e o celular está online), **"Apagar e preparar de novo"**.
- Pede confirmação clara ("O Celular 07 vai ser apagado e voltar de fábrica.
  Os alunos perdem o que estiver nele.") e só funciona com o celular online.
- O comando só é aceito pelo celular vindo de um PC em que ele confia (o mesmo
  canal TLS dos outros comandos). O agente apaga o aparelho pela política do
  Android de dono do aparelho, limpando também a proteção de fábrica, para que
  o celular volte à tela "Bem-vindo" sem pedir conta.
- Em seguida o assistente abre no passo 2 ("Apareceu Bem-vindo") com o mesmo
  número.
- Celular da escola que **não aparece** no Midview: ajuda "Celular travado
  com PIN" (modo de recuperação).

## Não-objetivos

- O QR dos 6 toques (bloqueado pelo Google para o agente).
- Ligar as chaves da Xiaomi pelo Midview (a MIUI exige toques no celular, com
  chip e conta Mi).
- Celulares de outras marcas: texto e imagens são da Xiaomi/MIUI.
- Vários PCs de professor para os mesmos celulares ("Permitir novo professor"
  do agente continua sendo o caminho para trocar de PC).
- Preparar vários celulares ao mesmo tempo.
- Restaurar de fábrica antes de preparar: não é preciso; basta não ter contas.

## Anti-objetivos (seria fracasso mesmo funcionando)

- O professor travar no meio sem saber o que fazer.
- Um aluno ligar a depuração pelo cabo num celular da escola e mexer nele.
- Outro aparelho entrar na turma sozinho sem ter passado pelo cabo deste PC.
- Qualquer piora para quem já usa o Midview só com computadores, ou para o
  pareamento com código de hoje.
- Texto técnico para o professor (Device Owner, APK, adb). Os nomes de menu
  da MIUI ("Depuração USB") aparecem porque são o que está escrito na tela.

## Restrições

- **Depende do plano 1 do agente Android**, em especial da Task 9B: a chave de
  assinatura e o APK publicado na versão do GitHub.
- **O `adb` vai junto com o Midview do professor** (`adb.exe`,
  `AdbWinApi.dll`, `AdbWinUsbApi.dll`), com o `NOTICE.txt` do platform-tools
  (Apache 2.0) ao lado. O Windows reconhece o Redmi 10C com o
  driver próprio (`winusb.inf`, Microsoft), sem instalar driver: conferido
  neste PC (Windows 11). Num PC com Windows 10 confirma-se na primeira
  preparação de verdade, nunca num PC do laboratório durante a construção.
- **O APK:** o Midview baixa o APK da versão publicada, confere o
  SHA-256/assinatura esperados e guarda em `MasterData`; sem internet e sem
  APK guardado, o assistente diz isso antes do passo 1.
- **Pareamento pelo cabo** substitui a prova do QR. O Midview entrega ao
  agente, pelo `adb`, o número do celular e o thumbprint deste PC; o agente
  devolve o thumbprint do certificado dele, e o Midview o guarda como num
  pareamento normal (`KnownMachinesFile`). As garantias de hoje continuam: o
  celular só confia no thumbprint deste PC (`TrustedMasters`) e o Master só
  aceita o certificado daquele celular.
- **A porta de entrada do agente para o cabo** só aceita quem fala pelo
  `adb` (usuário do shell do Android, uid 2000), só enquanto o agente **não é
  dono do aparelho e não confia em nenhum PC**. Nenhum outro app do celular
  consegue usá-la.
- **Ordem no celular:** tudo o que usa o cabo acontece antes de tornar o
  agente dono do aparelho (`dpm set-device-owner`), porque o agente, ao virar
  dono, liga a restrição que desliga a depuração (`no_debugging_features`,
  já em `Lockdown`).
- **Textos** em português, no tom do Midview, sem jargão; nunca citar produto
  concorrente.
- **Testes de interface** pelo harness do Master (`MasterData.Folder`
  temporário, roteiro falso, capturas com `RenderTargetBitmap`); nunca abrir
  o Midview do professor real nem testar contra máquinas do laboratório. Os
  comandos `adb` ficam atrás de uma interface, para testar o assistente com
  um celular falso.
- **Prints:** os do Redmi 10C ficam em
  `.superpowers/brainstorm/2043-1794420649/content/` (fora do git). Para
  refazer com o celular no cabo: `am start -a <ação de Configurações>` e
  `adb exec-out screencap -p`; a MIUI bloqueia `input` pelo `adb`, então só dá
  para capturar o topo de cada tela, e a tela de login da conta Mi sai vazia.
  Depois, fechar as telas abertas (`am stack remove <id>`). Borrar nomes de
  rede e contas.

## Escolhas minhas (a revisar pelo professor)

- Sem restaurar de fábrica antes: o celular só precisa estar sem contas.
- Pareamento pelo cabo em vez de prova pela rede. O código já feito no ramo
  `feat/assistente-celulares` para a prova do QR (mensagem `ClaimPhone`, campo
  `claim` da busca, provas guardadas no PC, entrada automática pela janela
  principal, montagem do QR) **sai inteiro**: caminho que ninguém usa não
  fica no programa. Fica o que serve ao cabo: número → nome, assistente,
  passos com prints, `WipePhone`, Wi-Fi guardado.
- **Quem pode parear pelo cabo:** basta a pergunta "Permitir depuração USB?"
  no celular, aceita por quem está com ele na mão, e as chaves da Xiaomi, que
  só ligam com toques no celular, chip e conta Mi. Não há confirmação extra
  no celular. Só um celular que ainda não confia em nenhum PC aceita.
- **Wi-Fi posto pelo agente** (política de dono do aparelho), igual em todas
  as versões do Android, em vez de `cmd wifi` (que exige Android 11).
- **MIUI 12/13:** o "Deu errado?" não inventa caminhos de menu sem print ou
  fonte; traz a dica da busca das Configurações ("desenvolvedor",
  "depuração", "conta"). Caminhos próprios entram quando alguém conferir num
  aparelho com essa versão.
- **Depois de "Apagar e preparar de novo"** as chaves da Xiaomi voltam
  desligadas (o modo de desenvolvedor é apagado junto): chip e conta Mi de
  novo. O assistente diz isso na confirmação.
- **O botão aparece** pelo mesmo controle de hoje no código
  (`PhoneProvisioning.IsReady`, ligado quando o APK é publicado), não pela
  internet.
- **O trabalho do agente** (porta do cabo, Wi-Fi, tela "Pronto", apagar)
  entra neste mesmo plano.
- O `adb` e o APK ficam com o Midview do professor; nada é instalado no PC
  além disso.
- Espera de **5 minutos** depois de pronto antes de "ainda não apareceu".
- O total "de 11" é opcional e perguntado uma vez.
- Preparar de novo um número que já existe troca o celular antigo pelo novo.
- Imagens empacotadas no Master como recursos (PNG), com o contorno vermelho
  desenhado pelo programa por coordenadas relativas.
- A tela "Pronto. Este é o Celular 07 da escola." no celular é do agente;
  fica aberta até alguém tocar em "OK" ou por alguns minutos.
- MIUI 12/13 tratado no "Deu errado?" de cada passo, sem perguntar o modelo.
- **Ordem:** este trabalho vem depois do plano 1 do agente (inclusive a Task
  9B) e não corre ao mesmo tempo que outro plano que mexa nos mesmos arquivos
  do agente: um de cada vez.

## Deixado para quem constrói

- Como o agente recebe o número e o thumbprint pelo cabo (um
  `ContentProvider` chamado por `adb shell content call`, que confere
  `Binder.getCallingUid()`, é o caminho natural), incluindo o Wi-Fi, que o
  agente guarda e põe ao virar dono do aparelho.
- Como o Midview fala com o `adb` (processo `adb.exe` com saída lida pelo
  programa é o caminho natural) e percebe o celular chegando no cabo.
- Como cada erro do `adb`/`dpm` vira a frase do objetivo 4 (por exemplo
  `INSTALL_FAILED_USER_RESTRICTED` → "Instalar via USB" desligado;
  `MANAGE_DEVICE_ADMINS` → chave de segurança desligada; "already some
  accounts" → passo 3 ou 7; "already several users" → Segundo espaço/Apps
  duplos).
- A organização da janela, os textos finais de cada passo e das ajudas.

## Pesquisa (2026-11-11)

- **QR bloqueado:** desde 2025 o Google só aceita no QR de provisionamento
  apps de gerenciamento de uma lista aprovada quando o celular tem Play
  Store/Play Protect; o agente do Midview não está nela.
- **Xiaomi e o cabo:** sem "Depuração USB (configurações de segurança)", a
  MIUI recusa `dpm set-device-owner` (`SecurityException`
  `MANAGE_DEVICE_ADMINS`) e mexer em permissões pelo `adb`; sem "Instalar via
  USB", recusa `adb install`. As duas chaves exigem conta Mi e, segundo os
  guias e o teste do professor, chip no celular na hora de ligar. Fontes:
  [AirDroid](https://help.airdroid.com/hc/articles/360045329413),
  [PhoneCheck](https://support.phonecheck.com/en/articles/3609501-xiaomi-devices-are-not-being-read-by-phonecheck-it-stays-on-a-red-square),
  [OwnDroid](https://github.com/BinTianqi/OwnDroid),
  [Kaspersky](https://support.kaspersky.com/help/KSMM/4.1/en-US/254349.htm).
- **Testado no Redmi 10C da escola (MIUI 14, Android 13):** chaves ligadas sem
  chip e sem conta Mi depois de ligadas; o agente já foi feito dono do
  aparelho pelo cabo (`dpm list-owners`: `DeviceOwner,Affiliated`); driver USB
  `winusb.inf` da Microsoft.
- **Preparação real pelo cabo (2026-11-12, Redmi 10C, sem apagar):** o passo
  do cabo levou 13 a 16 s e o celular entrou na rede sozinho em 27 a 30 s. A
  instalação abre duas perguntas no celular: às vezes o Google Play Protect
  ("Enviar o app para uma verificação de segurança?", a instalação espera até
  alguém responder; "Não enviar" segue) e sempre a MIUI ("Instalar via USB",
  com "Recusar (10)" que recusa sozinho em 10 s). Com a tela apagada ou
  bloqueada a MIUI nem pergunta e recusa depois de ~1 min. Por isso o Master
  acende e desbloqueia a tela antes de instalar e diz ao professor o que
  tocar. A MIUI só deixa o novo dono abrir telas ~1 s depois de virar dono, então
  o agente pede a tela "Pronto" de novo em 2, 5 e 10 s.
- **Versões:** os três modelos param no MIUI 14. Redmi 10C → Android 13;
  Redmi Note 10S → Android 13; Redmi Note 9 Pro → Android 12. Todos atendem ao
  mínimo Android 10 do agente.
- **Contas:** o Android só deixa um app virar dono do aparelho sem nenhuma
  conta e sem segundo usuário; a conta Mi conta como conta.
- **Modo de recuperação:** Ligar + Volume para cima com o celular desligado;
  Wipe Data → Wipe All Data → Confirm.
- **MIUI e o agente:** ser dono do aparelho não protege. O "Limpar tudo" dos
  recentes da MIUI (OneKeyClean) mata o agente; o commit `b0931e0` o faz
  voltar sozinho em 3 a 12 s, e os dois comandos do objetivo 4, item 2,
  ajudam. Detalhes em
  `.superpowers/sdd/2026-11-11-agente-android-2-tela/miui-kill-report.md`.
