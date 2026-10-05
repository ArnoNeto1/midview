# Midview Site Blocking Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Progress note (2026-10-05):** This plan has been carried out: site blocking is implemented and validated live (see the spec's status line). The steps below were never ticked off along the way, so their empty checkboxes don't mean the work is still pending.

**Goal:** Give `MidviewAgent` a full-internet block/unblock toggle and a specific-domain block list, both controllable from `MidviewMaster`, both surviving an Agent restart or reboot.

**Architecture:** `MidviewAgent` gains its own TCP+TLS listener (`AgentServer`, separate port from `MidviewSession`'s), reusing `Midview.Protocol`'s existing `SecureChannel`/`TrustStore`/`MessageFramer`. A `SiteBlockCommandHandler` orchestrates a `HostsFileManager` (per-domain blocking via the hosts file), a `NetshFirewallController` (full-internet block via Windows Firewall rules), and a persisted `SiteBlockState`, reapplying both at Agent startup. `MidviewMaster` gains a short-lived-connection `AgentCommandClient` and four buttons in `MainWindow`.

**Tech Stack:** C# / .NET 8 (Windows-only, `net8.0-windows`), same as the core sub-project. No new third-party dependencies.

**Spec:** `C:\Users\Neto\Desktop\Midview\docs\superpowers\specs\2026-09-22-site-blocking-design.md`

## Global Constraints

- Windows only, LAN only — same as the core sub-project.
- No GPL or copyleft-licensed source or libraries.
- Replicate Treezeview's two site-blocking mechanisms (full-internet-block via firewall, specific-domain block via hosts file) as two independent features — see the spec's Revision Note. No domain categories, no scheduling.
- `MidviewAgent` needs a *persisted* identity certificate (unlike `MidviewSession`'s per-run one), since it's a long-running service the teacher configures once.
- Live testing that touches the real firewall (full-internet-block specifically) happens on a second machine (Aluno-25), not the machine you're working from — same reasoning as the core sub-project's input-lock testing.

---

### Task 1: Protocol — new message types

**Files:**
- Modify: `src/Midview.Protocol/MessageType.cs`

**Interfaces:**
- Produces: five new `MessageType` values used by every later task in this plan: `BlockInternet`, `UnblockInternet`, `BlockDomain`, `UnblockDomain`, `CommandAck`.

- [ ] **Step 1: Add the new values**

```csharp
namespace Midview.Protocol;

public enum MessageType : byte
{
    Hello = 1,
    Heartbeat = 2,
    StartControl = 3,
    StopControl = 4,
    FrameTile = 5,
    InputEvent = 6,
    BlockInternet = 7,
    UnblockInternet = 8,
    BlockDomain = 9,
    UnblockDomain = 10,
    CommandAck = 11,
}
```

- [ ] **Step 2: Verify the solution still builds**

Run: `dotnet build`
Expected: succeeds with 0 errors (adding enum values can't break existing code that doesn't reference them).

- [ ] **Step 3: Commit**

```bash
git add src/Midview.Protocol/MessageType.cs
git commit -m "Add site-blocking message types to the protocol"
```

---

### Task 2: Agent — persisted identity certificate

**Files:**
- Create: `src/Midview.Agent/AgentIdentity.cs`
- Test: `tests/Midview.Agent.Tests/AgentIdentityTests.cs`

**Interfaces:**
- Consumes: `SelfSignedCertificate.Create(string) -> X509Certificate2` from `Midview.Protocol`.
- Produces: `AgentIdentity.LoadOrCreate(string certificatePath) -> X509Certificate2`, `AgentIdentity.DefaultCertificatePath` (a `string` property). Task 8's `AgentServerWorker` calls `LoadOrCreate(DefaultCertificatePath)`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Midview.Agent.Tests/AgentIdentityTests.cs
using Midview.Agent;
using Xunit;

public class AgentIdentityTests
{
    [Fact]
    public void LoadOrCreate_NoExistingFile_CreatesAndPersistsCertificate()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"midview-test-{Guid.NewGuid()}.pfx");
        try
        {
            var certificate = AgentIdentity.LoadOrCreate(tempPath);

            Assert.True(File.Exists(tempPath));
            Assert.NotEmpty(certificate.Thumbprint);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void LoadOrCreate_ExistingFile_ReturnsSameCertificateOnSecondCall()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"midview-test-{Guid.NewGuid()}.pfx");
        try
        {
            var first = AgentIdentity.LoadOrCreate(tempPath);
            var second = AgentIdentity.LoadOrCreate(tempPath);

            Assert.Equal(first.Thumbprint, second.Thumbprint);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Midview.Agent.Tests`
Expected: FAIL to compile — `AgentIdentity` not found.

- [ ] **Step 3: Implement `AgentIdentity.cs`**

```csharp
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Agent;

public static class AgentIdentity
{
    public static string DefaultCertificatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Midview", "agent-identity.pfx");

    public static X509Certificate2 LoadOrCreate(string certificatePath)
    {
        if (File.Exists(certificatePath))
        {
            return new X509Certificate2(certificatePath);
        }

        var certificate = SelfSignedCertificate.Create("MidviewAgent");
        var directory = Path.GetDirectoryName(certificatePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllBytes(certificatePath, certificate.Export(X509ContentType.Pfx));
        return certificate;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Midview.Agent.Tests`
Expected: PASS (2/2)

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Agent/AgentIdentity.cs tests/Midview.Agent.Tests/AgentIdentityTests.cs
git commit -m "Add persisted Agent identity certificate"
```

---

### Task 3: Agent — persisted trusted-masters list

**Files:**
- Create: `src/Midview.Agent/TrustedMastersFile.cs`
- Test: `tests/Midview.Agent.Tests/TrustedMastersFileTests.cs`

**Interfaces:**
- Produces: `TrustedMastersFile.Load(string path) -> IReadOnlyList<string>`. Task 8's `AgentServerWorker` calls this to build the `TrustStore` `AgentServer` authenticates incoming Masters against.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Midview.Agent.Tests/TrustedMastersFileTests.cs
using Midview.Agent;
using Xunit;

public class TrustedMastersFileTests
{
    [Fact]
    public void Load_NoFile_ReturnsEmptyList()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid()}.json");

        var result = TrustedMastersFile.Load(path);

        Assert.Empty(result);
    }

    [Fact]
    public void Load_ExistingFile_ReturnsThumbprintList()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "[\"ABCD1234\", \"EFGH5678\"]");

            var result = TrustedMastersFile.Load(path);

            Assert.Equal(new[] { "ABCD1234", "EFGH5678" }, result);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Midview.Agent.Tests`
Expected: FAIL to compile — `TrustedMastersFile` not found.

- [ ] **Step 3: Implement `TrustedMastersFile.cs`**

```csharp
using System.Text.Json;

namespace Midview.Agent;

public static class TrustedMastersFile
{
    public static IReadOnlyList<string> Load(string path)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<string>();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Midview.Agent.Tests`
Expected: PASS (4/4 including Task 2's tests)

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Agent/TrustedMastersFile.cs tests/Midview.Agent.Tests/TrustedMastersFileTests.cs
git commit -m "Add persisted trusted-masters list for the Agent"
```

---

### Task 4: Agent — persisted site-block state

**Files:**
- Create: `src/Midview.Agent/SiteBlockState.cs`
- Test: `tests/Midview.Agent.Tests/SiteBlockStateTests.cs`

**Interfaces:**
- Produces: `SiteBlockState` with public settable properties `bool InternetBlocked` and `HashSet<string> BlockedDomains`, plus `SiteBlockState.Load(string statePath) -> SiteBlockState` and instance method `Save(string statePath)`. Task 7's `SiteBlockCommandHandler` is constructed with a `SiteBlockState` and calls `Save` on every change.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Midview.Agent.Tests/SiteBlockStateTests.cs
using Midview.Agent;
using Xunit;

public class SiteBlockStateTests
{
    [Fact]
    public void Load_NoExistingFile_ReturnsEmptyDefaultState()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"midview-test-{Guid.NewGuid()}.json");

        var state = SiteBlockState.Load(tempPath);

        Assert.False(state.InternetBlocked);
        Assert.Empty(state.BlockedDomains);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsState()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), $"midview-test-{Guid.NewGuid()}.json");
        try
        {
            var state = new SiteBlockState { InternetBlocked = true };
            state.BlockedDomains.Add("youtube.com");
            state.Save(tempPath);

            var loaded = SiteBlockState.Load(tempPath);

            Assert.True(loaded.InternetBlocked);
            Assert.Contains("youtube.com", loaded.BlockedDomains);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Midview.Agent.Tests`
Expected: FAIL to compile — `SiteBlockState` not found.

- [ ] **Step 3: Implement `SiteBlockState.cs`**

```csharp
using System.Text.Json;

namespace Midview.Agent;

public sealed class SiteBlockState
{
    public bool InternetBlocked { get; set; }
    public HashSet<string> BlockedDomains { get; set; } = new();

    public static SiteBlockState Load(string statePath)
    {
        if (!File.Exists(statePath))
        {
            return new SiteBlockState();
        }

        var json = File.ReadAllText(statePath);
        return JsonSerializer.Deserialize<SiteBlockState>(json) ?? new SiteBlockState();
    }

    public void Save(string statePath)
    {
        var directory = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(statePath, JsonSerializer.Serialize(this));
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Midview.Agent.Tests`
Expected: PASS (6/6)

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Agent/SiteBlockState.cs tests/Midview.Agent.Tests/SiteBlockStateTests.cs
git commit -m "Add persisted site-block state (internet-blocked flag + domain set)"
```

---

### Task 5: Agent — hosts file reconciliation

**Files:**
- Create: `src/Midview.Agent/HostsFileManager.cs`
- Test: `tests/Midview.Agent.Tests/HostsFileManagerTests.cs`

**Interfaces:**
- Produces: `HostsFileManager.Reconcile(string existingContent, IEnumerable<string> domains) -> string` (pure, testable), `HostsFileManager.ExpandBareAndWww(IEnumerable<string> domains) -> HashSet<string>`, and `HostsFileManager.ApplyToFile(string hostsFilePath, IEnumerable<string> domains)` (reads, reconciles, writes, flushes DNS). Task 7's `SiteBlockCommandHandler` calls `ApplyToFile`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Midview.Agent.Tests/HostsFileManagerTests.cs
using Midview.Agent;
using Xunit;

public class HostsFileManagerTests
{
    [Fact]
    public void Reconcile_NoDomains_RemovesExistingBlockAndAddsNothing()
    {
        var existing = "127.0.0.1 localhost\n# BEGIN MIDVIEW SITE BLOCK\n127.0.0.1 old.com\n# END MIDVIEW SITE BLOCK\n";

        var result = HostsFileManager.Reconcile(existing, Array.Empty<string>());

        Assert.Equal("127.0.0.1 localhost\n", result);
    }

    [Fact]
    public void Reconcile_OneDomain_ExpandsToBareAndWww()
    {
        var result = HostsFileManager.Reconcile("127.0.0.1 localhost\n", new[] { "youtube.com" });

        Assert.Contains("127.0.0.1 youtube.com", result);
        Assert.Contains("127.0.0.1 www.youtube.com", result);
    }

    [Fact]
    public void Reconcile_WwwDomainGiven_AlsoBlocksBareForm()
    {
        var result = HostsFileManager.Reconcile("127.0.0.1 localhost\n", new[] { "www.youtube.com" });

        Assert.Contains("127.0.0.1 youtube.com", result);
        Assert.Contains("127.0.0.1 www.youtube.com", result);
    }

    [Fact]
    public void Reconcile_PreservesLinesOutsideManagedBlock()
    {
        var existing = "127.0.0.1 localhost\n192.168.1.1 router.local\n";

        var result = HostsFileManager.Reconcile(existing, new[] { "youtube.com" });

        Assert.Contains("127.0.0.1 localhost", result);
        Assert.Contains("192.168.1.1 router.local", result);
    }

    [Fact]
    public void Reconcile_CalledTwiceWithDifferentSets_ReplacesOldBlockEntirely()
    {
        var afterFirst = HostsFileManager.Reconcile("127.0.0.1 localhost\n", new[] { "old.com" });
        var afterSecond = HostsFileManager.Reconcile(afterFirst, new[] { "new.com" });

        Assert.DoesNotContain("old.com", afterSecond);
        Assert.Contains("new.com", afterSecond);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Midview.Agent.Tests`
Expected: FAIL to compile — `HostsFileManager` not found.

- [ ] **Step 3: Implement `HostsFileManager.cs`**

```csharp
using System.Diagnostics;

namespace Midview.Agent;

public static class HostsFileManager
{
    private const string BeginMarker = "# BEGIN MIDVIEW SITE BLOCK";
    private const string EndMarker = "# END MIDVIEW SITE BLOCK";

    public static string Reconcile(string existingContent, IEnumerable<string> domains)
    {
        var lines = existingContent.Split('\n');
        var kept = new List<string>();
        var insideBlock = false;

        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed == BeginMarker)
            {
                insideBlock = true;
                continue;
            }
            if (trimmed == EndMarker)
            {
                insideBlock = false;
                continue;
            }
            if (!insideBlock)
            {
                kept.Add(line);
            }
        }

        while (kept.Count > 0 && kept[^1].Trim().Length == 0)
        {
            kept.RemoveAt(kept.Count - 1);
        }

        var allDomains = ExpandBareAndWww(domains);

        if (allDomains.Count == 0)
        {
            return string.Join('\n', kept) + "\n";
        }

        var blockLines = new List<string> { BeginMarker };
        foreach (var domain in allDomains.OrderBy(d => d, StringComparer.Ordinal))
        {
            blockLines.Add($"127.0.0.1 {domain}");
        }
        blockLines.Add(EndMarker);

        return string.Join('\n', kept) + "\n" + string.Join('\n', blockLines) + "\n";
    }

    public static HashSet<string> ExpandBareAndWww(IEnumerable<string> domains)
    {
        var expanded = new HashSet<string>();
        foreach (var domain in domains)
        {
            expanded.Add(domain);
            expanded.Add(domain.StartsWith("www.", StringComparison.Ordinal)
                ? domain[4..]
                : "www." + domain);
        }
        return expanded;
    }

    public static void ApplyToFile(string hostsFilePath, IEnumerable<string> domains)
    {
        var existingContent = File.Exists(hostsFilePath) ? File.ReadAllText(hostsFilePath) : string.Empty;
        var newContent = Reconcile(existingContent, domains);
        File.WriteAllText(hostsFilePath, newContent);

        using var flushProcess = Process.Start(new ProcessStartInfo("ipconfig.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            ArgumentList = { "/flushdns" },
        });
        flushProcess?.WaitForExit();
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Midview.Agent.Tests`
Expected: PASS (11/11) — `Reconcile`/`ExpandBareAndWww` are pure functions with no file I/O, so all 5 new tests run without touching disk; `ApplyToFile` itself (which does touch disk and shells out to `ipconfig.exe`) has no automated test here and gets manually verified in Task 10.

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Agent/HostsFileManager.cs tests/Midview.Agent.Tests/HostsFileManagerTests.cs
git commit -m "Add hosts-file reconciliation for domain blocking"
```

---

### Task 6: Agent — firewall commands and controller

**Files:**
- Create: `src/Midview.Agent/IFirewallController.cs`
- Create: `src/Midview.Agent/FirewallCommands.cs`
- Create: `src/Midview.Agent/NetshFirewallController.cs`
- Test: `tests/Midview.Agent.Tests/FirewallCommandsTests.cs`

**Interfaces:**
- Produces: `IFirewallController` with `bool ApplyBlock()` / `void ApplyUnblock()`; `FirewallCommands.BuildBlockCommands(int agentPort, int sessionPort) -> IReadOnlyList<string[]>` and `FirewallCommands.BuildUnblockCommands() -> IReadOnlyList<string[]>` (pure, testable); `NetshFirewallController` implementing `IFirewallController` by actually running `netsh.exe`. Task 7's `SiteBlockCommandHandler` depends on `IFirewallController`; tests pass a fake, production code passes `NetshFirewallController`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Midview.Agent.Tests/FirewallCommandsTests.cs
using System.Linq;
using Midview.Agent;
using Xunit;

public class FirewallCommandsTests
{
    [Fact]
    public void BuildBlockCommands_IncludesLocalNetworkAllowRule()
    {
        var commands = FirewallCommands.BuildBlockCommands(agentPort: 5900, sessionPort: 5901);

        Assert.Contains(commands, c => c.Contains("localip=192.168.0.0/16"));
    }

    [Fact]
    public void BuildBlockCommands_AllowsBothAgentAndSessionPorts()
    {
        var commands = FirewallCommands.BuildBlockCommands(agentPort: 5900, sessionPort: 5901);

        Assert.Contains(commands, c => c.Contains("remoteport=5900"));
        Assert.Contains(commands, c => c.Contains("remoteport=5901"));
    }

    [Fact]
    public void BuildBlockCommands_EndsWithBlockAllOutboundRule()
    {
        var commands = FirewallCommands.BuildBlockCommands(agentPort: 5900, sessionPort: 5901);

        Assert.Contains(commands, c => c.Contains("action=block"));
    }

    [Fact]
    public void BuildUnblockCommands_DeletesEveryRuleBuildBlockCommandsCreates()
    {
        var blockCommands = FirewallCommands.BuildBlockCommands(agentPort: 5900, sessionPort: 5901);
        var unblockCommands = FirewallCommands.BuildUnblockCommands();

        var blockedRuleNames = blockCommands
            .Select(c => c.First(arg => arg.StartsWith("name=")))
            .OrderBy(n => n)
            .ToList();
        var unblockedRuleNames = unblockCommands
            .Select(c => c.First(arg => arg.StartsWith("name=")))
            .OrderBy(n => n)
            .ToList();

        Assert.Equal(blockedRuleNames, unblockedRuleNames);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Midview.Agent.Tests`
Expected: FAIL to compile — `FirewallCommands` not found.

- [ ] **Step 3: Implement `IFirewallController.cs`**

```csharp
namespace Midview.Agent;

public interface IFirewallController
{
    bool ApplyBlock();
    void ApplyUnblock();
}
```

- [ ] **Step 4: Implement `FirewallCommands.cs`**

```csharp
namespace Midview.Agent;

public static class FirewallCommands
{
    private const string RuleNamePrefix = "MidviewSiteBlock";

    public static IReadOnlyList<string[]> BuildBlockCommands(int agentPort, int sessionPort)
    {
        return new[]
        {
            new[] { "advfirewall", "firewall", "add", "rule",
                $"name={RuleNamePrefix}_AllowLocalNetwork", "dir=out", "action=allow",
                "localip=192.168.0.0/16", "remoteip=192.168.0.0/16" },
            new[] { "advfirewall", "firewall", "add", "rule",
                $"name={RuleNamePrefix}_AllowAgentPort", "dir=out", "action=allow",
                "protocol=TCP", $"remoteport={agentPort}" },
            new[] { "advfirewall", "firewall", "add", "rule",
                $"name={RuleNamePrefix}_AllowSessionPort", "dir=out", "action=allow",
                "protocol=TCP", $"remoteport={sessionPort}" },
            new[] { "advfirewall", "firewall", "add", "rule",
                $"name={RuleNamePrefix}_BlockAllOutbound", "dir=out", "action=block" },
        };
    }

    public static IReadOnlyList<string[]> BuildUnblockCommands()
    {
        return new[]
        {
            new[] { "advfirewall", "firewall", "delete", "rule", $"name={RuleNamePrefix}_AllowLocalNetwork" },
            new[] { "advfirewall", "firewall", "delete", "rule", $"name={RuleNamePrefix}_AllowAgentPort" },
            new[] { "advfirewall", "firewall", "delete", "rule", $"name={RuleNamePrefix}_AllowSessionPort" },
            new[] { "advfirewall", "firewall", "delete", "rule", $"name={RuleNamePrefix}_BlockAllOutbound" },
        };
    }
}
```

- [ ] **Step 5: Implement `NetshFirewallController.cs`**

```csharp
using System.Diagnostics;

namespace Midview.Agent;

public sealed class NetshFirewallController : IFirewallController
{
    private readonly int _agentPort;
    private readonly int _sessionPort;

    public NetshFirewallController(int agentPort, int sessionPort)
    {
        _agentPort = agentPort;
        _sessionPort = sessionPort;
    }

    public bool ApplyBlock()
    {
        foreach (var args in FirewallCommands.BuildBlockCommands(_agentPort, _sessionPort))
        {
            if (RunNetsh(args) != 0)
            {
                return false;
            }
        }
        return true;
    }

    public void ApplyUnblock()
    {
        // Sent unconditionally, including when no block was ever applied --
        // "no matching rule" is the expected common case here, not a
        // failure, so every command is attempted regardless of the
        // previous one's exit code (mirrors Treezeview's own FirewallController
        // reasoning for the equivalent unblock call).
        foreach (var args in FirewallCommands.BuildUnblockCommands())
        {
            RunNetsh(args);
        }
    }

    private static int RunNetsh(string[] args)
    {
        var startInfo = new ProcessStartInfo("netsh.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }
        using var process = Process.Start(startInfo);
        process!.WaitForExit();
        return process.ExitCode;
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/Midview.Agent.Tests`
Expected: PASS (15/15) — `NetshFirewallController` itself (which really runs `netsh.exe`) has no automated test; it gets manually verified in Task 10.

- [ ] **Step 7: Commit**

```bash
git add src/Midview.Agent/IFirewallController.cs src/Midview.Agent/FirewallCommands.cs src/Midview.Agent/NetshFirewallController.cs tests/Midview.Agent.Tests/FirewallCommandsTests.cs
git commit -m "Add firewall command builders and netsh-based controller for full-internet block"
```

---

### Task 7: Agent — site-block command handler

**Files:**
- Create: `src/Midview.Agent/SiteBlockCommandHandler.cs`
- Test: `tests/Midview.Agent.Tests/SiteBlockCommandHandlerTests.cs`

**Interfaces:**
- Consumes: `SiteBlockState` (Task 4), `HostsFileManager.ApplyToFile` (Task 5), `IFirewallController` (Task 6), `MessageType` (Task 1).
- Produces: `SiteBlockCommandHandler(SiteBlockState state, string statePath, string hostsFilePath, IFirewallController firewallController)` with `bool Handle(MessageType type, byte[] payload)` and `bool ReconcileAtStartup()` (returns whether the firewall reconcile succeeded — added after Task 7's own review found the original `void` signature silently discarded that failure). Task 8's `AgentServer` calls `Handle` per incoming message; `AgentServerWorker` calls `ReconcileAtStartup()` once at startup and logs a warning if it returns `false`.

- [ ] **Step 1: Write the failing tests**

```csharp
// tests/Midview.Agent.Tests/SiteBlockCommandHandlerTests.cs
using System.Text;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class FakeFirewallController : IFirewallController
{
    public int ApplyBlockCalls { get; private set; }
    public int ApplyUnblockCalls { get; private set; }
    public bool BlockShouldSucceed { get; set; } = true;

    public bool ApplyBlock()
    {
        ApplyBlockCalls++;
        return BlockShouldSucceed;
    }

    public void ApplyUnblock() => ApplyUnblockCalls++;
}

public class SiteBlockCommandHandlerTests
{
    private static (SiteBlockCommandHandler handler, string statePath, string hostsPath, FakeFirewallController firewall) CreateHandler()
    {
        var statePath = Path.Combine(Path.GetTempPath(), $"midview-state-{Guid.NewGuid()}.json");
        var hostsPath = Path.Combine(Path.GetTempPath(), $"midview-hosts-{Guid.NewGuid()}.txt");
        File.WriteAllText(hostsPath, "127.0.0.1 localhost\n");
        var firewall = new FakeFirewallController();
        var handler = new SiteBlockCommandHandler(new SiteBlockState(), statePath, hostsPath, firewall);
        return (handler, statePath, hostsPath, firewall);
    }

    [Fact]
    public void Handle_BlockInternet_AppliesFirewallBlockAndPersistsState()
    {
        var (handler, statePath, _, firewall) = CreateHandler();

        var result = handler.Handle(MessageType.BlockInternet, Array.Empty<byte>());

        Assert.True(result);
        Assert.Equal(1, firewall.ApplyBlockCalls);
        Assert.True(SiteBlockState.Load(statePath).InternetBlocked);
    }

    [Fact]
    public void Handle_BlockInternet_FirewallFails_DoesNotPersistBlockedState()
    {
        var (handler, statePath, _, firewall) = CreateHandler();
        firewall.BlockShouldSucceed = false;

        var result = handler.Handle(MessageType.BlockInternet, Array.Empty<byte>());

        Assert.False(result);
        Assert.False(SiteBlockState.Load(statePath).InternetBlocked);
    }

    [Fact]
    public void Handle_UnblockInternet_AppliesFirewallUnblockAndPersistsState()
    {
        var (handler, statePath, _, firewall) = CreateHandler();
        handler.Handle(MessageType.BlockInternet, Array.Empty<byte>());

        var result = handler.Handle(MessageType.UnblockInternet, Array.Empty<byte>());

        Assert.True(result);
        Assert.Equal(1, firewall.ApplyUnblockCalls);
        Assert.False(SiteBlockState.Load(statePath).InternetBlocked);
    }

    [Fact]
    public void Handle_BlockDomain_AddsDomainAndRewritesHostsFile()
    {
        var (handler, statePath, hostsPath, _) = CreateHandler();

        var result = handler.Handle(MessageType.BlockDomain, Encoding.UTF8.GetBytes("youtube.com"));

        Assert.True(result);
        Assert.Contains("youtube.com", SiteBlockState.Load(statePath).BlockedDomains);
        Assert.Contains("127.0.0.1 youtube.com", File.ReadAllText(hostsPath));
    }

    [Fact]
    public void Handle_UnblockDomain_RemovesDomainAndRewritesHostsFile()
    {
        var (handler, statePath, hostsPath, _) = CreateHandler();
        handler.Handle(MessageType.BlockDomain, Encoding.UTF8.GetBytes("youtube.com"));

        var result = handler.Handle(MessageType.UnblockDomain, Encoding.UTF8.GetBytes("youtube.com"));

        Assert.True(result);
        Assert.DoesNotContain("youtube.com", SiteBlockState.Load(statePath).BlockedDomains);
        Assert.DoesNotContain("youtube.com", File.ReadAllText(hostsPath));
    }

    [Fact]
    public void ReconcileAtStartup_PersistedDomainsAndBlockState_ReappliesBoth()
    {
        var (handler, statePath, hostsPath, _) = CreateHandler();
        handler.Handle(MessageType.BlockDomain, Encoding.UTF8.GetBytes("youtube.com"));
        handler.Handle(MessageType.BlockInternet, Array.Empty<byte>());
        // Simulate a fresh Agent process: reload persisted state into a new handler.
        var reloadedState = SiteBlockState.Load(statePath);
        var freshFirewall = new FakeFirewallController();
        var freshHandler = new SiteBlockCommandHandler(reloadedState, statePath, hostsPath, freshFirewall);

        freshHandler.ReconcileAtStartup();

        Assert.Equal(1, freshFirewall.ApplyBlockCalls);
        Assert.Contains("127.0.0.1 youtube.com", File.ReadAllText(hostsPath));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/Midview.Agent.Tests`
Expected: FAIL to compile — `SiteBlockCommandHandler` not found.

- [ ] **Step 3: Implement `SiteBlockCommandHandler.cs`**

```csharp
using System.Text;
using Midview.Protocol;

namespace Midview.Agent;

public sealed class SiteBlockCommandHandler
{
    private readonly SiteBlockState _state;
    private readonly string _statePath;
    private readonly string _hostsFilePath;
    private readonly IFirewallController _firewallController;

    public SiteBlockCommandHandler(
        SiteBlockState state, string statePath, string hostsFilePath, IFirewallController firewallController)
    {
        _state = state;
        _statePath = statePath;
        _hostsFilePath = hostsFilePath;
        _firewallController = firewallController;
    }

    public bool Handle(MessageType type, byte[] payload)
    {
        switch (type)
        {
            case MessageType.BlockInternet:
                if (!_firewallController.ApplyBlock())
                {
                    return false;
                }
                _state.InternetBlocked = true;
                _state.Save(_statePath);
                return true;

            case MessageType.UnblockInternet:
                _firewallController.ApplyUnblock();
                _state.InternetBlocked = false;
                _state.Save(_statePath);
                return true;

            case MessageType.BlockDomain:
                _state.BlockedDomains.Add(Encoding.UTF8.GetString(payload));
                HostsFileManager.ApplyToFile(_hostsFilePath, _state.BlockedDomains);
                _state.Save(_statePath);
                return true;

            case MessageType.UnblockDomain:
                _state.BlockedDomains.Remove(Encoding.UTF8.GetString(payload));
                HostsFileManager.ApplyToFile(_hostsFilePath, _state.BlockedDomains);
                _state.Save(_statePath);
                return true;

            default:
                return false;
        }
    }

    public void ReconcileAtStartup()
    {
        HostsFileManager.ApplyToFile(_hostsFilePath, _state.BlockedDomains);
        if (_state.InternetBlocked)
        {
            _firewallController.ApplyBlock();
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/Midview.Agent.Tests`
Expected: PASS (21/21)

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Agent/SiteBlockCommandHandler.cs tests/Midview.Agent.Tests/SiteBlockCommandHandlerTests.cs
git commit -m "Add site-block command handler orchestrating firewall, hosts file, and persisted state"
```

---

### Task 8: Agent — network listener and service wiring

**Files:**
- Create: `src/Midview.Agent/AgentServer.cs`
- Create: `src/Midview.Agent/AgentServerWorker.cs`
- Modify: `src/Midview.Agent/Program.cs`

**Interfaces:**
- Consumes: `SecureChannel`/`TrustStore` (`Midview.Protocol`), `AgentIdentity` (Task 2), `TrustedMastersFile` (Task 3), `SiteBlockState` (Task 4), `NetshFirewallController` (Task 6), `SiteBlockCommandHandler` (Task 7).
- Produces: `AgentServer(TcpListener, X509Certificate2, TrustStore, SiteBlockCommandHandler)` with `Run(CancellationToken)`; `AgentServerWorker : BackgroundService` wiring everything together and calling `ReconcileAtStartup()` once before accepting connections. Nothing later in this plan depends on this task; it's the deployable entry point for this sub-project.

- [ ] **Step 1: Implement `AgentServer.cs`**

```csharp
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Agent;

public sealed class AgentServer
{
    private readonly TcpListener _listener;
    private readonly X509Certificate2 _serverCertificate;
    private readonly TrustStore _masterTrustStore;
    private readonly SiteBlockCommandHandler _commandHandler;

    public AgentServer(
        TcpListener listener, X509Certificate2 serverCertificate, TrustStore masterTrustStore,
        SiteBlockCommandHandler commandHandler)
    {
        _listener = listener;
        _serverCertificate = serverCertificate;
        _masterTrustStore = masterTrustStore;
        _commandHandler = commandHandler;
    }

    public void Run(CancellationToken cancellationToken)
    {
        _listener.Start();
        while (!cancellationToken.IsCancellationRequested)
        {
            var tcpClient = _listener.AcceptTcpClient();
            HandleClient(tcpClient);
        }
    }

    private void HandleClient(TcpClient tcpClient)
    {
        using var channel = SecureChannel.AuthenticateAsServer(tcpClient, _serverCertificate, _masterTrustStore);
        var message = channel.Receive();
        var success = _commandHandler.Handle(message.Type, message.Payload);
        channel.Send(new Message(MessageType.CommandAck, new[] { (byte)(success ? 1 : 0) }));
    }
}
```

- [ ] **Step 2: Implement `AgentServerWorker.cs`**

```csharp
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Midview.Protocol;

namespace Midview.Agent;

public sealed class AgentServerWorker : BackgroundService
{
    public const int AgentPort = 5900;
    public const int SessionPort = 5901;

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var programDataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Midview");
        Directory.CreateDirectory(programDataDir);

        var certificate = AgentIdentity.LoadOrCreate(AgentIdentity.DefaultCertificatePath);
        var trustedMasters = TrustedMastersFile.Load(Path.Combine(programDataDir, "trusted-masters.json"));
        var statePath = Path.Combine(programDataDir, "site-block-state.json");
        var state = SiteBlockState.Load(statePath);
        var hostsFilePath = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\System32\drivers\etc\hosts");
        var firewallController = new NetshFirewallController(AgentPort, SessionPort);
        var commandHandler = new SiteBlockCommandHandler(state, statePath, hostsFilePath, firewallController);

        if (!commandHandler.ReconcileAtStartup())
        {
            Console.WriteLine("Warning: failed to reapply the full-internet-block firewall rules at startup — the persisted state says it should be on, but the firewall reconcile failed.");
        }

        var server = new AgentServer(
            new TcpListener(IPAddress.Any, AgentPort), certificate, new TrustStore(trustedMasters), commandHandler);

        return Task.Run(() => server.Run(stoppingToken), stoppingToken);
    }
}
```

- [ ] **Step 3: Register the new hosted service in `Program.cs`**

Modify `src/Midview.Agent/Program.cs` — add one line to the existing `.ConfigureServices(...)` block, alongside the existing `services.AddHostedService<MidviewAgentWorker>();` line:

```csharp
services.AddHostedService<AgentServerWorker>();
```

- [ ] **Step 4: Verify the project builds**

Run: `dotnet build src/Midview.Agent`
Expected: succeeds with 0 errors.

- [ ] **Step 5: Manually verify** (needs a real service install and a real Master connection — deferred to Task 10's end-to-end validation, on Aluno-25, with the project owner present)

- [ ] **Step 6: Commit**

```bash
git add src/Midview.Agent/AgentServer.cs src/Midview.Agent/AgentServerWorker.cs src/Midview.Agent/Program.cs
git commit -m "Add Agent network listener for site-blocking commands"
```

---

### Task 9: Master — command client and UI

**Files:**
- Create: `src/Midview.Master/AgentCommandClient.cs`
- Modify: `src/Midview.Master/MainWindow.xaml`
- Modify: `src/Midview.Master/MainWindow.xaml.cs`

**Interfaces:**
- Consumes: `SecureChannel`/`TrustStore`/`Message`/`MessageType` (`Midview.Protocol`), the `_masterCertificate` field already present in `MainWindow` (added when the core sub-project's thumbprint display was built).
- Produces: `AgentCommandClient.SendCommand(string hostAddress, int port, string targetThumbprint, X509Certificate2 masterCertificate, MessageType commandType, byte[] payload) -> bool`. This is the last task before end-to-end validation.

- [ ] **Step 1: Implement `AgentCommandClient.cs`**

```csharp
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using Midview.Protocol;

namespace Midview.Master;

public static class AgentCommandClient
{
    public static bool SendCommand(
        string hostAddress, int port, string targetThumbprint, X509Certificate2 masterCertificate,
        MessageType commandType, byte[] payload)
    {
        using var tcpClient = new TcpClient();
        tcpClient.Connect(hostAddress, port);
        using var channel = SecureChannel.AuthenticateAsClient(
            tcpClient, hostAddress, masterCertificate, new TrustStore(new[] { targetThumbprint }));

        channel.Send(new Message(commandType, payload));
        var ack = channel.Receive();
        return ack.Type == MessageType.CommandAck && ack.Payload.Length > 0 && ack.Payload[0] == 1;
    }
}
```

- [ ] **Step 2: Add the site-blocking section to `MainWindow.xaml`**

Add this `StackPanel` block just before the closing `</StackPanel>` tag that wraps the existing Connect UI, and bump the `Window`'s `Height` from `320` to `520`:

```xml
<Separator Margin="0,16,0,16" />
<TextBlock Text="Agent's certificate thumbprint:" Margin="0,0,0,4" />
<TextBox x:Name="AgentThumbprintTextBox" Margin="0,0,0,8" />
<TextBlock Text="Domain (for Block/Unblock Domain):" Margin="0,0,0,4" />
<TextBox x:Name="DomainTextBox" Margin="0,0,0,8" />
<WrapPanel>
    <Button Content="Block Internet" Click="OnBlockInternetClicked" Margin="0,0,8,4" />
    <Button Content="Unblock Internet" Click="OnUnblockInternetClicked" Margin="0,0,8,4" />
    <Button Content="Block Domain" Click="OnBlockDomainClicked" Margin="0,0,8,4" />
    <Button Content="Unblock Domain" Click="OnUnblockDomainClicked" Margin="0,0,0,4" />
</WrapPanel>
<TextBlock x:Name="SiteBlockStatusTextBlock" Margin="0,4,0,0" />
```

- [ ] **Step 3: Add the handler methods to `MainWindow.xaml.cs`**

```csharp
private void OnBlockInternetClicked(object sender, RoutedEventArgs e) =>
    SendAgentCommand(MessageType.BlockInternet, Array.Empty<byte>());

private void OnUnblockInternetClicked(object sender, RoutedEventArgs e) =>
    SendAgentCommand(MessageType.UnblockInternet, Array.Empty<byte>());

private void OnBlockDomainClicked(object sender, RoutedEventArgs e) =>
    SendAgentCommand(MessageType.BlockDomain, Encoding.UTF8.GetBytes(DomainTextBox.Text));

private void OnUnblockDomainClicked(object sender, RoutedEventArgs e) =>
    SendAgentCommand(MessageType.UnblockDomain, Encoding.UTF8.GetBytes(DomainTextBox.Text));

private void SendAgentCommand(MessageType commandType, byte[] payload)
{
    var success = AgentCommandClient.SendCommand(
        HostAddressTextBox.Text, AgentServerWorkerAgentPort, AgentThumbprintTextBox.Text,
        _masterCertificate, commandType, payload);
    SiteBlockStatusTextBlock.Text = success ? "OK" : "Failed";
}

private const int AgentServerWorkerAgentPort = 5900; // must match Midview.Agent.AgentServerWorker.AgentPort
```

Add `using System.Text;` to the top of the file alongside the existing `using` lines.

- [ ] **Step 4: Verify the project builds**

Run: `dotnet build src/Midview.Master`
Expected: succeeds with 0 errors.

- [ ] **Step 5: Commit**

```bash
git add src/Midview.Master/AgentCommandClient.cs src/Midview.Master/MainWindow.xaml src/Midview.Master/MainWindow.xaml.cs
git commit -m "Add Master UI and client for site-blocking commands"
```

---

### Task 10: End-to-end manual validation

No new files — this task runs the whole feature and confirms it actually works.

- [ ] **Step 1: Publish and deploy** `Midview.Agent` to Aluno-25 (same `dotnet publish -r win-x64 --self-contained true` pattern used for `Midview.Session` in the core sub-project), install it as a Windows Service there (`sc.exe create MidviewAgent binPath= "...\Midview.Agent.exe"`, `sc.exe start MidviewAgent`).
- [ ] **Step 2: Get the Agent's thumbprint.** Since `AgentServerWorker` doesn't print it anywhere yet (unlike `Midview.Session`'s `Program.cs`), read it off disk after first start: on Aluno-25, run a one-off script or use the already-built `Midview.Master` to attempt a connection (it will fail, but you can inspect `%ProgramData%\Midview\agent-identity.pfx`'s thumbprint via `certutil -dump` from an elevated prompt) — or simplest, temporarily add a `Console.WriteLine(certificate.Thumbprint)` in `AgentServerWorker.ExecuteAsync` right after loading it, matching the same manual-testing pattern `Midview.Session`'s `Program.cs` already uses, and remove it once you've captured the value.
- [ ] **Step 3: Manually create `trusted-masters.json`** on Aluno-25 at `%ProgramData%\Midview\trusted-masters.json` containing `["<your MidviewMaster's thumbprint>"]` (get the Master's own thumbprint from its UI, same as connecting to a Session), then restart the `MidviewAgent` service so it picks up the new trust list.
- [ ] **Step 4: From `Midview.Master`**, enter Aluno-25's host address and the Agent's thumbprint, and click **Block Domain** with a real domain (e.g. `example.com`). Confirm the status shows "OK".
- [ ] **Step 5: On Aluno-25**, confirm the domain no longer resolves (`ping example.com` should resolve to `127.0.0.1`) and that `C:\Windows\System32\drivers\etc\hosts` contains the managed block with both `example.com` and `www.example.com`.
- [ ] **Step 6: Click Unblock Domain** for the same domain, confirm the hosts file's managed block is now empty and the domain resolves normally again.
- [ ] **Step 7: Click Block Internet.** Confirm Aluno-25 loses general internet access (e.g. a browser can't load an arbitrary site) while `Midview.Master` can still reach `Midview.Agent`/`Midview.Session` on it (the allow-rules for those ports are exactly what's supposed to keep this working).
- [ ] **Step 8: Click Unblock Internet.** Confirm normal internet access returns.
- [ ] **Step 9: Confirm reconciliation across a reboot**: block a domain and the full internet again, then reboot Aluno-25. After it comes back up, confirm both are still in effect without sending any commands again — this is `ReconcileAtStartup()` actually being exercised for real. Then unblock both to leave Aluno-25 in a clean state.
- [ ] **Step 10: If any of the above fails**, use `superpowers:systematic-debugging` before making changes — don't patch symptoms.
- [ ] **Step 11: Once all checks pass, update the spec's status line** from "Approved for planning" to "Implemented and validated", and commit:

```bash
git add docs/superpowers/specs/2026-09-22-site-blocking-design.md
git commit -m "Mark site blocking as implemented and validated"
```

---

## Self-review notes

- **Spec coverage:** the corrected two-feature split (full-internet-block via `FirewallCommands`/`NetshFirewallController`, specific-domain block via `HostsFileManager`) is covered by Tasks 5–6; the persisted, reconciled state is Task 4 + `ReconcileAtStartup()` in Task 7; the Agent's own persisted identity and trust list are Tasks 2–3; the separate network listener is Task 8; the Master-side UI is Task 9; live validation of both features plus reboot survival is Task 10.
- **Type consistency checked:** `SiteBlockState.BlockedDomains`/`InternetBlocked` (Task 4) match what `SiteBlockCommandHandler` (Task 7) reads and writes. `IFirewallController.ApplyBlock()/ApplyUnblock()` (Task 6) match both `NetshFirewallController`'s implementation and `FakeFirewallController` in Task 7's tests. `MessageType.CommandAck`/`BlockInternet`/`UnblockInternet`/`BlockDomain`/`UnblockDomain` (Task 1) match their usage in `AgentServer` (Task 8) and `AgentCommandClient`/`MainWindow` (Task 9). `AgentServerWorker.AgentPort` (Task 8) matches the hardcoded port Task 9's `MainWindow` sends to (flagged with a comment since there's no shared constant between the two projects yet — a config-file-driven port would remove this duplication, but is out of scope here per the spec's minimal-UI note).
