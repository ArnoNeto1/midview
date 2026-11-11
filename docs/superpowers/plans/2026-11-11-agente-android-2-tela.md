# Agente Android, plano 2 de 4: tela (ver e bloquear) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O professor vê a tela do celular no quadradinho e na janela grande, como nos PCs, e bloqueia o celular com o cadeado, usando o protocolo que o Midview já fala com os PCs.

**Architecture:** Tudo na porta `5901`, como a Session do Windows: a miniatura passa a ser a imagem real da tela; a primeira mensagem `ScreenStream` abre o fluxo de `ScreenFrame`/`FrameAck` da janela grande; a primeira mensagem `LockScreen` abre a conexão que segura o bloqueio com `Heartbeat`. A captura usa `MediaProjection` (o aluno toca em "Iniciar agora"); o bloqueio usa o modo de tarefa bloqueada do Device Owner com uma tela de cadeado. No Master, celulares passam a aceitar "Ver a tela" e "Bloquear telas", e ganham "Pedir de novo para ver a tela".

**Tech Stack:** Kotlin (Android 10+, `MediaProjection`, `VirtualDisplay`, `ImageReader`, `DevicePolicyManager.setLockTaskPackages`), JUnit 4 JVM; .NET 8 / WPF / xUnit no Master; E2E em `tests/Midview.E2E`.

**Spec:** `docs/superpowers/specs/2026-11-11-agente-android-design.md` (seções 4 e 5, e "Escolhas minhas" sobre o pedido de captura).

## Global Constraints

- Tudo de tela na porta `5901` (`ConnectionClient.SessionPort`); o tipo da conexão é a primeira mensagem: `RequestThumbnail` (12), `ScreenStream` (48) ou `LockScreen` (34), como em `src/Midview.Session/SessionServer.cs:164-291`.
- `ScreenFrame` (49): cabeçalho de 16 bytes int32 **little-endian** (frameId, largura, altura, nº de regiões); cada região 20 bytes int32 LE (x, y, w, h, tamanho do JPEG) + JPEG; ids consecutivos a partir de 1; até 8 MiB de JPEG por mensagem (senão divide); regras de `ScreenFrame.Decode` (lados 1..16384, área ≤ 8192², região dentro da tela, soma das áreas ≤ 2× a tela arredondada em blocos de 64).
- `FrameAck` (50): int32 LE com o id; reconhecer o id mais novo reconhece os anteriores. No máximo **2** atualizações sem reconhecimento; sem espaço em **1 s**, descarta a mais antiga e segue (`FrameCredit.cs`).
- Primeira atualização e toda mudança de tamanho (rotação) = tela inteira.
- Ritmo: descanso de 66 ms parado, 33 ms com mudança (janela de atividade 1 s), nunca menos que metade do tempo de trabalho (`CaptureCadence.cs`).
- JPEG das regiões qualidade 90; miniatura qualidade 70, caixa pedida em `RequestThumbnail` (padrão 320×180, máximo 960×540), sem ampliar, mantendo a proporção.
- Tempos: primeira mensagem 10 s; depois 15 s de silêncio encerra (fluxo e bloqueio). O Master manda `Heartbeat` a cada 1 s no fluxo e a cada 3 s no bloqueio.
- `LockScreen` responde um `CommandAck` (`CommandResult`: 1 byte + UTF-8): sucesso `"tela bloqueada"`; recusa `"este celular não bloqueia a tela"` quando não é Device Owner. Vários bloqueios ao mesmo tempo: a tela só libera quando a última conexão termina. Um `LockScreen` novo com a tela já bloqueada é aceito.
- Tela de cadeado do aluno: fundo cinza-escuro (64,64,64), cadeado cinza-claro (220,220,220), **sem texto** (spec do bloqueio de tela, "What the student sees").
- `StartControl`, `StopControl` (fora do fluxo), `InputEvent`, `ClipboardText`, `FillAdminPassword` na 5901: o celular não aceita controle; encerra a conexão de controle sem travar nada.
- Textos no quadradinho: `"Aguardando o aluno permitir"`, `"Não permitiu ver a tela"`; menu do celular: `"Pedir de novo para ver a tela"`.
- Nova mensagem `RequestScreenConsent = 92` (Master → agente na porta `28900`, payload vazio, resposta `CommandAck` com `"pedido mostrado"` ou a falha). Agentes Windows nunca a recebem (o Master só manda a celulares).
- Android 10+ (`minSdk 29`); no Android 14+ a captura roda num serviço em primeiro plano do tipo `mediaProjection`, iniciado só depois do consentimento.
- Textos em português, no tom do Midview; nenhum produto concorrente citado; commits sem `Co-Authored-By`.

## Review Focus

- O aluno recusa ou ignora o "Iniciar agora": o quadradinho mostra "Não permitiu ver a tela" e nada trava; "Pedir de novo" mostra o pedido outra vez. (Tasks 3 e 6)
- O celular gira durante a janela grande: chega uma atualização de tela inteira com o novo tamanho e a janela se ajusta, sem fechar. (Task 2)
- O Midview do professor fecha ou o PC desliga com o celular bloqueado: o celular libera sozinho em até 15 s. (Task 5)
- O celular reinicia com o professor segurando o bloqueio: o Master reconecta e bloqueia de novo; o pedido de captura espera o desbloqueio. (Tasks 3 e 5)
- Um celular que não é aparelho da escola recebe "Bloquear telas": o professor vê "Não bloqueou (este celular não bloqueia a tela)" e nada quebra. (Tasks 5 e 6)

---

### Task 1: `ScreenFrame`, `FrameCredit` e blocos alterados no Kotlin

**Files:**
- Create: `src/Midview.Android/app/src/main/java/io/github/arnoneto1/midview/agent/protocol/ScreenFrame.kt`, `.../protocol/FrameAck.kt`, `.../screen/FrameCredit.kt`, `.../screen/TileDiff.kt`, `.../screen/CaptureCadence.kt`
- Modify: `.../protocol/MessageType.kt` (`StartControl=3`, `StopControl=4`, `InputEvent=6`, `LockScreen=34`, `ScreenStream=48`, `ScreenFrame=49`, `FrameAck=50`, `ClipboardText=60`, `FillAdminPassword=90`, `RequestScreenConsent=92`)
- Modify: `src/Midview.Protocol/MessageType.cs` (`RequestScreenConsent = 92`, com comentário: Master → agente Android, porta 28900)
- Test: `app/src/test/.../protocol/ScreenFrameTest.kt`, `.../screen/FrameCreditTest.kt`, `.../screen/TileDiffTest.kt`, `.../screen/CaptureCadenceTest.kt`; `tests/Midview.Protocol.Tests/ScreenFrameTests.cs` (vetor dourado)

**Interfaces:**
- Produces:
  - `data class ScreenRegion(val x: Int, val y: Int, val width: Int, val height: Int, val jpeg: ByteArray)`; `object ScreenFrame { fun encode(frameId: Int, screenW: Int, screenH: Int, regions: List<ScreenRegion>): ByteArray; fun split(regions: List<ScreenRegion>): List<List<ScreenRegion>> }` (limite 8 MiB por grupo, região maior vai sozinha).
  - `object FrameAck { fun decode(payload: ByteArray): Int? }` (4 bytes LE; outro tamanho → nulo).
  - `class FrameCredit(maxUnacked: Int = 2) { fun nextId(): Int; fun sent(id: Int); fun ack(id: Int); fun awaitRoom(timeoutMs: Long): Boolean; fun dropOldest() }`.
  - `class TileDiff(tile: Int = 64) { fun changed(previous: IntArray?, current: IntArray, width: Int, height: Int): List<Rect> }` (pixels ARGB; `previous` nulo ou tamanho diferente → tela inteira) e `fun merge(tiles: List<Rect>): List<Rect>` (junta blocos vizinhos da mesma linha, como `TileRegions.Merge`).
  - `class CaptureCadence { fun restAfter(workMs: Long, changed: Boolean, nowMs: Long): Long }` com 66/33 ms, janela de 1 s e mínimo de metade do trabalho.

- [ ] **Step 1: Write the failing tests**

```kotlin
@Test fun encodeMatchesDotNetLayout() // 1 região 2x1: 16 bytes de cabeçalho LE + 20 de região + JPEG; bytes comparados com o vetor de tests/Midview.Protocol.Tests
@Test fun splitKeepsEachGroupUnderEightMiB()
@Test fun frameAckReadsLittleEndianAndRejectsOtherSizes()
@Test fun creditAllowsTwoUnackedThenWaits()
@Test fun ackOfNewestAcksOlder()
@Test fun noRoomForOneSecondDropsOldest()
@Test fun firstFrameAndResizeAreFullScreen()
@Test fun onlyChangedTilesAreReportedAndMergedPerRow()
@Test fun cadenceIs66IdleAnd33ActiveAndNeverBelowHalfTheWork()
```

```csharp
[Fact] public void Kotlin_frame_vector_decodes() // os mesmos bytes do teste Kotlin → ScreenFrame.Decode devolve frameId 1, 2x1, 1 região
```

- [ ] **Step 2:** Run `src/Midview.Android/gradlew.bat -p src/Midview.Android testDebugUnitTest` e `dotnet test tests/Midview.Protocol.Tests --filter FullyQualifiedName~ScreenFrameTests`. Expected: Kotlin FAIL (tipos não existem); .NET FAIL (vetor novo).
- [ ] **Step 3:** Implement (JVM puro, sem classes do Android; o JPEG do vetor é um arquivo pequeno de teste).
- [ ] **Step 4:** Same commands. Expected: PASS.
- [ ] **Step 5:** Commit `feat(android): quadro de tela, crédito e blocos alterados`.

### Task 2: Captura da tela (MediaProjection) e janela grande

**Files:**
- Create: `.../screen/ScreenConsent.kt` (estado do pedido), `.../screen/ConsentActivity.kt` (pede o "Iniciar agora"), `.../screen/ScreenCaptureService.kt` (serviço `mediaProjection`), `.../screen/FrameSource.kt` (último quadro ARGB + tamanho, escala), `.../net/ScreenStreamHandler.kt`
- Modify: `.../net/ThumbnailHandler.kt` (despacho pela primeira mensagem: `RequestThumbnail`, `ScreenStream`, `LockScreen` reservado para a Task 4), `.../agent/AgentService.kt`, `AndroidManifest.xml` (`FOREGROUND_SERVICE_MEDIA_PROJECTION`, serviço com `foregroundServiceType="mediaProjection"`)
- Test: `.../screen/ScreenConsentTest.kt`, `.../net/ScreenStreamHandlerTest.kt` (com `FrameSource` falso)

**Interfaces:**
- Consumes: Task 1.
- Produces:
  - `enum class ConsentState { NotAsked, Waiting, Denied, Capturing }`; `class ScreenConsent(clock) { val state; fun ask(); fun granted(); fun denied(); fun stopped(); fun lockedChanged(locked: Boolean) }` — com a tela bloqueada, `ask()` só marca e o pedido aparece no desbloqueio.
  - `interface FrameSource { fun latest(): Frame?; fun awaitNew(timeoutMs: Long): Frame? }`, `data class Frame(val pixels: IntArray, val width: Int, val height: Int, val seq: Long)`; captura em resolução nativa limitada a 1280 no lado maior.
  - `ScreenStreamHandler(channel, source, credit, cadence)`: depois de `ScreenStream`, manda `ScreenFrame`s (primeira = tela inteira; mudou o tamanho = tela inteira), lê `FrameAck` e `Heartbeat`, encerra com `StopControl`, fim de conexão ou 15 s de silêncio; `StartControl`/`InputEvent`/`ClipboardText`/`FillAdminPassword` encerram a conexão.
  - Miniatura: com `Capturing`, JPEG 70 do último quadro na caixa pedida; senão a `StatusImage` com `"Aguardando o aluno permitir"` (`Waiting`/`NotAsked`) ou `"Não permitiu ver a tela"` (`Denied`).
  - O pedido aparece quando o primeiro professor confiável se conecta na 5901 (início da aula), depois de reiniciar, e no Android 14+ sempre que a captura parar.

- [ ] **Step 1: Write the failing tests**

```kotlin
@Test fun askWhileLockedWaitsForUnlock()
@Test fun deniedStaysDeniedUntilAskedAgain()
@Test fun streamStartsWithFullScreenThenOnlyChanges()
@Test fun rotationSendsFullScreenWithNewSize()
@Test fun twoUnackedFramesPauseCapture()
@Test fun stopControlAndSilenceEndTheStream()
@Test fun controlMessagesCloseTheConnection()
@Test fun thumbnailShowsStatusUntilCapturing()
```

- [ ] **Step 2:** Run `gradlew.bat -p src/Midview.Android testDebugUnitTest`. Expected: FAIL.
- [ ] **Step 3:** Implement. `ConsentActivity` usa `MediaProjectionManager.createScreenCaptureIntent()`; o resultado inicia `ScreenCaptureService` (primeiro plano, tipo `mediaProjection`), que cria `VirtualDisplay` + `ImageReader` (RGBA_8888) e alimenta o `FrameSource`; `MediaProjection.Callback.onStop` → `ScreenConsent.stopped()`. Rotação: recriar o `VirtualDisplay` com o novo tamanho.
- [ ] **Step 4:** Same command. Expected: PASS; `assembleDebug` OK.
- [ ] **Step 5:** Commit `feat(android): ver a tela do celular`.

### Task 3: "Pedir de novo para ver a tela"

**Files:**
- Modify: `.../net/CommandHandler.kt` (`RequestScreenConsent` → `ScreenConsent.ask()` → `CommandAck("pedido mostrado")`, ou falha `"a tela está bloqueada: o pedido aparece quando o celular for desbloqueado"` quando bloqueada)
- Create: `src/Midview.Master/ScreenConsentClient.cs`
- Modify: `src/Midview.Master/MainWindow.xaml(.cs)` (item no menu do quadradinho, só para celular: `"Pedir de novo para ver a tela"`)
- Test: `app/src/test/.../net/CommandHandlerTest.kt`; `tests/Midview.Master.Tests/ScreenConsentClientTests.cs`

**Interfaces:**
- Produces: `ScreenConsentClient.Ask(KnownMachine phone, X509Certificate2 master, int port = ConnectionClient.AgentPort) -> CommandResult` (conecta, manda `RequestScreenConsent`, lê `CommandAck`; timeout de conexão 5 s, resposta 10 s, como os outros clientes de comando).

- [ ] **Step 1: Write the failing tests**

```kotlin
@Test fun requestScreenConsentAsksAndAcks()
@Test fun requestScreenConsentWhileLockedSaysItWaits()
```

```csharp
[Fact] public void Ask_sends_request_and_reads_ack() // agente roteirizado como nos outros testes de cliente
[Fact] public void Menu_item_only_for_phones()      // por meio do método que monta o menu do quadradinho
```

- [ ] **Step 2:** Run both test commands. Expected: FAIL.
- [ ] **Step 3:** Implement. O resultado aparece na faixa de status ("Pedido mostrado no celular Cel 01." / a falha).
- [ ] **Step 4:** Same commands. Expected: PASS.
- [ ] **Step 5:** Commit `feat: pedir de novo para ver a tela do celular`.

### Task 4: Bloqueio da tela no celular

**Files:**
- Create: `.../lock/ScreenLock.kt` (contagem de conexões que seguram o bloqueio), `.../lock/LockActivity.kt` (cadeado em tela cheia, sem texto), `.../net/LockScreenHandler.kt`
- Modify: `.../policy/Lockdown.kt` + `DevicePolicyGateway` (`setLockTaskPackages([próprio pacote])` e `setLockTaskFeatures(LOCK_TASK_FEATURE_NONE)` quando Device Owner), `.../net/ThumbnailHandler.kt` (despacho de `LockScreen`), `AndroidManifest.xml` (`LockActivity` com `lockTaskMode="if_whitelisted"`, tema escuro sem animação)
- Test: `.../lock/ScreenLockTest.kt`, `.../net/LockScreenHandlerTest.kt`, `.../policy/LockdownTest.kt`

**Interfaces:**
- Produces:
  - `class ScreenLock(show: () -> Unit, hide: () -> Unit) { fun hold(): Handle; class Handle { fun release() } ; val locked: Boolean }` — mostra na primeira `hold`, esconde quando a última é liberada.
  - `LockScreenHandler(channel, lock, isDeviceOwner)`: não Device Owner → `CommandAck(false, "este celular não bloqueia a tela")` e fecha; senão `hold()`, `CommandAck(true, "tela bloqueada")`, lê `Heartbeat` até fim de conexão ou 15 s de silêncio, então `release()`.
  - `LockActivity`: `startLockTask()` ao abrir, `stopLockTask()` + `finish()` ao liberar; volta/início/recentes/notificações bloqueados pelo modo de tarefa; avisa `ScreenConsent.lockedChanged`.

- [ ] **Step 1: Write the failing tests**

```kotlin
@Test fun firstHoldShowsLastReleaseHides()
@Test fun secondLockWhileLockedIsAccepted()
@Test fun silenceOfFifteenSecondsReleases()
@Test fun connectionEndReleasesAtOnce()
@Test fun notDeviceOwnerRefusesWithText()
@Test fun lockdownAllowsLockTaskForOwnPackage()
```

- [ ] **Step 2:** Run `gradlew.bat -p src/Midview.Android testDebugUnitTest`. Expected: FAIL.
- [ ] **Step 3:** Implement. Desenho do cadeado igual ao do Windows (forma simples, `Canvas`), proporcional à tela.
- [ ] **Step 4:** Same command. Expected: PASS; `assembleDebug` OK.
- [ ] **Step 5:** Commit `feat(android): bloquear a tela do celular`.

### Task 5: Master liga "Ver a tela" e "Bloquear telas" para celular

**Files:**
- Modify: `src/Midview.Master/DeviceSupport.cs` (`Android` suporta `View` e `ScreenLock`), `src/Midview.Master/ViewControlWindow*.cs` (janela em pé sem faixas; "Controlar" continua só para PC), `src/Midview.Master/ScreenLockKeeper.cs` (nada a mudar se o protocolo bater — conferir), `src/Midview.Master/MainWindow.xaml.cs` (não iniciar `HoldSwitchUserBlock` para celular)
- Test: `tests/Midview.Master.Tests/DeviceSupportTests.cs`, `ScreenLockKeeperTests.cs`, `ViewWindowFitTests.cs`; `tests/Midview.E2E/AndroidAgentInteropTests.cs` (novos fatos)

**Interfaces:**
- Consumes: Tasks 2 e 4 (o celular responde na 5901 como a Session).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Phone_supports_view_and_screen_lock_only()
[Fact] public void Phone_lock_does_not_start_switch_user_block()
[Fact] public void Portrait_screen_fits_the_view_window() // 720x1650 → janela em pé, sem faixas
[Fact] public void Keeper_shows_refusal_from_phone_that_is_not_owner() // "Não bloqueou (este celular não bloqueia a tela)"
```

```csharp
// E2E com MIDVIEW_ANDROID_HOST:
[Fact] public void View_window_receives_frames_from_phone()   // ScreenStream → ≥ 1 ScreenFrame válido em 10 s (com captura permitida)
[Fact] public void Lock_on_phone_answers_and_releases()       // LockScreen → CommandAck; fechar → liberado (o teste lê o estado pelo adb)
```

- [ ] **Step 2:** Run `dotnet test tests/Midview.Master.Tests --filter "FullyQualifiedName~DeviceSupport|FullyQualifiedName~ScreenLockKeeper|FullyQualifiedName~ViewWindowFit"`. Expected: FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Same command. Expected: PASS; `dotnet test Midview.sln` verde (fora as falhas conhecidas de ambiente).
- [ ] **Step 5:** Commit `feat(master): ver e bloquear a tela do celular`.

### Task 6: Prova no celular de verdade

**Files:**
- Modify: `docs/superpowers/plans/2026-11-11-agente-android-2-tela.md` (resultado anotado no fim)

- [ ] **Step 1: Redmi 10C (não é aparelho da escola).** Instalar o APK de teste pela pasta Download (a MIUI recusa o `adb install`), administrador e notificações já ligados. Abrir uma **cópia de teste do Master** (pasta de dados separada em `MasterData.Folder`, nunca a sala real), parear, ver o quadradinho "Aguardando o aluno permitir" → tocar "Iniciar agora" no celular → miniatura real → "Ver a tela" em janela grande, girar o celular → "Bloquear telas" → "Não bloqueou (este celular não bloqueia a tela)". Recusar o pedido → "Não permitiu ver a tela" → "Pedir de novo" → pedido aparece.
- [ ] **Step 2: Bloqueio de verdade** num aparelho da escola: emulador Android (precisa da autorização do professor para baixar a imagem do sistema e aceitar a licença) com `dpm set-device-owner` e o APK **release** assinado com a chave de teste, ou o primeiro celular preparado pelo QR depois da Task 9 do plano 1. Conferir: cadeado em tela cheia, botões início/voltar/recentes sem efeito, fechar o Master → libera em até 15 s, reiniciar o celular com o Master segurando → bloqueia de novo.
- [ ] **Step 3:** Anotar os resultados aqui e fazer o commit `docs: resultado do teste da tela no celular`.

#### Resultado do teste

**Redmi 10C (passo 1, feito pelo professor numa cópia de teste do Master):** pareou, o quadradinho do celular apareceu e "Ver a tela" funcionou. "Bloquear telas" não bloqueou, como esperado: o Redmi não é aparelho da escola (não é Device Owner) e responde "este celular não bloqueia a tela". O provisionamento pelo QR no Redmi (Android 13, MIUI 14) parou em "Can't set up device": no emulador o mesmo APK provisiona pelo caminho da própria plataforma, e a causa provável é o Play Protect barrar um DPC fora da lista aprovada (investigação à parte).

**Emulador (passo 2), 11/11/2026:** AVD `midview33` (Android 13, `google_apis`, x86_64), apagado antes do teste, com o APK **debug** do commit 5277a6e feito Device Owner por `adb root` + `dpm set-device-owner io.github.arnoneto1.midview.agent/.AdminReceiver` (o debug mantém o adb). O PC chega ao agente por `adb forward` (38900→28900, 35901→5901). Do lado do professor, as classes de verdade do Master (`PairingSession`, `ThumbnailPoller`, `ConnectionClient` como a janela grande, `ViewWindowFit`, `ScreenConsentClient`, `ScreenLockKeeper`) num programa de teste descartável, e os testes E2E `AndroidAgentInteropTests` com o adb ligado.

| Verificação | Resultado | Evidência |
|---|---|---|
| Janela grande recebe a tela | passou | Primeira atualização de tela inteira 591x1280 logo depois do "Start now"; E2E `Screen_stream_sends_frames_from_phone` passou (primeira atualização em 0,1 s com a captura já liberada). |
| Girar o celular com a janela aberta | passou | Com Configurações aberto e `user_rotation 1`: quadro 4 de tela inteira 1280x591 (janela 1280x591), e de volta ao `user_rotation 0`: quadro 14 de tela inteira 591x1280 (janela 462x1001), sem fechar o fluxo (24 quadros em 45 s). No log do celular: "Screen capture now 1280x591" e "now 591x1280". A tela inicial do emulador não gira (só retrato), por isso o teste foi em Configurações. |
| Recusar o pedido | passou | "Cancel" tocado pelo `input tap` (posição pelo `uiautomator dump`): o quadradinho mostra "Não permitiu ver a tela". |
| "Pedir de novo" depois da recusa | passou | `ScreenConsentClient.Ask` → "pedido mostrado"; o pedido do sistema voltou na hora; "Start now" → miniatura real e quadros. |
| Pedido largado com o botão início | passou (com a correção) | Antes da correção, no Android 13 o início derrubava o pedido com o mesmo "não" do Cancel e o quadradinho dizia "Não permitiu ver a tela". Agora: "screen request left without an answer", o quadradinho segue "Aguardando o aluno permitir" e o "Pedir de novo" seguinte, dentro do minuto, mostra o pedido de novo na hora. |
| Tela apagada com o pedido aberto | passou no emulador | O Android 13 também derruba o pedido ao apagar a tela; conta como sem resposta ("Aguardando"), não como recusa. |
| Cadeado (bloqueio pelo `ScreenLockKeeper`) | passou | "tela bloqueada", `mLockTaskModeState=LOCKED`, cadeado em tela cheia (`screens/emulator-lock-debug.png`). Início, voltar e recentes (`input keyevent`): a tela de cima continua `LockActivity`, estado LOCKED, capturas idênticas byte a byte. |
| Professor some sem fechar a conexão | passou | O processo do professor foi congelado (conexão aberta, sem batimentos): o celular liberou sozinho em 13,9 s depois do congelamento (até 3 s de batimento antes + 15 s de silêncio). |
| Fechar o professor | passou | E2E `Lock_on_phone_answers_and_releases`, agora lendo o estado pelo adb: LOCKED em 0,6 s, NONE 0,1 s depois de fechar. Matar o processo do professor também libera na hora. |
| Reiniciar com o professor segurando | passou | `adb reboot` com o `ScreenLockKeeper` rodando: o agente subiu sozinho (`StartAtBoot`) e o cadeado voltou cerca de 3 s depois de o agente escutar (a nova tentativa do professor é a cada 3 s); LOCKED confirmado pelo adb. |
| Pedido com a tela bloqueada | passou | Com o cadeado, "Pedir de novo" responde "a tela está bloqueada: o pedido aparece quando o celular for desbloqueado"; ao liberar, o pedido aparece sozinho. Cadeado subindo com o pedido aberto: o pedido sai ("left without an answer") e volta ao liberar. |
| Celular que não é da escola recusa o bloqueio | passou (Redmi, passo 1) | No emulador Device Owner o teste `Lock_on_a_phone_that_is_not_device_owner_is_refused` falha, como esperado (ele bloqueia). |

**Ainda precisa do celular de verdade:**

- Provisionamento pelo QR num celular com Play Store (o Redmi falhou; investigação à parte sobre o Play Protect e os DPCs aprovados).
- Bateria e aquecimento com a captura ligada durante uma aula inteira.
- O que cada fabricante faz com o pedido do "Iniciar agora" quando a tela apaga ou o aluno vai para o início (no Android 10 a 12 o pedido pode ficar aberto; o agente agora o fecha como sem resposta quando o aluno sai, mas isso só foi visto no Android 13 do emulador).
- No Android 14 ou mais novo, se o bloqueio da tela para a captura (pede de novo ao liberar).
