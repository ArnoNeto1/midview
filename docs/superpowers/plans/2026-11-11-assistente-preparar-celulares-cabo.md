# Assistente "Preparar celulares" pelo cabo USB — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Qualquer professor prepara sozinho os celulares Xiaomi da escola: faz à mão os 8 passos que a Xiaomi exige (com prints e "Deu errado?"), liga o cabo, e o Midview instala o agente, entrega número e Wi-Fi, pareia e torna o celular "da escola" pelo `adb`; o celular entra na turma como "Celular 07" sem código, e pode ser apagado e preparado de novo por um botão.

**Architecture:** O Master leva o `adb` (pasta `adb\` junto do programa, servidor numa porta própria) e o usa por uma interface `IAdb`, para testar tudo com um celular falso. `PhoneCable` confere o celular e roda a preparação na ordem obrigatória: instalar o APK baixado da versão publicada (assinatura conferida), liberar o agente da limpeza da MIUI, entregar ao agente um pacote `setup` (número, thumbprint do PC, Wi-Fi) por `adb shell content call`, e por último `dpm set-device-owner`, que faz o agente desligar a depuração. O agente devolve o thumbprint dele e o Master grava o celular no roster com o endereço provisório `0.0.0.0`, corrigido pela busca (`Roster.NewAddresses`) quando o celular entra no Wi-Fi. A porta do cabo no agente (`CableSetupProvider`) só aceita o uid do shell (2000) e só enquanto o agente não é dono do aparelho nem confia em PC nenhum. O código da prova do QR feito no ramo sai inteiro; o resto (número → nome, roteiro com prints, janela, `WipePhone`) é aproveitado.

**Tech Stack:** .NET 8 + WPF + xUnit (Master, Protocol); Kotlin + JUnit 4 (agente, `src/Midview.Android`); `adb` do platform-tools 37.0.1 (Apache 2.0).

**Spec:** `docs/superpowers/specs/2026-11-11-assistente-preparar-celulares-design.md` (revisão 2, aprovada em 2026-11-11, commit `b2fd7e9`).

**Substitui:** `docs/superpowers/plans/2026-11-11-assistente-preparar-celulares.md` (caminho do QR). Este plano tem workspace e ledger próprios; o ledger antigo fica como registro das Tasks 1–3 e 6–8 já feitas no ramo.

**Execução escolhida pelo professor:** uma tarefa por vez (superpowers:subagent-driven-development, revisor a cada tarefa).

**Ordem:** trabalho no ramo `feat/assistente-celulares` (worktree `.worktrees/assistente-celulares`). As Tasks 5 e 6 (agente) só começam quando o plano 2 do agente não estiver mais editando arquivos do agente em `master`, e começam com um novo merge de `master` (receita da Task 1). A Task 10 só depois da Task 9B do plano 1 (chave real, `SignatureChecksum` real, APK publicado).

## Global Constraints

- Pacote do agente `io.github.arnoneto1.midview.agent`, componente `io.github.arnoneto1.midview.agent/.AdminReceiver` (`PhoneProvisioning.AgentPackage`/`AdminComponent`); APK em `PhoneProvisioning.ApkUrl`.
- Autoridade da porta do cabo: `io.github.arnoneto1.midview.agent.setup`, método `prepare`, um único extra string `setup` = Base64 URL sem `=` de UTF-8 do JSON `{"number","master","ssid","password"}` (número de 1 a 3 dígitos com zero à esquerda até 2; `master` = thumbprint do certificado do Master em hex maiúsculo).
- Resposta da porta: `Bundle` com `ok` (boolean) e, com `ok=true`, `thumbprint` (do certificado do agente, hex maiúsculo) e `name` ("Celular 07"); com `ok=false`, `error` ∈ `not-shell`, `already-owner`, `already-trusted`, `bad-setup`.
- Ordem no celular, sempre (cada comando com `-s <serial>` do único celular no cabo): conferir → (`pm uninstall` se o pacote já existe e não é dono; era `pm clear`, trocado na revisão final) → acender e desbloquear a tela (`input keyevent KEYCODE_WAKEUP`, `wm dismiss-keyguard`; 2026-11-12) → `install -r` → `cmd appops set <pacote> 10008 allow` → `cmd deviceidle whitelist +<pacote>` → `settings put secure enabled_accessibility_services <pacote>/<pacote>.screen.AccessibilityCapture` e `accessibility_enabled 1` (como no README; 2026-11-12) → `content call` → `dpm set-device-owner`. Nada usa o cabo depois do `dpm`.
- Servidor do `adb` do Midview na porta **5039** (`ANDROID_ADB_SERVER_PORT`), encerrado (`kill-server`) quando a janela do assistente fecha.
- Espera de **5 minutos** depois de pronto antes de "ainda não apareceu"; busca na rede a cada **10 s** nessa espera.
- Nome do celular: `PhoneNumber.NameOf(número)` ("Celular 07"); endereço provisório do roster `0.0.0.0`.
- `WipePhone = 93` (o 93 da `ClaimPhone`, que sai), igual em `src/Midview.Protocol/MessageType.cs` e `.../agent/protocol/MessageType.kt`; conferir em `master` que 93 ainda está livre.
- `PhoneProvisioning.WizardReady` = `IsReady` (checksum real) **e** `CableReady` (constante, nasce `false`, virada na Task 10). Falso esconde o botão, o link e o item "Apagar e preparar de novo".
- Textos em português, sem jargão para o professor (nada de Device Owner, APK, adb, provisionamento); os nomes de menu da MIUI aparecem como estão na tela. Nunca citar produto concorrente. Commits sem `Co-Authored-By`.
- .NET pelo SDK em `C:\Users\Neto\AppData\Local\Microsoft\dotnet\dotnet.exe`; Android por `src/Midview.Android/gradlew -p src/Midview.Android testDebugUnitTest`.
- Verificação visual só pelo harness do Master (`MasterData.Folder` temporário, `new Application()` com os recursos do `App.xaml`, nunca `new App()`, `RenderTargetBitmap`); nunca abrir o Midview do professor real nem falar com máquinas do laboratório. O Redmi 10C da escola no cabo só é usado onde a task diz.

## Review Focus

- O login da conta Mi pelo Google deixou uma conta Google: a conferência diz "Ainda há uma conta neste celular" e manda ao passo 7, sem tentar o `dpm`. (Task 4)
- Preparação que falhou no meio (agente instalado e já com PC confiável, mas não dono): tentar de novo funciona, porque o Master limpa o app antes (`pm clear`). (Tasks 4 e 5)
- Wi-Fi com aspas, `;`, acento ou espaço no nome ou na senha: chega igual ao agente (pacote Base64, mesmo vetor fixo nos dois lados). (Tasks 4 e 5)
- Dois celulares no cabo, ou o cabo solto no meio: frase clara e volta ao passo 8, nunca um erro técnico. (Task 4)
- Outro app do celular chamando a porta do cabo: recusado (`not-shell`), nada guardado. (Task 5)

---

### Task 1: Trazer `master` para o ramo

**Files:** os que o merge tocar; conflitos conhecidos em `src/Midview.Master/DeviceSupport.cs` e `src/Midview.Master/MainWindow.xaml.cs`.

- [ ] **Step 1:** No worktree, `git merge master`. Resolver os conflitos pela receita do Ruling K no ledger antigo (`.superpowers/sdd/2026-11-11-assistente-preparar-celulares/progress.md`): manter as ações novas de `master` (tela e cadeado do celular) e as do ramo (`WipePhone`).
- [ ] **Step 2:** `dotnet test Midview.sln` → verde, fora das falhas ambientais já registradas no plano 1 (ACL do Agent.Tests, captura do Desk.Tests); `gradlew -p src/Midview.Android testDebugUnitTest` → verde.
- [ ] **Step 3:** Commit do merge (`merge: master no assistente de celulares`).

### Task 2: Tirar o caminho do QR

**Files:**
- Delete: `src/Midview.Protocol/PhoneClaim.cs`, `src/Midview.Master/PendingPhoneClaimsFile.cs`, `PhoneClaimer.cs`, `PhoneClaimWatcher.cs`, `PhoneQrReuse.cs`, `PreparePhonesWindow.Qr.cs` e os testes deles (`PhoneClaimTests`, `PendingPhoneClaimsFileTests`, `PhoneClaimerTests`, `PhoneClaimWatcherTests`, `PhoneQrReuseTests`)
- Modify: `src/Midview.Protocol/MessageType.cs` (sai `ClaimPhone`; `WipePhone = 93`), `Discovery.cs` (sai `ClaimId`/`"claim"`), `Pairing.cs` (sai `PairingResult.Proof`), `src/Midview.Master/DiscoveryClient.cs` (sai `FoundMachine.ClaimId`), `PhoneProvisioning.cs`, `MainWindow.xaml.cs`/`MainWindow.PreparePhones.cs` (sai o watcher), `PreparePhonesWindow*.cs`, `PhoneSetupProgress.cs`
- Test: `tests/Midview.Protocol.Tests/DiscoveryTests.cs`, `PairingTests.cs`, `tests/Midview.Master.Tests/PhoneProvisioningTests.cs`, `PhoneSetupProgressTests.cs`, `DiscoveryClientTests.cs`

**Interfaces:**
- Produces: `PhoneProvisioning` sem `QrJson`, `ClaimLifetime` e `ClaimsReady`; com `public const bool CableReady = false;` e `WizardReadyWith(string checksum, bool cableReady)`. `PhoneSetupProgress` sem as páginas e membros do QR (`Waiting`, `NotAppeared`, `QrShown`, `Tick`, `NotAppearedAfter`); o último passo do roteiro leva a `Ready`. Estado intermediário aceito: a janela ainda mostra os passos antigos (escondida pelo `WizardReady`).

- [ ] **Step 1:** Ajustar os testes: `DiscoveryTests` sem `claim` (uma resposta com `"claim"` é aceita e ignorada); `PairingTests` sem `proof`; `PhoneProvisioningTests.Wizard_needs_both_gates` com `WizardReadyWith(checksum, cableReady)`; `PhoneSetupProgressTests` sem os casos de espera do QR.
- [ ] **Step 2:** Apagar e ajustar os arquivos; `grep -rn "Claim\|QrJson\|ClaimLifetime" src tests` só pode achar `PhoneProvisioning.CableReady` e nada do QR.
- [ ] **Step 3:** `dotnet test tests/Midview.Protocol.Tests` e `dotnet test tests/Midview.Master.Tests` → verdes.
- [ ] **Step 4:** Commit `refactor: tirar o caminho do QR do assistente de celulares`.

### Task 3: APK do agente baixado e conferido

**Files:**
- Create: `src/Midview.Master/ApkSignature.cs`, `src/Midview.Master/AgentApk.cs`
- Test: `tests/Midview.Master.Tests/ApkSignatureTests.cs`, `AgentApkTests.cs`

**Interfaces:**
- Produces:
  - `static class ApkSignature { string? SignerChecksum(string apkPath); }` — lê o APK Signing Block (v3 `0xf05368c0`, senão v2 `0x7109871a`), pega o primeiro certificado do primeiro signatário e devolve SHA-256 dele em Base64 URL sem `=` (o mesmo formato de `PhoneProvisioning.SignatureChecksum`); null para arquivo sem bloco ou corrompido, sem exceção.
  - `sealed class AgentApk(HttpClient http, string folder, string expectedChecksum)` com `Task<AgentApkResult> GetAsync(CancellationToken ct)`; `enum AgentApkResult { Ready, Offline, Invalid }` e `string ApkPath` (`<folder>\MidviewAgent.apk`). Baixa `PhoneProvisioning.ApkUrl` para um temporário; só substitui o guardado se `SignerChecksum` bate; sem internet usa o guardado se ele bate (`Ready`) ou devolve `Offline`; baixado com assinatura errada → `Invalid` e o guardado fica.

- [ ] **Step 1: Testes que falham.** `ApkSignatureTests` monta APKs sintéticos em memória (zip mínimo + bloco de assinatura com `"APK Sig Block 42"`, um certificado criado no teste com `CertificateRequest`): v2 → checksum do certificado; v3 e v2 juntos → o do v3; zip sem bloco → null; bytes truncados → null. `AgentApkTests` com `HttpMessageHandler` falso e pasta temporária: baixado bom → `Ready` e arquivo no lugar; handler lançando e guardado bom → `Ready`; handler lançando sem guardado → `Offline`; baixado com outro certificado → `Invalid` e o guardado intacto.
- [ ] **Step 2:** `dotnet test tests/Midview.Master.Tests --filter "FullyQualifiedName~ApkSignature|FullyQualifiedName~AgentApk"` → FAIL.
- [ ] **Step 3:** Implementar (formato do bloco: os 16 bytes mágicos antes do Central Directory, tamanho de 8 bytes antes deles; pares id/valor com tamanho de 8 bytes; dentro do v2/v3, sequências com prefixo de tamanho de 4 bytes: signers → signer → signed data → digests, certificates).
- [ ] **Step 4:** Mesmo comando → PASS.
- [ ] **Step 5:** Conferir à mão com o APK de debug (`gradlew -p src/Midview.Android assembleDebug`): `SignerChecksum` não é null e bate com `apksigner verify --print-certs` (SHA-256 em Base64 URL), se o `apksigner` estiver no SDK; anotar no relatório.
- [ ] **Step 6:** Commit `feat(master): baixar e conferir o app do celular`.

### Task 4: O Master prepara o celular pelo cabo

**Files:**
- Create: `src/Midview.Master/adb/` com `adb.exe`, `AdbWinApi.dll`, `AdbWinUsbApi.dll`, `NOTICE.txt` copiados de `C:\Users\Neto\AppData\Local\Android\Sdk\platform-tools` (37.0.1); `src/Midview.Master/Adb.cs` (`IAdb`, `AdbProcess`), `src/Midview.Master/PhoneCable.cs`, `src/Midview.Master/CableSetupPayload.cs`
- Modify: `src/Midview.Master/Midview.Master.csproj` (a pasta `adb\` vai para a saída e para o publish)
- Test: `tests/Midview.Master.Tests/PhoneCableTests.cs`, `CableSetupPayloadTests.cs`

**Interfaces:**
- Consumes: `AgentApk.ApkPath` (Task 3), `PhoneNumber.NameOf`, `PhoneProvisioning.AgentPackage`/`AdminComponent`.
- Produces:
  - `interface IAdb { AdbOutput Run(TimeSpan timeout, params string[] args); }`, `sealed record AdbOutput(int ExitCode, string Text, bool TimedOut)`; `sealed class AdbProcess : IAdb, IDisposable` (roda `<pasta do programa>\adb\adb.exe` com `ANDROID_ADB_SERVER_PORT=5039`, sem janela; `Dispose` roda `kill-server`).
  - `static class CableSetupPayload { string Encode(string number, string masterThumbprint, string ssid, string password); }` — JSON com as quatro chaves da Global Constraints, thumbprint em maiúsculas, Base64 URL sem `=`. Vetor fixo (copiado para a Task 5): `Encode("07", "ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12", "Lab \"1\"; é", "s'nh@ 1;")` = valor que o teste imprime na primeira execução e fica escrito no teste e no brief da Task 5.
  - `enum CableProblem { NoPhone, TooMany, Unauthorized, TooOld, SecurityOff, InstallOff, HasAccounts, HasUsers, AlreadySchool, InstallFailed, Refused, Lost }`
  - `sealed class PhoneCable(IAdb adb)`: `CableProblem? Check()`; `CableResult Prepare(string apkPath, string number, string masterThumbprint, string ssid, string password, Action<CableStage> stage)`; `enum CableStage { Checking, Installing, Wifi, Pairing, Owner }`; `sealed record CableResult(KnownMachine? Phone, CableProblem? Problem, bool OwnerUnconfirmed)`; `static (string Text, string? BackToStep) Describe(CableProblem p)`.
- `Check()`: `devices` (0 → `NoPhone`; mais de 1 → `TooMany`; `unauthorized` → `Unauthorized`); `getprop ro.build.version.sdk` < 29 → `TooOld`; com `ro.miui.ui.version.name` não vazio: `persist.security.adbinput` ≠ `1` → `SecurityOff`, `persist.security.adbinstall` ≠ `1` → `InstallOff`; `dumpsys account` com `Accounts: N`, N > 0 → `HasAccounts`; `pm list users` com mais de um `UserInfo{` → `HasUsers`; `dpm list-owners` com o pacote → `AlreadySchool`.
- `Prepare`: `Check()`; `pm list packages <pacote>` achou → `pm clear <pacote>`; `install -r <apk>` (timeout 3 min; sem `Success`: `INSTALL_FAILED_USER_RESTRICTED` → `InstallOff`, outro → `InstallFailed`); os dois comandos de liberação (falha não pára); `shell content call --uri content://io.github.arnoneto1.midview.agent.setup --method prepare --extra setup:s:<payload>` (lê `ok`, `thumbprint`, `name`, `error` do `Result: Bundle[{...}]`; `ok=false` → `Refused`); `dpm set-device-owner <AdminComponent>` (`Success` → pronto; texto com `MANAGE_DEVICE_ADMINS` → `SecurityOff`, `already some accounts` → `HasAccounts`, `already several users` → `HasUsers`; aparelho sumiu do `adb` durante o comando → pronto com `OwnerUnconfirmed = true`). O celular devolvido: `new KnownMachine(NameOf(number), "0.0.0.0", thumbprint, thumbprint, null, DeviceKind.Android)`. Aparelho sumiu antes do `dpm` → `Lost`.
- `Describe` (texto exato → passo):
  - `NoPhone` → "O celular não está no cabo." → `cabo`
  - `TooMany` → "Há mais de um celular no cabo. Deixe só um." → `cabo`
  - `Unauthorized` → "Falta tocar em Permitir no celular." → `cabo`
  - `TooOld` → "Este celular é antigo demais para o Midview (precisa do Android 10 ou mais novo)." → null
  - `SecurityOff` → "A chave \"Depuração USB (configurações de segurança)\" está desligada." → `chaves`
  - `InstallOff` → "A chave \"Instalar via USB\" está desligada." → `chaves`
  - `HasAccounts` → "Ainda há uma conta neste celular." → `sair-contas`
  - `HasUsers` → "O celular tem um Segundo espaço ou Apps duplos. Apague-os nas Configurações e tente de novo." → null
  - `AlreadySchool` → "Este celular já é da escola. Para prepará-lo de novo, use \"Apagar e preparar de novo\" no Midview." → null
  - `InstallFailed` → "O Midview não foi instalado no celular. Se apareceu uma pergunta no celular, toque em Instalar e tente de novo." → `cabo`
  - `Refused` → "O celular recusou a preparação. Tente de novo." → `cabo`
  - `Lost` → "O cabo soltou no meio. Ligue de novo e tente outra vez." → `cabo`

- [ ] **Step 1: Testes que falham.** `PhoneCableTests` com um `FakeAdb` que responde por argumentos e grava a ordem das chamadas: cada `CableProblem` de `Check` a partir das saídas reais (copiar as do Redmi 10C: `getprop`, `dumpsys account` com `Accounts: 0`/`Accounts: 1`, `pm list users` com `UserInfo{0:Proprietário:c13} running` e com mais um `UserInfo{999:...}`); celular sem MIUI (`ro.miui.ui.version.name` vazio) não confere as chaves; `Prepare` feliz chama na ordem exata da Global Constraints e devolve `Celular 07`, `0.0.0.0`, o thumbprint do `Bundle`; com pacote já instalado, `pm clear` antes do `install`; `dpm` com `already some accounts` → `HasAccounts`; aparelho some durante o `dpm` → `OwnerUnconfirmed`; nenhuma chamada depois do `dpm`; `Describe` de todos os valores. `CableSetupPayloadTests`: decodificar o vetor fixo devolve exatamente os quatro valores.
- [ ] **Step 2:** `dotnet test tests/Midview.Master.Tests --filter "FullyQualifiedName~PhoneCable|FullyQualifiedName~CableSetupPayload"` → FAIL.
- [ ] **Step 3:** Implementar; copiar a pasta `adb\` e conferir que `adb\adb.exe version` roda de uma pasta só com os quatro arquivos (sem `libwinpthread-1.dll`; se precisar, incluí-la e anotar).
- [ ] **Step 4:** Mesmo comando → PASS; `dotnet publish` do Master tem a pasta `adb\` (o instalador leva `professor\*` com subpastas).
- [ ] **Step 5:** Conferência só de leitura com o Redmi 10C no cabo: um teste manual (`[Fact(Skip=...)]` ou programa de uma linha no relatório) roda `new PhoneCable(new AdbProcess()).Check()` e espera `AlreadySchool` (o celular já é dono pelo agente de debug). Nada de `Prepare` no celular real nesta task.
- [ ] **Step 6:** Commit `feat(master): preparar o celular pelo cabo`.

### Task 5: Agente recebe a preparação pelo cabo

Começa com `git merge master` (receita da Task 1) se `master` andou nos arquivos do agente.

**Files:**
- Create: `.../agent/provisioning/CableSetup.kt` (arquivo e pacote), `.../agent/provisioning/CableSetupDoor.kt` (regra pura), `.../agent/provisioning/CableSetupProvider.kt`, `.../agent/ui/ReadyActivity.kt` (+ layout e strings)
- Modify: `AndroidManifest.xml` (provider exportado com a autoridade da Global Constraints; `ReadyActivity`), `.../agent/agent/DeviceName.kt`, `.../agent/AdminReceiver.kt`, `.../agent/policy/Lockdown.kt` (gateway ganha `addWifi(ssid, password)`), `.../agent/policy/AndroidDevicePolicyGateway.kt`
- Test: `app/src/test/.../provisioning/CableSetupTest.kt`, `CableSetupDoorTest.kt`, `.../agent/DeviceNameTest.kt`

**Interfaces:**
- Consumes: formato do pacote `setup` e o vetor fixo (Task 4).
- Produces:
  - `data class CableSetupData(val number: String, val master: String, val ssid: String, val password: String?)`; `object CableSetupPayload { fun decode(text: String?): CableSetupData? }` (null para Base64 ou JSON inválido, número fora de 1–3 dígitos, `master` sem ser hex de 40 a 64 caracteres, `ssid` vazio).
  - `class CableSetup(file: File) { fun save(data: CableSetupData); fun number(): String?; fun takeWifi(): Pair<String, String?>?; fun markReadyShown(): Boolean }` — `filesDir/cable-setup.json` com `AtomicWrite`; `takeWifi` devolve o Wi-Fi e apaga a senha do arquivo; `markReadyShown` devolve true só na primeira vez.
  - `class CableSetupDoor(setup: CableSetup, trusted: TrustedMasters, isOwner: () -> Boolean, identity: () -> Pair<String, String>)` com `fun prepare(callerUid: Int, payload: String?): Map<String, Any>` — uid ≠ 2000 → `error=not-shell`; dono → `already-owner`; `trusted.count() > 0` → `already-trusted`; pacote inválido → `bad-setup`; senão `setup.save`, `trusted.add(master)`, e `ok=true` com `thumbprint` e `name` de `identity()` (criada depois de salvar o número, para o certificado nascer com "Celular 07").
  - `CableSetupProvider.call("prepare", null, extras)` → `CableSetupDoor.prepare(Binder.getCallingUid(), extras?.getString("setup"))` em `Bundle`; outros métodos → null.
  - `DeviceName.choose(number: String?, androidId: String?, model: String?)`: com número, `"Celular " + number`; sem, o de hoje. `DeviceName.read` lê o número de `CableSetup`.
  - `AdminReceiver.lockDownAndStart`: depois de `lockDown`, `takeWifi()` → `gateway.addWifi` (falha não impede o resto); se há número e `markReadyShown()`, abre `ReadyActivity`.
  - `ReadyActivity`: tela cheia, "Pronto. Este é o <nome> da escola." e botão "OK"; fecha sozinha em 5 minutos.

- [ ] **Step 1: Testes que falham** (`gradlew -p src/Midview.Android testDebugUnitTest --tests '*CableSetup*'` etc.): `CableSetupPayload.decode(<vetor fixo da Task 4>)` = `("07", "AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12", "Lab \"1\"; é", "s'nh@ 1;")`; decode recusa os inválidos acima; `CableSetupDoorTest`: uid 2000 + pacote bom → `ok=true`, master confiável, número salvo, `name == "Celular 07"`; uid 10123 → `not-shell` e nada salvo nem confiável; dono → `already-owner`; já confia → `already-trusted`; `CableSetupTest`: `takeWifi` devolve a senha uma vez e depois `password == null`; `markReadyShown` true só uma vez; `DeviceNameTest`: `choose("07", ...) == "Celular 07"`, `choose(null, ...)` como hoje.
- [ ] **Step 2:** Rodar → FAIL.
- [ ] **Step 3:** Implementar. `addWifi` no gateway real: `WifiManager.addNetwork` com WPA2 (ou rede aberta sem senha) e `enableNetwork`, permitido ao dono do aparelho.
- [ ] **Step 4:** `testDebugUnitTest` inteiro → PASS; `assembleDebug` → BUILD SUCCESSFUL.
- [ ] **Step 5:** Commit `feat(android): preparar o celular pelo cabo`.

### Task 6: Agente apaga o aparelho a pedido

**Files:**
- Modify: `.../agent/protocol/MessageType.kt` (`WIPE_PHONE = 93`), `.../agent/policy/Lockdown.kt` (gateway `wipe()`), `.../agent/policy/AndroidDevicePolicyGateway.kt`, `.../agent/net/CommandHandler.kt`
- Test: `CommandHandlerTest.kt`, `LockdownTest.kt` (gateway falso ganha `wipe`)

**Interfaces:**
- Consumes: `MessageType.WipePhone = 93` (Task 2).
- Produces: `WIPE_PHONE` de PC confiável → `COMMAND_ACK (true, "apagando")` e, 2 s depois, fora do thread do canal, `gateway.wipe()`; de não confiável, a conexão termina sem resposta (regra de hoje); sem ser dono → `COMMAND_ACK (false, "Este celular não foi preparado pelo Midview.")`. `wipe()`: `dpm.wipeDevice(WIPE_RESET_PROTECTION_DATA)` em API 34+, `dpm.wipeData(WIPE_RESET_PROTECTION_DATA)` antes.

- [ ] **Step 1: Testes que falham:** `wipe_from_trusted_acks_then_wipes` (executor injetado sem espera; `wipe` depois do ack), `wipe_from_untrusted_never_wipes`, `wipe_when_not_device_owner_says_why`.
- [ ] **Step 2:** Rodar → FAIL.
- [ ] **Step 3:** Implementar (atraso e executor injetáveis no `CommandHandler`).
- [ ] **Step 4:** `testDebugUnitTest` inteiro → PASS.
- [ ] **Step 5:** Commit `feat(android): apagar e preparar de novo`.

### Task 7: Roteiro novo com os prints

**Files:**
- Modify: `src/Midview.Master/PhoneSetupGuide.cs`, `src/Midview.Master/Assets/PrepararCelular/` (sai `redefinicao.png` e `bem-vindo.png`; entra `desenvolvedor.png`, print de Configurações → Configurações adicionais → Opções do desenvolvedor, e o desenho `conta-mi.png` passa a mostrar o botão do Google), `Midview.Master.csproj`
- Test: `tests/Midview.Master.Tests/PhoneSetupGuideTests.cs`

**Interfaces:**
- Produces: `PhoneSetupGuide.Steps` com os ids, na ordem: `numero`, `ligar`, `conta-google`, `desenvolvedor`, `chip-conta-mi`, `chaves`, `sair-contas`, `cabo`; `PhoneSetupGuide.Gather` (a tela "Antes de começar, separe:" da spec, objetivo 2); ajudas fixas `UnknownAccount` e `LockedWithPin` ("Celular travado com PIN"); `NotAppeared` (rede: mesmo Wi-Fi do PC, celular perto do roteador; o celular já confia neste PC). Sai `ResetWithoutPin` e o passo `seis-toques`.
- Textos: os da spec (objetivos 2, 3 e 5), com a dica da busca das Configurações ("desenvolvedor", "depuração", "conta") no "Deu errado?" dos passos de menu e nenhum caminho de MIUI 12/13 inventado. O passo `cabo` avisa da pergunta "Permitir depuração USB?" ("Sempre permitir deste computador" + Permitir) e da pergunta de instalar ("toque em Instalar").

- [ ] **Step 1:** Capturar `desenvolvedor.png` do Redmi 10C no cabo: `adb shell am start -a android.settings.APPLICATION_DEVELOPMENT_SETTINGS`, `adb exec-out screencap -p`, fechar a tela aberta (`am stack remove <id>`); borrar o que for pessoal. Só leitura: não mudar nenhuma chave.
- [ ] **Step 2: Testes que falham:** ordem dos ids; `conta-google` antes de `desenvolvedor`, `sair-contas` antes de `cabo`; todo passo (menos `numero`) tem `Taps` e pelo menos um `Help`; toda `Image` existe como recurso; todo `Target` dentro de 0–1; nenhum texto contém `Device Owner`, `APK`, `provision`, `adb`, `DPC`; `Gather` cita cabo, chip, agulha e a conta Google da escola.
- [ ] **Step 3:** `dotnet test tests/Midview.Master.Tests --filter FullyQualifiedName~PhoneSetupGuide` → FAIL.
- [ ] **Step 4:** Escrever o roteiro e trocar as imagens; medir os alvos nos prints (720×1650).
- [ ] **Step 5:** Mesmo comando → PASS.
- [ ] **Step 6:** Commit `feat(master): roteiro do celular pelo cabo`.

### Task 8: Janela do assistente pelo cabo

**Files:**
- Modify: `src/Midview.Master/PhoneSetupProgress.cs`, `PreparePhonesWindow.xaml`, `PreparePhonesWindow.xaml.cs`, `PreparePhonesWindow.Lists.cs`, `MainWindow.PreparePhones.cs`
- Create: `src/Midview.Master/PreparePhonesWindow.Cable.cs`
- Test: `tests/Midview.Master.Tests/PhoneSetupProgressTests.cs`

**Interfaces:**
- Consumes: `PhoneSetupGuide` (Task 7), `PhoneCable`/`CableProblem`/`CableStage`/`IAdb` (Task 4), `AgentApk` (Task 3), `PhoneWifiFile`, `PhoneTotalFile`, `Roster.NewAddresses` (via `MainWindow`).
- Produces:
  - `PhoneSetupProgress` com páginas `Wifi`, `Gather`, `Step(i)`, `Cable`, `CableFailed`, `Ready`, `NotAppeared`, `ConfirmRedo`: `Start(bool hasWifi, bool gathered)` (com Wi-Fi e já separado começa em `numero`); ao chegar em `cabo` e o `adb` ver o celular autorizado, `CableSeen()` → `Cable`; `CableStageReached(CableStage)`; `CableFailed(CableProblem)` → `CableFailed` com `Describe`, e "Voltar ao passo" leva ao passo de `BackToStep` (ou fica, se null) e "Tentar de novo" volta a `Cable`; `CablePrepared(DateTimeOffset at)` → `Ready`; `Tick(DateTimeOffset now)` em `Ready` vira `NotAppeared` depois de 5 min se `Appeared` não foi chamado; `Appeared()`; `StartAt(string number)` (depois de apagar) vai a `ligar`; `ConfirmRedo` como hoje (confirmado e online oferece apagar; desligado segue para `ligar`).
  - `PreparePhonesWindow(Func<int> phonesInClass, Func<IAdb> adb, Action<KnownMachine> addPhone, Func<KnownMachine, bool> isOnline, Func<string, Task<(bool Ok, string Detail)>>? wipe = null)`; a página `cabo` consulta o `adb` a cada 1 s (fora do thread da UI) e avança sozinha; `Cable` mostra a lista dos cinco itens da spec (objetivo 4) marcados pelo `CableStage`; `Ready` mostra "Pronto. Pode tirar o cabo." e, a cada 10 s por até 5 min, pede ao `MainWindow` uma busca na rede (`RefreshAddresses`) e chama `Appeared()` quando `isOnline` do celular novo fica verdadeiro. `AgentApk.GetAsync` roda ao abrir; `Offline`/`Invalid` mostram, antes do passo 1, "Sem internet: o Midview precisa baixar o app do celular uma vez." / "O app do celular baixado não é o do Midview. Tente mais tarde." e não deixam começar.
  - `MainWindow.PreparePhones`: `addPhone` = roster com `ReplacingSameName` (já existe) → salvar → quadradinho novo (desligado até a busca achar).

- [ ] **Step 1: Testes que falham:** `PhoneSetupProgressTests` — sem Wi-Fi começa em `Wifi`, depois `Gather` na primeira vez; com tudo, em `numero`; `CableSeen` só vale em `cabo`; `CableFailed(HasAccounts)` → texto "Ainda há uma conta neste celular." e voltar leva a `sair-contas`; `CableFailed(TooOld)` não oferece voltar; `CablePrepared` → `Ready`; `Tick` com 4 min fica em `Ready`, com 5 min vai a `NotAppeared`, com `Appeared()` antes nunca vai; `StartAt("07")` → `ligar`; `ReadyText` com e sem total.
- [ ] **Step 2:** `dotnet test tests/Midview.Master.Tests --filter FullyQualifiedName~PhoneSetupProgress` → FAIL.
- [ ] **Step 3:** Implementar a máquina de estados, a janela (`PreparePhonesWindow.Cable.cs` com o `adb` e a lista) e a ligação no `MainWindow`; `AdbProcess` descartado ao fechar a janela.
- [ ] **Step 4:** Mesmo comando → PASS; `dotnet test Midview.sln` → verde (fora as falhas ambientais).
- [ ] **Step 5: Conferência visual pelo harness** (pasta temporária, roster falso com 2 PCs e nenhum celular, `FakeAdb` roteirizado, janela aberta direto no harness): capturas de `Gather`, de cada passo, de `Cable` no meio, de `CableFailed` com `HasAccounts`, de `Ready` e de `NotAppeared`, a 560×860 e na tela de 1366×768 (sem rolagem horizontal). Salvar em `.superpowers/sdd/<este plano>/screens/` e mostrar ao professor.
- [ ] **Step 6:** Commit `feat(master): assistente de celulares pelo cabo`.

### Task 9: "Apagar e preparar de novo" com o caminho novo

**Files:**
- Modify: `src/Midview.Master/PhoneWipe.cs`, `src/Midview.Master/PreparePhonesWindow.Wipe.cs`, `MainWindow.PreparePhones.cs`
- Test: `tests/Midview.Master.Tests/PhoneWipeTests.cs`

**Interfaces:**
- Produces: `PhoneWipe.ConfirmText("Celular 07")` = `"O Celular 07 vai ser apagado e voltar de fábrica. Os alunos perdem o que estiver nele. Para prepará-lo de novo, vai precisar outra vez do chip e da conta Mi."`; depois de apagar com sucesso, `PreparePhonesWindow.StartAt(number)` (passo `ligar`, com a ajuda "Apareceu Bem-vindo" aberta); o item segue escondido sem `WizardReady`.

- [ ] **Step 1: Teste que falha:** o texto novo de `ConfirmText`; `Send` com `WipePhone = 93`.
- [ ] **Step 2:** `dotnet test tests/Midview.Master.Tests --filter "FullyQualifiedName~PhoneWipe|FullyQualifiedName~DeviceSupport"` → FAIL.
- [ ] **Step 3:** Implementar.
- [ ] **Step 4:** Mesmo comando → PASS.
- [ ] **Step 5:** Commit `feat(master): apagar e preparar de novo pelo cabo`.

### Task 10: Liberar e preparar de verdade no Redmi 10C (com o professor)

Passo com aparelho: **pára e pede ao professor** antes de apagar. Só depois da Task 9B do plano 1 (APK publicado com a chave real e `SignatureChecksum` real) e de uma versão com as Tasks 1–9 instalada pelo professor.

**Files:**
- Modify: `src/Midview.Master/PhoneProvisioning.cs` (`CableReady = true`), `PhoneSetupGuide.cs` e `Assets/PrepararCelular/*` (prints reais que faltarem, troca do desenho da conta Mi se houver foto)
- Test: `PhoneProvisioningTests.cs`, `PhoneSetupGuideTests.cs`

- [ ] **Step 1:** Com o APK publicado contendo as Tasks 5 e 6: `CableReady = true`; testes → PASS; commit `feat(master): liberar o assistente de celulares`.
- [ ] **Step 2:** Com o professor: apagar o Redmi 10C (ele confirma), seguir o assistente do começo ao fim com um número de teste combinado, anotar cada tela, o texto exato dos botões e o tempo de cada passo.
- [ ] **Step 3:** O professor confere no Midview dele que o "Celular NN" entrou sozinho, que o cabo deixou de funcionar no celular pronto (depuração desligada) e que "Apagar e preparar de novo" volta o celular à tela "Bem-vindo" sem pedir conta.
- [ ] **Step 4:** Ajustar textos, tempos e prints pelo que foi visto; testes do roteiro → PASS.
- [ ] **Step 5:** Commit `feat(master): ajustes da preparação de verdade do celular`.
