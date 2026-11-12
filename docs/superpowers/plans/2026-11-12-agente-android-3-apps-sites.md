# Agente Android, plano 3 de 4: bloquear apps e sites Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** O professor bloqueia apps e sites nos celulares da escola pelo Midview, com os mesmos modos dos PCs ("Bloquear só estes" / "Liberar só estes") e a mesma janela de sites.

**Architecture:** Apps: uma política só de celular (nomes de pacote Android, não `.exe`), mensagens novas na porta `28900`, o celular informa a lista de apps com ícone, o Device Owner suspende os apps bloqueados (`setPackagesSuspended`). Sites: o celular aceita as mesmas mensagens 7–10 dos PCs; os domínios viram a lista de bloqueio do Chrome (configuração gerenciada) e "Bloquear a internet" liga a VPN do próprio Midview como VPN "sempre ativa", com bloqueio, que segura o tráfego de todos os apps (o Midview fica de fora); outros navegadores ficam escondidos. No Master: janela de apps para celulares, envio e observação pelo `ThumbnailPoller`, selo no quadradinho, e as ações liberadas para celular.

**Tech Stack:** Kotlin (Android 10+, `DevicePolicyManager.setPackagesSuspended`, `setApplicationRestrictions`, `setApplicationHidden`, `setAlwaysOnVpnPackage` com um `VpnService`), JUnit 4 JVM; .NET 8 / WPF / xUnit; E2E em `tests/Midview.E2E`.

**Spec:** `docs/superpowers/specs/2026-11-11-agente-android-design.md` (seções 6, 7 e 8).

## Global Constraints

- Tudo na porta `28900` (`ConnectionClient.AgentPort`), só professor confiável; quadro e `CommandAck` como hoje (`CommandResult`: 1 byte + UTF-8; para as mensagens 7–10 o ack é só `[1]` ou `[0]`, como no Windows).
- Mensagens novas (C# e Kotlin): `SetPhoneAppBlock = 93` → `CommandAck`; `GetPhoneAppBlock = 94` → `PhoneAppBlockStatus = 95`; `GetPhoneApps = 96` → `PhoneApps = 97`.
- `PhoneAppPolicy` = mesmo leiaute de `AppBlockPolicy` (`ver(1)=1 | SavedAt int64 BE | modo(1) | count u16 BE | [len(1) + UTF-8]*`), modos 0 Nenhum / 1 Bloquear só estes / 2 Liberar só estes, no máximo 200 nomes de até 255 bytes; nomes = pacote Android (`^[a-zA-Z][a-zA-Z0-9_]*(\.[a-zA-Z0-9_]+)+$`), sem repetição; o mais novo (`SavedAt`) vence, com as mesmas respostas do `AppBlockCommandHandler` do Windows ("mantido o bloqueio mais novo, salvo em …", "outro bloqueio foi salvo no mesmo segundo…").
- Nunca suspensos (protegidos): o próprio Midview, a interface do sistema, a tela inicial em uso, Serviços do Google Play, WebView do Android, instalador de pacotes, configurações (já travadas pelo Device Owner), discador/emergência.
- `PhoneApps` = `ver(1)=1 | count u16 BE | [pkgLen(1) pkg | labelLen(1) rótulo UTF-8 (≤ 64 chars) | iconLen u16 BE | PNG 48×48]*`, no máximo 300 apps; só apps que o aluno consegue abrir (com ícone na tela inicial), sem os protegidos.
- Sites: `BlockDomain`/`UnblockDomain` com o texto que o professor digitou, normalizado como o `DomainNormalizer` do Windows (tirar `scheme://`, cortar em `/ ? # :`, minúsculas, nome DNS válido; inválido → `[0]`); bloquear o domínio e o `www.`; desbloquear algo ausente → `[1]`. O conjunto fica guardado no celular e volta ao ligar.
- "Bloquear a internet" no celular: Chrome com `URLBlocklist = ["*"]` e a VPN do próprio Midview como VPN "sempre ativa" do aparelho, com bloqueio (`setAlwaysOnVpnPackage` com lockdown). Ela recebe o tráfego de todos os apps e não deixa nada sair; o DNS dela é o próprio endereço, onde nada responde, então os nomes também não resolvem. O agente fica fora da VPN, e o Midview continua falando com o professor pela rede local (por IP). Desbloquear volta a VPN "sempre ativa" que havia antes (normalmente nenhuma). O DNS privado não é mexido, então o do plano 4 continua valendo. (DNS estrito para um nome que não existe não serve: o Android recusa um servidor de DNS que não responde.)
- Outros navegadores (todo app que abre `http/https`, menos o Chrome) ficam escondidos (`setApplicationHidden`) enquanto o celular for aparelho da escola.
- Textos em português, no tom do Midview; nenhum produto concorrente citado; commits sem `Co-Authored-By`.

## Review Focus

- Professor bloqueia um app que o aluno está usando: o app fecha/fica cinza na hora; desbloquear volta a abrir. (Task 2)
- "Liberar só estes" com lista vazia: só o Midview e o essencial do sistema funcionam; o celular não trava. (Task 2)
- App instalado depois do bloqueio em "Liberar só estes": já entra suspenso. (Task 2)
- Celular reinicia ou é encerrado pela MIUI: bloqueios de apps e sites continuam valendo. (Tasks 2 e 3)
- "Bloquear a internet" e depois desbloquear: Midview continua falando com o celular o tempo todo e a internet volta. (Task 3)

---

### Task 1: Protocolo dos apps do celular (C# e Kotlin)

**Files:**
- Create: `src/Midview.Protocol/PhoneAppPolicy.cs`, `src/Midview.Protocol/PhoneAppList.cs`; Kotlin `.../protocol/PhoneAppPolicy.kt`, `.../protocol/PhoneAppList.kt`
- Modify: `src/Midview.Protocol/MessageType.cs` e `MessageType.kt` (93–97)
- Test: `tests/Midview.Protocol.Tests/PhoneAppPolicyTests.cs`, `PhoneAppListTests.cs`; JVM `PhoneAppPolicyTest.kt`, `PhoneAppListTest.kt` (mesmos vetores dourados nos dois lados)

**Interfaces:**
- `PhoneAppPolicy(long SavedAt, AppBlockMode Mode, IReadOnlyList<string> Packages)` com `Encode()`, `Decode(byte[]) -> PhoneAppPolicy?`, `IsValidPackageName`, `Lifted(savedAt)`, `SameRulesAs`; Kotlin igual.
- `PhoneAppList.Encode(IReadOnlyList<PhoneApp>)`/`Decode` com `PhoneApp(string Package, string Label, byte[] IconPng)`.

- [ ] Testes (vetores dourados idênticos C#/Kotlin; limites; nomes inválidos; ordem de bytes BE) → falham → implementar → passam → commit `feat(protocol): apps do celular no protocolo`.

### Task 2: Bloqueio de apps no celular

**Files:**
- Create: `.../apps/AppBlockStore.kt` (política guardada), `.../apps/AppSuspender.kt` (decide o que suspender), `.../apps/InstalledApps.kt` (lista com ícones), `.../apps/PackageWatcher.kt` (app instalado/removido)
- Modify: `.../net/CommandHandler.kt` (93, 94, 96), `.../policy/DevicePolicyGateway.kt` (`setPackagesSuspended`), `AgentService.kt` (reaplicar ao iniciar)
- Test: JVM para `AppSuspender` (modos, protegidos, app novo), `AppBlockStore` (mais novo vence, mesmas respostas do Windows), handler

**Interfaces:**
- `AppSuspender.toSuspend(policy, launchable: Set<String>, protected: Set<String>) -> Set<String>`; o celular suspende o conjunto e tira a suspensão do resto.
- Respostas de `SetPhoneAppBlock`: "bloqueando N app(s)", "permitindo só o essencial [e N app(s)]", "nenhum app bloqueado", e as do "mais novo vence".

- [ ] Testes → falham → implementar → passam.
- [ ] No Redmi (aparelho da escola, versão de teste): bloquear um app aberto → some/fica cinza; "Liberar só estes" vazio; instalar um app depois → suspenso; "Limpar tudo" e reiniciar → bloqueio continua. Commit `feat(android): bloquear apps no celular`.

### Task 3: Bloqueio de sites no celular

**Files:**
- Create: `.../sites/SiteBlockStore.kt`, `.../sites/DomainNormalizer.kt`, `.../sites/ChromePolicy.kt` (monta a configuração gerenciada), `.../sites/InternetBlock.kt` (DNS estrito e volta), `.../sites/OtherBrowsers.kt`
- Modify: `.../net/CommandHandler.kt` (7–10), `DevicePolicyGateway` (`setApplicationRestrictions`, `setApplicationHidden`, `setGlobalPrivateDnsModeSpecifiedHost`/modo), `AgentService.kt`, `Lockdown.kt` (esconder outros navegadores)
- Test: JVM para normalização (vetores do `DomainNormalizer.cs`), conjunto guardado, montagem da política do Chrome (`URLBlocklist` com domínio e `www.`; `["*"]` na internet bloqueada), escolha do DNS a restaurar

- [ ] Testes → falham → implementar → passam.
- [ ] No Redmi: bloquear `youtube.com` → Chrome mostra a página de bloqueio; desbloquear → abre; "Bloquear a internet" → nenhum app navega, Midview continua vendo a tela; desbloquear → internet volta; reiniciar → estado mantido. Commit `feat(android): bloquear sites no celular`.

### Task 4: Master — apps e sites para celular

**Files:**
- Create: `src/Midview.Master/PhoneAppBlockClient.cs`, `PhoneAppBlockFile.cs` (política da turma para celulares), `PhoneAppBlockWindow.xaml(.cs)` (lista com ícone e marcação, os dois modos), `PhoneAppBlockText.cs` (textos e selo)
- Modify: `DeviceSupport.cs` (celular passa a aceitar `BlockPrograms` e `BlockSites`), `MainWindow.xaml.cs` ("Bloquear programas" abre a janela de celular quando a seleção só tem celulares; mista → janela dos PCs + aviso de celulares de fora), `ThumbnailPoller.cs` (para celular: enviar a política da turma quando mudar e observar a cada 30 s, como o `SendAppBlockIfChanged`/`ObserveAppBlock`), `SiteBlockWindow` (já manda 7–10 a todos os alvos — conferir que inclui celulares)
- Test: `PhoneAppBlockClientTests` (agente roteirizado), `PhoneAppBlockFileTests`, `PhoneAppBlockTextTests` (selo "🔒 N apps bloqueados" / "🔒 só N apps liberados"), `DeviceSupportTests`, `ThumbnailPollerTests` (celular recebe a política)

- [ ] Testes → falham → implementar → passam; solução inteira verde (fora as falhas conhecidas de ambiente).
- [ ] Prévia da janela com o harness de UI (pasta de dados temporária, sala falsa, nunca o Master real), capturas em `.superpowers/sdd/.../screens/`. Commit `feat(master): bloquear apps e sites nos celulares`.

### Task 5: Prova no celular de verdade e README

- [ ] Com a cópia de teste do Master e o Redmi: bloquear apps pela janela nova (lista com ícones do Redmi), "Liberar só estes", sites e internet; conferir selos e avisos. Anotar o resultado no fim deste plano.
- [ ] README "Celulares": o que o professor consegue bloquear. Commit `docs: resultado do bloqueio de apps e sites no celular`.

---

## Resultado (Task 5)

Prova feita em 11/11/2026, das 23:40 às 23:52, no Redmi 10C da escola (Android 13, MIUI 14, Device Owner, versão de teste do agente), pela rede Wi-Fi.

- **Como:** um programa de teste abriu a `MainWindow` do Master fora da tela, com uma cópia da pasta de teste `Midview-teste-celular` (identidade de teste e sala só com o celular).
- Ele clicou nos botões da barra, da `PhoneAppBlockWindow` e da `SiteBlockWindow` como o professor faria.
- O Master instalado e a sala de verdade não foram usados.
- As capturas estão em `.superpowers/sdd/2026-11-12-agente-android-3-apps-sites/screens/task5-*.png`.

### Provado no celular

- **Lista de apps:** "Bloquear programas" (sala só com o celular) abriu "Bloquear apps no celular" em 4 s, com os 63 apps que o aluno pode abrir, todos com ícone.
- **Bloquear só estes, com a Calculadora:** a Calculadora estava aberta no celular.
  - "Aplicar bloqueio" respondeu "✓ Celular 35F7: bloqueando 1 app(s)" em 0,7 s.
  - A Calculadora fechou na hora e ficou cinza (`suspended=true`). Ao tocar nela aparece "Não é possível abrir este app".
  - Só ela ficou suspensa.
- **Liberar só estes, com a Calculadora:** a resposta foi "permitindo só o essencial e 1 app(s)".
  - 62 apps ficaram suspensos e cinza na tela inicial, e a Calculadora abre.
  - As Configurações, o telefone, a tela inicial, a Play Store e a Segurança continuam abrindo.
  - O Midview não foi suspenso e continuou respondendo e mandando a tela.
- **Selo no quadradinho:** "🔒 1 app bloqueado" e depois "🔒 só 1 app liberado" apareceram menos de 1 s depois de aplicar, e sumiram com "Liberar tudo". O `ThumbnailPoller` reenviou o bloqueio da turma e leu o estado do celular.
- **Sites:** "Bloquear sites", a mesma janela dos PCs, bloqueou `youtube.com` ("✓ Celular 35F7: youtube.com bloqueado").
  - O celular guardou `internet=0` e `youtube.com` e registrou "Site block applied: 1 site(s), internet free.".
  - A configuração que o agente passa ao Chrome não pode ser lida pelo cabo sem root.
- **Bloquear a internet:** a resposta foi "✓ Celular 35F7: internet bloqueada", em 0,4 s.
  - A VPN do Midview ligou como "sempre ativa", com bloqueio (`tun0` e o ícone VPN na barra).
  - O ping para 8.8.8.8 ficou sem resposta, e HTTP e HTTPS não conectaram.
  - O Midview continuou vendo o celular: 42 telas em 2 min 16 s, com no máximo 3,6 s entre elas. O estado foi lido em 179 ms, e a busca na rede achou o celular.
- **Liberar a internet:** a VPN desligou e a VPN "sempre ativa" voltou a nenhuma. O DNS privado não foi mexido, e o ping e o HTTP (204) voltaram.
- **Volta ao normal:** "Liberar o site" e "Liberar tudo". No fim: nenhum app suspenso, nenhum site bloqueado, internet livre e sem VPN.

### Achado

Durante "Bloquear a internet", os nomes dos sites ainda são resolvidos, mas nenhuma conexão sai.

- O Android consulta o DNS pela rede Wi-Fi quando a VPN não tem servidor de DNS.
- A internet fica cortada; só a consulta de nomes passa.
- Fica para a revisão final decidir se vale pôr um DNS dentro da VPN.
- Corrigido na rodada final de correções: o DNS da VPN é o próprio endereço dela. Com a internet bloqueada, `ping wikipedia.org` falha na hora ("unknown host"), e o Midview continua vendo o celular.

### Pendente

- **Página de bloqueio do Chrome.** O Chrome do celular nunca foi aberto, e a primeira tela dele pede para aceitar os termos do Google. Aceitar fica com o usuário. Passos:
  1. No celular, abrir o Chrome uma vez e aceitar a primeira tela, sem entrar em conta.
  2. No Midview, abrir "Bloquear sites", escrever `youtube.com` e clicar em "Bloquear o site".
  3. No Chrome do celular, abrir youtube.com. Deve aparecer a página de bloqueio do Chrome.
  4. Clicar em "Bloquear a internet". Qualquer site deve mostrar a página de bloqueio.
  5. Liberar a internet e o site.
  - Se o Chrome não bloquear, ver `chrome://policy`. Se a `URLBlocklist` aparecer com erro, a correção é trocar uma linha no `ChromePolicy`.
- **Bateria e calor numa aula de verdade:** não medidos. O celular ficou na tomada, com 100 % de bateria e 27–28 °C.
- **Celular que não é Device Owner** (selo sem nada suspenso): não deu para conferir, porque o único celular de teste é Device Owner.
