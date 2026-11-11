# Agente Android, plano 1 de 4: base (o celular aparece, pareia e fica online) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Um celular restaurado e preparado pelo QR code do Midview vira "aparelho da escola" (Device Owner), aparece na busca do professor, pareia pelo código de 6 dígitos e fica online no filtro "Celulares", com quadradinho em pé.

**Architecture:** App Android nativo em Kotlin (`src/Midview.Android/`, Gradle próprio, fora da solução .NET) que fala o protocolo que já existe: responde à busca UDP `28901`, aceita TLS mútuo nas portas `28900` (comandos e pareamento) e `5901` (miniatura) com um certificado próprio autoassinado e confia no professor pelo thumbprint fixado. Neste plano a miniatura é uma imagem de status gerada pelo agente (a captura real vem no plano 2), o que já dá ao Master o "online" sem mudar o `ThumbnailPoller`. No Master entram o tipo do aparelho (`DeviceKind`), o filtro Todos/Computadores/Celulares, o quadradinho do celular, os botões apagados para celular e a janela "Preparar celular" com o QR.

**Tech Stack:** Kotlin 2.x, Android Gradle Plugin 8.x, `minSdk 29` / `targetSdk 35`, JUnit 4 (testes JVM), BouncyCastle `bcpkix-jdk18on` (certificado autoassinado), Gradle Wrapper; .NET 8 + WPF + xUnit no Master; `QRCoder` (MIT) para o QR; GitHub Actions.

**Spec:** `docs/superpowers/specs/2026-11-11-agente-android-design.md`

**Os quatro planos:** 1 base (este), 2 tela (bloquear e ver), 3 apps e sites, 4 otimização e atualização do agente. Cada um é escrito depois que o anterior estiver pronto e testado num celular de verdade.

## Global Constraints

- Android 10 ou mais novo: `minSdk 29`.
- Pacote do app: `io.github.arnoneto1.midview.agent`; receptor de admin: `io.github.arnoneto1.midview.agent.AdminReceiver`.
- Portas e versões iguais às do Windows: busca `28901` (`Discovery.Port`, `Discovery.Version = 1`), comandos `28900` (`ConnectionClient.AgentPort`), miniatura `5901` (`ConnectionClient.SessionPort`).
- Quadro: `int32` big-endian com o tamanho de (tipo + payload), 1 byte de tipo, payload; tamanho máximo `16 * 1024 * 1024`; uma escrita por mensagem (`MessageFramer`).
- Thumbprint = SHA-1 do certificado DER, hexadecimal maiúsculo (o `X509Certificate2.Thumbprint` do .NET).
- Certificado do agente: RSA 2048, SHA-256, `CN=<nome do aparelho>`, gerado uma vez e guardado no aparelho.
- Código de pareamento: `SHA-256("Midview pairing v1" + masterThumbprint + agentThumbprint + agentNonce + masterNonce)`, primeiros 4 bytes big-endian `% 1_000_000`, mostrado como `"000 000"`; nonce de 32 bytes; prazo de confirmação 5 minutos.
- Provisionamento com `PROVISIONING_LEAVE_ALL_SYSTEM_APPS_ENABLED = true`, Wi-Fi WPA com senha, APK baixado de `https://github.com/ArnoNeto1/midview/releases/latest/download/MidviewAgent.apk`.
- Todo APK assinado com a **mesma chave** (segredo do CI `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD`, `ANDROID_KEY_ALIAS`); a chave nunca entra no repositório.
- Professor de versão antiga não pode quebrar com um celular na rede: campos novos são opcionais e ignorados por quem não os conhece.
- Textos para professor e aluno em português, no tom do Midview. Commits sem `Co-Authored-By` (regra do `CLAUDE.md`).

## Review Focus

- Celular que pareia mas ainda não tem tela liberada nem nada a mostrar: o quadradinho fica **online** com a imagem de status, não "sem resposta". (Task 6)
- Master antigo (sem `kind`) lendo a resposta da busca de um celular, e Master novo lendo `known-machines.json` antigo: nada quebra, tudo antigo vira computador. (Tasks 1 e 2)
- O `ThumbnailPoller` manda ao agente do celular comandos de PC (MAC, agenda de energia, bloqueio de programas, pasta da turma): o celular responde recusando e o Master não marca o celular como sem resposta por isso; melhor ainda, não manda. (Tasks 3 e 6)
- Wi-Fi com senha que tenha aspas, acentos ou `;`: o QR continua válido. (Task 8)
- Professor não confiável tentando comandos na `28900` sem parear: o agente recusa tudo que não for pareamento. (Task 5)

---

### Task 1: `DeviceKind` no protocolo (busca e pareamento)

**Files:**
- Create: `src/Midview.Protocol/DeviceKind.cs`
- Modify: `src/Midview.Protocol/Discovery.cs` (registro `DiscoveryReply`, `ReplyJson`)
- Modify: `src/Midview.Protocol/Pairing.cs` (registro `PairingResult`)
- Test: `tests/Midview.Protocol.Tests/DiscoveryTests.cs`, `tests/Midview.Protocol.Tests/PairingTests.cs`

**Interfaces:**
- Produces: `public enum DeviceKind { Windows = 0, Android = 1 }`; `DeviceKindText.ToWire(DeviceKind) -> string` (`"windows"`/`"android"`) e `DeviceKindText.FromWire(string?) -> DeviceKind` (nulo, vazio ou desconhecido → `Windows`); `DiscoveryReply(..., DeviceKind Kind = DeviceKind.Windows)`; `PairingResult(..., [property: JsonPropertyName("kind")] string? Kind = null)` com `PairingResult.DeviceKind => DeviceKindText.FromWire(Kind)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Reply_without_kind_is_a_windows_machine() // JSON de hoje, sem "kind"
    => Assert.Equal(DeviceKind.Windows, Discovery.ParseReply(OldReplyBytes)!.Kind);
[Fact] public void Reply_round_trips_android_kind() {
    var reply = new DiscoveryReply(Guid.NewGuid(), "Cel 01", "ABCD", TeacherStatus.None, false, DeviceKind.Android);
    Assert.Equal(DeviceKind.Android, Discovery.ParseReply(Discovery.Encode(reply))!.Kind);
    Assert.Contains("\"kind\":\"android\"", Encoding.UTF8.GetString(Discovery.Encode(reply)));
}
[Fact] public void Reply_with_unknown_kind_is_windows() // "kind":"tv"
[Fact] public void PairingResult_without_kind_is_windows()
[Fact] public void PairingResult_round_trips_android()
```

- [ ] **Step 2:** Run `dotnet test tests/Midview.Protocol.Tests --filter "FullyQualifiedName~DiscoveryTests|FullyQualifiedName~PairingTests"`. Expected: FAIL (compilação: `DeviceKind` não existe).
- [ ] **Step 3:** Implement. `ReplyJson` ganha `[property: JsonPropertyName("kind"), JsonIgnore(Condition = WhenWritingNull)] string? Kind = null`; o PC continua sem mandar `kind` (escreve só quando for `Android`).
- [ ] **Step 4:** Run the same command. Expected: PASS, e todo `tests/Midview.Protocol.Tests` verde.
- [ ] **Step 5:** Commit `feat(protocol): tipo do aparelho na busca e no pareamento`.

### Task 2: O Master guarda o tipo do aparelho

**Files:**
- Modify: `src/Midview.Master/KnownMachine.cs`, `src/Midview.Master/DiscoveryClient.cs` (`FoundMachine`, `Merge`), `src/Midview.Master/PairingSession.cs:76-85` (`Confirm`)
- Test: `tests/Midview.Master.Tests/KnownMachinesFileTests.cs`, `DiscoveryClientTests.cs`, `PairingSessionTests.cs`

**Interfaces:**
- Consumes: Task 1.
- Produces: `KnownMachine(..., string? MacAddress = null, DeviceKind Kind = DeviceKind.Windows)` com `bool IsPhone => Kind == DeviceKind.Android`; `FoundMachine(..., DeviceKind Kind = DeviceKind.Windows)`; `PairingSession.Confirm()` devolve `Kind = result.DeviceKind`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Old_roster_without_kind_loads_as_computers() // JSON de hoje → Kind == Windows
[Fact] public void Phone_kind_survives_save_and_load()
[Fact] public void Merge_keeps_the_kind_of_the_answer()
[Fact] public void Confirm_keeps_android_kind_from_PairDone() // usar o agente roteirizado que PairingSessionTests já usa
[Fact] public void IsSameEntryAs_ignores_nothing_but_mac() // mesmo nome/host/thumbprints, Kind diferente → false
```

- [ ] **Step 2:** Run `dotnet test tests/Midview.Master.Tests --filter "FullyQualifiedName~KnownMachinesFile|FullyQualifiedName~DiscoveryClient|FullyQualifiedName~PairingSession"`. Expected: FAIL.
- [ ] **Step 3:** Implement. `Kind` serializa como número no JSON do roster (padrão do `System.Text.Json`); ausente → `Windows` pelo valor padrão do parâmetro.
- [ ] **Step 4:** Same command. Expected: PASS; depois `dotnet test` da solução inteira verde.
- [ ] **Step 5:** Commit `feat(master): lembrar se a máquina é celular`.

### Task 3: Filtro Todos / Computadores / Celulares e o que o celular aceita

**Files:**
- Modify: `src/Midview.Master/GridFilter.cs`, `src/Midview.Master/GridViewFile.cs` (`GridView`)
- Create: `src/Midview.Master/DeviceSupport.cs`
- Modify: `src/Midview.Master/MainWindow.xaml` (três botões de alternância acima da grade, ao lado de "Só online"), `src/Midview.Master/MainWindow.xaml.cs` (filtro em `_shownTiles.Filter`, salvar a escolha, habilitar botões)
- Modify: `src/Midview.Master/ThumbnailPoller.cs:155-160` (não chamar os passos de PC para celular)
- Test: `tests/Midview.Master.Tests/GridFilterTests.cs`, `GridViewFileTests.cs`, create `DeviceSupportTests.cs`, `ThumbnailPollerTests.cs`

**Interfaces:**
- Consumes: `KnownMachine.IsPhone` (Task 2).
- Produces:
  - `public enum DeviceFilter { All = 0, Computers = 1, Phones = 2 }`; `GridView(..., DeviceFilter Devices = DeviceFilter.All)`.
  - `GridFilter.Shows(MachineTile tile, bool onlineOnly, DeviceFilter devices) -> bool`; `GridFilter.EmptyNotice(IReadOnlyCollection<MachineTile>, bool onlineOnly, DeviceFilter devices) -> string?`.
  - `public enum ToolbarAction { View, Control, ScreenLock, Power, Wake, Schedule, Message, Chat, SendFile, CollectFiles, CloseApps, BlockPrograms, BlockSites, OpenWebsite, OpenProgram, Install, Uninstall, LabConfig, ClassFolder, Update }`.
  - `DeviceSupport.Supports(DeviceKind kind, ToolbarAction action) -> bool` (neste plano, `Android` não suporta nenhuma; os planos 2 a 4 ligam `View`, `ScreenLock`, `BlockPrograms`, `BlockSites`, `Update`).
  - `DeviceSupport.IsEnabled(IReadOnlyCollection<KnownMachine> selected, ToolbarAction action) -> bool` (verdadeiro se algum selecionado suporta).
  - `DeviceSupport.LeftOutNotice(IReadOnlyCollection<KnownMachine> targets, ToolbarAction action) -> string?` (nulo se ninguém fica de fora).

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Phones_filter_hides_computers()
[Fact] public void Computers_filter_hides_phones()
[Fact] public void Empty_notice_when_filter_leaves_no_phones()
    => Assert.Equal("Nenhum celular nesta sala ainda. Use \"Preparar celular\" para colocar o primeiro.",
                    GridFilter.EmptyNotice(onlyComputers, onlineOnly: false, DeviceFilter.Phones));
[Fact] public void Old_grid_view_file_shows_all_devices()
[Fact] public void Phone_only_selection_disables_pc_actions()
    => Assert.False(DeviceSupport.IsEnabled(new[] { Phone("Cel 01") }, ToolbarAction.Power));
[Fact] public void Mixed_selection_warns_about_phones_left_out()
    => Assert.Equal("2 celulares ficaram de fora: essa ação ainda não funciona em celular.",
                    DeviceSupport.LeftOutNotice(new[] { Pc("PC 01"), Phone("Cel 01"), Phone("Cel 02") }, ToolbarAction.Power));
[Fact] public void One_phone_left_out_uses_singular() // "1 celular ficou de fora: essa ação ainda não funciona em celular."
[Fact] public void Poller_skips_pc_steps_for_phones() // após a miniatura de um celular, nenhuma conexão à porta do agente
```

- [ ] **Step 2:** Run `dotnet test tests/Midview.Master.Tests --filter "FullyQualifiedName~GridFilter|FullyQualifiedName~GridViewFile|FullyQualifiedName~DeviceSupport|FullyQualifiedName~ThumbnailPoller"`. Expected: FAIL.
- [ ] **Step 3:** Implement. Na `MainWindow`, os botões de ação passam por `DeviceSupport.IsEnabled` com a seleção atual (ou a turma inteira quando nada está selecionado, como hoje), e cada ação que age em vários alvos mostra `LeftOutNotice` na faixa de baixo antes de agir. Os três botões do filtro usam as cores e o estilo de alternância do "Só online" (visual Painel). No `ThumbnailPoller`, os passos depois da miniatura (`AskForMacAddressOnce` … `ObserveAppBlock`) só rodam quando `!_machine.IsPhone`.
- [ ] **Step 4:** Same command. Expected: PASS; solução inteira verde.
- [ ] **Step 5:** Commit `feat(master): filtro de celulares e ações por tipo de aparelho`.

### Task 4: Quadradinho do celular

**Files:**
- Modify: `src/Midview.Master/MachineTile.cs` (expor `IsPhone`), `src/Midview.Master/TileSize.cs`, `src/Midview.Master/MainWindow.xaml` (modelo do quadradinho), `src/Midview.Master/MainWindow.TileLayout.cs`
- Test: `tests/Midview.Master.Tests/TileSizeTests.cs`, `MachineTileTests.cs`

**Interfaces:**
- Consumes: `KnownMachine.IsPhone`.
- Produces: `TileSize.PhoneWidthOf(int width) -> int` = `round(TileSize.HeightOf(width) * 9 / 19.5)` (mesma altura do quadradinho de PC, em pé); `TileSize.PhoneRequest(int width, double dpiScale) -> (int Width, int Height)`, como `TileSize.Request` mas com a caixa em pé e dentro de `ThumbnailRequest.MaxWidth`/`MaxHeight`. O `_thumbnailSize` do poller de um celular usa `PhoneRequest`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Phone_tile_is_portrait_with_the_pc_tile_height()
    { var w = TileSize.Default; Assert.True(TileSize.PhoneWidthOf(w) < TileSize.HeightOf(w)); }
[Fact] public void Phone_request_never_exceeds_thumbnail_request_limits() // de TileSize.Smallest a Largest, dpi 1.0 e 1.5
[Fact] public void Tile_reports_phone()
```

- [ ] **Step 2:** Run `dotnet test tests/Midview.Master.Tests --filter "FullyQualifiedName~TileSize|FullyQualifiedName~MachineTile"`. Expected: FAIL.
- [ ] **Step 3:** Implement. No XAML, o quadradinho do celular tem cantos de raio 12 (PC: o raio atual), o mesmo ponto verde/cinza e o mesmo cadeado; "Organizar como a sala" trata o celular como uma célula estreita.
- [ ] **Step 4:** Same command. Expected: PASS. Abrir o Master com um `known-machines.json` de teste contendo um celular e conferir à vista.
- [ ] **Step 5:** Commit `feat(master): quadradinho em pé para celular`.

### Task 5: Projeto Android, identidade e protocolo básico

**Files:**
- Create: `src/Midview.Android/` (Gradle Wrapper, `settings.gradle.kts`, `build.gradle.kts`, `app/build.gradle.kts`, `app/src/main/AndroidManifest.xml`)
- Create: `app/src/main/java/io/github/arnoneto1/midview/agent/protocol/MessageFramer.kt`, `MessageType.kt` (os valores de `src/Midview.Protocol/MessageType.cs` usados aqui: `Heartbeat=2`, `CommandAck=11`, `RequestThumbnail=12`, `ThumbnailFrame=13`, `PairRequest=37` … `PairRefused=44`, `AllowNewTeacher=45`), `Discovery.kt`, `Pairing.kt`
- Create: `.../identity/AgentIdentity.kt` (certificado e chave em PKCS12 em `filesDir`), `.../identity/TrustedMasters.kt` (lista de thumbprints em arquivo)
- Test: `app/src/test/java/.../protocol/MessageFramerTest.kt`, `DiscoveryTest.kt`, `PairingTest.kt`, `identity/AgentIdentityTest.kt`
- Modify: `.gitignore` (`src/Midview.Android/**/build/`, `.gradle/`, `local.properties`, `*.jks`, `*.keystore`)

**Interfaces:**
- Produces (Kotlin):
  - `object MessageFramer { const val MAX = 16 * 1024 * 1024; fun write(out: OutputStream, m: Message); fun read(input: InputStream, maxLength: Int = MAX): Message }`, `data class Message(val type: Int, val payload: ByteArray)`.
  - `object Discovery { const val PORT = 28901; fun parseProbe(packet: ByteArray): Probe?; fun encodeReply(r: Reply): ByteArray }` com `Reply(probeId, name, agentThumbprint, teachers, newTeacherOpen)` sempre com `"kind":"android"`.
  - `object Pairing { fun code(masterThumb: String, agentThumb: String, agentNonce: ByteArray, masterNonce: ByteArray): Int; fun format(code: Int): String; fun commit(nonce: ByteArray): ByteArray; fun opens(commitment: ByteArray, nonce: ByteArray): Boolean; fun cleanTeacherName(name: String): String }`.
  - `class AgentIdentity(dir: File, deviceName: String) { val certificate: X509Certificate; val privateKey: PrivateKey; val thumbprint: String }` (cria uma vez, reusa depois).
  - `class TrustedMasters(file: File) { fun isTrusted(thumb: String): Boolean; fun add(thumb: String); fun count(): Int }`.

- [ ] **Step 1: Instalar as ferramentas.** JDK 17 (Temurin) e Android SDK command-line tools com `platforms;android-35`, `build-tools;35.0.0`, `platform-tools`. Run `java -version` e `sdkmanager --list_installed`. Expected: Java 17 e as três entradas.
- [ ] **Step 2: Write the failing tests**

```kotlin
@Test fun pairingCodeMatchesDotNet() {
    val m = "0123456789ABCDEF0123456789ABCDEF01234567"; val a = "89ABCDEF0123456789ABCDEF0123456789ABCDEF"
    val an = ByteArray(32) { it.toByte() }; val mn = ByteArray(32) { (it + 32).toByte() }
    assertEquals(602288, Pairing.code(m, a, an, mn)); assertEquals("602 288", Pairing.format(602288))
}
@Test fun commitIsSha256() // commit(0..31) == "630dcd2966c4336691125448bbb25b4ff412a49c732db2c8abc1b8581bd710dd"
@Test fun framerWritesLengthTypePayloadInOneFrame() // [0,0,0,3, 11, 1, 2] para Message(11, [1,2])
@Test fun framerRefusesZeroAndOversizedLength()
@Test fun probeFromDotNetIsRead() // {"midview":1,"probe":"<guid>","master":"ABC"}
@Test fun probeWithAgentFieldIsIgnored()
@Test fun replyHasAndroidKindAndDotNetFields() // midview, probe, name, agent, teachers, newTeacherOpen, kind
@Test fun identityIsCreatedOnceAndReused() // dois AgentIdentity no mesmo dir → mesmo thumbprint
@Test fun thumbprintIsUppercaseSha1OfDer()
```

Acrescentar em `tests/Midview.Protocol.Tests/PairingTests.cs` o mesmo vetor (`Pairing.Code(...) == 602288`), para os dois lados ficarem presos ao mesmo número.

- [ ] **Step 3:** Run `src/Midview.Android/gradlew -p src/Midview.Android testDebugUnitTest` e `dotnet test tests/Midview.Protocol.Tests --filter FullyQualifiedName~PairingTests`. Expected: Kotlin FAIL (classes não existem); .NET PASS (o vetor já vale no C#).
- [ ] **Step 4:** Implement. JSON com `org.json` (vem no Android; nos testes JVM, `testImplementation("org.json:json:20240303")`). Certificado com BouncyCastle `JcaX509v3CertificateBuilder`, validade de 20 anos.
- [ ] **Step 5:** Same Gradle command. Expected: PASS.
- [ ] **Step 6:** Commit `feat(android): projeto, identidade e protocolo básico`.

### Task 6: Agente no ar: busca, TLS, pareamento e miniatura de status

**Files:**
- Create: `.../agent/AgentService.kt` (serviço em primeiro plano, inicia no boot), `.../net/DiscoveryResponder.kt`, `.../net/TlsServer.kt`, `.../net/CommandHandler.kt` (porta 28900), `.../net/ThumbnailHandler.kt` (porta 5901), `.../pairing/PairingFlow.kt`, `.../ui/PairingCodeActivity.kt` (código em tela cheia), `.../status/StatusImage.kt`
- Test: `app/src/test/java/.../pairing/PairingFlowTest.kt`, `net/CommandHandlerTest.kt`, `status/StatusImageTest.kt` (Robolectric só se preciso para o `Bitmap`)
- Create: `tests/Midview.E2E/AndroidAgentInteropTests.cs` (marcado para rodar só com a variável `MIDVIEW_ANDROID_HOST` definida)

**Interfaces:**
- Consumes: Task 5.
- Produces:
  - `class PairingFlow(identity: AgentIdentity, trusted: TrustedMasters, showCode: (String, String) -> Unit, hideCode: () -> Unit)` com `fun run(channel: Channel)` seguindo a ordem do `Pairing.cs`: `PairRequest` → `PairCommit` → `PairNonce` → `PairReveal` → `PairConfirm`/`PairCancel` → `PairDone`; `PairDone` leva `PairingResult(name, session = identity.thumbprint, mac = null, kind = "android")`.
  - Regra de quem pode parear (igual ao PC): ninguém confiável ainda → qualquer professor; já há professor → só dentro da janela de 10 minutos aberta por `AllowNewTeacher` de um professor confiável; fora disso `PairRefused` com "Este celular já tem professor. Peça para ele usar \"Permitir novo professor\"."
  - `CommandHandler`: de professor não confiável, só mensagens de pareamento; qualquer outra → fecha a conexão. De professor confiável: `Heartbeat` → `Heartbeat`; `AllowNewTeacher` → abre a janela; tipo desconhecido → `CommandAck` com o `CommandResult` de falha "Ainda não funciona em celular." (formato de `src/Midview.Protocol/CommandResult.cs`).
  - `ThumbnailHandler`: só professor confiável (`TrustStore` = `TrustedMasters`); a cada `RequestThumbnail` responde `ThumbnailFrame` com o JPEG de `StatusImage.render(width, height, deviceName, "Online. A tela aparece aqui depois do plano 2.")` na caixa pedida (`ThumbnailRequest.Decode`).

- [ ] **Step 1: Write the failing tests**

```kotlin
@Test fun firstTeacherPairsAndIsTrusted() // roteiro completo com nonces fixos; código mostrado == Pairing.code(...)
@Test fun secondTeacherRefusedWithoutAllowNewTeacher()
@Test fun secondTeacherPairsInsideTheTenMinuteWindow()
@Test fun cancelLeavesNothingTrusted()
@Test fun commitThatDoesNotOpenIsRefused()
@Test fun untrustedTeacherCannotSendCommands() // Heartbeat de não confiável → conexão fechada
@Test fun unknownCommandGetsNotOnPhoneAck()
@Test fun statusImageFitsTheRequestedBox() // 320x180 pedido → JPEG cabe na caixa, mantém retrato
```

```csharp
[Fact] public void Master_pairs_with_real_android_agent() // PairingSession.Start(host) → Confirm() → Kind == Android
[Fact] public void Poller_sees_android_agent_online()    // ThumbnailPoller recebe um JPEG válido em até 10 s
```

- [ ] **Step 2:** Run `gradlew -p src/Midview.Android testDebugUnitTest`. Expected: FAIL.
- [ ] **Step 3:** Implement. `TlsServer` usa `SSLContext` TLS com `KeyManager` do `AgentIdentity`, `needClientAuth = true` e um `TrustManager` que aceita qualquer certificado na negociação; quem decide é o thumbprint do certificado do professor depois do handshake (como `AuthenticateAsServerAcceptingAnyClient`). `AgentService` sobe os três ouvintes, mostra a notificação fixa "Celular da escola, gerenciado pelo Midview" e reinicia no `BOOT_COMPLETED`.
- [ ] **Step 4:** Same Gradle command. Expected: PASS.
- [ ] **Step 5:** Instalar o APK de depuração num emulador ou no Redmi 10C (`adb install`), rodar `dotnet test tests/Midview.E2E --filter FullyQualifiedName~AndroidAgentInterop` com `MIDVIEW_ANDROID_HOST=<ip>`. Expected: PASS. Depois, no Master, "Procurar e parear" encontra o celular, os dois códigos batem, e o quadradinho fica online no filtro "Celulares".
- [ ] **Step 6:** Commit `feat(android): busca, pareamento e miniatura de status`.

### Task 7: Aparelho da escola (Device Owner) e travas

**Files:**
- Create: `.../AdminReceiver.kt`, `.../provisioning/ProvisioningModeActivity.kt` (`GET_PROVISIONING_MODE` → modo totalmente gerenciado), `.../provisioning/PolicyComplianceActivity.kt` (`ADMIN_POLICY_COMPLIANCE`), `.../policy/Lockdown.kt`, `app/src/main/res/xml/device_admin.xml`
- Test: `app/src/test/java/.../policy/LockdownTest.kt` (com um `DevicePolicyGateway` falso)

**Interfaces:**
- Produces: `interface DevicePolicyGateway { fun addUserRestriction(r: String); fun setUninstallBlocked(pkg: String, blocked: Boolean); fun setUserControlDisabled(pkgs: List<String>); fun isDeviceOwner(): Boolean; val sdkInt: Int }`; `class Lockdown(gw: DevicePolicyGateway) { fun apply() }` que aplica `DISALLOW_FACTORY_RESET`, `DISALLOW_DEBUGGING_FEATURES`, `DISALLOW_SAFE_BOOT`, `DISALLOW_ADD_USER`, `DISALLOW_USB_FILE_TRANSFER`, `setUninstallBlocked(<próprio pacote>, true)` e, no Android 11 ou mais novo (`sdkInt >= 30`), `setUserControlDisabledPackages([<próprio pacote>])` para o aluno não forçar parada nem limpar os dados; nada se não for Device Owner.

- [ ] **Step 1: Write the failing tests**

```kotlin
@Test fun appliesAllRestrictionsWhenDeviceOwner()
@Test fun blocksForceStopOnlyFromAndroid11() // sdkInt 29 → não chama; 30 → chama com o próprio pacote
@Test fun doesNothingWhenNotDeviceOwner()
@Test fun applyTwiceIsHarmless()
```

- [ ] **Step 2:** Run `gradlew -p src/Midview.Android testDebugUnitTest`. Expected: FAIL.
- [ ] **Step 3:** Implement. `AdminReceiver.onProfileProvisioningComplete` e a `PolicyComplianceActivity` chamam `Lockdown.apply()` e iniciam o `AgentService`. O serviço também chama `Lockdown.apply()` a cada início.
- [ ] **Step 4:** Same command. Expected: PASS. No emulador limpo: `adb shell dpm set-device-owner io.github.arnoneto1.midview.agent/.AdminReceiver`, depois conferir que "Restaurar" e "Opções do desenvolvedor" ficam indisponíveis e que o app não desinstala.
- [ ] **Step 5:** Commit `feat(android): aparelho da escola com travas`.

### Task 8: "Preparar celular" no Master (QR code)

**Files:**
- Create: `src/Midview.Master/PhoneProvisioning.cs`, `src/Midview.Master/PreparePhoneWindow.xaml(.cs)`, `src/Midview.Master/PhoneWifiFile.cs` (SSID e senha do laboratório, protegidos com DPAPI do usuário, como `AdminPasswordStore`)
- Modify: `src/Midview.Master/Midview.Master.csproj` (`QRCoder`), `THIRD-PARTY-NOTICES.txt`, menu Configurar na `MainWindow.xaml`
- Test: create `tests/Midview.Master.Tests/PhoneProvisioningTests.cs`, `PhoneWifiFileTests.cs`

**Interfaces:**
- Produces: `PhoneProvisioning.QrJson(string ssid, string password) -> string` com exatamente estas chaves:
  - `android.app.extra.PROVISIONING_DEVICE_ADMIN_COMPONENT_NAME` = `io.github.arnoneto1.midview.agent/.AdminReceiver`
  - `android.app.extra.PROVISIONING_DEVICE_ADMIN_PACKAGE_DOWNLOAD_LOCATION` = a URL do Global Constraints
  - `android.app.extra.PROVISIONING_DEVICE_ADMIN_SIGNATURE_CHECKSUM` = `PhoneProvisioning.SignatureChecksum` (constante: SHA-256 do certificado de assinatura, Base64 URL-safe sem `=`; preenchida na Task 9)
  - `android.app.extra.PROVISIONING_WIFI_SSID`, `..._WIFI_PASSWORD`, `..._WIFI_SECURITY_TYPE` = `"WPA"`
  - `android.app.extra.PROVISIONING_LEAVE_ALL_SYSTEM_APPS_ENABLED` = `true`
- `PhoneWifiFile.Load() -> (string Ssid, string Password)?`, `PhoneWifiFile.Save(string ssid, string password)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Qr_json_has_every_provisioning_key()
[Fact] public void Password_with_quotes_accents_and_semicolon_round_trips() // "a\"b;ç" → JSON válido, valor igual
[Fact] public void Empty_ssid_is_refused() // ArgumentException
[Fact] public void Wifi_file_round_trips_and_is_not_plain_text()
```

- [ ] **Step 2:** Run `dotnet test tests/Midview.Master.Tests --filter "FullyQualifiedName~PhoneProvisioning|FullyQualifiedName~PhoneWifiFile"`. Expected: FAIL.
- [ ] **Step 3:** Implement. A janela "Preparar celular" (visual Painel, grupo Configurar) pede o Wi-Fi uma vez, mostra o QR grande e os passos: 1. Nos Xiaomi, saia da conta Mi (Configurações → Conta Mi → Sair). 2. Restaure o celular de fábrica. 3. Na primeira tela, toque 6 vezes no fundo branco. 4. Escaneie este código. 5. Quando o celular terminar, use "Procurar e parear".
- [ ] **Step 4:** Same command. Expected: PASS.
- [ ] **Step 5:** Commit `feat(master): preparar celular por QR code`.

### Task 9: APK assinado no GitHub e prova no celular de verdade

**Files:**
- Create: `.github/workflows/android.yml` (testes JVM em todo push; em tag `v*`, gera `MidviewAgent.apk` assinado e anexa à versão do GitHub junto com o `MidviewSetup.exe`)
- Modify: `src/Midview.Android/app/build.gradle.kts` (`versionName` lido de `Directory.Build.props`, assinatura pelos segredos), `src/Midview.Master/PhoneProvisioning.cs` (`SignatureChecksum`), `README.md` (seção "Celulares")
- Test: o próprio fluxo e o roteiro manual abaixo

- [ ] **Step 1:** Criar a chave uma vez (`keytool -genkeypair -alias midview -keyalg RSA -keysize 4096 -validity 36500 -keystore midview-android.jks`), guardar o `.jks` fora do repositório e cadastrar os três segredos no GitHub. **Perder essa chave impede atualizar os celulares já preparados**; guarde uma cópia em lugar seguro.
- [ ] **Step 2:** Calcular o checksum (`keytool -printcert -jarfile MidviewAgent.apk`, SHA-256 → Base64 URL-safe sem `=`) e fixar em `PhoneProvisioning.SignatureChecksum`, com um teste que confere o formato (43 caracteres, só `A-Z a-z 0-9 - _`).
- [ ] **Step 3:** Run `dotnet test` (solução) e `gradlew -p src/Midview.Android testDebugUnitTest`. Expected: PASS.
- [ ] **Step 4:** Publicar uma versão de teste (tag `v0.0.51-android.1`) e conferir que `https://github.com/ArnoNeto1/midview/releases/latest/download/MidviewAgent.apk` baixa o APK. Se "latest" apontar para outra versão, ajustar a URL do Global Constraints e a da Task 8.
- [ ] **Step 5: Roteiro no Redmi 10C.** Sair da conta Mi → restaurar → 6 toques → escanear o QR do Master → o celular baixa, instala e vira aparelho da escola → "Procurar e parear" acha "Cel" com `kind` android → códigos batem → quadradinho em pé e online no filtro "Celulares" → reiniciar o celular → volta online sozinho → tentar "Restaurar" e "Opções do desenvolvedor" no celular → bloqueados. Anotar o resultado no `README.md`.
- [ ] **Step 6:** Commit `ci(android): APK assinado na versão do GitHub`.
