# Assistente "Preparar celulares" Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Qualquer professor prepara sozinho os celulares Xiaomi da escola por um assistente de um passo por vez no Midview do professor; cada celular escaneia um QR com o seu número, entra na turma sozinho como "Celular 07" (sem código de 6 dígitos) e pode ser apagado e preparado de novo por um botão.

**Architecture:** O QR de provisionamento ganha um `PROVISIONING_ADMIN_EXTRAS_BUNDLE` com o número, um segredo de uso único (`token`) e o thumbprint do PC. O agente guarda isso, se chama "Celular 07" e, enquanto ninguém o reivindicou, anuncia na busca um `claim` (hash curto do token). O Master guarda as reivindicações pendentes (DPAPI, 1 hora) e, enquanto houver alguma, procura na rede a cada 10 s; ao achar o `claim`, abre o TLS e manda `ClaimPhone` com um **nonce** novo (o token nunca trafega). O celular só aceita o `ClaimPhone` do PC cujo thumbprint veio no QR e responde `PairDone` com uma **prova**: `proof = Base64Url(HMAC-SHA256(chave = token, nonce + thumbprint do agente + thumbprint do Master))`, que só quem tem o token faz para esta conexão. O Master confere a prova (comparação de tempo fixo) com o thumbprint do canal e o seu próprio antes de aceitar, e dá ao celular o nome do número da reivindicação, nunca o nome que o celular disser (Ruling F e L do ledger). `WipePhone` (só de PC confiável) apaga o aparelho pela política de Device Owner. A janela nova `PreparePhonesWindow` (páginas, como `MachinesWindow`) mostra o roteiro de `PhoneSetupGuide` com os prints e substitui a `PreparePhoneWindow`.

**Tech Stack:** .NET 8 + WPF + xUnit (Master, Protocol); Kotlin + JUnit 4 (agente, `src/Midview.Android`); `QRCoder` (já usado).

**Spec:** `docs/superpowers/specs/2026-11-11-assistente-preparar-celulares-design.md`

**Execução escolhida pelo professor:** uma tarefa por vez (superpowers:subagent-driven-development, revisor a cada tarefa).

**Ordem:** começa só depois de o plano 1 do agente estar completo (inclusive Task 9B: chave, checksum real, APK publicado) e de o plano 2 (`2026-11-11-agente-android-2-*`) não estar mais editando o agente: as Tasks 4 e 5 mexem em `CommandHandler`, `DiscoveryResponder`, `AgentService`, `AdminReceiver` e nos `provisioning/*`. As Tasks 1–3 e 6–8 só tocam .NET e podem começar antes.

## Global Constraints

- Prova do QR válida por **1 hora** e **uma vez**; o Master espera **15 minutos** antes de "ainda não apareceu"; busca de reivindicações a cada **10 s** enquanto houver pendentes.
- Nome do celular preparado: `"Celular " + número`, número só com dígitos (1 a 3), com zero à esquerda até 2 dígitos (`7` → `07`, `12` → `12`, `105` → `105`).
- Chaves do pacote de extras (todas string): `midview.number`, `midview.token`, `midview.master`.
- `token`: 32 bytes aleatórios em Base64 URL sem `=`; `claim` na busca = 16 primeiros caracteres hex maiúsculos de `SHA-256(UTF-8 do token)`.
- Mensagens novas: `ClaimPhone` e `WipePhone`, com os **dois próximos números livres** de `MessageType` no momento da implementação, iguais em `src/Midview.Protocol/MessageType.cs` e `.../agent/protocol/MessageType.kt` (o plano 2 também acrescenta números; conferir os dois arquivos).
- Campos novos são opcionais: Master antigo ignora `claim`; agente antigo responde `CommandAck` com `NOT_ON_PHONE`/desconhecido a `ClaimPhone`/`WipePhone`, e o Master trata como "Midview antigo neste celular".
- `PhoneProvisioning.WizardReady` (= `IsReady` **e** `ClaimsReady`) falso esconde o botão, o link e o item "Apagar e preparar de novo". `ClaimsReady` é uma constante que nasce `false`; as Tasks 4/5 a viram para `true` quando o APK do agente com reivindicação e apagar for publicado (Ruling J).
- Textos em português, sem jargão (nada de Device Owner, APK, provisionamento, adb para o professor); nunca citar produto concorrente. Commits sem `Co-Authored-By`.
- .NET pelo SDK em `C:\Users\Neto\AppData\Local\Microsoft\dotnet\dotnet.exe`; Android por `src/Midview.Android/gradlew -p src/Midview.Android testDebugUnitTest`.
- Verificação visual só pelo harness do Master (`MasterData.Folder` temporário, `new Application()` com os recursos do `App.xaml`, nunca `new App()`, `RenderTargetBitmap`); nunca abrir o Midview do professor real nem falar com máquinas do laboratório.

## Review Focus

- Dois celulares preparados com o mesmo número (o professor errou ou refez o 07): o segundo a entrar substitui o primeiro na turma, sem duas entradas "Celular 07". (Task 3)
- Assistente fechado, Midview reiniciado ou 15 minutos passados antes de o celular aparecer: o celular entra sozinho mesmo assim enquanto a prova vale; depois de 1 hora, a prova some e o pareamento por código continua possível. (Tasks 2 e 3)
- Um PC que fotografou o QR (ou um aparelho que copiou o `claim`) tenta reivindicar: o celular recusa thumbprint diferente do QR e token errado; o token não vale de novo depois do primeiro uso. (Tasks 3 e 4)
- `WipePhone` vindo de PC não confiável, ou celular com agente antigo: nada é apagado e o professor lê o motivo. (Tasks 5 e 8)
- Celular que mostra a tela do Midview mas não aparece no PC (outra rede): o assistente diz que é rede, não manda restaurar. (Task 7)

---

### Task 1: Protocolo da reivindicação (`PhoneClaim`, `claim` na busca, mensagens novas)

**Files:**
- Create: `src/Midview.Protocol/PhoneClaim.cs`
- Modify: `src/Midview.Protocol/Discovery.cs` (`ReplyJson`, `DiscoveryReply`, `Encode`, `ParseReply`)
- Modify: `src/Midview.Protocol/MessageType.cs`
- Test: `tests/Midview.Protocol.Tests/PhoneClaimTests.cs`, `tests/Midview.Protocol.Tests/DiscoveryTests.cs`

**Interfaces:**
- Produces:
  - `MessageType.ClaimPhone`, `MessageType.WipePhone`
  - `static class PhoneClaim { string NewToken(); string NewNonce(); string ClaimId(string token); string Proof(string token, string nonce, string agentThumbprint, string masterThumbprint); }` — `NewNonce()` tem a forma do token (43 caracteres Base64 URL).
  - `sealed record PhoneClaimRequest(string Nonce, string TeacherName) { byte[] Encode(); static PhoneClaimRequest? Decode(byte[] payload); }` — JSON `{"nonce":..,"teacher":..}`, nonce = 43 caracteres Base64 URL; `Decode` devolve null para JSON inválido, nonce que não tem esse formato, professor ausente ou payload maior que 4096 bytes. O token nunca vai nesse pedido.
  - `PairingResult` ganha o campo opcional `"proof"` (só escrito quando não nulo); só um celular que responde `ClaimPhone` o manda.
  - `DiscoveryReply(..., DeviceKind Kind = Windows, string? ClaimId = null)`; JSON `"claim"` só escrito quando não nulo.

- [ ] **Step 1: Testes que falham**

```csharp
[Fact] public void New_tokens_are_43_url_safe_chars_and_differ()
{ var a = PhoneClaim.NewToken(); Assert.Matches("^[A-Za-z0-9_-]{43}$", a); Assert.NotEqual(a, PhoneClaim.NewToken()); }

[Fact] public void Claim_id_is_first_16_upper_hex_of_sha256()
    => Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("abc")))[..16], PhoneClaim.ClaimId("abc"));

[Fact] public void Claim_request_round_trips_and_rejects_a_bad_nonce()
{ var nonce = PhoneClaim.NewNonce();
  Assert.Equal(new PhoneClaimRequest(nonce, "PROF"), PhoneClaimRequest.Decode(new PhoneClaimRequest(nonce, "PROF").Encode()));
  Assert.Null(PhoneClaimRequest.Decode(new PhoneClaimRequest("curto", "PROF").Encode()));
  Assert.Null(PhoneClaimRequest.Decode(Encoding.UTF8.GetBytes("não é json"))); }

[Fact] public void Proof_matches_the_fixed_vector()
{ Assert.Equal("WSiEijnq4JkvauANCY9jxAA-15IWfTZOg00j1DJWdLE", PhoneClaim.Proof("token-123", "nonce-abc", "aa11", "BB22")); }

// DiscoveryTests
[Fact] public void Reply_claim_round_trips_and_is_absent_when_null()
{ var r = new DiscoveryReply(Guid.NewGuid(), "Celular 07", "AB", TeacherStatus.None, false, DeviceKind.Android, "0123456789ABCDEF");
  Assert.Equal("0123456789ABCDEF", Discovery.ParseReply(Discovery.Encode(r))!.ClaimId);
  Assert.DoesNotContain("claim", Encoding.UTF8.GetString(Discovery.Encode(r with { ClaimId = null }))); }
```

- [ ] **Step 2:** `dotnet test tests/Midview.Protocol.Tests --filter "FullyQualifiedName~PhoneClaim|FullyQualifiedName~DiscoveryTests"` → FAIL (tipos não existem).
- [ ] **Step 3:** Implementar `PhoneClaim`, `PhoneClaimRequest`, o campo `claim` e as duas mensagens (comentário de cada uma no estilo do `MessageType.cs`: quem manda, payload, resposta: `ClaimPhone` → `PairDone`/`PairRefused`; `WipePhone` → `CommandAck`).
- [ ] **Step 4:** O mesmo comando → PASS; `dotnet test tests/Midview.Protocol.Tests` inteiro verde.
- [ ] **Step 5:** Commit `feat(protocolo): reivindicação de celular preparado e mensagem de apagar`.

### Task 2: QR com número e prova, reivindicações pendentes guardadas

**Files:**
- Modify: `src/Midview.Master/PhoneProvisioning.cs`
- Create: `src/Midview.Master/PhoneNumber.cs`, `src/Midview.Master/PendingPhoneClaimsFile.cs`
- Test: `tests/Midview.Master.Tests/PhoneProvisioningTests.cs`, `PhoneNumberTests.cs`, `PendingPhoneClaimsFileTests.cs`

**Interfaces:**
- Consumes: `PhoneClaim.NewToken()` (Task 1).
- Produces:
  - `static class PhoneNumber { string? Normalize(string typed); string NameOf(string number); }` — `Normalize` tira espaços, aceita 1 a 3 dígitos, devolve com zero à esquerda até 2 ou null; `NameOf("07")` = `"Celular 07"`.
  - `sealed record PendingPhoneClaim(string Number, string Token, DateTimeOffset ExpiresAt)`
  - `static class PendingPhoneClaimsFile { IReadOnlyList<PendingPhoneClaim> LoadValid(DateTimeOffset now, string? folder = null); void Add(PendingPhoneClaim claim, string? folder = null); void Remove(string token, string? folder = null); }` — DPAPI CurrentUser como `PhoneWifiFile`, arquivo `phone-claims.bin`; `LoadValid` descarta (e regrava sem) as vencidas; `Add` de um número que já está pendente substitui a anterior.
  - `PhoneProvisioning.ClaimLifetime = TimeSpan.FromHours(1)`
  - `PhoneProvisioning.QrJson(string ssid, string password, string number, string token, string masterThumbprint)` — a versão antiga de 2 parâmetros sai (a janela antiga sai na Task 7).

- [ ] **Step 1: Testes que falham**
  - `PhoneNumberTests`: `Normalize("7")=="07"`, `Normalize(" 12 ")=="12"`, `Normalize("105")=="105"`, `Normalize("0")=="00"`; null para `""`, `"1234"`, `"7a"`, `"-1"`.
  - `PhoneProvisioningTests.Qr_carries_number_token_and_master_in_admin_extras`: o JSON tem `android.app.extra.PROVISIONING_ADMIN_EXTRAS_BUNDLE` com exatamente `midview.number="07"`, `midview.token=<token>`, `midview.master=<thumbprint em maiúsculas>`; os campos de hoje continuam.
  - `PendingPhoneClaimsFileTests` (pasta temporária): `Add` + `LoadValid` devolve a pendente; vencida (`ExpiresAt < now`) não volta e some do arquivo; `Add` do mesmo número troca o token; `Remove(token)` tira só aquela; arquivo corrompido → lista vazia, sem exceção.
- [ ] **Step 2:** `dotnet test tests/Midview.Master.Tests --filter "FullyQualifiedName~PhoneNumber|FullyQualifiedName~PhoneProvisioning|FullyQualifiedName~PendingPhoneClaims"` → FAIL.
- [ ] **Step 3:** Implementar. `PendingPhoneClaimsFile` guarda uma lista JSON dentro do blob DPAPI; escrita inteira (temporário + `File.Move` com overwrite).
- [ ] **Step 4:** Mesmo comando → PASS.
- [ ] **Step 5:** Commit `feat(master): QR do celular leva número e prova de uso único`.

### Task 3: Master reivindica sozinho o celular preparado

**Files:**
- Create: `src/Midview.Master/PhoneClaimer.cs`, `src/Midview.Master/PhoneClaimWatcher.cs`
- Modify: `src/Midview.Master/DiscoveryClient.cs` (`FoundMachine` ganha `string? ClaimId = null`, preenchido em `Merge`)
- Modify: `src/Midview.Master/MainWindow.xaml.cs` (cria o watcher; `AddPairedMachine` troca a entrada de mesmo nome)
- Test: `tests/Midview.Master.Tests/PhoneClaimerTests.cs`, `PhoneClaimWatcherTests.cs`, `DiscoveryClientTests.cs`

**Interfaces:**
- Consumes: `PhoneClaimRequest`, `PhoneClaim.NewNonce`/`Proof`, `PairingResult.Proof`, `MessageType.ClaimPhone`, `DiscoveryReply.ClaimId` (Task 1); `PendingPhoneClaimsFile`, `PhoneNumber` (Task 2).
- Produces:
  - `static class PhoneClaimer { KnownMachine Claim(string address, X509Certificate2 masterCertificate, string teacherName, string token, string number, int port = ConnectionClient.AgentPort); }` — TLS como `PairingSession.Start` (`AuthenticateAsClientAcceptingAnyServer`), manda `ClaimPhone` com `PhoneClaim.NewNonce()` (o token fica aqui), espera `PairDone`; recusa (`PairingRefusedException(PairingSession.InvalidAnswer)`) se `PairingResult.Proof` faltar ou diferir de `PhoneClaim.Proof(token, nonce, thumbprint do canal, thumbprint do Master)` (`FixedTimeEquals`); `PairRefused` → `PairingRefusedException(texto)`; `CommandAck` → `PairingRefusedException(PairingSession.OldAgent)`. Devolve `KnownMachine` com o nome `PhoneNumber.NameOf(number)` (nunca o nome do `PairingResult`: um aparelho com o token do 07 não pode se chamar "Celular 03"), `Kind` do `PairingResult` e `AgentThumbprint` do canal.
  - `sealed class PhoneClaimWatcher : IDisposable` — `PhoneClaimWatcher(Func<IReadOnlyList<FoundMachine>> search, Func<FoundMachine, PendingPhoneClaim, KnownMachine> claim, Action<KnownMachine> added, Func<DateTimeOffset> now, string? folder = null)`; `void Kick()` (o assistente chama ao mostrar um QR); `bool CheckOnce()` faz uma rodada: carrega as pendentes válidas, se não houver devolve false sem buscar; para cada `FoundMachine` cujo `ClaimId == PhoneClaim.ClaimId(token)`, chama `claim`; sucesso → `PendingPhoneClaimsFile.Remove(token)` e `added(machine)`; falha → fica pendente para a próxima rodada. Um timer de 10 s chama `CheckOnce` no thread pool; `added` é despachado para a UI pelo `MainWindow`.
  - `event Action<KnownMachine>? PhoneJoined` no `MainWindow` (o assistente da Task 7 escuta).

- [ ] **Step 1: Testes que falham**
  - `PhoneClaimerTests` (com um agente falso no estilo de `HonestAgent` de `PairingSessionTests`): o agente recebe `ClaimPhone` com o nonce (o corpo não contém o token) e o nome do professor e responde `PairDone` com `{"name":"Celular 03","session":<thumb>,"kind":"android","proof":<PhoneClaim.Proof(token, nonce, thumbprint do agente, thumbprint do Master)>}` para a reivindicação do 07 → `KnownMachine` com `Name=="Celular 07"` (o número da reivindicação, não o nome que o celular disse; um roster com "Celular 03" fica intacto), `IsPhone`, `AgentThumbprint` do agente; `PairRefused("Este celular não espera este computador.")` → `PairingRefusedException` com esse texto; `CommandAck` → `PairingSession.OldAgent`.
  - `PhoneClaimWatcherTests`: sem pendentes, `search` não é chamado; uma pendente e um `FoundMachine` com o `ClaimId` certo → `claim` chamado uma vez, `added` recebe a máquina e a pendente some; `ClaimId` de outro token → nada; `claim` lançando → pendente fica.
  - `DiscoveryClientTests.Merge_keeps_claim_id`.
  - `MainWindow` (helper puro testável, em `PhoneClaimWatcher` ou `KnownMachinesMerge`): `ReplacingSameName(IReadOnlyList<KnownMachine> roster, KnownMachine joined)` tira as entradas celular com o mesmo `Name` e acrescenta a nova; computador com o mesmo nome não é tocado.
- [ ] **Step 2:** `dotnet test tests/Midview.Master.Tests --filter "FullyQualifiedName~PhoneClaim|FullyQualifiedName~DiscoveryClient"` → FAIL.
- [ ] **Step 3:** Implementar; no `MainWindow`, o watcher nasce depois de o roster carregar, `search` = `DiscoveryClient.Search(thumbprint, WakeOnLanSender.DiscoveryTargets(WakeOnLanSender.LocalNetworks(), Discovery.Port), Discovery.ListenTime)` (como `MachinesWindow.SearchAsync`), `claim` = `PhoneClaimer.Claim(found.Address, cert, Environment.MachineName, pending.Token, pending.Number)`, `added` = `Dispatcher.Invoke` → roster com `ReplacingSameName` → salvar → `PhoneJoined`. Watcher descartado no fechamento.
- [ ] **Step 4:** Mesmo comando → PASS; `dotnet test tests/Midview.Master.Tests` inteiro verde.
- [ ] **Step 5:** Commit `feat(master): celular preparado entra na turma sozinho`.

### Task 4: Agente aceita a reivindicação do PC do QR

**Files:**
- Create: `.../agent/provisioning/ProvisioningSetup.kt` (guardar/ler os extras), `.../agent/pairing/ClaimFlow.kt`
- Modify: `.../agent/provisioning/ProvisioningModeActivity.kt`, `PolicyComplianceActivity.kt` (lêem `EXTRA_PROVISIONING_ADMIN_EXTRAS_BUNDLE` e chamam `ProvisioningSetup.save`)
- Modify: `.../agent/agent/DeviceName.kt` (`choose(number, androidId, model)`: com número, `"Celular " + number`), `.../agent/protocol/Discovery.kt` (`Reply.claimId: String? = null`, escrito como `"claim"` só se não nulo), `.../agent/protocol/MessageType.kt`, `.../agent/protocol/` (`PhoneClaimRequest.decode`, espelho do C#), `.../agent/net/DiscoveryResponder.kt`, `.../agent/net/CommandHandler.kt`, `.../agent/agent/AgentService.kt`
- Test: `src/Midview.Android/app/src/test/.../provisioning/ProvisioningSetupTest.kt`, `.../pairing/ClaimFlowTest.kt`, `DeviceNameTest.kt`, `DiscoveryResponderTest.kt`, `CommandHandlerTest.kt`, `protocol/DiscoveryTest.kt`

**Interfaces:**
- Consumes: formato de `PhoneClaimRequest`, `claim` e os números de `ClaimPhone`/`WipePhone` (Task 1).
- Produces:
  - `data class SetupClaim(val number: String, val token: String, val master: String)`; `class ProvisioningSetup(file: File) { fun save(extras: Map<String, String?>): Boolean; fun load(): SetupClaim?; fun consumeToken(); fun number(): String? }` — `save` só grava com as três chaves preenchidas e número de 1 a 3 dígitos; `consumeToken` apaga o token e mantém número e master.
  - `class ClaimFlow(setup: ProvisioningSetup, trusted: TrustedMasters, deviceName: () -> String, sessionThumbprint: String, onClaimed: (String) -> Unit) { fun claimId(): String?; fun run(channel: Channel, request: Message) }` — `claimId()` = hash do token só se há token e `trusted.count() == 0`. O celular se chama `"Celular " + número` (o número do QR; o Master também usa o número da reivindicação e ignora o nome que o celular mandar). `run`: recusa (`PairRefused`) se não há token ("Este celular já entrou numa turma."), se `channel.remoteThumbprint` difere de `master` ("Este celular não espera este computador.") ou se o `nonce` do pedido não tem 43 caracteres Base64 URL ("Código de preparação inválido."); senão `trusted.add(master)`, `consumeToken()`, `PairDone` com o resultado de `Pairing.encodeResult(deviceName(), sessionThumbprint, mac = null)` mais `"proof"` = Base64 URL sem `=` de HMAC-SHA256(chave = UTF-8 do token, mensagem = UTF-8 de `nonce + thumbprint do agente + thumbprint do master`, ambos em maiúsculas) — o mesmo `PhoneClaim.Proof` do C#, onde o thumbprint do agente é `identity.thumbprint` (o do certificado TLS do agente, o mesmo que o Master vê no canal) e o do master é o do QR (Ruling F do ledger: o token nunca trafega). Vetor fixo, copiado de `tests/Midview.Protocol.Tests/PhoneClaimTests.cs`: `proof(token = "token-123", nonce = "nonce-abc", agente = "aa11", master = "BB22") == "WSiEijnq4JkvauANCY9jxAA-15IWfTZOg00j1DJWdLE"` (os thumbprints entram aparados e em maiúsculas) — e `onClaimed(deviceName())`.
  - `CommandHandler(trusted, pairing, claim: ClaimFlow)`: primeira mensagem `CLAIM_PHONE` (de qualquer um, com o limite `UNTRUSTED_MAX_LENGTH`) vai para `claim.run` e encerra.

- [ ] **Step 1: Testes que falham** (`./gradlew testDebugUnitTest --tests '*ClaimFlowTest*'` etc.)
  - `ProvisioningSetupTest`: salva e lê; recusa sem token, sem master, número `"1234"`; `consumeToken` deixa `number()=="07"` e `load()` sem token.
  - `ClaimFlowTest` (com `FakeChannel`): thumbprint certo → `PAIR_DONE` com nome "Celular 07" e `proof` igual ao vetor fixo acima (`token-123`, `nonce-abc`, `aa11`, `BB22` → `WSiEijnq4JkvauANCY9jxAA-15IWfTZOg00j1DJWdLE`), professor confiável, token consumido, `onClaimed("Celular 07")`; segunda vez → `PAIR_REFUSED "Este celular já entrou numa turma."`; thumbprint de outro PC → recusa e nada confiável; `claimId()` null depois de reivindicado e quando já há professor.
  - `DeviceNameTest`: número `"07"` → `"Celular 07"`; sem número, comportamento de hoje.
  - `DiscoveryResponderTest`: com `ClaimFlow` pendente, a resposta tem `"claim"`; sem, não tem.
  - `CommandHandlerTest`: `CLAIM_PHONE` de não confiável chega ao `ClaimFlow`.
- [ ] **Step 2:** Rodar → FAIL.
- [ ] **Step 3:** Implementar. Arquivo do setup em `filesDir/provisioning-setup.json` com `AtomicWrite`; `AgentService` monta `ProvisioningSetup`, usa `number()` no nome (inclusive no CN do `AgentIdentity` criado depois do provisionamento) e passa `ClaimFlow` ao `CommandHandler` e ao `DiscoveryResponder`.
- [ ] **Step 4:** `gradlew -p src/Midview.Android testDebugUnitTest` inteiro → PASS.
- [ ] **Step 5:** Commit `feat(android): celular preparado aceita o PC do QR sem código`. Quando o APK do agente com a reivindicação (esta Task) e o apagar (Task 5) for publicado, as Tasks 4/5 viram `PhoneProvisioning.ClaimsReady` para `true` no Master (Ruling J); antes disso o assistente fica escondido.

### Task 5: Agente apaga o aparelho a pedido e mostra "Pronto"

**Files:**
- Modify: `.../agent/policy/Lockdown.kt` (interface do gateway: `fun wipe()`), `.../agent/policy/AndroidDevicePolicyGateway.kt`, `.../agent/net/CommandHandler.kt`
- Create: `.../agent/ui/ReadyActivity.kt` (+ layout/strings), registrado no manifesto
- Modify: `.../agent/agent/AgentService.kt` (`onClaimed` abre `ReadyActivity`)
- Test: `CommandHandlerTest.kt`, `LockdownTest.kt` (gateway falso ganha `wipe`)

**Interfaces:**
- Consumes: `ClaimFlow.onClaimed` (Task 4), `MessageType.WIPE_PHONE` (Task 1).
- Produces: `WIPE_PHONE` de PC confiável → `COMMAND_ACK` `(true, "apagando")` e, 2 s depois, fora do thread do canal, `gateway.wipe()`; de não confiável, a conexão termina sem resposta (regra de hoje). `AndroidDevicePolicyGateway.wipe()`: `dpm.wipeDevice(WIPE_RESET_PROTECTION_DATA)` em API 34+, `dpm.wipeData(WIPE_RESET_PROTECTION_DATA)` antes; sem ser Device Owner → `COMMAND_ACK (false, "Este celular não foi preparado pelo Midview.")`.
- `ReadyActivity`: tela cheia simples, texto "Pronto. Este é o <nome> da escola." e botão "OK"; fecha sozinha em 5 minutos.

- [ ] **Step 1: Testes que falham:** `CommandHandlerTest.wipe_from_trusted_acks_then_wipes` (o `wipe` falso é chamado depois do ack, com um executor injetado sem espera), `wipe_from_untrusted_never_wipes`, `wipe_when_not_device_owner_says_why`.
- [ ] **Step 2:** Rodar → FAIL.
- [ ] **Step 3:** Implementar (o atraso e o executor são injetáveis no `CommandHandler`).
- [ ] **Step 4:** `testDebugUnitTest` inteiro → PASS; `assembleDebug` → BUILD SUCCESSFUL.
- [ ] **Step 5:** Commit `feat(android): apagar e preparar de novo, tela de pronto`. Com esta Task e a Task 4 no APK publicado, vire `PhoneProvisioning.ClaimsReady` para `true` (Ruling J).

### Task 6: Roteiro do assistente e imagens

**Files:**
- Create: `src/Midview.Master/PhoneSetupGuide.cs`
- Create: `src/Midview.Master/Assets/PrepararCelular/` com `configuracoes.png`, `contas.png`, `sobre.png`, `redefinicao.png` (os prints de `.superpowers/brainstorm/2043-1794420649/content/`, já borrados) e os desenhos `conta-mi.png`, `bem-vindo.png` (simples, no estilo dos prints, marcados no código como provisórios)
- Modify: `src/Midview.Master/Midview.Master.csproj` (`<Resource>` das imagens)
- Test: `tests/Midview.Master.Tests/PhoneSetupGuideTests.cs`

**Interfaces:**
- Produces:
  - `sealed record GuideHelp(string Problem, string Answer)`
  - `sealed record GuideStep(string Id, string Title, IReadOnlyList<string> Taps, string? Image, Rect? Target, string? Timing, string NextText, IReadOnlyList<GuideHelp> Help)` — `Target` em frações (0–1) da imagem.
  - `static class PhoneSetupGuide { IReadOnlyList<GuideStep> Steps; GuideHelp UnknownAccount; GuideHelp ResetWithoutPin; IReadOnlyList<GuideHelp> NotAppeared; IReadOnlyList<GuideHelp> WifiHelp; }`
  - Ids, na ordem: `numero`, `bateria`, `conta-mi`, `conta-google`, `restaurar`, `seis-toques`. O passo `seis-toques` tem o texto genérico da spec ("toque em Aceitar/Continuar até aparecer a tela do Midview") até a Task 9.
- Textos: os da spec (objetivos 3 e 5), com os caminhos MIUI 12/13 no "Deu errado?" de `conta-mi`, `conta-google` e `restaurar`, e a dica da busca das Configurações. Alvos dos prints (frações medidas nos prints 720×1650): `contas.png` linha Google `(0.06, 0.55, 0.88, 0.09)`; `redefinicao.png` botão `(0.48, 0.87, 0.47, 0.06)`; `configuracoes.png` "Sobre o telefone" `(0.06, 0.23, 0.88, 0.06)`.

- [ ] **Step 1: Testes que falham:** ordem dos ids; `conta-mi` e `conta-google` antes de `restaurar`; todo passo tem pelo menos um `Help` e `Taps` não vazio (exceto `numero`); toda `Image` existe como recurso (`Application.GetResourceStream` com pack URI ou leitura do `.csproj`); todo `Target` dentro de 0–1; nenhum texto contém `Device Owner`, `APK`, `provision`, `adb`, `DPC`.
- [ ] **Step 2:** `dotnet test tests/Midview.Master.Tests --filter FullyQualifiedName~PhoneSetupGuide` → FAIL.
- [ ] **Step 3:** Escrever o roteiro e copiar as imagens (conferir antes que nome de Wi-Fi e contas estão borrados).
- [ ] **Step 4:** Mesmo comando → PASS.
- [ ] **Step 5:** Commit `feat(master): roteiro passo a passo para preparar celulares`.

### Task 7: Janela do assistente e pontos de entrada

**Files:**
- Create: `src/Midview.Master/PreparePhonesWindow.xaml`, `PreparePhonesWindow.xaml.cs`, `src/Midview.Master/PhoneSetupProgress.cs`
- Delete: `src/Midview.Master/PreparePhoneWindow.xaml`, `PreparePhoneWindow.xaml.cs`
- Modify: `src/Midview.Master/MachinesWindow.xaml(.cs)` (o link abre `PreparePhonesWindow`), `src/Midview.Master/GridFilter.cs` + `MainWindow.xaml(.cs)` (botão "Preparar celulares" no painel vazio)
- Test: `tests/Midview.Master.Tests/PhoneSetupProgressTests.cs`, `GridFilterTests.cs`

**Interfaces:**
- Consumes: `PhoneSetupGuide` (Task 6); `PhoneNumber`, `PendingPhoneClaimsFile`, `PhoneProvisioning.QrJson` (Task 2); `MainWindow.PhoneJoined`, `PhoneClaimWatcher.Kick` (Task 3); `PhoneWifiFile` (existe).
- Produces:
  - `sealed class PhoneSetupProgress` — máquina de estados pura da janela: páginas `Wifi`, `Step(i)`, `Waiting`, `NotAppeared`, `Ready`; `Start(bool hasWifi)`, `Next()`, `Back()`, `QrShown(DateTimeOffset at)`, `Tick(DateTimeOffset now)` (vira `NotAppeared` depois de 15 min), `Joined(string name)` (`Ready` só se `name == PhoneNumber.NameOf(number)`), `StartAt(string number)` (vai direto a `seis-toques`), `Again()` (volta a `numero`), `NumberTaken(bool online)` (no `Next` de `numero` quando já há um celular com esse nome: página `ConfirmRedo` com "Já existe um Celular 07. Vai preparar de novo o 07?"; confirmado e online → oferece "Apagar e preparar de novo" (Task 8) e vai a `seis-toques`; confirmado e desligado → segue para `bateria`), `PhonesInClass`/`Total` para "Celular 07 pronto ✓ (3 de 11)" ou "(3 celulares na turma)".
  - `static string? GridFilter.EmptyNotice(...)` passa a devolver `"Nenhum celular nesta sala ainda."` quando os celulares estão prontos e o filtro é Celulares sem nenhum; `static bool GridFilter.OffersPhoneSetup(IReadOnlyCollection<MachineTile> tiles, DeviceFilter devices, bool phonesReady)` (verdadeiro com filtro Celulares, nenhum celular e `phonesReady`, mesmo com zero máquinas).
  - `PreparePhonesWindow(Func<int> phonesInClass, Action kick, Func<string, Task<(bool Ok, string Detail)>>? wipe = null)` com `StartAt(string number)`; `wipe` recebe o nome ("Celular 07") e é ligado na Task 8; sem ele, `ConfirmRedo` online segue como desligado; ao mostrar o QR, `PendingPhoneClaimsFile.Add(new(number, PhoneClaim.NewToken(), now + PhoneProvisioning.ClaimLifetime))` e `kick()`.
- Página do passo: imagem à esquerda com o contorno vermelho desenhado por cima em `Target`, título, toques numerados, tempo, botão de avançar com `NextText`, "Deu errado?" que expande as ajudas. Página `Waiting`: "Esperando o Celular 07 terminar…" e a explicação simples. `NotAppeared`: `PhoneSetupGuide.NotAppeared` na ordem da spec, incluindo "se o celular já mostra a tela do Midview, é a rede", o caminho de reserva (depois de 1 hora, Máquinas da sala → Procurar e parear, com o código de 6 dígitos) e "Recomeçar este celular". `Ready`: "Preparar o próximo" e "Terminei". "Wi-Fi: <nome> · trocar" discreto nas páginas de passo. O total "de 11" é perguntado uma vez na primeira página de número, opcional, e guardado em `MasterData.Folder` (`phone-total.txt`).

- [ ] **Step 1: Testes que falham:** `PhoneSetupProgressTests` — sem Wi-Fi começa em `Wifi`, com Wi-Fi em `Step(numero)`; `Next` não sai de `numero` sem número válido; `Tick` antes de 15 min fica em `Waiting`, depois `NotAppeared`; `Joined("Celular 08")` esperando o 07 não muda; `Joined("Celular 07")` → `Ready`; `StartAt("07")` → `Step(seis-toques)`; número já existente → `ConfirmRedo`, e daí `seis-toques` (online) ou `bateria` (desligado); texto pronto com e sem total. `GridFilterTests`: `OffersPhoneSetup` verdadeiro sem máquinas e com só PCs no filtro Celulares; falso com `phonesReady` falso e com algum celular; a mensagem nova.
- [ ] **Step 2:** `dotnet test tests/Midview.Master.Tests --filter "FullyQualifiedName~PhoneSetupProgress|FullyQualifiedName~GridFilter"` → FAIL.
- [ ] **Step 3:** Implementar `PhoneSetupProgress`, a janela e os pontos de entrada; apagar a janela antiga e o que só ela usava.
- [ ] **Step 4:** Mesmo comando → PASS; `dotnet test Midview.sln` inteiro (lembrar as falhas ambientais já registradas no plano 1: ACL do Agent.Tests e captura do Desk.Tests).
- [ ] **Step 5: Conferência visual pelo harness** (pasta temporária, roster falso com 2 PCs e nenhum celular, `PhoneProvisioning.IsReady` verdadeiro na build de teste): capturas do filtro Celulares vazio com o botão, de cada página do assistente a 560×860 e na menor tela de 1366×768 (a janela cabe sem rolagem horizontal), do "ainda não apareceu" e do "pronto". Salvar em `.superpowers/sdd/<este plano>/screens/` e mostrar ao professor.
- [ ] **Step 6:** Commit `feat(master): assistente Preparar celulares`.

### Task 8: "Apagar e preparar de novo" no Master

**Files:**
- Modify: `src/Midview.Master/DeviceSupport.cs` (`ToolbarAction.WipePhone`, suportado só em `DeviceKind.Android`), `src/Midview.Master/MainWindow.xaml` (item no menu do quadradinho, `local:DeviceActions.Action="WipePhone"`), `src/Midview.Master/MainWindow.xaml.cs`
- Create: `src/Midview.Master/PhoneWipe.cs`
- Test: `tests/Midview.Master.Tests/PhoneWipeTests.cs`, `DeviceSupportTests.cs`

**Interfaces:**
- Consumes: `MessageType.WipePhone` (Task 1); `PreparePhonesWindow.StartAt` e `PhoneSetupProgress.NumberTaken` (Task 7; esta task liga o parâmetro `wipe` da janela a `PhoneWipe.Send` no `MainWindow`); `CommandClient.SendCommandForResult` (existe).
- Produces:
  - `static class PhoneWipe { string ConfirmText(string name); (bool Ok, string Detail) Send(KnownMachine phone, X509Certificate2 cert); string? NumberOf(string name); }` — `ConfirmText("Celular 07")` = `"O Celular 07 vai ser apagado e voltar de fábrica. Os alunos perdem o que estiver nele."`; `Send` sem `AgentThumbprint` → `(false, "sem a identificação do celular")`, `CommandAck` vazio de agente antigo → `(false, "Midview antigo neste celular: atualize-o pela rede.")`, exceção → `FailureText.DescribeAgent`; `NumberOf("Celular 07")=="07"`, null para nome sem número.
  - Clique no item: confirmação (`Sim, apagar` / `Cancelar`), envia, e com sucesso abre o assistente (Task 7) em `PreparePhonesWindow.StartAt(number)` no passo dos 6 toques; com falha, mostra o motivo.

- [ ] **Step 1: Testes que falham:** os textos e casos acima; `DeviceSupport.Supports(DeviceKind.Android, ToolbarAction.WipePhone)` verdadeiro e falso para Windows.
- [ ] **Step 2:** `dotnet test tests/Midview.Master.Tests --filter "FullyQualifiedName~PhoneWipe|FullyQualifiedName~DeviceSupport"` → FAIL.
- [ ] **Step 3:** Implementar; o item só aparece com `PhoneProvisioning.IsReady` e só fica ativo com um celular online selecionado.
- [ ] **Step 4:** Mesmo comando → PASS.
- [ ] **Step 5:** Commit `feat(master): apagar e preparar de novo um celular da escola`.

### Task 9: Preparação de verdade no Redmi 10C (com o professor)

Passo com aparelho: **pára e pede ao professor** antes de apagar. Só depois da Task 9B do plano 1 (APK publicado com a chave real) e das Tasks 1–8 numa versão que o professor instalou.

**Files:**
- Modify: `src/Midview.Master/PhoneSetupGuide.cs`, `src/Midview.Master/Assets/PrepararCelular/*` (prints reais de `bem-vindo` e das telas depois do QR; troca dos desenhos provisórios)
- Test: `tests/Midview.Master.Tests/PhoneSetupGuideTests.cs` (os ids novos, se surgirem passos)

- [ ] **Step 1:** Com o professor: seguir o assistente do começo ao fim no Redmi 10C (número de teste combinado com ele), apagando o celular. Anotar cada tela que aparece depois do QR, com o texto exato dos botões, e o tempo de cada etapa.
- [ ] **Step 2:** Capturar as telas: as que não deixam print pelo `adb` (o assistente de configuração não tem depuração), o professor fotografa ou tira print pelos botões do celular; borrar dados pessoais.
- [ ] **Step 3:** Conferir no Master real do professor (por ele) que o "Celular NN" entrou sozinho e que "Apagar e preparar de novo" volta o celular à tela "Bem-vindo" sem pedir conta.
- [ ] **Step 4:** Trocar o passo genérico por passos com os prints reais; ajustar tempos; atualizar os testes do roteiro → PASS.
- [ ] **Step 5:** Commit `feat(master): telas reais da preparação do celular no assistente`.
