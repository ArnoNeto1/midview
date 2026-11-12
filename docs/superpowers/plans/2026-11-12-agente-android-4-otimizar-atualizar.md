# Agente Android, plano 4 de 4: otimizar e atualizar pela rede Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O celular da escola fica leve e sem anúncios sozinho (apps de anúncios escondidos e DNS sem anúncios, com volta automática se a rede bloquear), e o professor atualiza o app do Midview nos celulares pela rede, sem cabo.

**Architecture:** Otimizar: mensagem nova na porta `28900`; o Device Owner esconde a lista de anúncios (`setApplicationHidden`) e liga o DNS privado `dns.adguard-dns.com` só depois de conferir a internet, voltando ao automático se a rede bloquear (na hora ou depois, ao trocar de rede). Atualizar: as mesmas mensagens da atualização dos PCs (`NetworkUpdate`: oferta, pedaços, situação), com o APK no lugar do zip; o agente confere pacote, versão e assinatura e instala a si mesmo pelo `PackageInstaller` sem toque do aluno. O bloqueio da internet do plano 3 é uma VPN (não o DNS privado), então o DNS sem anúncios não precisa ser restaurado por ele. No Master: grupo **CELULARES** na barra (só aparece se a sala tem celular) com **Otimizar** e **Atualizar celulares**; o APK vem da versão do GitHub igual à do Master, conferido pela soma SHA-256 publicada.

**Tech Stack:** Kotlin (Android 10+, `DevicePolicyManager.setApplicationHidden`, `setGlobalPrivateDnsModeSpecifiedHost`/`Opportunistic`, `ConnectivityManager.NetworkCallback`, `PackageInstaller`), JUnit 4 JVM; .NET 8 / WPF / xUnit.

**Spec:** `docs/superpowers/specs/2026-11-11-agente-android-design.md` (seções 3 e 8, "Restrições" e "Escolhas minhas": atualizar pela rede). Ledgers dos planos 1–3 em `.superpowers/sdd/2026-11-1*-agente-android-*/progress.md` continuam valendo.

## Global Constraints

- Tudo na porta `28900` (`ConnectionClient.AgentPort`), só professor confiável; agente antigo responde `CommandAck` a mensagem que não conhece → o Master diz "o Midview deste celular é de uma versão anterior".
- Mensagens novas (C# e Kotlin): `OptimizePhone = 98` (sem conteúdo) → `PhoneOptimizeReport = 99` (JSON UTF-8: `optimizedAt` ISO-8601 UTC ou `null`, `hidden` string[], `failed` string[], `dns` um de `on`/`offNoInternet`/`offNetworkBlocks`/`offInternetBlocked`/`offError`, `dnsHost` string), vetores dourados iguais nos dois lados.
- Lista de anúncios (esconder só as que existirem): `com.miui.msa.global`, `com.miui.analytics`, `com.xiaomi.mipicks`, `com.xiaomi.glgm`, `com.miui.yellowpage`, `com.facebook.services`, `com.facebook.system`, `com.facebook.appmanager`, `com.amazon.appmanager`, `com.miui.android.fashiongallery`, `com.xiaomi.discover`, `com.miui.miservice`, `com.miui.bugreport`, `com.google.android.projection.gearhead`, `com.google.android.apps.subscriptions.red`.
- Nunca esconder (vence a lista acima): Chrome `com.android.chrome`, o WebView em uso, `com.google.android.gms`, `com.android.vending`, a tela inicial em uso, `com.android.settings`, a câmera do sistema, o próprio agente, `com.miui.securitycenter`, `com.miui.powerkeeper`, `com.xiaomi.joyose`, `com.miui.notification`, e todo o conjunto protegido do bloqueio de apps (plano 3, Task 2).
- DNS sem anúncios: `dns.adguard-dns.com`, modo estrito pelo Device Owner, chamado fora da thread principal. Primeiro pela API `setGlobalPrivateDnsModeSpecifiedHost`; se o teste dela (só o primeiro endereço, às vezes um IPv6 sem rota) falhar mas o agente alcançar o servidor por IPv4 (TLS na 853 com o nome conferido), grava `private_dns_specifier` e depois `private_dns_mode = hostname` por `setGlobalSetting` (permitido ao Device Owner do Android 10 ao 16), com a mesma validação de 20 s e o mesmo vigia (correção da Task 2). Antes: o agente confere a internet (HTTPS `https://connectivitycheck.gstatic.com/generate_204` → 204, 5 s). Depois: até 20 s para a rede ativa ficar `NET_CAPABILITY_VALIDATED` com `LinkProperties.isPrivateDnsActive` e `privateDnsServerName == dns.adguard-dns.com`; se não, volta ao automático (`setGlobalPrivateDnsModeOpportunistic`) e registra `offNetworkBlocks`. Com a internet bloqueada pelo professor (VPN do plano 3): não mexe no DNS, registra `offInternetBlocked` e tenta de novo ao desbloquear.
- Rede trocada depois: DNS estrito ligado e rede ativa sem `VALIDATED` por 30 s → volta ao automático, `offNetworkBlocks`. Com DNS desligado por `offNoInternet`/`offNetworkBlocks`, nova tentativa a cada comando de professor confiável, no máximo uma vez a cada 30 min.
- A primeira otimização roda sozinha no primeiro comando de professor confiável depois do pareamento (`optimizedAt == null`), em segundo plano; "Otimizar" repete (idempotente) e devolve o relatório.
- Atualizar: mensagens `UpdateOffer 25` → `UpdateAccepted 26` ou `CommandAck`; `FileChunk 18` até o tamanho oferecido → `CommandAck`; `GetUpdateStatus 27` → `UpdateStatus 28` (`UpdateStatusReport`), como em `src/Midview.Protocol/NetworkUpdate.cs` e no tratamento de `src/Midview.Agent/AgentServer.cs`. Para celular: `Fingerprint` = `ZipSha256` = SHA-256 do APK, `ZipSize` = `UncompressedSize` = tamanho do APK, no máximo `PhoneUpdate.MaxApkSize = 64 MiB`. No `UpdateStatusReport` do celular: `fingerprint` = SHA-256 hex do APK instalado, `build` = `versionName`; estados `receiving`, `verifying`, `applying`, `committed`, `failed`.
- O agente só instala APK com o mesmo nome de pacote, `versionCode` maior que o instalado e o mesmo certificado de assinatura; oferta com o SHA-256 igual ao instalado → `CommandAck` sucesso "já está nesta versão". Uma tentativa por vez.
- O Master pega o APK da versão do GitHub com a mesma versão dele: `https://api.github.com/repos/ArnoNeto1/midview/releases/tags/v{versão}`, asset `MidviewAgent.apk`, só de `github.com` por HTTPS, conferido pelo `digest` `sha256:` do asset (como `TeacherUpdate`); guardado em `MasterData.Folder\celular\MidviewAgent-{versão}.apk`.
- Barra: grupo **CELULARES** com **Otimizar** e **Atualizar celulares**, visível só se a sala tem ao menos um celular (sala só de PCs fica igual). Alvo: os celulares da seleção; sem celular selecionado, todos os celulares da sala.
- "Preparar celular" fica onde está (Máquinas da sala): a preparação pelo cabo é de outra sessão; este plano não mexe na `PreparePhoneWindow`.
- As travas `no_install_unknown_sources(_globally)` e `no_modify_accounts` (entraram na correção final do plano 3) não podem impedir a atualização do próprio agente: a Task 3 prova isso no aparelho (e, se a build debug ficar sem elas, liga-as durante o teste).
- Textos em português, no tom do Midview; nenhum produto concorrente citado (o nome do servidor de DNS aparece só como endereço técnico); commits sem `Co-Authored-By`.

## Review Focus

- Rede do laboratório bloqueia DNS privado (porta 853): o celular continua com internet e o professor vê "a rede bloqueou o DNS sem anúncios". (Task 2: teste da decisão com validação que não chega)
- Celular otimizado vai para outra rede que bloqueia o DNS: em até ~30 s a internet volta sozinha. (Task 2: teste do vigia)
- Atualização interrompida (Wi-Fi cai no meio do envio, celular reinicia durante a instalação): o agente antigo continua rodando, tentativa `failed`, nova tentativa funciona; nunca um celular sem agente. (Task 3)
- APK da mesma versão, mais antigo ou assinado com outra chave: recusado com texto claro, nada instalado. (Task 3: tabela do `ApkCheck`)
- "Otimizar" com a internet bloqueada pelo professor: DNS não muda até desbloquear; ao desbloquear, liga sozinho. (Task 2)

---

### Task 1: Protocolo de otimizar e atualizar celular (C# e Kotlin)

**Files:**
- Create: `src/Midview.Protocol/PhoneOptimizeReport.cs`, `src/Midview.Protocol/PhoneUpdate.cs` (`MaxApkSize`); Kotlin `.../protocol/PhoneOptimizeReport.kt`, `.../protocol/NetworkUpdateCodec.kt` (decodifica `UpdateOffer` de 96 bytes BE; codifica `UpdateStatusReport` em JSON igual ao C#)
- Modify: `MessageType.cs` e `MessageType.kt` (98, 99; e 18, 25–28 no Kotlin se ainda não existem)
- Test: `tests/Midview.Protocol.Tests/PhoneOptimizeReportTests.cs`; JVM `PhoneOptimizeReportTest.kt`, `NetworkUpdateCodecTest.kt` (oferta gerada pelo `NetworkUpdate.EncodeOffer` do C# e relatório que o `UpdateStatusReport.TryDecode` do C# aceita, como vetores dourados)

**Interfaces:**
- Produces: `PhoneOptimizeReport(DateTimeOffset? OptimizedAt, IReadOnlyList<string> Hidden, IReadOnlyList<string> Failed, string Dns, string DnsHost)` com `Encode()`/`TryDecode(byte[], out …)` e constantes `PhoneDnsStates.On`… ; Kotlin igual. `PhoneUpdate.MaxApkSize = 64L * 1024 * 1024`.

- [ ] Testes (vetores dourados idênticos, JSON inválido → falso, estado de DNS desconhecido → falso) → falham → implementar → passam → commit `feat(protocol): otimizar e atualizar celular no protocolo`.

### Task 2: Otimização no celular

**Files:**
- Create: `.../optimize/BloatList.kt` (listas e `toHide(installed: Set<String>, neverHide: Set<String>): Set<String>`), `.../optimize/AdblockDns.kt` (decisão pura + aplicação), `.../optimize/DnsWatcher.kt` (vigia de rede), `.../optimize/OptimizeStore.kt` (relatório guardado, gravação atômica), `.../optimize/Optimizer.kt`
- Modify: `.../net/CommandHandler.kt` (98; gatilho da primeira otimização e da nova tentativa de DNS), `.../policy/DevicePolicyGateway.kt` (modo/host do DNS privado), `.../sites/InternetBlock.kt` (ao desbloquear, tentar o DNS pendente), `AgentService.kt` (iniciar o vigia)
- Test: `BloatListTest`, `AdblockDnsTest`, `DnsWatcherTest`, `OptimizeStoreTest`, handler 98 (só confiável)

**Interfaces:**
- Produces: `Optimizer.run(): PhoneOptimizeReport`; `AdblockDns.decide(internetOk: Boolean, internetBlockedByTeacher: Boolean, setResult: Int?, validated: Boolean?): DnsOutcome`; `DnsWatcher.shouldFallBack(strictOnOurHost: Boolean, unvalidatedFor: Duration): Boolean` (30 s); `OptimizeStore.retryDue(now, lastDnsAttempt): Boolean` (30 min).

- [ ] Testes: só esconde o que existe; "nunca esconder" vence; sem internet → `offNoInternet` sem mexer; internet bloqueada → `offInternetBlocked` sem mexer; `PRIVATE_DNS_SET_ERROR_HOST_NOT_SERVING` → `offNetworkBlocks`; validação não chega em 20 s → volta ao automático + `offNetworkBlocks`; validou → `on`; vigia volta ao automático após 30 s sem validar; nova tentativa só após 30 min → falham → implementar → passam.
- [ ] No Redmi (debug): "Otimizar" pelo roteiro → apps da lista que existem ficam escondidos (anotar quais), `settings get global private_dns_mode` = `hostname` e `private_dns_specifier` = `dns.adguard-dns.com`, um domínio de anúncio não resolve e um site comum abre; com a internet bloqueada (plano 3) → `offInternetBlocked`, desbloquear → DNS liga sozinho; reiniciar → tudo mantido. Deixar o celular otimizado. Commit `feat(android): otimizar o celular (anúncios e DNS)`.

### Task 3: Atualizar o agente pela rede

**Files:**
- Create: `.../update/ApkUpdateStore.kt` (tentativa guardada), `.../update/ApkUpload.kt` (recebe os pedaços em `filesDir/update/`, confere tamanho e SHA-256), `.../update/ApkCheck.kt` (decisão pura), `.../update/SelfInstaller.kt` (`PackageInstaller`, sessão silenciosa do Device Owner; `setRequireUserAction(USER_ACTION_NOT_REQUIRED)` no Android 12+), `.../update/PackageReplacedReceiver.kt` (`MY_PACKAGE_REPLACED` → inicia o `AgentService`)
- Modify: `.../net/CommandHandler.kt` (25, 18, 27), `AgentService.kt` (ao iniciar, fecha a tentativa: `versionCode` atual == esperado → `committed`, senão `failed`; calcula o SHA-256 do APK instalado em segundo plano, `ready` falso até lá), `AndroidManifest.xml` (`REQUEST_INSTALL_PACKAGES`, receptor)
- Test: `ApkCheckTest` (pacote diferente, versão igual/menor, certificado diferente, ok), `ApkUploadTest` (tamanho ou SHA-256 errado → `failed` e arquivo apagado), handler (SHA igual ao instalado → "já está nesta versão"; tentativa em andamento → recusa; status JSON), fechamento da tentativa ao iniciar

**Interfaces:**
- Consumes: `NetworkUpdateCodec` e `PhoneUpdate.MaxApkSize` (Task 1).
- Produces: comportamento das mensagens 25/18/27 igual ao do Agent dos PCs para o `UpdateClient` do Master (Task 4).

- [ ] Testes → falham → implementar → passam.
- [ ] No Redmi: instalar uma build debug e mandar, por um roteiro com as mensagens 25/18/27, outra build debug igual com `versionCode` maior (versão de teste por propriedade do Gradle): instala sem toque, agente volta em ≤ 20 s com a versão nova, continua Device Owner, a acessibilidade continua ligada (tela sem pedir "Iniciar agora"), bloqueios de apps/sites/internet continuam; mandar a mesma versão → "já está nesta versão"; uma mais antiga e uma assinada com outra chave → recusadas; cortar o Wi-Fi no meio do envio → `failed`, agente antigo rodando, repetir funciona; com as travas de instalar apps desconhecidos e de contas ligadas, a atualização continua funcionando. Deixar o celular na build mais nova. Commit `feat(android): atualizar o app do celular pela rede`.

### Task 4: Master — grupo CELULARES (Otimizar e Atualizar celulares)

**Files:**
- Create: `src/Midview.Master/PhoneOptimizeClient.cs`, `PhoneOptimizeText.cs`, `PhoneOptimizeWindow.xaml(.cs)`, `PhoneAgentApk.cs` (APK do GitHub, cache e conferência), `PhoneUpdateText.cs`, `PhoneUpdateWindow.xaml(.cs)`
- Modify: `UpdateClient.cs` (`Push` recebe `IUpdatePayload`), `UpdatePackage.cs` (implementa `IUpdatePayload`), `DeviceSupport.cs` (ações `Optimize` e `UpdatePhones`, só celular), `MainWindow.xaml(.cs)` (grupo CELULARES visível só com celular na sala; menu do quadradinho do celular com as duas ações)
- Test: `PhoneOptimizeClientTests` (agente roteirizado; agente antigo), `PhoneOptimizeTextTests`, `PhoneAgentApkTests` (asset sem digest/fora do github.com/soma errada → erro com texto do professor; cache reaproveitado), `PhoneUpdateTextTests`, `UpdateClientTests` (APK por `IUpdatePayload`), `DeviceSupportTests`

**Interfaces:**
- Consumes: `PhoneOptimizeReport`, `PhoneUpdate.MaxApkSize` (Task 1); mensagens 25/18/27 do celular (Task 3).
- Produces: `public interface IUpdatePayload { string ZipPath { get; } long ZipSize { get; } UpdateOffer Offer(byte[] attemptId); }`; `PhoneAgentApk.FromFile(string path)`, `PhoneAgentApk.ForVersionAsync(Version, HttpClient, string cacheFolder, CancellationToken)`, `string Sha256Hex`; `PhoneOptimizeClient.Optimize(KnownMachine, X509Certificate2, int port = ConnectionClient.AgentPort) -> PhoneOptimizeReport?` (`null` = agente antigo; tempo de leitura 90 s: o pior caso no celular é ~40 s); `PhoneUpdateText.Plan(UpdateStatusReport? status, string apkSha256Hex) -> PhoneUpdateStep` (`UpToDate`, `Push`, `OldAgent`).

- [ ] Testes → falham → implementar → passam; solução inteira verde (fora as falhas conhecidas de ambiente).
- [ ] Textos: Otimizar → "N apps de anúncios escondidos · DNS sem anúncios ligado" / "… DNS sem anúncios desligado: a rede bloqueou" / "…: o celular estava sem internet" / "…: a internet está bloqueada; liga sozinho ao desbloquear". Atualizar → por celular "já está na versão X", "atualizando…", "atualizado para X", "não atualizou: <motivo>", e o aviso de baixar do GitHub quando o APK ainda não está guardado.
- [ ] Prévia com o harness de UI (pasta de dados temporária, sala falsa com PCs e celulares roteirizados, nunca o Master real, nunca a tela real): barra com o grupo CELULARES, sala só de PCs sem o grupo, janela do Otimizar com resultados, janela do Atualizar durante e depois. Capturas em `.superpowers/sdd/2026-11-12-agente-android-4-otimizar-atualizar/screens/`. Commit `feat(master): otimizar e atualizar os celulares`.

### Task 5: Prova no celular de verdade e README

- [ ] Com o código do Master (harness, identidade de teste em cópia de `%LOCALAPPDATA%\Midview-teste-celular`) e o Redmi: "Otimizar" pela janela nova e conferir no celular; "Atualizar celulares" com um APK local (`PhoneAgentApk.FromFile`) de versão maior, conferir a versão nova no celular e na janela. Anotar o resultado no fim deste plano (o que ficou pendente: baixar do GitHub só depois da primeira versão publicada com o APK).
- [ ] README "Celulares": "Otimizar" e "Atualizar celulares" em poucas linhas; manter "Em breve" e não mexer nos passos de preparação. Commit `docs: resultado de otimizar e atualizar os celulares`.

## Resultado (Task 5)

Prova feita em 12/11/2026, das 06:50 às 06:53, no Redmi 10C da escola (Android 13, MIUI 14, Device Owner, versão de teste do agente), pela rede Wi-Fi.

- **Como:** um programa de teste abriu a `MainWindow` do Master fora da tela, com uma cópia da pasta de teste `Midview-teste-celular` (identidade de teste e sala só com o celular).
- Ele clicou em "Otimizar" e "Atualizar celulares" na barra, e nos botões da `PhoneOptimizeWindow`, da `PhoneUpdateWindow` e da `SiteBlockWindow`, como o professor faria.
- Na janela do Atualizar, a origem do APK foi trocada por `PhoneApkSource.File` (APK local) antes de clicar em "Atualizar"; o GitHub não foi chamado.
- O Master instalado e a sala de verdade não foram usados.
- As capturas estão em `.superpowers/sdd/2026-11-12-agente-android-4-otimizar-atualizar/screens/task5-*.png`.

### Provado no celular

- **Barra:** com o celular na sala, o grupo CELULARES aparece com "Otimizar" e "Atualizar celulares", os dois ativos. Com a janela em 1366 × 728, o grupo fica numa segunda linha da barra, como na prévia da Task 4.
- **Otimizar:** a janela "Otimizar o celular" respondeu em cerca de 1 s: "✓ 15 apps de anúncios escondidos · DNS sem anúncios ligado", "Pronto.".
  - No celular, `optimize.txt` foi regravado com os mesmos 15 apps, `dns=on`, e o DNS privado continuou `hostname` / `dns.adguard-dns.com`.
- **Otimizar com a internet bloqueada:** depois de "Bloquear a internet" na janela de sites, "Otimizar" respondeu "! 15 apps de anúncios escondidos · DNS sem anúncios desligado: a internet está bloqueada; liga sozinho ao desbloquear".
  - O agente registrou `dns=offInternetBlocked` e não mexeu no DNS privado.
- **Ao desbloquear:** "Liberar a internet" desligou a VPN, e 0,25 s depois o agente registrou "Ad-free DNS: on.", sem clicar em nada. "Otimizar de novo" voltou a dizer "DNS sem anúncios ligado".
- **Atualizar celulares, de 0.0.53 para 0.0.54** (APK de teste do mesmo código, com `-PmidviewVersion=0.0.54`):
  - Antes, a janela mostrou "versão 0.0.53".
  - Depois de clicar em "Atualizar": "enviando o app… 0%" até "100%" com a barra em 0,5 s, "atualizando…" em 1 s e "✓ atualizado para 0.0.54" em 8,1 s, com "Terminado: o celular está na versão nova.".
  - No celular: `versionName=0.0.54`, `versionCode=54`, instalado por ele mesmo, sem nenhum toque no celular; tentativa `committed` "atualizado para 0.0.54".
  - Continua Device Owner, com a acessibilidade ligada. Os 15 apps continuam escondidos, o DNS continua ligado e validado, as travas são as mesmas, e nenhum app ou site ficou bloqueado. Comparando o estado antes e depois, só mudaram o pacote, a tentativa e o processo do agente.
  - A tela do celular voltou sozinha: o quadradinho ficou uma vez "Sem resposta" (3 s, enquanto o agente reiniciava) e voltou a receber a tela a cada ~3 s.
- **De novo com o mesmo APK:** "✓ já está na versão 0.0.54" em 0,2 s, sem enviar nada.
- **Otimizar na versão nova:** "✓ 15 apps de anúncios escondidos · DNS sem anúncios ligado".

### Achado

- Com a internet bloqueada, a janela diz "DNS sem anúncios desligado" e "O celular não foi otimizado", mas o DNS privado continua no servidor sem anúncios (o agente não mexe nele de propósito) e os 15 apps estão escondidos. É só o texto; com a internet bloqueada nada sai mesmo, e ao desbloquear o DNS é conferido de novo. Fica para a revisão final decidir se vale outro texto quando o DNS já estava ligado.

### Pendente

- **Baixar o APK do GitHub:** só depois da primeira versão publicada com o `MidviewAgent.apk`. Até lá, a janela diz que o app ainda não está neste PC e, ao clicar em "Atualizar", que ele não pôde ser obtido (o download só foi testado com um GitHub simulado).
- **Vigia do DNS:** a volta ao DNS automático depois de 30 s sem validar, e quando o DNS ligado deixa a internet sem resposta, só foram testadas na JVM.
- **Lugar dos botões na barra:** esperando a escolha do usuário (segunda linha, a linha dos filtros ou um botão "Celulares" com menu).
- **Bateria e calor numa aula de verdade:** não medidos. O celular ficou na tomada, com 100 % de bateria e 27 °C.
- **Vários celulares:** só há um celular de teste, então "um celular primeiro e os outros depois" só foi visto na prévia da Task 4 e nos testes.
- **Instalação recusada pelo Android** (e o prazo de 3 min): só testados na JVM.
