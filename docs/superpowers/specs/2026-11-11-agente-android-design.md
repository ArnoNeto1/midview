# Agente Android: celulares da escola no Midview (desenho)

Data: 2026-11-11. Vale para um projeto novo (o agente Android) e para
mudanças no `src/Midview.Master` (o programa do professor).

## Intenção

O professor tem 11 celulares Android da escola usados pelos alunos no
laboratório. Hoje, para deixar cada um mais rápido e sem anúncios, ele
precisa ligar a depuração USB, conectar o cabo e rodar comandos `adb`
celular por celular (o script `celulares/otimizar-celulares.ps1` faz isso,
mas ainda exige o cabo e alguém em cada aparelho). E na aula ele não tem
nenhum controle sobre os celulares, como tem sobre os computadores.

Ele quer que os celulares entrem no Midview "igual aos computadores": o
professor vê a tela, bloqueia, bloqueia apps e sites, e o celular fica leve e
sem anúncios, tudo pela rede, sem cabo e sem precisar dele em cada aparelho.

## Decidido pelo professor

- **Primeira versão faz quatro coisas no celular:** ver a tela, bloquear a
  tela, bloquear apps e sites, e a otimização (esconder apps de anúncios,
  DNS bloqueador de anúncios).
- **Uma janela só, com filtro.** A janela principal do Midview ganha três
  botões acima dos quadradinhos: **Todos**, **Computadores**, **Celulares**
  (mockup "B · Uma tela só, com filtro", em
  `.superpowers/brainstorm/878-1794398837/content/aba-celulares.html`).
  "Celulares" serve para identificar só os celulares; "Todos" integra as
  telas.
- **Mesmo Wi-Fi do laboratório.** Na aula, os celulares estão na mesma rede
  do computador do professor (a busca por broadcast funciona como nos PCs).
  É um Wi-Fi com **senha comum (WPA2)**, **com internet** e **sem página de
  login**, então o QR pode levar o Wi-Fi e o celular baixa o agente do
  GitHub durante a preparação.
- **Os 11 celulares podem ser restaurados de fábrica** uma vez, apagando tudo.
- **Sem conta Google nos celulares.** Os alunos usam só o Chrome e os apps
  que já vêm no celular. Instalar apps novos fica para uma versão seguinte.
- **Bloquear apps** tem os mesmos dois modos dos PCs ("Bloquear só estes" e
  "Liberar só estes"), mas o professor **escolhe numa lista** dos apps que o
  celular informa (nome e ícone), em vez de digitar nomes.
- **Bloquear sites** funciona como nos PCs, aplicado ao Chrome do celular.
- **Modelos misturados.** Nem todos são Redmi 10C; há outros modelos e
  versões de Android (ainda não levantados).

## Objetivos

### 1. Preparar cada celular uma única vez (QR code)

- O professor sai da conta Mi (nos Xiaomi) e restaura o celular de fábrica.
- Na primeira tela do Android, toca 6 vezes no fundo, abre o leitor de QR e
  escaneia um QR code que o **Midview do professor mostra** (nova opção em
  Configurar, por exemplo "Preparar celular").
- O QR é o de provisionamento de **Device Owner** do Android: baixa o APK do
  agente, confere a assinatura/checksum, entra no Wi-Fi do laboratório e
  torna o agente o dono do aparelho ("aparelho da escola").
- Depois disso, nada mais é feito no celular: o agente aparece na busca do
  Midview e o professor pareia como pareia um PC.

### 2. O agente no celular

- Fica sempre ativo (inclusive depois de reiniciar) e o aluno não consegue
  desinstalar, desativar, forçar parada, restaurar de fábrica nem abrir as
  Opções do desenvolvedor.
- Fala o **protocolo que já existe** com o Midview do professor:
  - Busca: responde ao probe UDP na porta `28901` (`Discovery`, JSON).
  - Comandos: escuta TLS na porta `28900`, com certificado próprio
    autoassinado, guardado no aparelho, e confia no professor pelo
    thumbprint fixado (`SecureChannel`, `TrustStore`), quadros com
    `MessageFramer`.
  - Pareamento: o mesmo fluxo de `Pairing` (commit, nonce, reveal, código de
    6 dígitos de 5 minutos), com o código aparecendo na tela do celular.
- Mostra para o aluno, numa notificação fixa, que o aparelho é gerenciado
  pela escola.

### 3. Otimização

Aplicada sozinha na primeira conexão com o professor e repetível por um botão
**"Otimizar"** (no grupo dos celulares, na janela principal).

- **Apps de anúncios e bloatware ficam escondidos** (o aluno não os vê e eles
  não rodam). A lista inicial é a de `celulares/otimizar-celulares.ps1`
  (`$Remover`): `com.miui.msa.global`, `com.miui.analytics`,
  `com.xiaomi.mipicks`, `com.xiaomi.glgm`, `com.miui.yellowpage`, os três
  `com.facebook.*`, `com.amazon.appmanager`,
  `com.miui.android.fashiongallery`, `com.xiaomi.discover`,
  `com.miui.miservice`, `com.miui.bugreport`,
  `com.google.android.projection.gearhead`,
  `com.google.android.apps.subscriptions.red`. Só os que existirem no
  aparelho.
- **Nunca** esconder: Chrome, WebView do Android, Serviços do Google Play,
  Play Store, a tela inicial, as Configurações, a câmera, o próprio agente,
  nem os apps de sistema de energia, segurança e notificação da MIUI
  (`com.miui.securitycenter`, `com.miui.powerkeeper`, `com.xiaomi.joyose`,
  `com.miui.notification`).
- **DNS bloqueador de anúncios** (`dns.adguard-dns.com`) para o aparelho
  inteiro. Antes de ligar, confere se há internet; depois de ligar, confere
  de novo e, se a rede bloquear esse DNS, desliga e avisa o professor
  (mesma lógica do script).
- O professor vê, no quadradinho ou num resumo, se a otimização foi aplicada e
  o que não foi possível.

### 4. Ver a tela

- O professor vê a tela do celular no quadradinho (miniatura) e abre em
  janela grande com dois cliques, como nos PCs, em pé, sem faixas pretas.
- O Android exige que alguém **toque em "Iniciar agora"** no celular cada vez
  que a captura começa. **Decidido pelo professor:** o pedido aparece em
  todos os celulares no começo da aula (assim que o Midview do professor se
  conecta a eles), o aluno toca uma vez e os quadradinhos mostram a tela ao
  vivo, como nos PCs. O pedido volta depois de reiniciar e, no Android 14 ou
  mais novo, sempre que a captura parar.
- *(Escolha minha.)* Enquanto ninguém aceita, o quadradinho mostra
  "Aguardando o aluno permitir". Se o aluno recusar, mostra "Não permitiu
  ver a tela", e o professor tem **"Pedir de novo"** no celular escolhido. O
  celular não é bloqueado por recusar. Com a tela bloqueada, o pedido espera
  o desbloqueio para aparecer.
- Controle remoto (mexer no celular pelo PC) **não** entra.

### 5. Bloquear a tela

- O mesmo "Bloquear telas" dos PCs: o celular mostra o cadeado em tela cheia
  e o aluno não consegue sair (nem pelo botão voltar, início, recentes ou
  barra de notificações) até o professor liberar.
- **Mesma regra dos PCs** (`2026-10-05-screen-lock-design.md`, "nunca deixa
  uma turma bloqueada"): o bloqueio dura enquanto a conexão do professor o
  segura (`LockScreen` e heartbeats). Se o Midview do professor fecha, cai ou
  o PC desliga, o celular libera depois do mesmo tempo limite da Session.
  Se o celular reinicia com o professor ainda segurando o bloqueio, o
  Midview bloqueia de novo assim que ele volta (como o `ScreenLockKeeper`
  faz com os PCs). *(Escolha minha.)*

### 6. Bloquear apps

- Modos "Bloquear só estes" e "Liberar só estes", como em `AppBlockPolicy`.
- O celular informa ao professor a lista de apps que o aluno pode abrir
  (nome, pacote e ícone). O professor marca os apps nessa lista.
- *(Escolha minha.)* Há **uma política de apps para os celulares da turma**,
  separada da dos PCs (que é por nome de `.exe`). Com vários celulares
  selecionados, a lista mostrada é a junção dos apps de todos eles. O botão
  "Bloquear programas" abre a janela de PCs ou a de celulares conforme a
  seleção; numa seleção mista, abre a dos PCs e avisa que os celulares ficam
  de fora. Celular pareado depois recebe a política em vigor ao conectar.
- App bloqueado fica cinza e não abre. Em "Liberar só estes", o agente, o
  Chrome (se o professor liberou), as Configurações bloqueadas e a tela
  inicial continuam funcionando.
- A política fica guardada no celular e vale depois de reiniciar.

### 7. Bloquear sites

- Os celulares recebem **os mesmos comandos da mesma janela** dos PCs
  (`SiteBlockWindow`): `BlockDomain`/`UnblockDomain` um domínio por vez e
  `BlockInternet`/`UnblockInternet`. Não há lista nova nem modo "só liberar"
  nesta versão. Como nos PCs, quem guarda o conjunto bloqueado é o agente.
- No celular, o agente aplica isso pelas **políticas gerenciadas do Chrome**
  (`URLBlocklist`; para bloquear a internet, bloquear tudo). *(Escolha minha:
  o mecanismo exato fica com quem constrói, desde que valha para o Chrome e
  para o aparelho todo no caso de "Bloquear a internet".)*
- As opções do `LabConfig` (Lockdown, BlockDownloads, Adblock) **não** valem
  para celular nesta versão; o bloqueio de anúncios do celular é o DNS da
  otimização. *(Escolha minha.)*
- Outros navegadores ficam escondidos ou bloqueados, para o aluno não fugir
  do Chrome.

### 8. Programa do professor

- Filtro **Todos / Computadores / Celulares** acima dos quadradinhos.
- Quadradinho do celular **em pé e com cantos mais redondos**, com o mesmo
  ponto verde/cinza de online e o cadeado de bloqueado.
- Os botões que não funcionam em celular ficam **apagados** quando só há
  celulares selecionados. Numa seleção mista, o botão age nos PCs e o
  Midview avisa quantos celulares ficaram de fora.
- Novo grupo de botões para os celulares: **Otimizar** e **Preparar
  celular** (o QR code).

## Não-objetivos (primeira versão)

- Controlar o celular remotamente.
- Desligar, ligar pela rede, agendar ligar e desligar (o Android não deixa
  desligar nem ligar remotamente; reiniciar pode entrar se for barato).
- Mensagens, chat, mandar e recolher arquivos, pasta da turma.
- Instalar apps novos, Play Store, contas Google nos celulares.
- Os tablets da SED-SC (são gerenciados pela Secretaria; ficam de fora).
- iPhone.

## Anti-objetivos (seria um fracasso mesmo funcionando)

- Precisar do cabo ou de alguém mexendo em cada celular depois da preparação.
- O aluno conseguir tirar o agente, desligar o bloqueio, resetar o aparelho
  pelas Configurações ou ligar as Opções do desenvolvedor.
- **Brecha aceita pelo professor:** apagar o celular pelo modo de recuperação
  (botões ligar + volume) não tem como ser impedido sem conta Google. O
  celular volta de fábrica, aparece desligado no Midview e o professor o
  prepara de novo com o QR.
- Esconder algo que quebre o celular (Chrome, WebView, Serviços do Google
  Play, tela inicial) ou deixar o celular sem internet por causa do DNS.
- Qualquer piora para os PCs: o Midview do professor continua funcionando
  igual com computadores, e um professor com versão antiga não quebra ao
  ver um celular na rede.

## Restrições

- **Android 10 ou mais novo** (minha escolha: é o mínimo para o DNS privado
  pelo Device Owner e para o QR de provisionamento ser estável). Celular mais
  antigo não é suportado.
- **Device Owner sem conta**: só funciona com o aparelho recém-restaurado.
- **Xiaomi/MIUI**: restaura com a conta Mi já removida, senão o aparelho fica
  travado na conta.
- **Ver a tela** depende do consentimento a cada sessão (MediaProjection). A
  partir do Android 14, o consentimento vale para uma única sessão e a
  captura precisa de serviço em primeiro plano do tipo `mediaProjection`.
- **Atualizar o agente** em aparelhos Device Owner exige que todo APK novo
  seja assinado com **a mesma chave** do primeiro. A chave de assinatura
  precisa ser criada uma vez e guardada fora do repositório (segredo do CI).
- **Mesma rede**: busca por broadcast UDP; sem servidor na internet.
- **Compatibilidade de protocolo**: o agente Android tem que ser aceito pelo
  `Midview.Master` atual nas partes que já existem (busca, pareamento, TLS,
  `ScreenFrame`). Campos novos (por exemplo, dizer que é um celular) não
  podem quebrar professor de versão antiga.
- **Ferramentas**: o notebook não tem Android SDK nem Java hoje; o projeto
  precisa de Android SDK, JDK e Gradle. O GitHub Actions passa a gerar o APK
  assinado junto com o `MidviewSetup.exe`.
- Textos para o aluno e para o professor em português, no mesmo tom do
  Midview.

## Escolhas minhas (a revisar pelo professor)

- **Kotlin, app Android nativo**, em `src/Midview.Android/` (fora da solução
  .NET), com seu próprio Gradle.
- **O APK fica na versão do GitHub**, como o instalador do Windows, e o QR
  aponta para ele.
- **O QR já leva o Wi-Fi e a senha do laboratório**, que o professor digita
  uma vez em "Preparar celular".
- **Atualizar o agente pela rede** entra na primeira versão, mínimo: o
  professor manda a versão nova e o agente instala sozinho (sem isso, cada
  atualização voltaria a precisar de alguém em cada celular).
- **Restringir segundo plano e desligar animações**, que o script fazia, não
  entram: o Device Owner não tem como fazer isso pela API pública.
- **O provisionamento deixa todos os apps de sistema ligados**
  (`EXTRA_PROVISIONING_LEAVE_ALL_SYSTEM_APPS_ENABLED = true`). O padrão do
  Android desligaria câmera, galeria, calculadora e arquivos, que os alunos
  usam, e isso não dá para mudar depois sem restaurar de novo. Quem some é só
  a lista de anúncios e os outros navegadores.
- **Celular de referência: Redmi 10C** (Android 13, MIUI 14). Os outros
  modelos são "melhor esforço"; o modelo e a versão de cada um ficam
  registrados quando o celular é pareado. Modelo que recusar o Device Owner
  fica de fora.
- **"Permitir novo professor" entra** (`AllowNewTeacher`), para que
  reinstalar o PC do professor (certificado novo) não obrigue a restaurar os
  11 celulares. "Dar acesso a um colega" e o segundo PC da turma ficam para
  depois.

## Deixado para quem constrói

- Como o agente organiza serviços, permissões e o modo de tarefa bloqueada.
- Em qual porta fica a tela (a `5901` da Session, a `28900`, ou outra) e
  como encaixar `ScreenFrame` na captura do Android, desde que o Master veja a
  tela pelo caminho que já usa para os PCs ou por um caminho novo e limpo.
- Como o Master sabe que o aparelho é celular (campo novo na resposta da
  busca, no `PairDone`, ou outro), respeitando a compatibilidade.
- Mensagens novas do protocolo (lista de apps com ícones, otimização,
  política de apps de celular) e seus formatos.
- Testes: unitários no agente e no Master, e teste em aparelho de verdade (o
  Redmi 10C da escola) ou emulador.
