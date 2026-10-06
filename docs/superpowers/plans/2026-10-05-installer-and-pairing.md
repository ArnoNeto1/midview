# Midview Setup Program and Pairing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the USB-stick package with one `Midview-Instalador-<versão>.exe` (Professor or Aluno), and let the teacher's program find student machines on the network and pair with them by comparing a 6-digit code.

**Architecture:** Trust stays pinned thumbprints in `trusted-masters.json`, but the file is now re-read when it changes (`TrustStore.FromFile`), so the Agent can add a teacher at runtime. The Agent accepts any client certificate at the TLS handshake and gates every message after it: unknown teachers may only send `PairRequest`, handled by a `PairingService` (commit–reveal of two nonces, a code computed from both certificates, shown on the student's screen by launching `Midview.Session.exe --pairing-code`). A UDP responder (port 28901) answers the Master's broadcast probe. The Master gets `DiscoveryClient`, `PairingSession` and a "Máquinas da sala" window. The setup is Inno Setup 6 driving the existing, tested PowerShell service logic.

**Tech Stack:** C# / .NET 8 (`net8.0-windows`), WPF (Master), WinForms (Session), xUnit, Windows PowerShell 5.1, Inno Setup 6 (Pascal Script).

**Spec:** `docs/superpowers/specs/2026-10-05-installer-and-pairing-design.md`

## Global Constraints

- Build and test with the SDK at `C:\Users\Neto\AppData\Local\Microsoft\dotnet\dotnet.exe`; the `dotnet` on PATH has no SDK. Below, `$DOTNET` means that full path.
- **No commits or pushes.** The user said "Vamos testar antes de mandar": each task ends with a test checkpoint, not a commit. The user decides when to commit after testing in the lab.
- The working copy uses CRLF (`core.autocrlf=true`). Edit with the Edit/Write tools; never with `sed -i`, which strips CR.
- Everything the teacher or the student reads is Portuguese, and the exact strings are the ones in the spec. Code comments are English, as in the rest of the codebase.
- Ports: Agent TCP 28900, Session TCP 5901, discovery UDP 28901 (`Discovery.Port`).
- The pairing code is SHA-256("Midview pairing v1" ‖ master thumbprint ‖ agent thumbprint ‖ `Na` ‖ `Nm`): the first 4 bytes, big-endian, mod 1,000,000, shown "482 913". Thumbprints are hex, uppercase, ASCII. Nonces are 32 bytes.
- Timings: the teacher has 5 minutes to confirm (`Pairing.ConfirmTimeout`); "Permitir novo professor" lasts 10 minutes (`Pairing.NewTeacherWindow`); discovery listens 2 s (`Discovery.ListenTime`).
- Install folders: Aluno `C:\Program Files\Midview`; Professor `C:\Program Files\Midview Professor`, with the student program in `...\Midview Professor\aluno`.
- PowerShell that ships inside the setup (`installer\scripts`) has Portuguese text: the builder writes it with a UTF-8 BOM. Scripts run straight from the repository (`Gerar-Instalador.ps1`, `installer\tests\*`) stay ASCII-only. All of them must run on Windows PowerShell 5.1.

---

## Stage 1 — Trust that follows the file

### Task 1: File-backed `TrustStore` and `TrustedMastersFile.Add`

**Files:**
- Modify: `src/Midview.Protocol/TrustStore.cs` (whole file)
- Modify: `src/Midview.Protocol/TrustedMastersFile.cs` (whole file)
- Test: `tests/Midview.Protocol.Tests/TrustStoreTests.cs` (new), `tests/Midview.Protocol.Tests/TrustedMastersFileTests.cs` (add tests)

**Interfaces:**
- Produces: `TrustStore.FromFile(string path)`, `TrustStore.IsTrusted(string thumbprint)`, `TrustStore.IsTrusted(X509Certificate)`, `TrustStore.Count`; `TrustedMastersFile.Add(string path, string thumbprint) : IReadOnlyList<string>` (throws `InvalidDataException` for an unreadable list).

- [ ] **Step 1: Write the failing tests**

`tests/Midview.Protocol.Tests/TrustStoreTests.cs`:

```csharp
using Midview.Protocol;
using Xunit;

public class TrustStoreTests : IDisposable
{
    private static readonly string A = new('A', 40);
    private static readonly string B = new('B', 40);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"midview-trust-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        File.Delete(_path);
        File.Delete(_path + ".tmp");
    }

    [Fact]
    public void AFixedList_TrustsItsThumbprints_InAnyCase()
    {
        var store = new TrustStore(new[] { A.ToLowerInvariant() });

        Assert.True(store.IsTrusted(A));
        Assert.False(store.IsTrusted(B));
    }

    [Fact]
    public void FromFile_NoFile_TrustsNobody()
    {
        var store = TrustStore.FromFile(_path);

        Assert.Equal(0, store.Count);
        Assert.False(store.IsTrusted(A));
    }

    [Fact]
    public void FromFile_SeesATeacherAddedLater()
    {
        var store = TrustStore.FromFile(_path);
        Assert.False(store.IsTrusted(A));

        TrustedMastersFile.Add(_path, A);

        Assert.True(store.IsTrusted(A));
        Assert.Equal(1, store.Count);
    }

    [Fact]
    public void FromFile_ReadsTheFileAgainOnlyWhenItChanges()
    {
        File.WriteAllText(_path, $"[\"{A}\"]");
        var store = TrustStore.FromFile(_path);
        Assert.True(store.IsTrusted(A));

        // Same size, same time: taken as unchanged, so not read again.
        var stamp = File.GetLastWriteTimeUtc(_path);
        File.WriteAllText(_path, $"[\"{B}\"]");
        File.SetLastWriteTimeUtc(_path, stamp);
        Assert.True(store.IsTrusted(A));
        Assert.False(store.IsTrusted(B));

        File.SetLastWriteTimeUtc(_path, stamp.AddSeconds(1));
        Assert.True(store.IsTrusted(B));
        Assert.False(store.IsTrusted(A));
    }

    [Fact]
    public void FromFile_ABrokenFile_KeepsWhatWasTrusted()
    {
        File.WriteAllText(_path, $"[\"{A}\"]");
        var store = TrustStore.FromFile(_path);
        Assert.True(store.IsTrusted(A));

        File.WriteAllText(_path, "[\"AAA");
        Assert.True(store.IsTrusted(A));

        File.WriteAllText(_path, $"[\"{B}\"]");
        Assert.True(store.IsTrusted(B));
        Assert.False(store.IsTrusted(A));
    }

    [Fact]
    public void FromFile_TheFileGone_TrustsNobody()
    {
        File.WriteAllText(_path, $"[\"{A}\"]");
        var store = TrustStore.FromFile(_path);
        Assert.True(store.IsTrusted(A));

        File.Delete(_path);

        Assert.False(store.IsTrusted(A));
    }

    [Fact]
    public void ACertificate_IsTrustedByItsThumbprint()
    {
        using var certificate = SelfSignedCertificate.Create("teacher");
        File.WriteAllText(_path, $"[\"{certificate.Thumbprint.ToLowerInvariant()}\"]");

        Assert.True(TrustStore.FromFile(_path).IsTrusted(certificate));
    }
}
```

Add to `tests/Midview.Protocol.Tests/TrustedMastersFileTests.cs` (inside the class):

```csharp
    [Fact]
    public void Add_NoFile_CreatesTheList()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid()}.json");
        try
        {
            var result = TrustedMastersFile.Add(path, "abcd1234");

            Assert.Equal(new[] { "ABCD1234" }, result);
            Assert.Equal(new[] { "ABCD1234" }, TrustedMastersFile.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Add_KeepsTheOthers_AndNeverDuplicates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "[\"aaaa\", \"BBBB\"]");

            Assert.Equal(new[] { "AAAA", "BBBB" }, TrustedMastersFile.Add(path, " bbbb "));
            Assert.Equal(new[] { "AAAA", "BBBB", "CCCC" }, TrustedMastersFile.Add(path, "CCCC"));
            Assert.Equal(new[] { "AAAA", "BBBB", "CCCC" }, TrustedMastersFile.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Add_AListThatCantBeRead_IsLeftAlone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "isto não é json");

            Assert.Throws<InvalidDataException>(() => TrustedMastersFile.Add(path, "CCCC"));
            Assert.Equal("isto não é json", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
```

- [ ] **Step 2: Run them and see them fail**

Run: `"$DOTNET" test tests/Midview.Protocol.Tests --filter "FullyQualifiedName~TrustStoreTests|FullyQualifiedName~TrustedMastersFileTests"`
Expected: build errors (`FromFile`, `IsTrusted(string)`, `Count`, `Add` don't exist).

- [ ] **Step 3: Implement**

`src/Midview.Protocol/TrustStore.cs`:

```csharp
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace Midview.Protocol;

// The thumbprints a connection's other end must have (pinned, never a
// certificate chain). Either a fixed list, or trusted-masters.json itself
// (FromFile): read again whenever the file changes, so a teacher the Agent
// pairs with is trusted at once -- by the Agent, and by the Session in its
// own process -- without a restart.
public sealed class TrustStore
{
    private readonly object _gate = new();
    private readonly string? _path;
    private HashSet<string> _trustedThumbprints;
    private (DateTime WriteTimeUtc, long Length)? _loadedVersion;

    public TrustStore(IEnumerable<string> trustedThumbprints)
    {
        _trustedThumbprints = Normalize(trustedThumbprints);
    }

    private TrustStore(string path)
    {
        _path = path;
        _trustedThumbprints = new HashSet<string>();
    }

    public static TrustStore FromFile(string path) => new(path);

    // How many thumbprints are trusted right now (none: nobody paired yet).
    public int Count
    {
        get
        {
            lock (_gate)
            {
                RefreshIfChanged();
                return _trustedThumbprints.Count;
            }
        }
    }

    public bool IsTrusted(X509Certificate certificate)
    {
        using var certificate2 = new X509Certificate2(certificate);
        return IsTrusted(certificate2.Thumbprint);
    }

    public bool IsTrusted(string thumbprint)
    {
        lock (_gate)
        {
            RefreshIfChanged();
            return _trustedThumbprints.Contains(thumbprint.Trim().ToUpperInvariant());
        }
    }

    // The file's write time and size say whether it changed, so a check
    // costs one look at the file, not a read of it.
    private void RefreshIfChanged()
    {
        if (_path is null)
        {
            return;
        }

        var file = new FileInfo(_path);
        (DateTime, long)? version = file.Exists ? (file.LastWriteTimeUtc, file.Length) : null;
        if (Nullable.Equals(version, _loadedVersion))
        {
            return;
        }

        try
        {
            _trustedThumbprints = version is null ? new HashSet<string>() : Normalize(TrustedMastersFile.Load(_path));
            _loadedVersion = version;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Being written or locked right now: keep trusting whom we
            // trusted, and read it again next time.
        }
    }

    private static HashSet<string> Normalize(IEnumerable<string?> thumbprints) =>
        thumbprints
            .Where(thumbprint => !string.IsNullOrWhiteSpace(thumbprint))
            .Select(thumbprint => thumbprint!.Trim().ToUpperInvariant())
            .ToHashSet();
}
```

`src/Midview.Protocol/TrustedMastersFile.cs`:

```csharp
using System.Text.Json;

namespace Midview.Protocol;

// %ProgramData%\Midview\trusted-masters.json: the thumbprints of the
// teachers this machine trusts, as a JSON list of strings. The Agent adds
// to it when a teacher pairs; the Agent and the Session read it
// (TrustStore.FromFile).
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

    // Adds a teacher and returns the whole list: the others are kept, each
    // upper-cased and only once. Written to a temporary file and moved over
    // the old one, so the Session, reading while the Agent writes, never
    // sees half a list. A list that can't be read is left alone
    // (InvalidDataException): it holds the teachers this machine trusts.
    public static IReadOnlyList<string> Add(string path, string thumbprint)
    {
        IReadOnlyList<string> current;
        try
        {
            current = Load(path);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                $"a lista de professores desta máquina não pôde ser lida ({ErrorText.Describe(ex)})", ex);
        }

        var updated = current
            .Append(thumbprint)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(updated));
        File.Move(tempPath, path, overwrite: true);
        return updated;
    }
}
```

- [ ] **Step 4: Run them and see them pass**

Run: `"$DOTNET" test tests/Midview.Protocol.Tests`
Expected: all pass (the existing `SecureChannelTests` still use `new TrustStore(...)`).

---

### Task 2: The Session starts with no teacher

**Files:**
- Modify: `src/Midview.Session/Program.cs:31-46` and the `new TrustStore(trustedMasters)` argument (line 55)
- Test: `tests/Midview.Session.Tests/SessionServerTests.cs` (harness parameter, one new test)

**Interfaces:**
- Consumes: `TrustStore.FromFile`, `TrustStore.Count` (Task 1).

- [ ] **Step 1: Write the failing test**

In `ServerHarness` (`tests/Midview.Session.Tests/SessionServerTests.cs`), add a last constructor parameter `string? trustedMastersFile = null`, pass it to the server instead of the fixed list, and expose the master's thumbprint:

```csharp
            var server = new SessionServer(
                _listener, _serverCert,
                trustedMastersFile is null ? new TrustStore(new[] { _masterCert.Thumbprint }) : TrustStore.FromFile(trustedMastersFile),
                capture, Injector,
```

```csharp
        public string MasterThumbprint => _masterCert.Thumbprint;
```

New test, next to the `RequestThumbnail_*` tests:

```csharp
    [Fact]
    public async Task NoTeacherYet_RefusesConnections_ThenAcceptsTheTeacherOncePaired()
    {
        var trustedMastersFile = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid():N}.json");
        try
        {
            using var harness = new ServerHarness(new FixedSizeScreenCapture(1920, 1080), trustedMastersFile: trustedMastersFile);

            await Assert.ThrowsAnyAsync<Exception>(() => Task.Run(() =>
            {
                using var stranger = harness.ConnectMaster();
                stranger.RequestThumbnail();
            }).WaitAsync(TimeSpan.FromSeconds(30)));

            // What the Agent does when this teacher pairs.
            TrustedMastersFile.Add(trustedMastersFile, harness.MasterThumbprint);

            using var preview = harness.ConnectMaster();
            var reply = await Task.Run(() => preview.RequestThumbnail()).WaitAsync(TimeSpan.FromSeconds(30));
            Assert.Equal(MessageType.ThumbnailFrame, reply.Type);
        }
        finally
        {
            File.Delete(trustedMastersFile);
        }
    }
```

- [ ] **Step 2: Run it and see it fail**

Run: `"$DOTNET" test tests/Midview.Session.Tests --filter "FullyQualifiedName~NoTeacherYet"`
Expected: the test fails to build until the harness change is in. After that, it passes already: the server-side change is `TrustStore.FromFile` (Task 1). Continue to Step 3 anyway, because the product code in `Program.cs` still throws.

- [ ] **Step 3: Implement**

In `src/Midview.Session/Program.cs`, replace the block from `// Reuses the same trusted-masters.json` through the closing `}` of the `if (trustedMasters.Count == 0)` with:

```csharp
// trusted-masters.json: the teachers this machine trusts, written by the
// Agent when a teacher pairs and read again whenever it changes, so a
// teacher who pairs is accepted on their next connection. With no teacher
// yet, Session still runs and refuses everyone: pairing goes through the
// Agent. A thumbprint on the command line is still accepted for one-off
// manual testing.
var trustedMastersPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "Midview", "trusted-masters.json");
var masterTrustStore = args.Length > 0 ? new TrustStore(new[] { args[0] }) : TrustStore.FromFile(trustedMastersPath);
if (masterTrustStore.Count == 0)
{
    Console.WriteLine($"No teacher has paired with this machine yet ({trustedMastersPath}): refusing every connection until one does.");
}
```

and in the `new SessionServer(...)` call replace `new TrustStore(trustedMasters),` with `masterTrustStore,`.

- [ ] **Step 4: Run the Session tests**

Run: `"$DOTNET" test tests/Midview.Session.Tests`
Expected: all pass.

---

## Stage 2 — Pairing

### Task 3: `SecureChannel` accepts unknown certificates and names them

**Files:**
- Modify: `src/Midview.Protocol/SecureChannel.cs` (whole file)
- Test: `tests/Midview.Protocol.Tests/SecureChannelTests.cs` (one new test)

**Interfaces:**
- Produces: `SecureChannel.RemoteThumbprint` (uppercase), `SecureChannel.AuthenticateAsServerAcceptingAnyClient(TcpClient, X509Certificate2)`, `SecureChannel.AuthenticateAsClientAcceptingAnyServer(TcpClient, string targetHost, X509Certificate2 clientCertificate)`.

- [ ] **Step 1: Write the failing test**

Add to `SecureChannelTests` (add `using System.Net; using System.Net.Sockets; using System.Text;` if missing):

```csharp
    [Fact]
    public async Task AcceptingAnyCertificate_LetsStrangersThrough_AndNamesThem()
    {
        using var serverCert = SelfSignedCertificate.Create("agent");
        using var clientCert = SelfSignedCertificate.Create("stranger");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        try
        {
            var serverSide = Task.Run(() =>
            {
                using var accepted = listener.AcceptTcpClient();
                using var channel = SecureChannel.AuthenticateAsServerAcceptingAnyClient(accepted, serverCert);
                var message = channel.Receive();
                channel.Send(new Message(MessageType.CommandAck, Encoding.ASCII.GetBytes(channel.RemoteThumbprint)));
                return message.Type;
            });

            using var tcpClient = new TcpClient();
            tcpClient.Connect(IPAddress.Loopback, port);
            using var client = SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcpClient, "agent", clientCert);
            client.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));
            var reply = client.Receive();

            Assert.Equal(clientCert.Thumbprint, Encoding.ASCII.GetString(reply.Payload));
            Assert.Equal(serverCert.Thumbprint, client.RemoteThumbprint);
            Assert.Equal(MessageType.Heartbeat, await serverSide.WaitAsync(TimeSpan.FromSeconds(30)));
        }
        finally
        {
            listener.Stop();
        }
    }
```

- [ ] **Step 2: Run it and see it fail**

Run: `"$DOTNET" test tests/Midview.Protocol.Tests --filter "FullyQualifiedName~AcceptingAnyCertificate"`
Expected: build error (the new methods don't exist).

- [ ] **Step 3: Implement**

`src/Midview.Protocol/SecureChannel.cs`:

```csharp
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace Midview.Protocol;

public sealed class SecureChannel : IDisposable
{
    private readonly SslStream _sslStream;

    private SecureChannel(SslStream sslStream)
    {
        _sslStream = sslStream;
        RemoteThumbprint = ThumbprintOf(sslStream.RemoteCertificate);
    }

    // The other end's certificate thumbprint, upper case ("" if it sent none).
    public string RemoteThumbprint { get; }

    public static SecureChannel AuthenticateAsServer(
        TcpClient client, X509Certificate2 serverCertificate, TrustStore clientTrustStore) =>
        Server(client, serverCertificate, clientTrustStore.IsTrusted);

    // For the Agent, which lets a teacher it doesn't trust yet through the
    // handshake to ask to pair (see Pairing). A certificate is still
    // required, so RemoteThumbprint always names one: what that teacher may
    // do is decided after the handshake, from it.
    public static SecureChannel AuthenticateAsServerAcceptingAnyClient(TcpClient client, X509Certificate2 serverCertificate) =>
        Server(client, serverCertificate, _ => true);

    public static SecureChannel AuthenticateAsClient(
        TcpClient client, string targetHost, X509Certificate2 clientCertificate, TrustStore serverTrustStore) =>
        Client(client, targetHost, clientCertificate, serverTrustStore.IsTrusted);

    // The teacher's side of pairing: the machine's certificate isn't known
    // yet, and the pairing code is what ties it to the machine on the desk.
    public static SecureChannel AuthenticateAsClientAcceptingAnyServer(
        TcpClient client, string targetHost, X509Certificate2 clientCertificate) =>
        Client(client, targetHost, clientCertificate, _ => true);

    public void Send(Message message) => MessageFramer.Write(_sslStream, message);

    public Message Receive() => MessageFramer.Read(_sslStream);

    public void Dispose() => _sslStream.Dispose();

    private static SecureChannel Server(TcpClient client, X509Certificate2 serverCertificate, Func<X509Certificate, bool> accept)
    {
        var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        sslStream.AuthenticateAsServer(new SslServerAuthenticationOptions
        {
            ServerCertificate = serverCertificate,
            ClientCertificateRequired = true,
            CertificateChainPolicy = OfflineChainPolicy(),
            RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null && accept(cert),
        });

        return new SecureChannel(sslStream);
    }

    private static SecureChannel Client(
        TcpClient client, string targetHost, X509Certificate2 clientCertificate, Func<X509Certificate, bool> accept)
    {
        var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false);
        sslStream.AuthenticateAsClient(new SslClientAuthenticationOptions
        {
            TargetHost = targetHost,
            ClientCertificates = new X509CertificateCollection { clientCertificate },
            EnabledSslProtocols = SslProtocols.None,
            CertificateChainPolicy = OfflineChainPolicy(),
            RemoteCertificateValidationCallback = (_, cert, _, _) => cert is not null && accept(cert),
        });

        return new SecureChannel(sslStream);
    }

    private static string ThumbprintOf(X509Certificate? certificate)
    {
        if (certificate is null)
        {
            return string.Empty;
        }

        using var certificate2 = new X509Certificate2(certificate);
        return certificate2.Thumbprint.ToUpperInvariant();
    }

    // Trust is the pinned thumbprint (TrustStore), never the certificate
    // chain, so the chain SslStream builds anyway must not reach the network:
    // on Windows, building one for a self-signed certificate can go looking
    // for roots and revocation lists online, which on a lab network without
    // internet (or with Midview's own internet block on) stalls every
    // handshake until that times out.
    private static X509ChainPolicy OfflineChainPolicy() => new()
    {
        RevocationMode = X509RevocationMode.NoCheck,
        DisableCertificateDownloads = true,
    };
}
```

- [ ] **Step 4: Run the Protocol, Session and Agent tests**

Run: `"$DOTNET" test tests/Midview.Protocol.Tests` then `"$DOTNET" test tests/Midview.Session.Tests` then `"$DOTNET" test tests/Midview.Agent.Tests`
Expected: all pass. Nothing else changed behavior.

---

### Task 4: Pairing primitives and message types

**Files:**
- Create: `src/Midview.Protocol/Pairing.cs`, `src/Midview.Protocol/PairingCodeArguments.cs`
- Modify: `src/Midview.Protocol/MessageType.cs` (append after `ClearDeliveryFolder = 36,`)
- Test: `tests/Midview.Protocol.Tests/PairingTests.cs` (new)

**Interfaces:**
- Produces: `Pairing.NonceLength`, `Pairing.MaxTeacherNameLength`, `Pairing.ConfirmTimeout`, `Pairing.NewTeacherWindow`, `Pairing.NewNonce()`, `Pairing.Commit(byte[])`, `Pairing.Opens(byte[] commitment, byte[] nonce)`, `Pairing.Code(string masterThumbprint, string agentThumbprint, byte[] agentNonce, byte[] masterNonce) : int`, `Pairing.Format(int) : string`, `Pairing.CleanTeacherName(string)`, `Pairing.Refused(string) : Message`, `Pairing.RefusalText(byte[]) : string`; `record PairingResult(string Name, string SessionThumbprint, string? MacAddress)` with `Encode()` / `static Decode(byte[]) : PairingResult?`; `PairingCodeArguments.CodeFlag`, `.FromFlag`, `.Build(int code, string teacherName)`, `.Parse(string[] args) : (int Code, string TeacherName)?`; `MessageType.PairRequest = 37` … `PairRefused = 44`, `AllowNewTeacher = 45`.

- [ ] **Step 1: Write the failing tests**

`tests/Midview.Protocol.Tests/PairingTests.cs` (the reference values were computed with Python's `hashlib`, independently of this code):

```csharp
using System.Text;
using Midview.Protocol;
using Xunit;

public class PairingTests
{
    private static readonly string Master = new('A', 40);
    private static readonly string Agent = new('B', 40);

    private static byte[] Bytes(int start) => Enumerable.Range(start, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void Code_MatchesTheReferenceValues()
    {
        Assert.Equal(433504, Pairing.Code(Master, Agent, Bytes(0), Bytes(32)));
        Assert.Equal(353409, Pairing.Code(Master, Agent, Bytes(0), Bytes(1)));
    }

    [Fact]
    public void Code_DoesNotCareAboutTheThumbprintsCase()
    {
        Assert.Equal(433504, Pairing.Code(Master.ToLowerInvariant(), Agent.ToLowerInvariant(), Bytes(0), Bytes(32)));
    }

    [Fact]
    public void Code_ChangesWithEachInput()
    {
        Assert.NotEqual(433504, Pairing.Code(new string('A', 39) + "C", Agent, Bytes(0), Bytes(32)));
        Assert.NotEqual(433504, Pairing.Code(Master, new string('B', 39) + "C", Bytes(0), Bytes(32)));
        Assert.NotEqual(433504, Pairing.Code(Master, Agent, Bytes(1), Bytes(32)));
        Assert.NotEqual(433504, Pairing.Code(Agent, Master, Bytes(0), Bytes(32)));
    }

    [Theory]
    [InlineData(42, "000 042")]
    [InlineData(433504, "433 504")]
    [InlineData(999999, "999 999")]
    public void Format_ShowsTwoGroupsOfThree(int code, string expected)
    {
        Assert.Equal(expected, Pairing.Format(code));
    }

    [Fact]
    public void Commit_IsTheNoncesSha256()
    {
        Assert.Equal(
            "630DCD2966C4336691125448BBB25B4FF412A49C732DB2C8ABC1B8581BD710DD",
            Convert.ToHexString(Pairing.Commit(Bytes(0))));
    }

    [Fact]
    public void Opens_OnlyTheCommittedNonce()
    {
        var commitment = Pairing.Commit(Bytes(0));

        Assert.True(Pairing.Opens(commitment, Bytes(0)));
        Assert.False(Pairing.Opens(commitment, Bytes(1)));
        Assert.False(Pairing.Opens(commitment, new byte[31]));
        Assert.False(Pairing.Opens(new byte[5], Bytes(0)));
    }

    [Fact]
    public void NewNonce_Is32RandomBytes()
    {
        var first = Pairing.NewNonce();

        Assert.Equal(32, first.Length);
        Assert.NotEqual(first, Pairing.NewNonce());
    }

    [Theory]
    [InlineData("PROF-01", "PROF-01")]
    [InlineData("  sala \"2\" & co ", "sala 2  co")]
    [InlineData("", "?")]
    [InlineData("\"&|", "?")]
    public void CleanTeacherName_KeepsOnlyWhatIsSafeToShow(string name, string expected)
    {
        Assert.Equal(expected, Pairing.CleanTeacherName(name));
    }

    [Fact]
    public void CleanTeacherName_IsNeverLongerThanTheLimit()
    {
        Assert.Equal(new string('x', Pairing.MaxTeacherNameLength), Pairing.CleanTeacherName(new string('x', 60)));
    }

    [Fact]
    public void ARefusal_CarriesItsReason()
    {
        var message = Pairing.Refused("Esta máquina já tem professor.");

        Assert.Equal(MessageType.PairRefused, message.Type);
        Assert.Equal("Esta máquina já tem professor.", Pairing.RefusalText(message.Payload));
    }

    [Fact]
    public void PairingResult_RoundTrips()
    {
        var result = new PairingResult("ALUNO-01", Agent, "00-1A-2B-3C-4D-5E");

        Assert.Equal(result, PairingResult.Decode(result.Encode()));
        Assert.Equal(result with { MacAddress = null }, PairingResult.Decode((result with { MacAddress = null }).Encode()));
    }

    [Fact]
    public void PairingResult_Decode_RefusesWhatIsNotOne()
    {
        Assert.Null(PairingResult.Decode(Encoding.UTF8.GetBytes("{}")));
        Assert.Null(PairingResult.Decode(Encoding.UTF8.GetBytes("isto não é json")));
        Assert.Null(PairingResult.Decode(Array.Empty<byte>()));
    }

    [Fact]
    public void PairingCodeArguments_BuildAndParse()
    {
        Assert.Equal("--pairing-code 004213 --from \"PROF 01\"", PairingCodeArguments.Build(4213, "PROF 01"));
        Assert.Equal("--pairing-code 000001 --from \"ab\"", PairingCodeArguments.Build(1, "a\"b"));
        Assert.Equal((4213, "PROF 01"), PairingCodeArguments.Parse(new[] { "--pairing-code", "004213", "--from", "PROF 01" }));
    }

    [Fact]
    public void PairingCodeArguments_Parse_RefusesAnythingElse()
    {
        Assert.Null(PairingCodeArguments.Parse(Array.Empty<string>()));
        Assert.Null(PairingCodeArguments.Parse(new[] { "--pairing-code", "12345", "--from", "x" }));
        Assert.Null(PairingCodeArguments.Parse(new[] { "--pairing-code", "12a456", "--from", "x" }));
        Assert.Null(PairingCodeArguments.Parse(new[] { new string('A', 40) }));
    }
}
```

- [ ] **Step 2: Run them and see them fail**

Run: `"$DOTNET" test tests/Midview.Protocol.Tests --filter "FullyQualifiedName~PairingTests"`
Expected: build errors (no `Pairing`).

- [ ] **Step 3: Implement**

Append to `MessageType` after `ClearDeliveryFolder = 36,`:

```csharp

    // Pairing a teacher with this machine (see Pairing for the exchange):
    // the only Agent exchange a teacher it doesn't trust yet may start.
    PairRequest = 37,
    PairCommit = 38,
    PairNonce = 39,
    PairReveal = 40,
    PairConfirm = 41,
    PairCancel = 42,
    PairDone = 43,
    PairRefused = 44,

    // An Agent command, from a trusted teacher only: for
    // Pairing.NewTeacherWindow, let one more teacher pair ("Permitir novo
    // professor"). Answered with one CommandAck carrying a CommandResult.
    AllowNewTeacher = 45,
```

`src/Midview.Protocol/Pairing.cs`:

```csharp
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Midview.Protocol;

// How a teacher and a student machine come to trust each other, over the
// Agent's TLS port, each side accepting a certificate it doesn't know yet:
//   Master -> PairRequest  the teacher's computer name, shown to the student
//   Agent  -> PairCommit   SHA-256 of the Agent's random nonce, or PairRefused (why, in Portuguese)
//   Master -> PairNonce    the Master's random nonce
//   Agent  -> PairReveal   the Agent's nonce; the code is on the student's screen now (or PairRefused)
//   Master -> PairConfirm or PairCancel, once the teacher compared the codes
//   Agent  -> PairDone     the machine, for the classroom list (PairingResult), or PairRefused
// Both ends compute the same 6-digit code from both certificates and both
// nonces. A computer in the middle holds two TLS sessions with different
// certificates, so the two screens show different codes; the commitment
// keeps it from steering them, since each nonce is fixed before the other
// one is known. The code protects the teacher: the student's side confirms
// nothing.
public static class Pairing
{
    public const int NonceLength = 32;
    public const int MaxTeacherNameLength = 40;

    // How long the teacher has to compare and confirm, from the reveal.
    public static readonly TimeSpan ConfirmTimeout = TimeSpan.FromMinutes(5);

    // How long "Permitir novo professor" lets one more teacher pair.
    public static readonly TimeSpan NewTeacherWindow = TimeSpan.FromMinutes(10);

    private static readonly byte[] Label = Encoding.ASCII.GetBytes("Midview pairing v1");

    public static byte[] NewNonce() => RandomNumberGenerator.GetBytes(NonceLength);

    public static byte[] Commit(byte[] nonce) => SHA256.HashData(nonce);

    public static bool Opens(byte[] commitment, byte[] nonce) =>
        nonce.Length == NonceLength && commitment.Length == SHA256.HashSizeInBytes
        && CryptographicOperations.FixedTimeEquals(commitment, Commit(nonce));

    public static int Code(string masterThumbprint, string agentThumbprint, byte[] agentNonce, byte[] masterNonce)
    {
        var input = Label
            .Concat(Encoding.ASCII.GetBytes(masterThumbprint.Trim().ToUpperInvariant()))
            .Concat(Encoding.ASCII.GetBytes(agentThumbprint.Trim().ToUpperInvariant()))
            .Concat(agentNonce)
            .Concat(masterNonce)
            .ToArray();
        var hash = SHA256.HashData(input);
        return (int)(BinaryPrimitives.ReadUInt32BigEndian(hash) % 1_000_000);
    }

    public static string Format(int code) => $"{code / 1000:000} {code % 1000:000}";

    // The teacher's computer name as the student sees it: letters, digits,
    // '-', '_', '.' and spaces only (it also goes on a command line, see
    // PairingCodeArguments), never longer than MaxTeacherNameLength, and
    // "?" if nothing is left.
    public static string CleanTeacherName(string name)
    {
        var kept = new string(name.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.' or ' ').ToArray()).Trim();
        if (kept.Length > MaxTeacherNameLength)
        {
            kept = kept[..MaxTeacherNameLength].TrimEnd();
        }
        return kept.Length == 0 ? "?" : kept;
    }

    public static Message Refused(string reason) => new(MessageType.PairRefused, Encoding.UTF8.GetBytes(reason));

    public static string RefusalText(byte[] payload) => Encoding.UTF8.GetString(payload);
}

// What PairDone carries: the machine as the classroom list keeps it.
// MacAddress ("AA-BB-CC-DD-EE-FF") is the card the pairing came in on, for
// Wake-on-LAN; null if the Agent couldn't tell.
public sealed record PairingResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("session")] string SessionThumbprint,
    [property: JsonPropertyName("mac")] string? MacAddress)
{
    public byte[] Encode() => JsonSerializer.SerializeToUtf8Bytes(this);

    public static PairingResult? Decode(byte[] payload)
    {
        try
        {
            var result = JsonSerializer.Deserialize<PairingResult>(payload);
            return result is { Name.Length: > 0, SessionThumbprint.Length: > 0 } ? result : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
```

`src/Midview.Protocol/PairingCodeArguments.cs`:

```csharp
namespace Midview.Protocol;

// The Session's command line for showing a pairing code on the student's
// screen: Midview.Session.exe --pairing-code 482913 --from "PROF-01". The
// Agent builds it; the Session reads it. The teacher's name goes through
// Pairing.CleanTeacherName, so no quote can reach the command line.
public static class PairingCodeArguments
{
    public const string CodeFlag = "--pairing-code";
    public const string FromFlag = "--from";

    public static string Build(int code, string teacherName) =>
        $"{CodeFlag} {code:000000} {FromFlag} \"{Pairing.CleanTeacherName(teacherName)}\"";

    public static (int Code, string TeacherName)? Parse(string[] args) =>
        args is [CodeFlag, var code, FromFlag, var from] && code.Length == 6 && code.All(char.IsAsciiDigit)
            ? (int.Parse(code), Pairing.CleanTeacherName(from))
            : null;
}
```

- [ ] **Step 4: Run them and see them pass**

Run: `"$DOTNET" test tests/Midview.Protocol.Tests`
Expected: all pass.

---

### Task 5: Agent `PairingService`

**Files:**
- Create: `src/Midview.Agent/IPairingCodeDisplay.cs`, `src/Midview.Agent/PairingService.cs`
- Test: `tests/Midview.Agent.Tests/PairingServiceTests.cs` (new)

**Interfaces:**
- Consumes: `TrustStore.FromFile/Count/IsTrusted(string)`, `TrustedMastersFile.Add` (Task 1); everything in `Pairing` (Task 4); `TeacherStatus` (Task 6).
- Produces: `interface IPairingCodeDisplay { bool SomeoneLoggedOn { get; } IDisposable? Show(int code, string teacherName); }`; `PairingService(TrustStore trust, string trustedMastersPath, IPairingCodeDisplay display, Func<DateTime> now, string machineName, Func<string> sessionThumbprint)` with `Handle(string masterThumbprint, string agentThumbprint, byte[] requestPayload, Func<Message> receive, Action<Message> send, string? macAddress)`, `AllowNewTeacher() : (bool Succeeded, string Detail)`, `bool NewTeacherOpen`, `TeachersSeenBy(string masterThumbprint) : string`.

> Do **Task 6 first**, then this one: `PairingService.TeachersSeenBy` returns `TeacherStatus` values.

- [ ] **Step 1: Write the failing tests**

`tests/Midview.Agent.Tests/PairingServiceTests.cs`:

```csharp
using System.Text;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class PairingServiceTests : IDisposable
{
    private static readonly string TeacherA = new('A', 40);
    private static readonly string TeacherB = new('B', 40);
    private static readonly string TeacherC = new('C', 40);
    private static readonly string AgentThumbprint = new('D', 40);
    private static readonly string SessionThumbprint = new('5', 40);
    private static readonly byte[] MasterNonce = Enumerable.Range(100, 32).Select(i => (byte)i).ToArray();

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid():N}.json");
    private readonly FakeDisplay _display = new();
    private DateTime _now = new(2026, 10, 5, 14, 22, 0);

    public void Dispose()
    {
        File.Delete(_path);
        File.Delete(_path + ".tmp");
    }

    private PairingService NewService() =>
        new(TrustStore.FromFile(_path), _path, _display, () => _now, "ALUNO-07", () => SessionThumbprint);

    private static void Pair(PairingService service, string master, Exchange exchange, string? mac = "00-1A-2B-3C-4D-5E") =>
        service.Handle(master, AgentThumbprint, Encoding.UTF8.GetBytes("PROF-01"), exchange.Receive, exchange.Send, mac);

    private static Exchange Confirming() =>
        new Exchange().Then(MessageType.PairNonce, MasterNonce).Then(MessageType.PairConfirm);

    private void Trust(params string[] teachers) =>
        File.WriteAllText(_path, "[" + string.Join(",", teachers.Select(t => $"\"{t}\"")) + "]");

    [Fact]
    public void WithNoTeacher_TheFirstConfirmedPairing_IsTrusted()
    {
        var exchange = Confirming();

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(new[] { MessageType.PairCommit, MessageType.PairReveal, MessageType.PairDone }, exchange.SentTypes);
        var agentNonce = exchange.Sent[1].Payload;
        Assert.True(Pairing.Opens(exchange.Sent[0].Payload, agentNonce));
        Assert.Equal((Pairing.Code(TeacherA, AgentThumbprint, agentNonce, MasterNonce), "PROF-01"), _display.Shown.Single());
        Assert.Equal(0, _display.OpenWindows);
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
        Assert.Equal(new PairingResult("ALUNO-07", SessionThumbprint, "00-1A-2B-3C-4D-5E"), PairingResult.Decode(exchange.Sent[2].Payload));
    }

    [Fact]
    public void WithATeacher_AnotherOne_IsRefused()
    {
        Trust(TeacherA);
        var exchange = Confirming();

        Pair(NewService(), TeacherB, exchange);

        Assert.Equal(new[] { MessageType.PairRefused }, exchange.SentTypes);
        Assert.Contains("já tem professor", Pairing.RefusalText(exchange.Sent[0].Payload));
        Assert.Empty(_display.Shown);
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
    }

    [Fact]
    public void InsideTheNewTeacherWindow_OneMoreTeacherPairs_AndTheWindowCloses()
    {
        Trust(TeacherA);
        var service = NewService();

        var (succeeded, detail) = service.AllowNewTeacher();
        Assert.True(succeeded);
        Assert.Equal("liberada até 14:32", detail);
        Assert.True(service.NewTeacherOpen);

        var second = Confirming();
        Pair(service, TeacherB, second);
        Assert.Equal(MessageType.PairDone, second.Sent.Last().Type);
        Assert.Equal(new[] { TeacherA, TeacherB }, TrustedMastersFile.Load(_path));
        Assert.False(service.NewTeacherOpen);

        var third = Confirming();
        Pair(service, TeacherC, third);
        Assert.Equal(new[] { MessageType.PairRefused }, third.SentTypes);
    }

    [Fact]
    public void TheNewTeacherWindow_Expires()
    {
        Trust(TeacherA);
        var service = NewService();
        service.AllowNewTeacher();

        _now += Pairing.NewTeacherWindow + TimeSpan.FromSeconds(1);

        Assert.False(service.NewTeacherOpen);
        var exchange = Confirming();
        Pair(service, TeacherB, exchange);
        Assert.Equal(new[] { MessageType.PairRefused }, exchange.SentTypes);
    }

    [Fact]
    public void ATeacherAlreadyTrusted_MayPairAgain()
    {
        Trust(TeacherA);
        var exchange = Confirming();

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(MessageType.PairDone, exchange.Sent.Last().Type);
        Assert.Equal(new[] { TeacherA }, TrustedMastersFile.Load(_path));
    }

    [Fact]
    public void Cancelling_TrustsNothing_AndTakesTheCodeDown()
    {
        var exchange = new Exchange().Then(MessageType.PairNonce, MasterNonce).Then(MessageType.PairCancel);

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(new[] { MessageType.PairCommit, MessageType.PairReveal }, exchange.SentTypes);
        Assert.Single(_display.Shown);
        Assert.Equal(0, _display.OpenWindows);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void ADroppedConnection_TrustsNothing_TakesTheCodeDown_AndFreesTheMachine()
    {
        var service = NewService();
        var dropped = new Exchange().Then(MessageType.PairNonce, MasterNonce);

        Assert.Throws<IOException>(() => Pair(service, TeacherA, dropped));

        Assert.Equal(0, _display.OpenWindows);
        Assert.False(File.Exists(_path));
        var again = Confirming();
        Pair(service, TeacherA, again);
        Assert.Equal(MessageType.PairDone, again.Sent.Last().Type);
    }

    [Fact]
    public void AConfirmationTooLate_IsRefused()
    {
        var exchange = new Exchange()
            .Then(MessageType.PairNonce, MasterNonce)
            .Then(() =>
            {
                _now += Pairing.ConfirmTimeout + TimeSpan.FromSeconds(1);
                return new Message(MessageType.PairConfirm, Array.Empty<byte>());
            });

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(new[] { MessageType.PairCommit, MessageType.PairReveal, MessageType.PairRefused }, exchange.SentTypes);
        Assert.Contains("tempo", Pairing.RefusalText(exchange.Sent[2].Payload));
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void NobodyLoggedOn_IsRefused()
    {
        _display.SomeoneLoggedOn = false;
        var exchange = Confirming();

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(new[] { MessageType.PairRefused }, exchange.SentTypes);
        Assert.Contains("Ninguém está usando esta máquina", Pairing.RefusalText(exchange.Sent[0].Payload));
    }

    [Fact]
    public void ACodeThatCantBeShown_IsRefused()
    {
        _display.Fails = true;
        var exchange = Confirming();

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(new[] { MessageType.PairCommit, MessageType.PairRefused }, exchange.SentTypes);
        Assert.False(File.Exists(_path));
    }

    [Fact]
    public void OnePairingAtATime()
    {
        var service = NewService();
        var second = new Exchange();
        var first = new Exchange()
            .Then(MessageType.PairNonce, MasterNonce)
            .Then(() =>
            {
                Pair(service, TeacherB, second);
                return new Message(MessageType.PairConfirm, Array.Empty<byte>());
            });

        Pair(service, TeacherA, first);

        Assert.Equal(new[] { MessageType.PairRefused }, second.SentTypes);
        Assert.Contains("Outro pareamento", Pairing.RefusalText(second.Sent[0].Payload));
        Assert.Equal(MessageType.PairDone, first.Sent.Last().Type);
    }

    [Fact]
    public void ABadNonce_EndsThePairing()
    {
        var exchange = new Exchange().Then(MessageType.PairNonce, new byte[5]);

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(new[] { MessageType.PairCommit }, exchange.SentTypes);
        Assert.Empty(_display.Shown);
    }

    [Fact]
    public void AListThatCantBeRead_IsNotReplaced()
    {
        File.WriteAllText(_path, "isto não é json");
        var exchange = Confirming();

        Pair(NewService(), TeacherA, exchange);

        Assert.Equal(MessageType.PairRefused, exchange.Sent.Last().Type);
        Assert.Contains("Não foi possível gravar", Pairing.RefusalText(exchange.Sent.Last().Payload));
        Assert.Equal("isto não é json", File.ReadAllText(_path));
    }

    [Fact]
    public void TeachersSeenBy_SaysWhoseMachineItIs()
    {
        var service = NewService();
        Assert.Equal(TeacherStatus.None, service.TeachersSeenBy(TeacherA));

        Trust(TeacherA);
        Assert.Equal(TeacherStatus.You, service.TeachersSeenBy(TeacherA));
        Assert.Equal(TeacherStatus.Others, service.TeachersSeenBy(TeacherB));
    }

    private sealed class FakeDisplay : IPairingCodeDisplay
    {
        public bool SomeoneLoggedOn { get; set; } = true;

        public bool Fails { get; set; }

        public List<(int Code, string Teacher)> Shown { get; } = new();

        public int OpenWindows { get; private set; }

        public IDisposable? Show(int code, string teacherName)
        {
            if (Fails)
            {
                return null;
            }
            Shown.Add((code, teacherName));
            OpenWindows++;
            return new Closer(() => OpenWindows--);
        }

        private sealed class Closer(Action close) : IDisposable
        {
            public void Dispose() => close();
        }
    }

    // The teacher's side, scripted: what it answers, in order, and what the
    // Agent sent it. Out of answers means the teacher hung up.
    private sealed class Exchange
    {
        private readonly Queue<Func<Message>> _answers = new();

        public List<Message> Sent { get; } = new();

        public IEnumerable<MessageType> SentTypes => Sent.Select(message => message.Type);

        public Exchange Then(Func<Message> answer)
        {
            _answers.Enqueue(answer);
            return this;
        }

        public Exchange Then(MessageType type, byte[]? payload = null) =>
            Then(() => new Message(type, payload ?? Array.Empty<byte>()));

        public Message Receive() => _answers.Count > 0 ? _answers.Dequeue()() : throw new IOException("the teacher hung up");

        public void Send(Message message) => Sent.Add(message);
    }
}
```

- [ ] **Step 2: Run them and see them fail**

Run: `"$DOTNET" test tests/Midview.Agent.Tests --filter "FullyQualifiedName~PairingServiceTests"`
Expected: build errors (no `PairingService`).

- [ ] **Step 3: Implement**

`src/Midview.Agent/IPairingCodeDisplay.cs`:

```csharp
namespace Midview.Agent;

// The pairing code on the student's screen (SessionPairingCodeDisplay;
// fakes in tests).
public interface IPairingCodeDisplay
{
    // Whether someone is logged on at the machine, so a code would appear.
    bool SomeoneLoggedOn { get; }

    // The code stays up until the returned handle is disposed; null if it
    // couldn't be shown.
    IDisposable? Show(int code, string teacherName);
}
```

`src/Midview.Agent/PairingService.cs`:

```csharp
using System.Text;
using Midview.Protocol;

namespace Midview.Agent;

// Who may pair with this machine, and the Agent's side of the exchange (see
// Pairing). A machine with no teacher takes the first confirmed pairing; a
// teacher it already trusts may pair again (a lost classroom list); anyone
// else only while a trusted teacher's "Permitir novo professor" lasts, which
// ends with the first new teacher. One pairing at a time.
public sealed class PairingService
{
    private readonly object _gate = new();
    private readonly TrustStore _trust;
    private readonly string _trustedMastersPath;
    private readonly IPairingCodeDisplay _display;
    private readonly Func<DateTime> _now;
    private readonly string _machineName;
    private readonly Func<string> _sessionThumbprint;
    private DateTime? _newTeacherUntil;
    private bool _busy;

    // now is local time: it's what "liberada até 14:32" shows.
    public PairingService(
        TrustStore trust, string trustedMastersPath, IPairingCodeDisplay display, Func<DateTime> now,
        string machineName, Func<string> sessionThumbprint)
    {
        _trust = trust;
        _trustedMastersPath = trustedMastersPath;
        _display = display;
        _now = now;
        _machineName = machineName;
        _sessionThumbprint = sessionThumbprint;
    }

    public bool NewTeacherOpen
    {
        get
        {
            lock (_gate)
            {
                return WindowOpen();
            }
        }
    }

    // For discovery: whom this machine trusts, as the asking teacher sees it.
    public string TeachersSeenBy(string masterThumbprint) =>
        _trust.Count == 0 ? TeacherStatus.None
        : _trust.IsTrusted(masterThumbprint) ? TeacherStatus.You
        : TeacherStatus.Others;

    // AllowNewTeacher, from a trusted teacher (AgentServer checks that).
    public (bool Succeeded, string Detail) AllowNewTeacher()
    {
        lock (_gate)
        {
            var until = _now() + Pairing.NewTeacherWindow;
            _newTeacherUntil = until;
            AgentLog.Warn($"New-teacher pairing allowed until {until:HH:mm}.");
            return (true, $"liberada até {until:HH:mm}");
        }
    }

    // Everything after a PairRequest, over that connection (receive and send
    // are its own). macAddress is the card the connection came in on. A
    // dropped connection surfaces as the receive's exception, after the
    // code has been taken down.
    public void Handle(
        string masterThumbprint, string agentThumbprint, byte[] requestPayload,
        Func<Message> receive, Action<Message> send, string? macAddress)
    {
        var teacherName = Pairing.CleanTeacherName(Encoding.UTF8.GetString(requestPayload));
        bool usesWindow;
        lock (_gate)
        {
            var refusal = Refusal(masterThumbprint, out usesWindow);
            if (refusal is not null)
            {
                AgentLog.Warn($"Pairing with {masterThumbprint} ({teacherName}) refused: {refusal}");
                send(Pairing.Refused(refusal));
                return;
            }
            _busy = true;
        }

        try
        {
            Exchange(masterThumbprint, agentThumbprint, teacherName, receive, send, macAddress, usesWindow);
        }
        finally
        {
            lock (_gate)
            {
                _busy = false;
            }
        }
    }

    private string? Refusal(string masterThumbprint, out bool usesWindow)
    {
        usesWindow = false;
        if (_busy)
        {
            return "Outro pareamento está em andamento nesta máquina.";
        }
        if (_trust.Count > 0 && !_trust.IsTrusted(masterThumbprint))
        {
            if (!WindowOpen())
            {
                return "Esta máquina já tem professor. Peça a um professor dela para clicar em \"Permitir novo professor\".";
            }
            usesWindow = true;
        }
        if (!_display.SomeoneLoggedOn)
        {
            return "Ninguém está usando esta máquina agora. Entre com um usuário nela e tente de novo.";
        }
        return null;
    }

    private bool WindowOpen() => _newTeacherUntil is { } until && _now() < until;

    private void Exchange(
        string masterThumbprint, string agentThumbprint, string teacherName,
        Func<Message> receive, Action<Message> send, string? macAddress, bool usesWindow)
    {
        var agentNonce = Pairing.NewNonce();
        send(new Message(MessageType.PairCommit, Pairing.Commit(agentNonce)));

        var nonce = receive();
        if (nonce.Type != MessageType.PairNonce || nonce.Payload.Length != Pairing.NonceLength)
        {
            AgentLog.Warn($"Pairing with {masterThumbprint}: expected the teacher's nonce, got {nonce.Type}.");
            return;
        }

        // On the screen before the reveal: once the teacher has the code,
        // the student must be able to see it too.
        var code = Pairing.Code(masterThumbprint, agentThumbprint, agentNonce, nonce.Payload);
        using var shown = _display.Show(code, teacherName);
        if (shown is null)
        {
            send(Pairing.Refused("Não foi possível mostrar o código na tela do aluno. Tente de novo."));
            return;
        }

        var revealedAt = _now();
        send(new Message(MessageType.PairReveal, agentNonce));

        var answer = receive();
        if (answer.Type != MessageType.PairConfirm)
        {
            AgentLog.Warn($"Pairing with {masterThumbprint} ({teacherName}) cancelled ({answer.Type}).");
            return;
        }
        if (_now() - revealedAt > Pairing.ConfirmTimeout)
        {
            send(Pairing.Refused("O tempo para conferir o código acabou. Tente de novo."));
            return;
        }

        try
        {
            TrustedMastersFile.Add(_trustedMastersPath, masterThumbprint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            send(Pairing.Refused($"Não foi possível gravar o professor nesta máquina: {ErrorText.Describe(ex)}"));
            return;
        }

        if (usesWindow)
        {
            lock (_gate)
            {
                _newTeacherUntil = null;
            }
        }
        AgentLog.Warn($"Paired with teacher {masterThumbprint} ({teacherName}).");
        send(new Message(MessageType.PairDone, new PairingResult(_machineName, _sessionThumbprint(), macAddress).Encode()));
    }
}
```

- [ ] **Step 4: Run them and see them pass**

Run: `"$DOTNET" test tests/Midview.Agent.Tests --filter "FullyQualifiedName~PairingServiceTests"`
Expected: all pass.

---

### Task 6: Discovery messages (Protocol)

**Files:**
- Create: `src/Midview.Protocol/Discovery.cs`
- Test: `tests/Midview.Protocol.Tests/DiscoveryTests.cs` (new)

**Interfaces:**
- Produces: `Discovery.Port = 28901`, `Discovery.MaxPacketSize = 2048`, `Discovery.Version = 1`, `Discovery.ListenTime`, `Discovery.Encode(DiscoveryProbe)`, `Discovery.Encode(DiscoveryReply)`, `Discovery.ParseProbe(byte[]) : DiscoveryProbe?`, `Discovery.ParseReply(byte[]) : DiscoveryReply?`; `record DiscoveryProbe(Guid Id, string MasterThumbprint)`; `record DiscoveryReply(Guid ProbeId, string Name, string AgentThumbprint, string Teachers, bool NewTeacherOpen)`; `TeacherStatus.None/You/Others` ("none", "you", "others").

- [ ] **Step 1: Write the failing tests**

`tests/Midview.Protocol.Tests/DiscoveryTests.cs`:

```csharp
using System.Text;
using System.Text.Json;
using Midview.Protocol;
using Xunit;

public class DiscoveryTests
{
    private static readonly string Master = new('A', 40);
    private static readonly string Agent = new('B', 40);
    private static readonly Guid Probe = Guid.Parse("6f2b1c7e-4d3a-4b8e-9c61-2e7a5d0b9f14");

    [Fact]
    public void AProbe_RoundTrips_InTheSpecsShape()
    {
        var bytes = Discovery.Encode(new DiscoveryProbe(Probe, Master));

        Assert.Equal(new DiscoveryProbe(Probe, Master), Discovery.ParseProbe(bytes));
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(1, json.RootElement.GetProperty("midview").GetInt32());
        Assert.Equal(Probe.ToString(), json.RootElement.GetProperty("probe").GetString());
        Assert.Equal(Master, json.RootElement.GetProperty("master").GetString());
    }

    [Fact]
    public void AReply_RoundTrips_InTheSpecsShape()
    {
        var reply = new DiscoveryReply(Probe, "ALUNO-01", Agent, TeacherStatus.Others, true);
        var bytes = Discovery.Encode(reply);

        Assert.Equal(reply, Discovery.ParseReply(bytes));
        using var json = JsonDocument.Parse(bytes);
        Assert.Equal(1, json.RootElement.GetProperty("midview").GetInt32());
        Assert.Equal("ALUNO-01", json.RootElement.GetProperty("name").GetString());
        Assert.Equal(Agent, json.RootElement.GetProperty("agent").GetString());
        Assert.Equal("others", json.RootElement.GetProperty("teachers").GetString());
        Assert.True(json.RootElement.GetProperty("newTeacherOpen").GetBoolean());
    }

    [Theory]
    [InlineData("")]
    [InlineData("isto não é json")]
    [InlineData("[1, 2]")]
    [InlineData("{\"midview\":2,\"probe\":\"6f2b1c7e-4d3a-4b8e-9c61-2e7a5d0b9f14\",\"master\":\"AAAA\"}")]
    [InlineData("{\"midview\":1,\"probe\":\"6f2b1c7e-4d3a-4b8e-9c61-2e7a5d0b9f14\"}")]
    [InlineData("{\"midview\":1,\"master\":\"AAAA\"}")]
    public void WhatIsNotAProbe_IsIgnored(string packet)
    {
        Assert.Null(Discovery.ParseProbe(Encoding.UTF8.GetBytes(packet)));
    }

    [Fact]
    public void AReplyIsNotAProbe_NorTheOtherWayRound()
    {
        Assert.Null(Discovery.ParseProbe(Discovery.Encode(new DiscoveryReply(Probe, "ALUNO-01", Agent, TeacherStatus.None, false))));
        Assert.Null(Discovery.ParseReply(Discovery.Encode(new DiscoveryProbe(Probe, Master))));
    }

    [Fact]
    public void AReply_WithAnUnknownStatus_OrNoName_IsIgnored()
    {
        Assert.Null(Discovery.ParseReply(Discovery.Encode(new DiscoveryReply(Probe, "ALUNO-01", Agent, "maybe", false))));
        Assert.Null(Discovery.ParseReply(Discovery.Encode(new DiscoveryReply(Probe, "", Agent, TeacherStatus.None, false))));
    }

    [Fact]
    public void APacketOverTheLimit_IsIgnored()
    {
        var big = Discovery.Encode(new DiscoveryReply(Probe, new string('x', Discovery.MaxPacketSize), Agent, TeacherStatus.None, false));

        Assert.True(big.Length > Discovery.MaxPacketSize);
        Assert.Null(Discovery.ParseReply(big));
    }
}
```

- [ ] **Step 2: Run them and see them fail**

Run: `"$DOTNET" test tests/Midview.Protocol.Tests --filter "FullyQualifiedName~DiscoveryTests"`
Expected: build errors.

- [ ] **Step 3: Implement**

`src/Midview.Protocol/Discovery.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Midview.Protocol;

// Finding the machines with Midview on the local network: the Master
// broadcasts a probe to UDP Port, and every Agent that hears it answers the
// sender. Nothing here is trusted: a reply only tells the teacher what is
// there. Identity comes from pairing, or from thumbprints already pinned in
// the classroom list. One reply per probe, about the probe's size, so it
// can't amplify traffic.
public static class Discovery
{
    public const int Port = 28901;
    public const int MaxPacketSize = 2048;
    public const int Version = 1;

    // How long the Master listens for replies. Windows lets unicast
    // replies to a broadcast in for 3 seconds by default.
    public static readonly TimeSpan ListenTime = TimeSpan.FromSeconds(2);

    public static byte[] Encode(DiscoveryProbe probe) =>
        JsonSerializer.SerializeToUtf8Bytes(new ProbeJson(Version, probe.Id, probe.MasterThumbprint));

    public static byte[] Encode(DiscoveryReply reply) =>
        JsonSerializer.SerializeToUtf8Bytes(new ReplyJson(
            Version, reply.ProbeId, reply.Name, reply.AgentThumbprint, reply.Teachers, reply.NewTeacherOpen));

    public static DiscoveryProbe? ParseProbe(byte[] packet) =>
        Read<ProbeJson>(packet) is { Midview: Version, Master: { Length: > 0 } master, Agent: null } json && json.Probe != Guid.Empty
            ? new DiscoveryProbe(json.Probe, master)
            : null;

    public static DiscoveryReply? ParseReply(byte[] packet) =>
        Read<ReplyJson>(packet) is { Midview: Version, Name: { Length: > 0 } name, Agent: { Length: > 0 } agent, Teachers: { } teachers } json
        && json.Probe != Guid.Empty && TeacherStatus.IsKnown(teachers)
            ? new DiscoveryReply(json.Probe, name, agent, teachers, json.NewTeacherOpen)
            : null;

    private static T? Read<T>(byte[] packet) where T : class
    {
        if (packet.Length == 0 || packet.Length > MaxPacketSize)
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(packet);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // "agent" is read here only to tell a reply apart from a probe.
    private sealed record ProbeJson(
        [property: JsonPropertyName("midview")] int Midview,
        [property: JsonPropertyName("probe")] Guid Probe,
        [property: JsonPropertyName("master")] string? Master,
        [property: JsonPropertyName("agent"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Agent = null);

    private sealed record ReplyJson(
        [property: JsonPropertyName("midview")] int Midview,
        [property: JsonPropertyName("probe")] Guid Probe,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("agent")] string? Agent,
        [property: JsonPropertyName("teachers")] string? Teachers,
        [property: JsonPropertyName("newTeacherOpen")] bool NewTeacherOpen);
}

public sealed record DiscoveryProbe(Guid Id, string MasterThumbprint);

public sealed record DiscoveryReply(Guid ProbeId, string Name, string AgentThumbprint, string Teachers, bool NewTeacherOpen);

// DiscoveryReply.Teachers: whom the machine trusts, as the asking teacher
// sees it.
public static class TeacherStatus
{
    public const string None = "none";
    public const string You = "you";
    public const string Others = "others";

    public static bool IsKnown(string value) => value is None or You or Others;
}
```

Note: a reply carries `"agent"`, so `ParseProbe` refuses it; a probe has no `"name"`, so `ParseReply` refuses it.

- [ ] **Step 4: Run them and see them pass**

Run: `"$DOTNET" test tests/Midview.Protocol.Tests`
Expected: all pass.

---

### Task 7: `AgentServer` gate, `PairRequest` and `AllowNewTeacher`

**Files:**
- Modify: `src/Midview.Agent/AgentServer.cs` (constructor, `HandleClient`)
- Test: `tests/Midview.Agent.Tests/AgentPairingTests.cs` (new)

**Interfaces:**
- Consumes: `SecureChannel.AuthenticateAsServerAcceptingAnyClient`, `.RemoteThumbprint` (Task 3); `PairingService` (Task 5); `TrustStore.IsTrusted(string)` (Task 1).
- Produces: `AgentServer(..., AppBlockCommandHandler? appBlock = null, PairingService? pairing = null)`.

- [ ] **Step 1: Write the failing tests**

`tests/Midview.Agent.Tests/AgentPairingTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

// The Agent over real TLS on loopback. Any certificate gets through the
// handshake now, so what keeps a stranger out is AgentServer's gate.
public class AgentPairingTests : IDisposable
{
    private readonly string _trustedPath = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid():N}.json");
    private readonly string _statePath = Path.Combine(Path.GetTempPath(), $"midview-state-{Guid.NewGuid():N}.json");
    private readonly X509Certificate2 _agentCert = SelfSignedCertificate.Create("agent-under-test");
    private readonly X509Certificate2 _teacher = SelfSignedCertificate.Create("teacher-under-test");
    private readonly FakeFirewallController _firewall = new();
    private readonly RecordingDisplay _display = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serverTask;
    private readonly int _port;

    public AgentPairingTests()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var trust = TrustStore.FromFile(_trustedPath);
        var pairing = new PairingService(trust, _trustedPath, _display, () => DateTime.Now, "ALUNO-07", () => new string('5', 40));
        var handler = new SiteBlockCommandHandler(new SiteBlockState(), _statePath, "unused-hosts-path", _firewall);
        var server = new AgentServer(listener, _agentCert, trust, handler, pairing: pairing);
        _serverTask = Task.Run(() => server.Run(_stop.Token));
    }

    public void Dispose()
    {
        _stop.Cancel();
        _serverTask.Wait(TimeSpan.FromSeconds(5));
        File.Delete(_trustedPath);
        File.Delete(_statePath);
        _agentCert.Dispose();
        _teacher.Dispose();
    }

    private (TcpClient Tcp, SecureChannel Channel) Connect()
    {
        var tcp = new TcpClient();
        tcp.Connect(IPAddress.Loopback, _port);
        tcp.ReceiveTimeout = 30_000;
        return (tcp, SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcp, "agent-under-test", _teacher));
    }

    // The teacher's side of Pairing, by hand (the Master's PairingSession
    // does the same).
    private (int Code, PairingResult Result) PairOverTheNetwork()
    {
        var (tcp, channel) = Connect();
        using var _ = tcp;
        using var __ = channel;
        channel.Send(new Message(MessageType.PairRequest, Encoding.UTF8.GetBytes("PROF-01")));
        var commit = channel.Receive();
        Assert.Equal(MessageType.PairCommit, commit.Type);
        var masterNonce = Pairing.NewNonce();
        channel.Send(new Message(MessageType.PairNonce, masterNonce));
        var reveal = channel.Receive();
        Assert.Equal(MessageType.PairReveal, reveal.Type);
        Assert.True(Pairing.Opens(commit.Payload, reveal.Payload));
        Assert.Equal(_agentCert.Thumbprint, channel.RemoteThumbprint);
        var code = Pairing.Code(_teacher.Thumbprint, channel.RemoteThumbprint, reveal.Payload, masterNonce);
        channel.Send(new Message(MessageType.PairConfirm, Array.Empty<byte>()));
        var done = channel.Receive();
        Assert.Equal(MessageType.PairDone, done.Type);
        return (code, PairingResult.Decode(done.Payload)!);
    }

    // The reply, or null when the Agent closed the connection unanswered.
    private Message? Command(MessageType type)
    {
        var (tcp, channel) = Connect();
        using var _ = tcp;
        using var __ = channel;
        channel.Send(new Message(type, Array.Empty<byte>()));
        try
        {
            return channel.Receive();
        }
        catch (Exception)
        {
            return null;
        }
    }

    [Fact]
    public async Task APairedTeacher_IsTrustedForCommands()
    {
        Assert.Null(await Task.Run(() => Command(MessageType.UnblockInternet)).WaitAsync(TimeSpan.FromSeconds(30)));

        var (code, result) = await Task.Run(PairOverTheNetwork).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(code, _display.Shown.Single());
        Assert.Equal("ALUNO-07", result.Name);

        var ack = await Task.Run(() => Command(MessageType.UnblockInternet)).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(MessageType.CommandAck, ack!.Type);
    }

    [Theory]
    [InlineData(MessageType.Power)]
    [InlineData(MessageType.SetPowerSchedule)]
    [InlineData(MessageType.UpdateOffer)]
    [InlineData(MessageType.GetUpdateStatus)]
    [InlineData(MessageType.SetAppBlock)]
    [InlineData(MessageType.GetAppBlock)]
    [InlineData(MessageType.GetWakeAddress)]
    [InlineData(MessageType.BlockInternet)]
    [InlineData(MessageType.UnblockInternet)]
    [InlineData(MessageType.BlockDomain)]
    [InlineData(MessageType.UnblockDomain)]
    [InlineData(MessageType.AllowNewTeacher)]
    public async Task ATeacherItDoesNotTrust_IsCutOffUnanswered(MessageType command)
    {
        File.WriteAllText(_trustedPath, $"[\"{new string('C', 40)}\"]");

        var reply = await Task.Run(() => Command(command)).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Null(reply);
        Assert.Equal(0, _firewall.ApplyBlockCalls);
        Assert.Equal(0, _firewall.ApplyUnblockCalls);
    }

    [Fact]
    public async Task AllowNewTeacher_FromATrustedTeacher_OpensTheWindow()
    {
        File.WriteAllText(_trustedPath, $"[\"{_teacher.Thumbprint}\"]");

        var ack = await Task.Run(() => Command(MessageType.AllowNewTeacher)).WaitAsync(TimeSpan.FromSeconds(30));

        var (succeeded, detail) = CommandResult.Decode(ack!.Payload);
        Assert.True(succeeded);
        Assert.StartsWith("liberada até", detail);
    }

    private sealed class RecordingDisplay : IPairingCodeDisplay
    {
        public bool SomeoneLoggedOn => true;

        public List<int> Shown { get; } = new();

        public IDisposable? Show(int code, string teacherName)
        {
            Shown.Add(code);
            return new MemoryStream();
        }
    }
}
```

- [ ] **Step 2: Run them and see them fail**

Run: `"$DOTNET" test tests/Midview.Agent.Tests --filter "FullyQualifiedName~AgentPairingTests"`
Expected: build error (`pairing:` parameter doesn't exist).

- [ ] **Step 3: Implement**

In `src/Midview.Agent/AgentServer.cs`:
- add `using System.Text;` is not needed (`Pairing.Refused` builds the payload);
- add the constant and field:

```csharp
    // A pairing waits on the teacher, who has Pairing.ConfirmTimeout to
    // compare the codes; a little more, so the Agent's own deadline is
    // what ends it.
    private static readonly int PairingTimeoutMs = (int)(Pairing.ConfirmTimeout + TimeSpan.FromSeconds(30)).TotalMilliseconds;

    private readonly PairingService? _pairing;
```

- add the last constructor parameter `PairingService? pairing = null` and `_pairing = pairing;`;
- replace the first lines of `HandleClient`, from `using var channel = SecureChannel.AuthenticateAsServer(...)` through `var message = channel.Receive();`, with:

```csharp
        using var channel = SecureChannel.AuthenticateAsServerAcceptingAnyClient(tcpClient, _serverCertificate);
        var message = channel.Receive();

        // Anyone may ask to pair (PairingService decides who may); any other
        // message needs a teacher this machine trusts. Any certificate gets
        // through the handshake now, so this is what keeps strangers out.
        if (message.Type == MessageType.PairRequest)
        {
            if (_pairing is null)
            {
                channel.Send(Pairing.Refused("Esta máquina não aceita pareamento."));
                return;
            }
            client.ReceiveTimeout = PairingTimeoutMs;
            _pairing.Handle(
                channel.RemoteThumbprint, _serverCertificate.Thumbprint, message.Payload,
                channel.Receive, channel.Send, WakeAddressText(client));
            return;
        }

        if (!_masterTrustStore.IsTrusted(channel.RemoteThumbprint))
        {
            AgentLog.Warn($"Refused {message.Type} from a teacher this machine doesn't trust ({channel.RemoteThumbprint}).");
            return;
        }

        if (message.Type == MessageType.AllowNewTeacher)
        {
            var (succeeded, detail) = _pairing?.AllowNewTeacher() ?? (false, "esta máquina não aceita pareamento");
            channel.Send(new Message(MessageType.CommandAck, CommandResult.Encode(succeeded, detail)));
            return;
        }
```

- add, at the end of the class:

```csharp
    // The MAC address of the card this connection came in on, as the
    // classroom list keeps it, or null.
    private string? WakeAddressText(TcpClient client)
    {
        var localAddress = ((IPEndPoint)client.Client.LocalEndPoint!).Address;
        return _findWakeAddress(localAddress) is { } mac && WakeOnLan.IsUsableMacAddress(mac)
            ? WakeOnLan.FormatMacAddress(mac)
            : null;
    }
```

(Check `WakeOnLan.FormatMacAddress` produces "AA-BB-CC-DD-EE-FF"; if it uses another separator, pass the result through `WakeOnLan.NormalizeMacAddress`.)

- [ ] **Step 4: Run all Agent tests**

Run: `"$DOTNET" test tests/Midview.Agent.Tests`
Expected: all pass. That includes `ConnectionThatNeverHandshakes_DoesNotBlockTheNextCommand`: its teacher is trusted, so the gate lets it through.

---

### Task 8: The code on the student's screen

**Files:**
- Modify: `src/Midview.Agent/UserSessionProcessLauncher.cs`
- Create: `src/Midview.Agent/SessionPairingCodeDisplay.cs`, `src/Midview.Session/PairingCodeWindow.cs`
- Modify: `src/Midview.Session/Program.cs` (right after `EnableVisualStyles()`)
- Test: `tests/Midview.Session.Tests/PairingCodeWindowTests.cs` (new)

**Interfaces:**
- Consumes: `PairingCodeArguments` (Task 4), `IPairingCodeDisplay` (Task 5).
- Produces: `UserSessionProcessLauncher.TryLaunchWithArguments(string arguments, out int processId)`, `UserSessionProcessLauncher.SomeoneLoggedOn()`; `SessionPairingCodeDisplay(UserSessionProcessLauncher)`; `PairingCodeWindow.Texts(int code, string teacherName) : (string Title, string Detail, string Code, string Footer)`, `PairingCodeWindow.Run(int code, string teacherName)`.

- [ ] **Step 1: Write the failing test**

`tests/Midview.Session.Tests/PairingCodeWindowTests.cs`:

```csharp
using Midview.Session;
using Xunit;

public class PairingCodeWindowTests
{
    [Fact]
    public void Texts_AreTheSpecs()
    {
        var (title, detail, code, footer) = PairingCodeWindow.Texts(482913, "PROF-01");

        Assert.Equal("Pareamento com o professor", title);
        Assert.Equal("O computador PROF-01 está conectando esta máquina ao Midview.", detail);
        Assert.Equal("482 913", code);
        Assert.Equal("O professor confere se o mesmo código aparece na tela dele.", footer);
    }
}
```

- [ ] **Step 2: Run it and see it fail**

Run: `"$DOTNET" test tests/Midview.Session.Tests --filter "FullyQualifiedName~PairingCodeWindowTests"`
Expected: build error.

- [ ] **Step 3: Implement**

`src/Midview.Session/PairingCodeWindow.cs`:

```csharp
using System.Drawing;
using System.Windows.Forms;
using Midview.Protocol;

namespace Midview.Session;

// The pairing code on the student's screen while a teacher pairs with this
// machine. The Agent starts it (Midview.Session.exe --pairing-code ...)
// and ends it when the pairing ends; it also closes itself after
// Pairing.ConfirmTimeout. Nothing to click: the teacher compares the code
// with the one on their own screen.
public static class PairingCodeWindow
{
    private static readonly Color Background = Color.FromArgb(64, 64, 64);
    private static readonly Color Ink = Color.FromArgb(235, 235, 235);
    private static readonly Color MutedInk = Color.FromArgb(190, 190, 190);

    public static (string Title, string Detail, string Code, string Footer) Texts(int code, string teacherName) => (
        "Pareamento com o professor",
        $"O computador {teacherName} está conectando esta máquina ao Midview.",
        Pairing.Format(code),
        "O professor confere se o mesmo código aparece na tela dele.");

    public static void Run(int code, string teacherName)
    {
        var (title, detail, codeText, footer) = Texts(code, teacherName);
        using var form = new Form
        {
            Text = title,
            FormBorderStyle = FormBorderStyle.None,
            StartPosition = FormStartPosition.CenterScreen,
            TopMost = true,
            ShowInTaskbar = false,
            BackColor = Background,
            Size = new Size(620, 330),
            Padding = new Padding(28),
        };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, BackColor = Background };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(NewLabel(title, new Font("Segoe UI Semibold", 20f), Ink), 0, 0);
        layout.Controls.Add(NewLabel(detail, new Font("Segoe UI", 12f), MutedInk), 0, 1);
        layout.Controls.Add(NewLabel(codeText, new Font("Segoe UI", 54f, FontStyle.Bold), Ink), 0, 2);
        layout.Controls.Add(NewLabel(footer, new Font("Segoe UI", 11f), MutedInk), 0, 3);
        form.Controls.Add(layout);

        using var timer = new System.Windows.Forms.Timer { Interval = (int)Pairing.ConfirmTimeout.TotalMilliseconds };
        timer.Tick += (_, _) => form.Close();
        timer.Start();
        Application.Run(form);
    }

    private static Label NewLabel(string text, Font font, Color color) => new()
    {
        Text = text,
        Font = font,
        ForeColor = color,
        AutoSize = false,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Height = font.Height + 12,
    };
}
```

In `src/Midview.Session/Program.cs`, right after `System.Windows.Forms.Application.EnableVisualStyles();`:

```csharp

// Started by the Agent to show a pairing code (see PairingCodeWindow): only
// the window, never a second server.
if (PairingCodeArguments.Parse(args) is { } pairing)
{
    PairingCodeWindow.Run(pairing.Code, pairing.TeacherName);
    return;
}
```

In `src/Midview.Agent/UserSessionProcessLauncher.cs`:
- add `using System.Text;`;
- rename the body of `TryLaunch(out int processId)` into `private bool Launch(string? arguments, out int processId)` and make `TryLaunch` call `Launch(null, out processId)`;
- add:

```csharp
    // Another Session for something on the student's screen (the pairing
    // code), never the server: its arguments say what to show.
    public bool TryLaunchWithArguments(string arguments, out int processId) => Launch(arguments, out processId);

    // Whether someone is logged on at the console, so something launched
    // there would be seen.
    public static bool SomeoneLoggedOn()
    {
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF || !WTSQueryUserToken(sessionId, out var userToken))
        {
            return false;
        }
        CloseHandle(userToken);
        return true;
    }
```

- in `Launch`: call `ProtectStudentFolders(primaryToken)` only when `arguments is null`; build `var commandLine = arguments is null ? null : new StringBuilder($"\"{_executablePath}\" {arguments}");` and pass `commandLine` instead of `null` to `CreateProcessAsUser`;
- change the P/Invoke parameter `string? commandLine` to `StringBuilder? commandLine` (CreateProcessW may write to that buffer).

`src/Midview.Agent/SessionPairingCodeDisplay.cs`:

```csharp
using System.ComponentModel;
using System.Diagnostics;
using Midview.Protocol;

namespace Midview.Agent;

// Shows the pairing code by starting the Session in the student's session
// with PairingCodeArguments, and takes it down by ending that process. The
// Agent runs as SYSTEM in session 0 and can't show a window itself.
public sealed class SessionPairingCodeDisplay : IPairingCodeDisplay
{
    private readonly UserSessionProcessLauncher _launcher;

    public SessionPairingCodeDisplay(UserSessionProcessLauncher launcher)
    {
        _launcher = launcher;
    }

    public bool SomeoneLoggedOn => UserSessionProcessLauncher.SomeoneLoggedOn();

    public IDisposable? Show(int code, string teacherName) =>
        _launcher.TryLaunchWithArguments(PairingCodeArguments.Build(code, teacherName), out var processId)
            ? new CodeWindow(processId)
            : null;

    private sealed class CodeWindow(int processId) : IDisposable
    {
        public void Dispose()
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                process.Kill();
            }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // Already gone (it closes itself after the timeout).
            }
        }
    }
}
```

- [ ] **Step 4: Build and run the tests**

Run: `"$DOTNET" build Midview.sln` then `"$DOTNET" test tests/Midview.Session.Tests` then `"$DOTNET" test tests/Midview.Agent.Tests`
Expected: build succeeds; all pass. `SessionPairingCodeDisplay` and the launcher change are Windows glue: the E2E `pair` command checks them (Task 16).

---

## Stage 3 — Finding machines

### Task 9: Agent discovery responder, firewall rule and service wiring

**Files:**
- Create: `src/Midview.Agent/DiscoveryResponder.cs`, `src/Midview.Agent/DiscoveryWorker.cs`
- Modify: `src/Midview.Agent/AgentServerWorker.cs`, `src/Midview.Agent/Program.cs`, `tests/Midview.Agent.Tests/UninstallCleanupTests.cs:33`
- Test: `tests/Midview.Agent.Tests/DiscoveryResponderTests.cs` (new)

**Interfaces:**
- Consumes: `Discovery`, `TeacherStatus` (Task 6); `PairingService` (Task 5); `SessionPairingCodeDisplay` (Task 8).
- Produces: `DiscoveryResponder(PairingService, string agentThumbprint, string machineName)` with `Answer(byte[] packet) : byte[]?` and `RunAsync(UdpClient, CancellationToken)`; `AgentServerWorker.TrustedMastersPath`; `AgentServerWorker.InboundRuleNames` now has three names (`MidviewAgentDiscovery` last).

- [ ] **Step 1: Write the failing tests**

`tests/Midview.Agent.Tests/DiscoveryResponderTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Text;
using Midview.Agent;
using Midview.Protocol;
using Xunit;

public class DiscoveryResponderTests : IDisposable
{
    private static readonly string Teacher = new('A', 40);
    private static readonly string Other = new('C', 40);
    private static readonly string AgentThumbprint = new('B', 40);

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_path);

    private PairingService Service() =>
        new(TrustStore.FromFile(_path), _path, new NoDisplay(), () => new DateTime(2026, 10, 5, 14, 0, 0), "ALUNO-07", () => new string('5', 40));

    private static DiscoveryReply Ask(DiscoveryResponder responder, string master, Guid probe) =>
        Discovery.ParseReply(responder.Answer(Discovery.Encode(new DiscoveryProbe(probe, master)))!)!;

    [Fact]
    public void AMachineWithNoTeacher_SaysSo()
    {
        var probe = Guid.NewGuid();

        var reply = Ask(new DiscoveryResponder(Service(), AgentThumbprint, "ALUNO-07"), Teacher, probe);

        Assert.Equal(new DiscoveryReply(probe, "ALUNO-07", AgentThumbprint, TeacherStatus.None, false), reply);
    }

    [Fact]
    public void ItTellsTheAskingTeacherWhetherItIsTheirs()
    {
        File.WriteAllText(_path, $"[\"{Teacher}\"]");
        var responder = new DiscoveryResponder(Service(), AgentThumbprint, "ALUNO-07");

        Assert.Equal(TeacherStatus.You, Ask(responder, Teacher, Guid.NewGuid()).Teachers);
        Assert.Equal(TeacherStatus.Others, Ask(responder, Other, Guid.NewGuid()).Teachers);
    }

    [Fact]
    public void ItSaysWhenANewTeacherMayPair()
    {
        File.WriteAllText(_path, $"[\"{Teacher}\"]");
        var service = Service();
        service.AllowNewTeacher();

        Assert.True(Ask(new DiscoveryResponder(service, AgentThumbprint, "ALUNO-07"), Other, Guid.NewGuid()).NewTeacherOpen);
    }

    [Fact]
    public void WhatIsNotAProbe_GetsNoAnswer()
    {
        var responder = new DiscoveryResponder(Service(), AgentThumbprint, "ALUNO-07");

        Assert.Null(responder.Answer(Encoding.UTF8.GetBytes("olá")));
        Assert.Null(responder.Answer(Discovery.Encode(new DiscoveryReply(Guid.NewGuid(), "X", AgentThumbprint, TeacherStatus.None, false))));
    }

    [Fact]
    public async Task ItAnswersAProbeOverUdp()
    {
        using var agentSide = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)agentSide.Client.LocalEndPoint!).Port;
        using var stop = new CancellationTokenSource();
        var running = new DiscoveryResponder(Service(), AgentThumbprint, "ALUNO-07").RunAsync(agentSide, stop.Token);
        try
        {
            using var master = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
            var probe = Guid.NewGuid();
            var packet = Discovery.Encode(new DiscoveryProbe(probe, Teacher));
            await master.SendAsync(packet, packet.Length, new IPEndPoint(IPAddress.Loopback, port));

            var received = await master.ReceiveAsync().WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(probe, Discovery.ParseReply(received.Buffer)!.ProbeId);
        }
        finally
        {
            stop.Cancel();
            await running.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    private sealed class NoDisplay : IPairingCodeDisplay
    {
        public bool SomeoneLoggedOn => true;

        public IDisposable? Show(int code, string teacherName) => null;
    }
}
```

In `tests/Midview.Agent.Tests/UninstallCleanupTests.cs`, change the expected rule list to:

```csharp
            new[] { "name=MidviewAgentInbound", "name=MidviewSessionInbound", "name=MidviewAgentDiscovery" },
```

- [ ] **Step 2: Run them and see them fail**

Run: `"$DOTNET" test tests/Midview.Agent.Tests --filter "FullyQualifiedName~DiscoveryResponderTests|FullyQualifiedName~UninstallCleanupTests"`
Expected: build error (no `DiscoveryResponder`).

- [ ] **Step 3: Implement**

`src/Midview.Agent/DiscoveryResponder.cs`:

```csharp
using System.Net.Sockets;
using Midview.Protocol;

namespace Midview.Agent;

// Answers the Master's "who's there?" (see Discovery): this machine's name
// and Agent thumbprint, and whom it trusts as the asking teacher sees it.
public sealed class DiscoveryResponder
{
    private readonly PairingService _pairing;
    private readonly string _agentThumbprint;
    private readonly string _machineName;

    public DiscoveryResponder(PairingService pairing, string agentThumbprint, string machineName)
    {
        _pairing = pairing;
        _agentThumbprint = agentThumbprint;
        _machineName = machineName;
    }

    public byte[]? Answer(byte[] packet) =>
        Discovery.ParseProbe(packet) is { } probe
            ? Discovery.Encode(new DiscoveryReply(
                probe.Id, _machineName, _agentThumbprint, _pairing.TeachersSeenBy(probe.MasterThumbprint), _pairing.NewTeacherOpen))
            : null;

    public async Task RunAsync(UdpClient udp, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            UdpReceiveResult received;
            try
            {
                received = await udp.ReceiveAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                // A reset left by an earlier reply that couldn't be
                // delivered: the socket itself still works.
                continue;
            }

            if (Answer(received.Buffer) is not { } reply)
            {
                continue;
            }

            try
            {
                await udp.SendAsync(reply, received.RemoteEndPoint, cancellationToken);
            }
            catch (SocketException)
            {
                // The teacher's PC went away; nothing to do.
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
```

`src/Midview.Agent/DiscoveryWorker.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Midview.Protocol;

namespace Midview.Agent;

// Runs the DiscoveryResponder on UDP Discovery.Port for as long as the
// service runs; a port that can't be opened is retried, as AgentServer's.
public sealed class DiscoveryWorker : BackgroundService
{
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(5);

    // Without this, Windows ends a UDP socket's next receive with an error
    // when an earlier send got "port unreachable" back.
    private const int SioUdpConnReset = -1744830452;

    private readonly PairingService _pairing;

    public DiscoveryWorker(PairingService pairing)
    {
        _pairing = pairing;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        string agentThumbprint;
        try
        {
            using var certificate = AgentIdentity.LoadOrCreate(AgentIdentity.DefaultCertificatePath);
            agentThumbprint = certificate.Thumbprint;
        }
        catch (Exception ex)
        {
            AgentLog.Warn($"Discovery: no Agent identity, not answering: {ex.Message}");
            return;
        }

        var responder = new DiscoveryResponder(_pairing, agentThumbprint, Environment.MachineName);
        var failing = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, Discovery.Port));
                udp.Client.IOControl(SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null);
                if (failing)
                {
                    AgentLog.Warn("Discovery: answering again.");
                    failing = false;
                }
                await responder.RunAsync(udp, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (!failing)
                {
                    AgentLog.Warn($"Discovery failed, retrying every {RetryDelay.TotalSeconds} s: {ex.Message}");
                    failing = true;
                }
            }

            try
            {
                await Task.Delay(RetryDelay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
```

`src/Midview.Agent/AgentServerWorker.cs`:
- add `private const string DiscoveryInboundRuleName = "MidviewAgentDiscovery";` and make `InboundRuleNames` `new[] { AgentInboundRuleName, SessionInboundRuleName, DiscoveryInboundRuleName }`;
- add `public static string TrustedMastersPath => Path.Combine(AgentDataDirectory.DirectoryPath, "trusted-masters.json");`;
- inject `TrustStore masterTrustStore, PairingService pairing` (fields `_masterTrustStore`, `_pairing`);
- in `ExecuteAsync`: delete the `programDataDir` and `trustedMasters` lines and `var trustStore = new TrustStore(trustedMasters);`; add `EnsureInboundFirewallRuleExists(DiscoveryInboundRuleName, Discovery.Port, "UDP");` after the Session rule; build the server with `_masterTrustStore` and `pairing: _pairing`;
- change `EnsureInboundFirewallRuleExists(string ruleName, int port)` to `(string ruleName, int port, string protocol = "TCP")` and use `$"protocol={protocol}"`.

`src/Midview.Agent/Program.cs`, inside `ConfigureServices`, before `services.AddHostedService<MidviewAgentWorker>();`:

```csharp
        // trusted-masters.json, read again whenever pairing (or anything
        // else) changes it.
        services.AddSingleton(TrustStore.FromFile(AgentServerWorker.TrustedMastersPath));
        services.AddSingleton(provider => new PairingService(
            provider.GetRequiredService<TrustStore>(), AgentServerWorker.TrustedMastersPath,
            new SessionPairingCodeDisplay(new UserSessionProcessLauncher(sessionExecutablePath)),
            () => DateTime.Now, Environment.MachineName,
            () => SessionIdentity.EnsureExists(SessionIdentity.DefaultCertificatePath)));
```

and after `services.AddHostedService<AgentServerWorker>();`: `services.AddHostedService<DiscoveryWorker>();`.

- [ ] **Step 4: Run all Agent tests and build**

Run: `"$DOTNET" test tests/Midview.Agent.Tests` then `"$DOTNET" build Midview.sln`
Expected: all pass, build succeeds.

---

### Task 10: Master — classroom order, roster changes, found machines, discovery client

**Files:**
- Create: `src/Midview.Master/NaturalOrder.cs` (moved out of `InstallPackage.cs`), `src/Midview.Master/Roster.cs`, `src/Midview.Master/DiscoveryClient.cs`, `src/Midview.Master/FoundMachineText.cs`
- Modify: `src/Midview.Master/InstallPackage.cs` (delete the nested `NaturalOrder`; it uses the new public one until Task 13 deletes the file), `src/Midview.Master/WakeOnLanSender.cs` (`BroadcastTargets` port parameter)
- Test: `tests/Midview.Master.Tests/RosterTests.cs`, `tests/Midview.Master.Tests/FoundMachineTextTests.cs`, `tests/Midview.Master.Tests/DiscoveryClientTests.cs` (new)

**Interfaces:**
- Produces: `NaturalOrder.Instance` (`IComparer<string>`); `Roster.Place(IReadOnlyList<KnownMachine> roster, KnownMachine paired) : (int Index, bool Replaces, KnownMachine Machine)`; `Roster.NewAddresses(IReadOnlyList<KnownMachine> roster, IReadOnlyList<FoundMachine> found) : IReadOnlyList<(int Index, KnownMachine Updated)>`; `record FoundMachine(string Name, string Address, string AgentThumbprint, string Teachers, bool NewTeacherOpen)`; `DiscoveryClient.Search(string masterThumbprint, IReadOnlyList<IPEndPoint> targets, TimeSpan listenTime) : IReadOnlyList<FoundMachine>`; `DiscoveryClient.Merge(Guid probeId, IEnumerable<(IPAddress From, DiscoveryReply Reply)>)`; `FoundMachineText.InRoster` ("Já está na sala"), `.Status(FoundMachine, IReadOnlyCollection<KnownMachine>)`, `.CheckedByDefault(...)`, `.SearchSummary(int found, int refreshed)`, `.AllowNewTeacherSummary(IReadOnlyList<(string Name, bool Opened, string Detail)>)`; `WakeOnLanSender.BroadcastTargets(networks, int port = WakeOnLan.Port)`.

- [ ] **Step 1: Write the failing tests**

`tests/Midview.Master.Tests/RosterTests.cs`:

```csharp
using Midview.Master;
using Xunit;

public class RosterTests
{
    private static readonly string X = new('A', 40);
    private static readonly string Y = new('B', 40);
    private static readonly string Session = new('5', 40);

    private static KnownMachine Machine(string name, string host, string? agent, string? mac = null) =>
        new(name, host, Session, agent, mac);

    [Fact]
    public void ANewMachine_GoesInClassroomOrder()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X), Machine("Aluno-10", "10.0.0.10", Y) };

        Assert.Equal((1, false), Pick(Roster.Place(roster, Machine("Aluno-3", "10.0.0.3", new string('C', 40)))));
        Assert.Equal((2, false), Pick(Roster.Place(roster, Machine("Aluno-11", "10.0.0.11", new string('C', 40)))));
        Assert.Equal((0, false), Pick(Roster.Place(roster, Machine("Aluno-1", "10.0.0.1", new string('C', 40)))));
    }

    [Fact]
    public void TheSameAgent_ReplacesItsEntry_EvenUnderAnotherName()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X), Machine("Aluno-10", "10.0.0.10", Y) };

        Assert.Equal((1, true), Pick(Roster.Place(roster, Machine("Lab-10", "10.0.0.50", Y.ToLowerInvariant()))));
    }

    [Fact]
    public void TheSameName_ReplacesIt()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X) };

        Assert.Equal((0, true), Pick(Roster.Place(roster, Machine("aluno-2", "10.0.0.2", Y))));
    }

    [Fact]
    public void TheNetworkCardLearnedBefore_IsKept()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X, "00-1A-2B-3C-4D-5E") };

        var placed = Roster.Place(roster, Machine("Aluno-2", "10.0.0.2", X));

        Assert.Equal("00-1A-2B-3C-4D-5E", placed.Machine.MacAddress);
    }

    [Fact]
    public void AnEntryKeptByAddress_MovesToWhereTheMachineAnswered()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X), Machine("Aluno-3", "10.0.0.3", Y) };
        var found = new[] { new FoundMachine("Aluno-2", "10.0.0.9", X.ToLowerInvariant(), "you", false) };

        var changes = Roster.NewAddresses(roster, found);

        Assert.Equal(0, changes.Single().Index);
        Assert.Equal("10.0.0.9", changes.Single().Updated.HostAddress);
    }

    [Fact]
    public void AnEntryKeptByName_StaysAsTheTeacherTypedIt()
    {
        var roster = new[] { Machine("Aluno-2", "ALUNO-02", X) };
        var found = new[] { new FoundMachine("Aluno-2", "10.0.0.9", X, "you", false) };

        Assert.Empty(Roster.NewAddresses(roster, found));
    }

    [Fact]
    public void NothingMoved_NothingToChange()
    {
        var roster = new[] { Machine("Aluno-2", "10.0.0.2", X), Machine("Aluno-3", "10.0.0.3", null) };
        var found = new[]
        {
            new FoundMachine("Aluno-2", "10.0.0.2", X, "you", false),
            new FoundMachine("Aluno-9", "10.0.0.99", new string('C', 40), "none", false),
        };

        Assert.Empty(Roster.NewAddresses(roster, found));
    }

    private static (int, bool) Pick((int Index, bool Replaces, KnownMachine Machine) placed) => (placed.Index, placed.Replaces);
}
```

`tests/Midview.Master.Tests/FoundMachineTextTests.cs`:

```csharp
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class FoundMachineTextTests
{
    private static readonly string X = new('A', 40);

    private static FoundMachine Found(string teachers, bool open = false) => new("Aluno-2", "10.0.0.2", X, teachers, open);

    [Theory]
    [InlineData(TeacherStatus.None, false, "Sem professor")]
    [InlineData(TeacherStatus.You, false, "Pareada com você")]
    [InlineData(TeacherStatus.Others, false, "Com outro professor")]
    [InlineData(TeacherStatus.Others, true, "Com outro professor — aceitando novo professor")]
    public void Status_SaysWhoseMachineItIs(string teachers, bool open, string expected)
    {
        Assert.Equal(expected, FoundMachineText.Status(Found(teachers, open), Array.Empty<KnownMachine>()));
    }

    [Fact]
    public void AMachineAlreadyInTheRoster_SaysSo_AndIsNotChecked()
    {
        var roster = new[] { new KnownMachine("Aluno-2", "10.0.0.2", new string('5', 40), X.ToLowerInvariant()) };

        Assert.Equal("Já está na sala", FoundMachineText.Status(Found(TeacherStatus.You), roster));
        Assert.False(FoundMachineText.CheckedByDefault(Found(TeacherStatus.None), roster));
    }

    [Fact]
    public void OnlyMachinesWithNoTeacher_AreCheckedByDefault()
    {
        Assert.True(FoundMachineText.CheckedByDefault(Found(TeacherStatus.None), Array.Empty<KnownMachine>()));
        Assert.False(FoundMachineText.CheckedByDefault(Found(TeacherStatus.You), Array.Empty<KnownMachine>()));
        Assert.False(FoundMachineText.CheckedByDefault(Found(TeacherStatus.Others, true), Array.Empty<KnownMachine>()));
    }

    [Theory]
    [InlineData(0, 0, "Nenhuma máquina respondeu. Confira se elas estão ligadas, com o Midview Aluno instalado e nesta mesma rede.")]
    [InlineData(1, 0, "1 máquina encontrada.")]
    [InlineData(25, 0, "25 máquinas encontradas.")]
    [InlineData(25, 1, "25 máquinas encontradas. 1 endereço atualizado na lista da sala.")]
    [InlineData(25, 3, "25 máquinas encontradas. 3 endereços atualizados na lista da sala.")]
    public void SearchSummary(int found, int refreshed, string expected)
    {
        Assert.Equal(expected, FoundMachineText.SearchSummary(found, refreshed));
    }

    [Fact]
    public void AllowNewTeacherSummary_CountsAndNamesTheOnesLeftOut()
    {
        Assert.Equal("A lista da sala está vazia.", FoundMachineText.AllowNewTeacherSummary(Array.Empty<(string, bool, string)>()));
        Assert.Equal(
            "2 de 2 máquinas liberadas por 10 minutos.",
            FoundMachineText.AllowNewTeacherSummary(new[] { ("Aluno-1", true, "liberada até 14:32"), ("Aluno-2", true, "liberada até 14:32") }));
        Assert.Equal(
            "1 de 3 máquinas liberadas por 10 minutos. Não liberadas: Aluno-2 (Sem resposta); Aluno-3 (Midview antigo: atualize-a).",
            FoundMachineText.AllowNewTeacherSummary(new[]
            {
                ("Aluno-1", true, "liberada até 14:32"),
                ("Aluno-2", false, "Sem resposta"),
                ("Aluno-3", false, "Midview antigo: atualize-a"),
            }));
    }
}
```

`tests/Midview.Master.Tests/DiscoveryClientTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class DiscoveryClientTests
{
    private static readonly string Master = new('A', 40);
    private static readonly string X = new('B', 40);
    private static readonly string Y = new('C', 40);

    [Fact]
    public void Merge_OneEntryPerMachine_InClassroomOrder_OnlyForThisProbe()
    {
        var probe = Guid.NewGuid();
        var answers = new (IPAddress, DiscoveryReply)[]
        {
            (IPAddress.Parse("10.0.0.10"), new DiscoveryReply(probe, "Aluno-10", X, TeacherStatus.None, false)),
            (IPAddress.Parse("192.168.56.10"), new DiscoveryReply(probe, "Aluno-10", X.ToLowerInvariant(), TeacherStatus.None, false)),
            (IPAddress.Parse("10.0.0.2"), new DiscoveryReply(probe, "Aluno-2", Y, TeacherStatus.You, false)),
            (IPAddress.Parse("10.0.0.3"), new DiscoveryReply(Guid.NewGuid(), "Aluno-3", new string('D', 40), TeacherStatus.None, false)),
        };

        var found = DiscoveryClient.Merge(probe, answers);

        Assert.Equal(
            new[]
            {
                new FoundMachine("Aluno-2", "10.0.0.2", Y, TeacherStatus.You, false),
                new FoundMachine("Aluno-10", "10.0.0.10", X, TeacherStatus.None, false),
            },
            found);
    }

    [Fact]
    public async Task Search_FindsAMachineThatAnswers()
    {
        using var agentSide = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        var port = ((IPEndPoint)agentSide.Client.LocalEndPoint!).Port;
        var answering = Task.Run(async () =>
        {
            var received = await agentSide.ReceiveAsync();
            var probe = Discovery.ParseProbe(received.Buffer)!;
            Assert.Equal(Master, probe.MasterThumbprint);
            var reply = Discovery.Encode(new DiscoveryReply(probe.Id, "ALUNO-07", X, TeacherStatus.None, false));
            await agentSide.SendAsync(reply, reply.Length, received.RemoteEndPoint);
        });

        var found = await Task.Run(() =>
            DiscoveryClient.Search(Master, new[] { new IPEndPoint(IPAddress.Loopback, port) }, TimeSpan.FromSeconds(2)));

        await answering.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(new[] { new FoundMachine("ALUNO-07", "127.0.0.1", X, TeacherStatus.None, false) }, found);
    }
}
```

- [ ] **Step 2: Run them and see them fail**

Run: `"$DOTNET" test tests/Midview.Master.Tests --filter "FullyQualifiedName~RosterTests|FullyQualifiedName~FoundMachineTextTests|FullyQualifiedName~DiscoveryClientTests"`
Expected: build errors.

- [ ] **Step 3: Implement**

`src/Midview.Master/NaturalOrder.cs`: move the nested class from `InstallPackage.cs` as a public top-level class, with the same body:

```csharp
using System.Text.RegularExpressions;

namespace Midview.Master;

// "Aluno-2" before "Aluno-10": runs of digits compare as numbers.
public sealed class NaturalOrder : IComparer<string>
{
    public static readonly NaturalOrder Instance = new();

    private static readonly Regex Parts = new(@"\d+|\D+", RegexOptions.CultureInvariant);

    public int Compare(string? x, string? y)
    {
        var left = Parts.Matches(x ?? string.Empty);
        var right = Parts.Matches(y ?? string.Empty);
        for (var i = 0; i < Math.Min(left.Count, right.Count); i++)
        {
            var a = left[i].Value;
            var b = right[i].Value;
            var result = char.IsDigit(a[0]) && char.IsDigit(b[0])
                ? CompareNumbers(a, b)
                : string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            if (result != 0)
            {
                return result;
            }
        }
        return left.Count.CompareTo(right.Count);
    }

    // By length first once leading zeros are gone, so any number of
    // digits works without overflowing.
    private static int CompareNumbers(string a, string b)
    {
        a = a.TrimStart('0');
        b = b.TrimStart('0');
        return a.Length != b.Length ? a.Length.CompareTo(b.Length) : string.CompareOrdinal(a, b);
    }
}
```

Delete the nested `NaturalOrder` class from `InstallPackage.cs` (its `NaturalOrder.Instance` reference then resolves to the new one).

`src/Midview.Master/Roster.cs`:

```csharp
using System.Net;

namespace Midview.Master;

// The classroom-list changes that pairing and searching make, worked out
// on the list; MainWindow applies them to its tiles.
public static class Roster
{
    // Where a newly paired machine goes: in place of the entry with the
    // same Agent (it paired again), else of the one with the same name (it
    // was reinstalled), else in classroom order (Aluno-2 before Aluno-10).
    // A network card learned before is kept if the new entry has none.
    public static (int Index, bool Replaces, KnownMachine Machine) Place(IReadOnlyList<KnownMachine> roster, KnownMachine paired)
    {
        var index = IndexOf(roster, entry => SameAgent(entry, paired.AgentThumbprint));
        if (index < 0)
        {
            index = IndexOf(roster, entry => string.Equals(entry.Name, paired.Name, StringComparison.OrdinalIgnoreCase));
        }
        if (index >= 0)
        {
            return (index, true, paired.KeepingMacAddressOf(roster[index]));
        }

        var position = IndexOf(roster, entry => NaturalOrder.Instance.Compare(entry.Name, paired.Name) > 0);
        return (position < 0 ? roster.Count : position, false, paired);
    }

    // Machines that answered a search from another address than the list
    // has, matched by Agent thumbprint. An answer is only a hint: connecting
    // still checks the pinned thumbprint, so a false one can only make a
    // machine unreachable. Entries kept by IP address only: one kept by
    // computer name stays as the teacher typed it.
    public static IReadOnlyList<(int Index, KnownMachine Updated)> NewAddresses(
        IReadOnlyList<KnownMachine> roster, IReadOnlyList<FoundMachine> found)
    {
        var changes = new List<(int, KnownMachine)>();
        for (var i = 0; i < roster.Count; i++)
        {
            var entry = roster[i];
            if (!IPAddress.TryParse(entry.HostAddress, out _))
            {
                continue;
            }

            var answer = found.FirstOrDefault(machine => SameAgent(entry, machine.AgentThumbprint));
            if (answer is not null && !string.Equals(answer.Address, entry.HostAddress, StringComparison.OrdinalIgnoreCase))
            {
                changes.Add((i, entry with { HostAddress = answer.Address }));
            }
        }
        return changes;
    }

    private static bool SameAgent(KnownMachine entry, string? agentThumbprint) =>
        entry.AgentThumbprint is not null && agentThumbprint is not null
        && string.Equals(entry.AgentThumbprint, agentThumbprint, StringComparison.OrdinalIgnoreCase);

    private static int IndexOf(IReadOnlyList<KnownMachine> roster, Func<KnownMachine, bool> match)
    {
        for (var i = 0; i < roster.Count; i++)
        {
            if (match(roster[i]))
            {
                return i;
            }
        }
        return -1;
    }
}
```

`src/Midview.Master/DiscoveryClient.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using Midview.Protocol;

namespace Midview.Master;

// A machine that answered "Procurar na rede". Address is where the answer
// came from.
public sealed record FoundMachine(string Name, string Address, string AgentThumbprint, string Teachers, bool NewTeacherOpen);

// The Master's side of Discovery: one probe to every target (the
// broadcasts, see WakeOnLanSender.BroadcastTargets), then the answers that
// arrive within listenTime.
public static class DiscoveryClient
{
    // See DiscoveryWorker: keeps one unreachable target from ending the
    // listening early.
    private const int SioUdpConnReset = -1744830452;

    public static IReadOnlyList<FoundMachine> Search(string masterThumbprint, IReadOnlyList<IPEndPoint> targets, TimeSpan listenTime)
    {
        var probe = new DiscoveryProbe(Guid.NewGuid(), masterThumbprint);
        var packet = Discovery.Encode(probe);
        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0)) { EnableBroadcast = true };
        udp.Client.IOControl(SioUdpConnReset, new byte[] { 0, 0, 0, 0 }, null);
        foreach (var target in targets)
        {
            try
            {
                udp.Send(packet, packet.Length, target);
            }
            catch (SocketException)
            {
                // A card that just went down: the other targets still go.
            }
        }

        var answers = new List<(IPAddress, DiscoveryReply)>();
        var deadline = DateTime.UtcNow + listenTime;
        while (deadline - DateTime.UtcNow is var left && left > TimeSpan.Zero)
        {
            try
            {
                using var timeout = new CancellationTokenSource(left);
                var received = udp.ReceiveAsync(timeout.Token).AsTask().GetAwaiter().GetResult();
                if (Discovery.ParseReply(received.Buffer) is { } reply)
                {
                    answers.Add((received.RemoteEndPoint.Address, reply));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (SocketException)
            {
                // Keep listening.
            }
        }
        return Merge(probe.Id, answers);
    }

    // One entry per machine (by Agent; a machine with two cards answers
    // twice, and the first answer wins), in classroom order. Answers to
    // someone else's probe are dropped.
    public static IReadOnlyList<FoundMachine> Merge(Guid probeId, IEnumerable<(IPAddress From, DiscoveryReply Reply)> answers) =>
        answers
            .Where(answer => answer.Reply.ProbeId == probeId)
            .GroupBy(answer => answer.Reply.AgentThumbprint.ToUpperInvariant())
            .Select(group => group.First())
            .Select(answer => new FoundMachine(
                answer.Reply.Name, answer.From.ToString(), answer.Reply.AgentThumbprint.ToUpperInvariant(),
                answer.Reply.Teachers, answer.Reply.NewTeacherOpen))
            .OrderBy(machine => machine.Name, NaturalOrder.Instance)
            .ToList();
}
```

`src/Midview.Master/FoundMachineText.cs`:

```csharp
using Midview.Protocol;

namespace Midview.Master;

// What "Máquinas da sala" says about a search and the machines it found.
public static class FoundMachineText
{
    public const string InRoster = "Já está na sala";

    public static string Status(FoundMachine found, IReadOnlyCollection<KnownMachine> roster) =>
        IsInRoster(found, roster) ? InRoster
        : found.Teachers switch
        {
            TeacherStatus.None => "Sem professor",
            TeacherStatus.You => "Pareada com você",
            _ when found.NewTeacherOpen => "Com outro professor — aceitando novo professor",
            _ => "Com outro professor",
        };

    public static bool CheckedByDefault(FoundMachine found, IReadOnlyCollection<KnownMachine> roster) =>
        found.Teachers == TeacherStatus.None && !IsInRoster(found, roster);

    public static string SearchSummary(int found, int refreshed)
    {
        if (found == 0)
        {
            return "Nenhuma máquina respondeu. Confira se elas estão ligadas, com o Midview Aluno instalado e nesta mesma rede.";
        }

        var text = found == 1 ? "1 máquina encontrada." : $"{found} máquinas encontradas.";
        return refreshed switch
        {
            0 => text,
            1 => $"{text} 1 endereço atualizado na lista da sala.",
            _ => $"{text} {refreshed} endereços atualizados na lista da sala.",
        };
    }

    public static string AllowNewTeacherSummary(IReadOnlyList<(string Name, bool Opened, string Detail)> results)
    {
        if (results.Count == 0)
        {
            return "A lista da sala está vazia.";
        }

        var opened = results.Count(result => result.Opened);
        var text = $"{opened} de {results.Count} máquinas liberadas por 10 minutos.";
        var leftOut = results.Where(result => !result.Opened).Select(result => $"{result.Name} ({result.Detail})").ToList();
        return leftOut.Count == 0 ? text : $"{text} Não liberadas: {string.Join("; ", leftOut)}.";
    }

    private static bool IsInRoster(FoundMachine found, IReadOnlyCollection<KnownMachine> roster) =>
        roster.Any(entry => string.Equals(entry.AgentThumbprint, found.AgentThumbprint, StringComparison.OrdinalIgnoreCase));
}
```

In `WakeOnLanSender.BroadcastTargets`, add the parameter `int port = WakeOnLan.Port` and use `port` in both `new IPEndPoint(...)` calls.

- [ ] **Step 4: Run all Master tests**

Run: `"$DOTNET" test tests/Midview.Master.Tests`
Expected: all pass, including `InstallPackageTests` and `WakeOnLanSenderTests`.

---

## Stage 4 — The teacher's program

### Task 11: Master `PairingSession`

**Files:**
- Create: `src/Midview.Master/PairingSession.cs`
- Test: `tests/Midview.Master.Tests/PairingSessionTests.cs` (new)

**Interfaces:**
- Consumes: `SecureChannel.AuthenticateAsClientAcceptingAnyServer` (Task 3), `Pairing`, `PairingResult` (Task 4), `TcpConnect.WithTimeout`, `ConnectionClient.AgentPort`, `WakeOnLan.NormalizeMacAddress`.
- Produces: `PairingSession.Start(string address, X509Certificate2 masterCertificate, string teacherName, int port = ConnectionClient.AgentPort)`, `.Address`, `.AgentThumbprint`, `.Code`, `.CodeText`, `.Confirm() : KnownMachine`, `.Cancel()`, `IDisposable`; `PairingRefusedException`; `PairingSession.InvalidAnswer`, `PairingSession.OldAgent`.

- [ ] **Step 1: Write the failing tests**

`tests/Midview.Master.Tests/PairingSessionTests.cs`:

```csharp
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class PairingSessionTests : IDisposable
{
    private static readonly string Session = new('5', 40);

    private readonly X509Certificate2 _agentCert = SelfSignedCertificate.Create("agent-under-test");
    private readonly X509Certificate2 _teacher = SelfSignedCertificate.Create("teacher-under-test");

    public void Dispose()
    {
        _agentCert.Dispose();
        _teacher.Dispose();
    }

    // One connection to a scripted Agent on loopback.
    private (int Port, Task Done) FakeAgent(Action<SecureChannel> script)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var done = Task.Run(() =>
        {
            try
            {
                using var client = listener.AcceptTcpClient();
                client.ReceiveTimeout = 30_000;
                using var channel = SecureChannel.AuthenticateAsServerAcceptingAnyClient(client, _agentCert);
                script(channel);
            }
            finally
            {
                listener.Stop();
            }
        });
        return (port, done);
    }

    // An Agent that pairs as Pairing says; records what it saw.
    private sealed class HonestAgent
    {
        public string TeacherName { get; private set; } = string.Empty;

        public int Code { get; private set; } = -1;

        public MessageType Answer { get; private set; }

        public void Run(SecureChannel channel, string agentThumbprint)
        {
            var request = channel.Receive();
            Assert.Equal(MessageType.PairRequest, request.Type);
            TeacherName = Encoding.UTF8.GetString(request.Payload);
            var agentNonce = Pairing.NewNonce();
            channel.Send(new Message(MessageType.PairCommit, Pairing.Commit(agentNonce)));
            var masterNonce = channel.Receive().Payload;
            Code = Pairing.Code(channel.RemoteThumbprint, agentThumbprint, agentNonce, masterNonce);
            channel.Send(new Message(MessageType.PairReveal, agentNonce));
            Answer = channel.Receive().Type;
            if (Answer == MessageType.PairConfirm)
            {
                channel.Send(new Message(MessageType.PairDone, new PairingResult("ALUNO-07", Session, "00:1a:2b:3c:4d:5e").Encode()));
            }
        }
    }

    [Fact]
    public async Task Confirming_GivesTheMachineForTheClassroomList()
    {
        var agent = new HonestAgent();
        var (port, done) = FakeAgent(channel => agent.Run(channel, _agentCert.Thumbprint));

        using var session = await Task.Run(() => PairingSession.Start("127.0.0.1", _teacher, "PROF-01", port)).WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(agent.Code, session.Code);
        Assert.Equal(Pairing.Format(agent.Code), session.CodeText);
        Assert.Equal(_agentCert.Thumbprint, session.AgentThumbprint);

        var machine = await Task.Run(session.Confirm).WaitAsync(TimeSpan.FromSeconds(30));
        await done.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("PROF-01", agent.TeacherName);
        Assert.Equal(new KnownMachine("ALUNO-07", "127.0.0.1", Session, _agentCert.Thumbprint, "00-1A-2B-3C-4D-5E"), machine);
    }

    [Fact]
    public async Task Cancelling_TellsTheMachine()
    {
        var agent = new HonestAgent();
        var (port, done) = FakeAgent(channel => agent.Run(channel, _agentCert.Thumbprint));

        using (var session = await Task.Run(() => PairingSession.Start("127.0.0.1", _teacher, "PROF-01", port)).WaitAsync(TimeSpan.FromSeconds(30)))
        {
            session.Cancel();
        }
        await done.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(MessageType.PairCancel, agent.Answer);
    }

    [Fact]
    public async Task ARevealThatBreaksTheCommitment_IsRefused()
    {
        var (port, done) = FakeAgent(channel =>
        {
            channel.Receive();
            channel.Send(new Message(MessageType.PairCommit, Pairing.Commit(Pairing.NewNonce())));
            channel.Receive();
            channel.Send(new Message(MessageType.PairReveal, Pairing.NewNonce()));
            try
            {
                channel.Receive();
            }
            catch (Exception)
            {
                // The teacher's side hung up, as it should.
            }
        });

        var error = await Assert.ThrowsAsync<PairingRefusedException>(() =>
            Task.Run(() => PairingSession.Start("127.0.0.1", _teacher, "PROF-01", port)).WaitAsync(TimeSpan.FromSeconds(30)));
        await done.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(PairingSession.InvalidAnswer, error.Message);
    }

    [Fact]
    public async Task ARefusal_CarriesTheMachinesReason()
    {
        var (port, done) = FakeAgent(channel =>
        {
            channel.Receive();
            channel.Send(Pairing.Refused("Esta máquina já tem professor."));
        });

        var error = await Assert.ThrowsAsync<PairingRefusedException>(() =>
            Task.Run(() => PairingSession.Start("127.0.0.1", _teacher, "PROF-01", port)).WaitAsync(TimeSpan.FromSeconds(30)));
        await done.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal("Esta máquina já tem professor.", error.Message);
    }

    [Fact]
    public async Task AnOldAgent_IsToldToUpdate()
    {
        var (port, done) = FakeAgent(channel =>
        {
            channel.Receive();
            channel.Send(new Message(MessageType.CommandAck, new byte[] { 0 }));
        });

        var error = await Assert.ThrowsAsync<PairingRefusedException>(() =>
            Task.Run(() => PairingSession.Start("127.0.0.1", _teacher, "PROF-01", port)).WaitAsync(TimeSpan.FromSeconds(30)));
        await done.WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(PairingSession.OldAgent, error.Message);
    }
}
```

- [ ] **Step 2: Run them and see them fail**

Run: `"$DOTNET" test tests/Midview.Master.Tests --filter "FullyQualifiedName~PairingSessionTests"`
Expected: build errors.

- [ ] **Step 3: Implement**

`src/Midview.Master/PairingSession.cs`:

```csharp
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Midview.Protocol;

namespace Midview.Master;

// The machine said no (Message is its reason, in Portuguese) or answered in
// a way pairing doesn't allow.
public sealed class PairingRefusedException(string message) : Exception(message);

// The teacher's side of pairing with one machine (see Pairing): Start runs
// the exchange up to the code; the teacher compares it with the one on the
// student's screen, then Confirm or Cancel. The connection stays open in
// between, for up to Pairing.ConfirmTimeout.
public sealed class PairingSession : IDisposable
{
    public const string InvalidAnswer = "A máquina respondeu de um jeito inválido ao pareamento. Tente de novo.";
    public const string OldAgent = "Midview antigo nesta máquina: atualize-a pela rede antes de parear.";

    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    private const int StepTimeoutMs = 15_000;

    private readonly TcpClient _tcpClient;
    private readonly SecureChannel _channel;

    private PairingSession(TcpClient tcpClient, SecureChannel channel, string address, int code)
    {
        _tcpClient = tcpClient;
        _channel = channel;
        Address = address;
        Code = code;
    }

    public string Address { get; }

    public string AgentThumbprint => _channel.RemoteThumbprint;

    public int Code { get; }

    public string CodeText => Pairing.Format(Code);

    public static PairingSession Start(string address, X509Certificate2 masterCertificate, string teacherName, int port = ConnectionClient.AgentPort)
    {
        var tcpClient = TcpConnect.WithTimeout(address, port, ConnectTimeout);
        SecureChannel? channel = null;
        try
        {
            tcpClient.ReceiveTimeout = StepTimeoutMs;
            tcpClient.SendTimeout = StepTimeoutMs;
            channel = SecureChannel.AuthenticateAsClientAcceptingAnyServer(tcpClient, address, masterCertificate);
            channel.Send(new Message(MessageType.PairRequest, Encoding.UTF8.GetBytes(teacherName)));
            var commitment = Expect(channel.Receive(), MessageType.PairCommit);

            var masterNonce = Pairing.NewNonce();
            channel.Send(new Message(MessageType.PairNonce, masterNonce));
            var agentNonce = Expect(channel.Receive(), MessageType.PairReveal);
            if (!Pairing.Opens(commitment, agentNonce))
            {
                throw new PairingRefusedException(InvalidAnswer);
            }

            var code = Pairing.Code(masterCertificate.Thumbprint, channel.RemoteThumbprint, agentNonce, masterNonce);
            return new PairingSession(tcpClient, channel, address, code);
        }
        catch
        {
            channel?.Dispose();
            tcpClient.Dispose();
            throw;
        }
    }

    // The teacher saw the same code on both screens.
    public KnownMachine Confirm()
    {
        _channel.Send(new Message(MessageType.PairConfirm, Array.Empty<byte>()));
        var result = PairingResult.Decode(Expect(_channel.Receive(), MessageType.PairDone))
            ?? throw new PairingRefusedException(InvalidAnswer);
        return new KnownMachine(
            result.Name, Address, result.SessionThumbprint.Trim().ToUpperInvariant(), AgentThumbprint,
            WakeOnLan.NormalizeMacAddress(result.MacAddress));
    }

    // The codes didn't match, or the teacher gave up: the machine takes its
    // code down and trusts nothing.
    public void Cancel()
    {
        try
        {
            _channel.Send(new Message(MessageType.PairCancel, Array.Empty<byte>()));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or SocketException)
        {
            // Gone already; nothing was trusted.
        }
    }

    public void Dispose()
    {
        _channel.Dispose();
        _tcpClient.Dispose();
    }

    private static byte[] Expect(Message message, MessageType expected) => message.Type switch
    {
        _ when message.Type == expected => message.Payload,
        MessageType.PairRefused => throw new PairingRefusedException(Pairing.RefusalText(message.Payload)),
        MessageType.CommandAck => throw new PairingRefusedException(OldAgent),
        _ => throw new PairingRefusedException(InvalidAnswer),
    };
}
```

- [ ] **Step 4: Run all Master tests**

Run: `"$DOTNET" test tests/Midview.Master.Tests`
Expected: all pass.

---

### Task 12: `UpdatePackage` takes the student program's folder

**Files:**
- Modify: `src/Midview.Master/UpdatePackage.cs`, `src/Midview.Master/UpdateWindow.xaml.cs` (constructor, comment at line 64-66), `tests/Midview.Master.Tests/UpdatePackageTests.cs`, `tests/Midview.Master.Tests/UpdateClientTests.cs:21`

**Interfaces:**
- Produces: `UpdatePackage.Identify(string studentFolder)`, `UpdatePackage.Create(string studentFolder, string tempDirectory)`, `UpdatePackage.BundledStudentFolder` (`<Master's folder>\aluno`); `UpdateWindow(string studentFolder, Func<IReadOnlyList<KnownMachine>> targets, X509Certificate2 masterCertificate)`. `StudentProgramFolder` is removed.

- [ ] **Step 1: Change the tests first**

In `UpdatePackageTests`:
- constructor: `Directory.CreateDirectory(_package);`
- `private string Folder => _package;`
- `Identify_APackageWithoutTheStudentProgram_IsTooOld`: replace `Directory.Delete(Folder);` and the call with `Assert.Throws<InvalidDataException>(() => UpdatePackage.Identify(Path.Combine(_package, "nao-existe")))`. Keep the `Assert.Contains("antigo demais", ...)`.

In `UpdateClientTests.cs:21`, replace `var folder = UpdatePackage.StudentProgramFolder(_package);` with `var folder = _package;` (and make sure `_package` is created first).

- [ ] **Step 2: Run them and see them fail**

Run: `"$DOTNET" test tests/Midview.Master.Tests --filter "FullyQualifiedName~UpdatePackageTests|FullyQualifiedName~UpdateClientTests"`
Expected: failures. `Identify(_package)` still looks in `_package\programa\aluno`.

- [ ] **Step 3: Implement**

In `UpdatePackage.cs`:
- replace `StudentProgramFolder` with:

```csharp
    // The student program the setup installs next to the Master
    // ("C:\Program Files\Midview Professor\aluno"): what "Atualizar as
    // máquinas" sends. Missing in a development build.
    public static string BundledStudentFolder => Path.Combine(AppContext.BaseDirectory, "aluno");
```

- `Identify(string studentFolder)` and `Create(string studentFolder, string tempDirectory)` use the parameter as `folder` directly;
- messages: `"Este programa do aluno é antigo demais para atualizar pela rede (não tem midview-build.json): instale de novo com o Midview-Instalador atual."` and `"Este programa do aluno é antigo demais para atualizar pela rede: instale de novo com o Midview-Instalador atual."`; `$"O programa do aluno não tem {missing}."`; `$"O programa do aluno está incompleto: {ErrorText.Describe(ex)}."`;
- update the class comment to say the package is the student program's folder.

In `UpdateWindow.xaml.cs`: rename `_packageDirectory` to `_studentFolder` and the constructor parameter `packageDirectory` to `studentFolder`; `PackageTextBlock.Text = $"Programa do aluno: {studentFolder}";`; the comment on `IdentifyPackageAsync` becomes "the folder may have been replaced since (a newer setup installed in the meantime)".

- [ ] **Step 4: Run all Master tests**

Run: `"$DOTNET" test tests/Midview.Master.Tests`
Expected: all pass. `LabInstallWindow` still compiles: it passes its package folder. Task 13 removes it.

---

### Task 13: "Máquinas da sala", the empty-list button, and the package flow removed

**Files:**
- Create: `src/Midview.Master/FoundRow.cs`, `src/Midview.Master/MachinesWindow.xaml`, `src/Midview.Master/MachinesWindow.xaml.cs`
- Modify: `src/Midview.Master/MainWindow.xaml` (Configurar group, empty-list panel), `src/Midview.Master/MainWindow.xaml.cs`, `src/Midview.Master/App.xaml.cs`, `src/Midview.Master/AddMachineWindow.xaml:9,18`, `src/Midview.Master/AddMachineWindow.xaml.cs:12`, `src/Midview.Master/SiteBlockWindow.xaml.cs:84`
- Delete: `src/Midview.Master/LabInstallWindow.xaml`, `src/Midview.Master/LabInstallWindow.xaml.cs`, `src/Midview.Master/InstallPackage.cs`, `tests/Midview.Master.Tests/InstallPackageTests.cs`

**Interfaces:**
- Consumes: `DiscoveryClient`, `FoundMachine`, `FoundMachineText`, `Roster` (Task 10); `PairingSession` (Task 11); `UpdatePackage.BundledStudentFolder`, `UpdateWindow` (Task 12); `WakeOnLanSender.BroadcastTargets(networks, port)`.
- Produces: `MachinesWindow(X509Certificate2 masterCertificate, Func<IReadOnlyList<KnownMachine>> roster, Action<KnownMachine> addPaired, Func<IReadOnlyList<FoundMachine>, int> refreshAddresses)` with `public Task SearchAsync()`; the named mutex `Midview.Master` (and `Global\Midview.Master`) the setup's `AppMutex` looks for.

- [ ] **Step 1: The row view model**

`src/Midview.Master/FoundRow.cs`:

```csharp
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Midview.Master;

// One machine in "Máquinas da sala" after a search, and how pairing it
// goes. Session is the open pairing while the teacher compares the codes.
public sealed class FoundRow : INotifyPropertyChanged
{
    private string _status;
    private bool _isChecked;
    private string _codeText = string.Empty;
    private bool _awaitingConfirmation;
    private string _outcome = string.Empty;
    private bool _outcomeIsError;

    public FoundRow(FoundMachine machine, string status, bool isChecked)
    {
        Machine = machine;
        _status = status;
        _isChecked = isChecked;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public FoundMachine Machine { get; }

    public string Name => Machine.Name;

    public string Address => Machine.Address;

    public PairingSession? Session { get; set; }

    // Being paired: left out of another "Parear".
    public bool IsBusy { get; set; }

    public string Status { get => _status; set => Set(ref _status, value); }

    public bool IsChecked { get => _isChecked; set => Set(ref _isChecked, value); }

    public string CodeText { get => _codeText; set => Set(ref _codeText, value); }

    public bool AwaitingConfirmation { get => _awaitingConfirmation; set => Set(ref _awaitingConfirmation, value); }

    public string Outcome { get => _outcome; set => Set(ref _outcome, value); }

    public bool OutcomeIsError { get => _outcomeIsError; set => Set(ref _outcomeIsError, value); }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
```

- [ ] **Step 2: The window**

`src/Midview.Master/MachinesWindow.xaml`:

```xml
<Window x:Class="Midview.Master.MachinesWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Máquinas da sala" Height="680" Width="780"
        WindowStartupLocation="CenterOwner"
        Background="{StaticResource CanvasBrush}" FontFamily="{StaticResource UiFont}"
        Closed="OnClosed">
    <Window.Resources>
        <BooleanToVisibilityConverter x:Key="VisibleWhenTrue" />
        <Style x:Key="SectionTitle" TargetType="TextBlock">
            <Setter Property="FontSize" Value="15" />
            <Setter Property="FontWeight" Value="SemiBold" />
            <Setter Property="Foreground" Value="{StaticResource InkBrush}" />
        </Style>
        <Style x:Key="SectionHint" TargetType="TextBlock">
            <Setter Property="TextWrapping" Value="Wrap" />
            <Setter Property="Foreground" Value="{StaticResource InkMutedBrush}" />
            <Setter Property="Margin" Value="0,2,0,8" />
        </Style>
    </Window.Resources>
    <DockPanel Margin="16">
        <StackPanel DockPanel.Dock="Top">
            <TextBlock Text="Procurar e parear" Style="{StaticResource SectionTitle}" />
            <TextBlock Style="{StaticResource SectionHint}"
                       Text="Procura as máquinas com o Midview Aluno nesta rede. Ao parear, cada máquina mostra um código na tela: confira se é o mesmo que aparece aqui." />
            <StackPanel Orientation="Horizontal">
                <Button x:Name="SearchButton" Content="Procurar na rede" Click="OnSearchClicked" />
                <Button x:Name="PairButton" Content="Parear as selecionadas" Click="OnPairClicked" IsEnabled="False" Margin="8,0,0,0" />
                <TextBlock x:Name="SearchStatusTextBlock" VerticalAlignment="Center" Margin="12,0,0,0"
                           TextWrapping="Wrap" MaxWidth="440" Foreground="{StaticResource InkMutedBrush}" />
            </StackPanel>
        </StackPanel>

        <StackPanel DockPanel.Dock="Bottom" Margin="0,14,0,0">
            <Border BorderBrush="{StaticResource CardLineBrush}" BorderThickness="0,1,0,0" Padding="0,12,0,0">
                <StackPanel>
                    <TextBlock Text="Atualizar as máquinas" Style="{StaticResource SectionTitle}" />
                    <TextBlock x:Name="StudentVersionTextBlock" Style="{StaticResource SectionHint}" />
                    <Button x:Name="UpdateButton" Content="Atualizar as máquinas pela rede..." Click="OnUpdateClicked" HorizontalAlignment="Left" />
                </StackPanel>
            </Border>
            <Border BorderBrush="{StaticResource CardLineBrush}" BorderThickness="0,1,0,0" Padding="0,12,0,0" Margin="0,14,0,0">
                <StackPanel>
                    <TextBlock Text="Outro professor" Style="{StaticResource SectionTitle}" />
                    <TextBlock Style="{StaticResource SectionHint}"
                               Text="Para um colega parear o computador dele com as máquinas desta sala." />
                    <Button x:Name="AllowNewTeacherButton" Content="Permitir novo professor (10 minutos)" Click="OnAllowNewTeacherClicked" HorizontalAlignment="Left" />
                    <TextBlock x:Name="AllowNewTeacherResultTextBlock" TextWrapping="Wrap" Margin="0,6,0,0" />
                </StackPanel>
            </Border>
        </StackPanel>

        <ScrollViewer VerticalScrollBarVisibility="Auto" Margin="0,12,0,0">
            <ItemsControl x:Name="FoundItemsControl">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Border Background="{StaticResource CardBrush}" BorderBrush="{StaticResource CardLineBrush}"
                                BorderThickness="1" CornerRadius="6" Padding="10,8" Margin="0,0,0,6">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <CheckBox IsChecked="{Binding IsChecked}" VerticalAlignment="Center" Margin="0,0,10,0" />
                                <StackPanel Grid.Column="1">
                                    <TextBlock>
                                        <Run Text="{Binding Name, Mode=OneWay}" FontWeight="SemiBold" Foreground="{StaticResource InkBrush}" />
                                        <Run Text="  " />
                                        <Run Text="{Binding Address, Mode=OneWay}" Foreground="{StaticResource InkMutedBrush}" />
                                    </TextBlock>
                                    <TextBlock Text="{Binding Status}" FontSize="12" Foreground="{StaticResource InkMutedBrush}" />
                                    <TextBlock Text="{Binding Outcome}" FontSize="12" TextWrapping="Wrap" Margin="0,2,0,0">
                                        <TextBlock.Style>
                                            <Style TargetType="TextBlock">
                                                <Setter Property="Foreground" Value="{StaticResource InkBrush}" />
                                                <Style.Triggers>
                                                    <DataTrigger Binding="{Binding OutcomeIsError}" Value="True">
                                                        <Setter Property="Foreground" Value="#B3261E" />
                                                    </DataTrigger>
                                                    <DataTrigger Binding="{Binding Outcome}" Value="">
                                                        <Setter Property="Visibility" Value="Collapsed" />
                                                    </DataTrigger>
                                                </Style.Triggers>
                                            </Style>
                                        </TextBlock.Style>
                                    </TextBlock>
                                </StackPanel>
                                <StackPanel Grid.Column="2" Orientation="Horizontal" VerticalAlignment="Center">
                                    <TextBlock Text="{Binding CodeText}" FontFamily="Consolas" FontSize="26" FontWeight="Bold"
                                               Foreground="{StaticResource InkBrush}" VerticalAlignment="Center" Margin="0,0,12,0" />
                                    <Button Content="Confere" Click="OnConfirmClicked" Margin="0,0,6,0"
                                            Visibility="{Binding AwaitingConfirmation, Converter={StaticResource VisibleWhenTrue}}" />
                                    <Button Content="Não confere" Click="OnMismatchClicked"
                                            Visibility="{Binding AwaitingConfirmation, Converter={StaticResource VisibleWhenTrue}}" />
                                </StackPanel>
                            </Grid>
                        </Border>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </ScrollViewer>
    </DockPanel>
</Window>
```

`src/Midview.Master/MachinesWindow.xaml.cs`:

```csharp
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// "Máquinas da sala": find the machines with Midview on this network and
// pair with them (each shows a code the teacher compares), push the student
// program installed with this one to the class, and let another teacher
// pair for 10 minutes.
public partial class MachinesWindow : Window
{
    private const string MismatchText =
        "O código não conferiu. Pode haver outro computador no meio da conexão. Tente de novo e, se repetir, avise o responsável pela rede.";

    private readonly X509Certificate2 _masterCertificate;
    private readonly Func<IReadOnlyList<KnownMachine>> _roster;
    private readonly Action<KnownMachine> _addPaired;
    private readonly Func<IReadOnlyList<FoundMachine>, int> _refreshAddresses;
    private readonly ObservableCollection<FoundRow> _rows = new();
    private bool _closed;

    // addPaired and refreshAddresses change the classroom list (MainWindow);
    // both may throw when the list can't be written to disk.
    public MachinesWindow(
        X509Certificate2 masterCertificate, Func<IReadOnlyList<KnownMachine>> roster,
        Action<KnownMachine> addPaired, Func<IReadOnlyList<FoundMachine>, int> refreshAddresses)
    {
        InitializeComponent();
        _masterCertificate = masterCertificate;
        _roster = roster;
        _addPaired = addPaired;
        _refreshAddresses = refreshAddresses;
        FoundItemsControl.ItemsSource = _rows;
        ShowStudentProgramVersion();
    }

    private async void OnSearchClicked(object sender, RoutedEventArgs e) => await SearchAsync();

    public async Task SearchAsync()
    {
        SearchButton.IsEnabled = false;
        SearchStatusTextBlock.Text = "Procurando...";
        try
        {
            var thumbprint = _masterCertificate.Thumbprint;
            var found = await Task.Run(() => DiscoveryClient.Search(
                thumbprint, WakeOnLanSender.BroadcastTargets(WakeOnLanSender.LocalNetworks(), Discovery.Port), Discovery.ListenTime));
            if (_closed)
            {
                return;
            }

            ShowFound(found);
            var summary = FoundMachineText.SearchSummary(found.Count, 0);
            try
            {
                summary = FoundMachineText.SearchSummary(found.Count, _refreshAddresses(found));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                summary += $" Os endereços novos não puderam ser gravados na lista da sala: {ErrorText.Describe(ex)}";
            }
            SearchStatusTextBlock.Text = summary;
        }
        catch (Exception ex) when (ex is SocketException or IOException)
        {
            SearchStatusTextBlock.Text = $"A procura falhou: {ErrorText.Describe(ex)}";
        }
        finally
        {
            SearchButton.IsEnabled = true;
            UpdatePairButton();
        }
    }

    // Rows still being paired stay; the rest are replaced by what answered.
    private void ShowFound(IReadOnlyList<FoundMachine> found)
    {
        foreach (var row in _rows.Where(row => !row.IsBusy).ToList())
        {
            _rows.Remove(row);
        }

        var roster = _roster();
        foreach (var machine in found)
        {
            if (_rows.Any(row => string.Equals(row.Machine.AgentThumbprint, machine.AgentThumbprint, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            var row = new FoundRow(machine, FoundMachineText.Status(machine, roster), FoundMachineText.CheckedByDefault(machine, roster));
            row.PropertyChanged += OnRowChanged;
            _rows.Add(row);
        }
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FoundRow.IsChecked))
        {
            UpdatePairButton();
        }
    }

    private void UpdatePairButton() => PairButton.IsEnabled = _rows.Any(row => row.IsChecked && !row.IsBusy);

    private async void OnPairClicked(object sender, RoutedEventArgs e)
    {
        var chosen = _rows.Where(row => row.IsChecked && !row.IsBusy).ToList();
        var teacherName = Environment.MachineName;
        await Task.WhenAll(chosen.Select(row => StartPairingAsync(row, teacherName)));
    }

    private async Task StartPairingAsync(FoundRow row, string teacherName)
    {
        row.IsBusy = true;
        row.IsChecked = false;
        row.OutcomeIsError = false;
        row.Outcome = "Conectando...";
        UpdatePairButton();
        try
        {
            var session = await Task.Run(() => PairingSession.Start(row.Address, _masterCertificate, teacherName));
            if (_closed)
            {
                session.Cancel();
                session.Dispose();
                return;
            }
            row.Session = session;
            row.CodeText = session.CodeText;
            row.AwaitingConfirmation = true;
            row.Outcome = "Confira se a tela da máquina mostra este mesmo código. Você tem 5 minutos.";
        }
        catch (PairingRefusedException ex)
        {
            Fail(row, ex.Message);
        }
        catch (Exception ex) when (ex is SocketException or IOException or AuthenticationException or OperationCanceledException)
        {
            Fail(row, $"Não foi possível conectar: {FailureText.DescribeAgent(ex)}");
        }
    }

    private async void OnConfirmClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not FoundRow { Session: { } session } row)
        {
            return;
        }

        row.AwaitingConfirmation = false;
        row.Outcome = "Confirmando...";
        try
        {
            var machine = await Task.Run(session.Confirm);
            row.Status = FoundMachineText.InRoster;
            try
            {
                _addPaired(machine);
                row.OutcomeIsError = false;
                row.Outcome = "Pareada. Já está na sala.";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                row.OutcomeIsError = true;
                row.Outcome = $"Pareada e colocada na grade, mas a lista da sala não pôde ser gravada: {ErrorText.Describe(ex)}";
            }
        }
        catch (PairingRefusedException ex)
        {
            row.OutcomeIsError = true;
            row.Outcome = ex.Message;
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            row.OutcomeIsError = true;
            row.Outcome = "A máquina encerrou o pareamento (o tempo para conferir pode ter acabado). Tente de novo.";
        }
        finally
        {
            EndSession(row);
        }
    }

    private void OnMismatchClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not FoundRow { Session: { } session } row)
        {
            return;
        }

        session.Cancel();
        EndSession(row);
        row.OutcomeIsError = true;
        row.Outcome = MismatchText;
    }

    private void EndSession(FoundRow row)
    {
        row.Session?.Dispose();
        row.Session = null;
        row.AwaitingConfirmation = false;
        row.CodeText = string.Empty;
        row.IsBusy = false;
        UpdatePairButton();
    }

    private void Fail(FoundRow row, string text)
    {
        row.IsBusy = false;
        row.OutcomeIsError = true;
        row.Outcome = text;
        UpdatePairButton();
    }

    private void ShowStudentProgramVersion()
    {
        try
        {
            var manifest = BuildManifest.Load(UpdatePackage.BundledStudentFolder);
            StudentVersionTextBlock.Text = $"Versão do programa do aluno neste PC: {manifest.Build}";
            UpdateButton.IsEnabled = true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidDataException or IOException)
        {
            StudentVersionTextBlock.Text = "Esta cópia não traz o programa do aluno. Instale pelo Midview-Instalador.";
            UpdateButton.IsEnabled = false;
        }
    }

    private void OnUpdateClicked(object sender, RoutedEventArgs e) =>
        new UpdateWindow(UpdatePackage.BundledStudentFolder, _roster, _masterCertificate) { Owner = this }.Show();

    private async void OnAllowNewTeacherClicked(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(this,
            "Por 10 minutos, as máquinas da sala vão aceitar parear com outro professor. Use quando um colega for parear o computador dele.",
            "Permitir novo professor", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (answer != MessageBoxResult.OK)
        {
            return;
        }

        AllowNewTeacherButton.IsEnabled = false;
        AllowNewTeacherResultTextBlock.Text = "Avisando as máquinas...";
        var machines = _roster();
        var results = await Task.WhenAll(machines.Select(machine => Task.Run(() => AllowNewTeacher(machine))));
        AllowNewTeacherResultTextBlock.Text = FoundMachineText.AllowNewTeacherSummary(results);
        AllowNewTeacherButton.IsEnabled = true;
    }

    private (string Name, bool Opened, string Detail) AllowNewTeacher(KnownMachine machine)
    {
        if (machine.AgentThumbprint is null)
        {
            return (machine.Name, false, "sem a identificação do Agent");
        }

        try
        {
            var (succeeded, detail) = CommandClient.SendCommandForResult(
                machine.HostAddress, ConnectionClient.AgentPort, machine.AgentThumbprint, _masterCertificate,
                MessageType.AllowNewTeacher, Array.Empty<byte>());
            return (machine.Name, succeeded, succeeded || detail.Length > 0 ? detail : "Midview antigo: atualize-a");
        }
        catch (Exception ex)
        {
            return (machine.Name, false, FailureText.DescribeAgent(ex));
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _closed = true;
        foreach (var row in _rows.Where(row => row.Session is not null))
        {
            row.Session!.Cancel();
            row.Session.Dispose();
            row.Session = null;
        }
    }
}
```

- [ ] **Step 3: Wire it into the main window**

`MainWindow.xaml`, Configurar group: replace the "Instalar nos alunos" button with:

```xml
                        <local:ToolbarButton Content="Máquinas da sala" Icon="&#xE7F8;" Click="OnMachinesClicked"
                                             ToolTip="Procura e pareia as máquinas, atualiza o programa dos alunos e permite outro professor." />
```

Replace the `EmptyRosterTextBlock` with:

```xml
            <StackPanel x:Name="EmptyRosterPanel" Visibility="Collapsed" Panel.ZIndex="1"
                        HorizontalAlignment="Center" VerticalAlignment="Center">
                <TextBlock FontSize="14" TextAlignment="Center" Foreground="{StaticResource InkMutedBrush}"
                           Text="Nenhuma máquina na sala ainda.&#x0a;Instale o Midview Aluno nas máquinas e procure-as na rede." />
                <Button Content="Procurar máquinas na rede" Click="OnSearchNetworkClicked"
                        HorizontalAlignment="Center" Margin="0,12,0,0" Padding="16,7" />
            </StackPanel>
```

`MainWindow.xaml.cs`:
- `UpdateEmptyHint()` sets `EmptyRosterPanel.Visibility`;
- replace `OnLabInstallClicked` with:

```csharp
    private MachinesWindow? _machinesWindow;

    private void OnMachinesClicked(object sender, RoutedEventArgs e) => OpenMachinesWindow(search: false);

    private void OnSearchNetworkClicked(object sender, RoutedEventArgs e) => OpenMachinesWindow(search: true);

    // One at a time: asking again brings it to the front.
    private async void OpenMachinesWindow(bool search)
    {
        var window = _machinesWindow;
        if (window is null)
        {
            window = new MachinesWindow(
                _masterCertificate, () => _tiles.Select(tile => tile.Machine).ToList(), AddPairedMachine, RefreshAddresses) { Owner = this };
            window.Closed += (_, _) => _machinesWindow = null;
            _machinesWindow = window;
            window.Show();
        }
        else
        {
            window.Activate();
        }

        if (search)
        {
            await window.SearchAsync();
        }
    }
```

- replace `ImportMachines` (and its comment) with:

```csharp
    // Called by MachinesWindow when a pairing is confirmed (see Roster.Place).
    // The grid changes first; a failure to write the list then propagates
    // for MachinesWindow to report.
    private void AddPairedMachine(KnownMachine machine)
    {
        var (index, replaces, placed) = Roster.Place(_tiles.Select(tile => tile.Machine).ToList(), machine);
        if (replaces)
        {
            RemoveTileAt(index);
        }
        InsertTile(index, placed);
        UpdateEmptyHint();
        SaveRoster();
    }

    // Called by MachinesWindow after a search: machines found at another
    // address (see Roster.NewAddresses). Returns how many moved.
    private int RefreshAddresses(IReadOnlyList<FoundMachine> found)
    {
        var changes = Roster.NewAddresses(_tiles.Select(tile => tile.Machine).ToList(), found);
        foreach (var (index, updated) in changes)
        {
            RemoveTileAt(index);
            InsertTile(index, updated);
        }
        if (changes.Count > 0)
        {
            SaveRoster();
        }
        return changes.Count;
    }
```

`App.xaml.cs`: before `new MainWindow().Show();`:

```csharp
        // What the setup's AppMutex looks for, to ask that the program be
        // closed before it is replaced. Kept for the process's lifetime.
        _runningMutexes = new[] { new Mutex(false, "Midview.Master"), new Mutex(false, @"Global\Midview.Master") };
```

with the field `private static Mutex[]? _runningMutexes;`. Creating the `Global\` one can fail for an unprivileged user on some setups, so wrap it in `try { ... } catch (UnauthorizedAccessException) { }` and keep only the local one in that case.

Texts:
- `AddMachineWindow.xaml:9`: `Text="Thumbprint deste MidviewMaster (as máquinas passam a aceitá-lo quando você as pareia em &quot;Máquinas da sala&quot;):"`
- `AddMachineWindow.xaml:18`: `Text="Normalmente as máquinas entram por &quot;Máquinas da sala&quot; &gt; &quot;Procurar na rede&quot;. Use isto só para uma máquina em outra rede, ou para corrigir o endereço de uma máquina."`
- `AddMachineWindow.xaml.cs:12` comment: `"Máquinas da sala" doesn't cover`.
- `SiteBlockWindow.xaml.cs:84`: `"a lista da sala não tem o certificado do Agent desta máquina: pareie-a de novo em \"Máquinas da sala\""`.

Delete `LabInstallWindow.xaml`, `LabInstallWindow.xaml.cs`, `InstallPackage.cs`, `tests/Midview.Master.Tests/InstallPackageTests.cs`.

- [ ] **Step 4: Build and run everything**

Run: `"$DOTNET" build Midview.sln` then `"$DOTNET" test Midview.sln`
Expected: build succeeds except for `tests/Midview.E2E`, which still uses `InstallPackage` and isn't in `Midview.sln` (Task 16 fixes it). All tests pass.

- [ ] **Step 5: Look at it**

Run the Master from `src/Midview.Master/bin/Debug/net8.0-windows/Midview.Master.exe` on a copy of the roster (or with an empty one): the toolbar fits in one row at 1280 px, "Máquinas da sala" opens, "Procurar na rede" finishes in about 2 s with the "Nenhuma máquina respondeu" text, and the update part says "Esta cópia não traz o programa do aluno". Take a screenshot for the user.

---

## Stage 5 — The setup program

### Task 14: Installer scripts

**Files:**
- Modify: `installer/scripts/Comum.ps1` (whole file), `installer/scripts/Desinstalar-Aluno.ps1` (whole file)
- Create: `installer/scripts/Parar-Aluno.ps1`, `installer/scripts/Configurar-Aluno.ps1`
- Delete: `installer/scripts/Instalar-Aluno.ps1`, `installer/scripts/Instalar-Professor.ps1`, `installer/pacote/` (the three `.cmd` files and `LEIA-ME.txt`)
- Modify: `installer/tests/Testar-Scripts.ps1` (whole file)

**Interfaces:**
- Produces: each script takes `-Erro <file>`. On failure it writes its message there in UTF-8 and exits 1; `Midview.iss` shows that message. `Configurar-Aluno.ps1 [-Destino]`, `Desinstalar-Aluno.ps1 [-RemoverDados] [-Destino]` (default `-Destino`: the folder above the script's own), `Parar-Aluno.ps1`.

- [ ] **Step 1: `Comum.ps1`**

Keep, unchanged in behavior: the header, `$NomeServico`, `$PastaDados`, the update constants, `Write-Etapa`, `Assert-PastaDoMidview`, `Stop-Midview`, `Enter-TravaDeAtualizacao`, `ConvertFrom-CaminhoDoServico`, `Get-ExecutavelDoServico`, `Set-ExecutavelDoServico`, `Remove-RestosDeAtualizacao`, `Invoke-Programa`. Delete `ConvertTo-ThumbprintNormalizado`, `Read-ProfessoresAutorizados`, `ConvertTo-TextoJson`, `Write-ProfessoresAutorizados`, `New-FichaMaquina`, `ConvertTo-EnderecoMacNormalizado`, `Get-EnderecoMac`, `Write-TextoUtf8`, `Get-EnderecosIPv4` and `Copy-Pasta`. Change:

```powershell
function Assert-Administrador {
    $identidade = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal $identidade
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'É preciso rodar como administrador.'
    }
}

# A pasta que estes scripts tratam (e o desinstalador apaga) nunca pode ser
# outra coisa que a pasta do próprio Midview.
function Assert-PastaDoMidview([string]$Pasta) {
    $nome = Split-Path -Path $Pasta -Leaf
    if (-not $nome -or $nome -notlike 'Midview*') {
        throw "A pasta de instalação precisa se chamar Midview... (recebi: $Pasta)."
    }
}

# O instalador (installer\Midview.iss) mostra ao professor a mensagem de
# erro gravada aqui, em UTF-8, que ele lê com os acentos certos.
function Write-Erro([string]$Caminho, [string]$Mensagem) {
    if (-not $Caminho) {
        return
    }
    try {
        [IO.File]::WriteAllText($Caminho, $Mensagem, (New-Object Text.UTF8Encoding $false))
    }
    catch {
        # Sem o arquivo, o instalador mostra só o código de saída.
    }
}
```

and in `Remove-RestosDeAtualizacao`'s comment, "Depois de instalar pelo pendrive" becomes "Depois de instalar pelo Midview-Instalador".

- [ ] **Step 2: `Parar-Aluno.ps1`**

```powershell
# Para o Midview Aluno antes de o instalador (Midview-Instalador) copiar os
# arquivos novos: espera uma atualização pela rede terminar, se houver uma,
# e para o serviço e a Session. Numa máquina ainda sem Midview, não faz
# nada. Rodado pelo instalador, como administrador.
[CmdletBinding()]
param(
    [string]$Erro
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Comum.ps1')

$trava = $null
try {
    Assert-Administrador
    Write-Etapa 'Parando a versão anterior (se houver)'
    $trava = Enter-TravaDeAtualizacao
    Stop-Midview
    exit 0
}
catch {
    Write-Host "O Midview não pôde ser parado: $($_.Exception.Message)" -ForegroundColor Red
    Write-Erro $Erro $_.Exception.Message
    exit 1
}
finally {
    if ($trava) {
        $trava.Dispose()
    }
}
```

- [ ] **Step 3: `Configurar-Aluno.ps1`**

```powershell
# Configura o Midview Aluno nesta máquina depois que o instalador
# (Midview-Instalador) copiou os arquivos para a pasta acima desta:
#   1. espera uma atualização pela rede terminar, se houver uma, e para o
#      Midview;
#   2. prepara a identidade da máquina (Midview.Agent.exe --prepare-install);
#   3. registra o serviço MidviewAgent nesta cópia (mesmo que uma
#      atualização pela rede o tenha levado para Midview.update), apaga o
#      que as atualizações pela rede deixaram e inicia o serviço.
# Os professores que a máquina já aceita (trusted-masters.json) ficam como
# estão: quem acrescenta um professor é o pareamento, no Midview Professor.
[CmdletBinding()]
param(
    [string]$Destino = (Split-Path -Path $PSScriptRoot -Parent),
    [string]$Erro
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Comum.ps1')

$trava = $null
try {
    Assert-Administrador
    Assert-PastaDoMidview $Destino
    $agente = Join-Path $Destino 'Midview.Agent.exe'
    if (-not (Test-Path -LiteralPath $agente)) {
        throw "Não achei $agente. Rode o instalador de novo."
    }

    Write-Etapa 'Parando a versão anterior (se houver)'
    $trava = Enter-TravaDeAtualizacao
    Stop-Midview

    Write-Etapa 'Preparando a identidade desta máquina'
    $preparo = Invoke-Programa $agente @('--prepare-install')
    if ($preparo.Codigo -ne 0) {
        throw 'O Midview.Agent não conseguiu preparar a máquina.'
    }

    Write-Etapa 'Registrando e iniciando o serviço MidviewAgent'
    $caminhoServico = '"' + $agente + '"'
    if (Get-Service -Name $NomeServico -ErrorAction SilentlyContinue) {
        Set-ExecutavelDoServico $caminhoServico
        Set-Service -Name $NomeServico -StartupType Automatic
    }
    else {
        New-Service -Name $NomeServico -BinaryPathName $caminhoServico -DisplayName 'Midview Agent' `
            -Description 'Midview: controle da sala de aula pelo professor.' -StartupType Automatic | Out-Null
    }
    # Se o serviço cair, o Windows o reinicia: depois de 5 s, 5 s e 1 min.
    & sc.exe failure $NomeServico reset= 86400 actions= restart/5000/restart/5000/restart/60000 | Out-Null
    Remove-RestosDeAtualizacao
    Start-Service -Name $NomeServico

    Write-Host ''
    Write-Host "Midview Aluno configurado em $env:COMPUTERNAME." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ''
    Write-Host "A configuração NÃO terminou: $($_.Exception.Message)" -ForegroundColor Red
    Write-Erro $Erro $_.Exception.Message
    exit 1
}
finally {
    if ($trava) {
        $trava.Dispose()
    }
}
```

- [ ] **Step 4: `Desinstalar-Aluno.ps1`**

```powershell
# Remove o serviço do Midview Aluno. Rodado pelo desinstalador (Aplicativos
# instalados), como administrador, antes de ele apagar os arquivos. Antes de
# tudo, desfaz o que uma aula pode ter deixado: internet ou sites
# bloqueados, programas bloqueados (Midview.Agent.exe --uninstall-cleanup,
# da versão que o serviço roda, que depois de uma atualização pela rede
# fica em Midview.update). Apaga também o que as atualizações pela rede
# deixaram.
#
# Os dados em C:\ProgramData\Midview (a identidade da máquina e os
# professores pareados) ficam, para uma reinstalação continuar pareada.
# -RemoverDados apaga também.
[CmdletBinding()]
param(
    [switch]$RemoverDados,
    [string]$Destino = (Split-Path -Path $PSScriptRoot -Parent),
    [string]$Erro
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Comum.ps1')

$trava = $null
try {
    Assert-Administrador
    Assert-PastaDoMidview $Destino

    Write-Etapa 'Parando o Midview'
    $trava = Enter-TravaDeAtualizacao
    Stop-Midview

    Write-Etapa 'Desfazendo bloqueios de internet, de sites e de programas'
    $agente = Get-ExecutavelDoServico
    if (-not $agente -or -not (Test-Path -LiteralPath $agente)) {
        $agente = Join-Path $Destino 'Midview.Agent.exe'
    }
    $limpezaCompleta = $false
    if (Test-Path -LiteralPath $agente) {
        $limpezaCompleta = (Invoke-Programa $agente @('--uninstall-cleanup')).Codigo -eq 0
    }
    else {
        Write-Warning "Não achei $agente, então bloqueios que estejam ativos não puderam ser desfeitos."
    }

    Write-Etapa 'Removendo o serviço MidviewAgent'
    if (Get-Service -Name $NomeServico -ErrorAction SilentlyContinue) {
        & sc.exe delete $NomeServico | Out-Null
    }
    Remove-RestosDeAtualizacao -Obrigatorio

    if ($trava) {
        $trava.Dispose()
        $trava = $null
    }
    if ($RemoverDados) {
        Write-Etapa "Apagando $PastaDados"
        if (Test-Path -LiteralPath $PastaDados) {
            Remove-Item -LiteralPath $PastaDados -Recurse -Force
        }
    }
    else {
        Write-Host "Os dados em $PastaDados foram mantidos: instalando de novo, a máquina continua pareada com os mesmos professores."
    }

    if (-not $limpezaCompleta) {
        $mensagem = 'nem tudo o que uma aula deixou pôde ser desfeito.'
        Write-Host "Midview removido, mas $mensagem" -ForegroundColor Yellow
        Write-Erro $Erro $mensagem
        exit 1
    }
    Write-Host "Midview removido de $env:COMPUTERNAME." -ForegroundColor Green
    exit 0
}
catch {
    Write-Host ''
    Write-Host "A remoção NÃO terminou: $($_.Exception.Message)" -ForegroundColor Red
    Write-Erro $Erro $_.Exception.Message
    exit 1
}
finally {
    if ($trava) {
        $trava.Dispose()
    }
}
```

Delete `installer/scripts/Instalar-Aluno.ps1`, `installer/scripts/Instalar-Professor.ps1` and the whole `installer/pacote` folder.

- [ ] **Step 5: `Testar-Scripts.ps1`**

Rewrite it against the setup's build output (`-Pacote` is now `Gerar-Instalador.ps1`'s output folder). Keep `Check` and the failure count. Checks, in order:

```powershell
# Checks a built setup's scripts and contents under whatever PowerShell runs
# this -- in CI (.github/workflows/pacote.yml), Windows PowerShell 5.1, the
# one lab machines have:
#
#   powershell -ExecutionPolicy Bypass -File installer\tests\Testar-Scripts.ps1 -Pacote dist
#
# -Pacote is installer\Gerar-Instalador.ps1's output: the setup, and in
# programa\ what went into it. The scripts are checked there because those
# copies are the ones with the BOM 5.1 needs. The shared functions
# (Comum.ps1) are run for real.
#
# ASCII-only: 5.1 reads a .ps1 without a BOM as ANSI.
```

1. Every `programa\instalador\*.ps1` parses, and starts with a UTF-8 BOM (as today, with `programa\instalador` for `programa\scripts`).
2. `programa\instalador` has `Comum.ps1`, `Parar-Aluno.ps1`, `Configurar-Aluno.ps1`, `Desinstalar-Aluno.ps1` and no `Instalar-Aluno.ps1`.
3. Exactly one `Midview-Instalador-*.exe` in `$Pacote`, named like `^Midview-Instalador-\d{4}\.\d{2}\.\d{2}-\d{4}\.exe$`.
4. The programs (`aluno\Midview.Agent.exe`, `aluno\Midview.Session.exe`, `professor\Midview.Master.exe`) and the manifest checks: the same block as today.
5. Dot-source `programa\instalador\Comum.ps1`, then:
   - `Write-Erro`: writing `"n$([char]0x00E3)o parou"` to a file gives UTF-8 bytes without a BOM that read back equal (`[IO.File]::ReadAllText($f, [Text.Encoding]::UTF8)`); `Write-Erro '' 'x'` does nothing and doesn't throw;
   - keep as they are: the service-path checks, the update-lock checks, and `Remove-RestosDeAtualizacao`. In that block, write the stand-in `update-state.json` with `[IO.File]::WriteAllText(..., '{}')` instead of `Write-TextoUtf8`;
   - keep the `Assert-PastaDoMidview` checks;
   - delete the teacher-list, machine-file, MAC and address checks (those functions are gone).

- [ ] **Step 6: Parse the scripts locally**

Run (PowerShell 5.1):

```powershell
foreach ($f in Get-ChildItem installer\scripts\*.ps1, installer\tests\*.ps1) { $e = $null; [System.Management.Automation.Language.Parser]::ParseFile($f.FullName, [ref]$null, [ref]$e) | Out-Null; "$($f.Name): $($e.Count) errors" }
```

Expected: 0 errors each. The full check runs in Task 15, after the build.

---

### Task 15: `Midview.iss` and `Gerar-Instalador.ps1`

**Files:**
- Create: `installer/Midview.iss`, `installer/Gerar-Instalador.ps1`
- Delete: `installer/Gerar-Pacote.ps1`

**Interfaces:**
- Consumes: the scripts (Task 14); `/TIPO`; `Midview.Master` mutex (Task 13).
- Produces: `dist\Midview-Instalador-<yyyy.MM.dd-HHmm>.exe` and `dist\programa\{aluno,professor,instalador}`; setup exit code 10 when the files were copied but `Configurar-Aluno.ps1` failed.

- [ ] **Step 1: `installer/Midview.iss`**

```iss
; Midview's setup program: one .exe that installs either the teacher's
; program ("Professor") or a lab machine ("Aluno"). See
; docs/superpowers/specs/2026-10-05-installer-and-pairing-design.md.
;
; Built by installer\Gerar-Instalador.ps1, which compiles a UTF-8-with-BOM
; copy of this file and passes:
;   /DVersao=2026.10.05.1718         the version Windows shows
;   /DVersaoArquivo=2026.10.05-1718  in the file name
;   /DPrograma=<folder>              aluno\, professor\ and instalador\
;   /DSaida=<folder>                 where the setup goes
;
; Exit codes beyond Inno's own: 10 = the files were copied, but the
; MidviewAgent service could not be configured (Configurar-Aluno.ps1).

#ifndef Versao
  #define Versao "0.0.0.0"
#endif
#ifndef VersaoArquivo
  #define VersaoArquivo "dev"
#endif
#ifndef Programa
  #error Passe /DPrograma=<pasta com aluno, professor e instalador>: use installer\Gerar-Instalador.ps1.
#endif
#ifndef Saida
  #define Saida "."
#endif

[Setup]
AppId={{6F2B1C7E-4D3A-4B8E-9C61-2E7A5D0B9F14}
AppName=Midview
AppVersion={#Versao}
AppVerName=Midview {#Versao}
AppPublisher=Midview
VersionInfoVersion={#Versao}
DefaultDirName={code:PastaPadrao}
UsePreviousAppDir=no
DisableDirPage=yes
DisableProgramGroupPage=yes
UninstallDisplayName={code:NomeNaLista}
UninstallDisplayIcon={app}\{code:ExecutavelPrincipal}
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
AppMutex=Midview.Master,Global\Midview.Master
CloseApplications=no
RestartApplications=no
WizardStyle=modern
OutputDir={#Saida}
OutputBaseFilename=Midview-Instalador-{#VersaoArquivo}
Compression=lzma2
SolidCompression=yes
SetupLogging=yes

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Files]
Source: "{#Programa}\aluno\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: EhAluno
Source: "{#Programa}\instalador\*.ps1"; DestDir: "{app}\instalador"; Flags: ignoreversion; Check: EhAluno
Source: "{#Programa}\professor\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: EhProfessor
Source: "{#Programa}\aluno\*"; DestDir: "{app}\aluno"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: EhProfessor
; For PrepareToInstall, before anything is copied.
Source: "{#Programa}\instalador\Comum.ps1"; Flags: dontcopy
Source: "{#Programa}\instalador\Parar-Aluno.ps1"; Flags: dontcopy

[Icons]
Name: "{commondesktop}\Midview - Sala de aula"; Filename: "{app}\Midview.Master.exe"; Check: EhProfessor
Name: "{commonprograms}\Midview - Sala de aula"; Filename: "{app}\Midview.Master.exe"; Check: EhProfessor

[InstallDelete]
; What Instalar-Professor.cmd (the USB-stick package) installed for one user.
Type: filesandordirs; Name: "{localappdata}\Programs\Midview Professor"; Check: EhProfessor
Type: files; Name: "{userdesktop}\Midview - Sala de aula.lnk"; Check: EhProfessor
Type: files; Name: "{userprograms}\Midview - Sala de aula.lnk"; Check: EhProfessor

[Registry]
Root: HKLM; Subkey: "Software\Midview"; ValueType: string; ValueName: "Tipo"; ValueData: "{code:TipoEscolhido}"; Flags: uninsdeletekey

[UninstallDelete]
; Everything in it, including files left by an older USB-stick install.
Type: filesandordirs; Name: "{app}"

[Code]
const
  TipoProfessor = 'professor';
  TipoAluno = 'aluno';
  CodigoFalhaAoConfigurar = 10;

var
  PaginaTipo: TInputOptionWizardPage;
  Tipo: String;
  TipoAnterior: String;
  FalhaAoConfigurar: Boolean;

{ The kind already installed on this computer, or ''. }
function TipoInstalado: String;
begin
  if not RegQueryStringValue(HKLM, 'Software\Midview', 'Tipo', Result) then
    Result := '';
end;

function NomeDoTipo(const Valor: String): String;
begin
  if Valor = TipoProfessor then
    Result := 'Professor'
  else
    Result := 'Aluno';
end;

function EhAluno: Boolean;
begin
  Result := Tipo = TipoAluno;
end;

function EhProfessor: Boolean;
begin
  Result := Tipo = TipoProfessor;
end;

function TipoEscolhido(Param: String): String;
begin
  Result := Tipo;
end;

function NomeNaLista(Param: String): String;
begin
  Result := 'Midview ' + NomeDoTipo(Tipo);
end;

function ExecutavelPrincipal(Param: String): String;
begin
  if EhProfessor then
    Result := 'Midview.Master.exe'
  else
    Result := 'Midview.Agent.exe';
end;

function PastaDoTipo(const Valor: String): String;
begin
  if Valor = TipoProfessor then
    Result := ExpandConstant('{autopf}\Midview Professor')
  else
    Result := ExpandConstant('{autopf}\Midview');
end;

function PastaPadrao(Param: String): String;
begin
  Result := PastaDoTipo(Tipo);
end;

function RecusaOutroTipo(const Escolhido: String): String;
begin
  Result := '';
  if (TipoAnterior <> '') and (Escolhido <> TipoAnterior) then
    Result := 'Este computador já tem o Midview ' + NomeDoTipo(TipoAnterior) +
      '. Desinstale-o antes de instalar como ' + NomeDoTipo(Escolhido) + '.';
end;

{ The script's error message (UTF-8, see Write-Erro in Comum.ps1), or ''. }
function LerErro(const Arquivo: String): String;
var
  Linhas: TArrayOfString;
  I: Integer;
begin
  Result := '';
  if LoadStringsFromFile(Arquivo, Linhas) then
    for I := 0 to GetArrayLength(Linhas) - 1 do
      Result := Result + Linhas[I] + #13#10;
  Result := Trim(Result);
end;

function RodarScript(const Script, Argumentos, ArquivoDeErro: String; var Codigo: Integer): Boolean;
begin
  DeleteFile(ArquivoDeErro);
  Result := Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -ExecutionPolicy Bypass -File "' + Script + '" -Erro "' + ArquivoDeErro + '"' + Argumentos,
    '', SW_HIDE, ewWaitUntilTerminated, Codigo);
  Log(Format('%s%s: started=%d, exit code=%d', [Script, Argumentos, Ord(Result), Codigo]));
end;

function DescreverFalha(const ArquivoDeErro: String; Codigo: Integer): String;
begin
  Result := LerErro(ArquivoDeErro);
  if Result = '' then
    Result := 'o script terminou com o código ' + IntToStr(Codigo) + '.';
end;

function InitializeSetup: Boolean;
var
  Pedido, Recusa: String;
begin
  Result := True;
  TipoAnterior := TipoInstalado;
  Pedido := Lowercase(Trim(ExpandConstant('{param:TIPO|}')));
  if Pedido <> '' then begin
    if (Pedido <> TipoProfessor) and (Pedido <> TipoAluno) then begin
      SuppressibleMsgBox('/TIPO precisa ser "professor" ou "aluno" (recebi "' + Pedido + '").', mbError, MB_OK, IDOK);
      Result := False;
      Exit;
    end;
    Recusa := RecusaOutroTipo(Pedido);
    if Recusa <> '' then begin
      SuppressibleMsgBox(Recusa, mbError, MB_OK, IDOK);
      Result := False;
      Exit;
    end;
    Tipo := Pedido;
  end else if WizardSilent then begin
    Log('Silent install without /TIPO: nothing installed.');
    SuppressibleMsgBox('Na instalação silenciosa, diga o tipo: /TIPO=professor ou /TIPO=aluno.', mbError, MB_OK, IDOK);
    Result := False;
  end;
end;

procedure InitializeWizard;
begin
  PaginaTipo := CreateInputOptionPage(wpWelcome,
    'Tipo de instalação', 'Este computador é o do professor ou uma máquina de aluno?',
    'Escolha uma opção e clique em Avançar.', True, False);
  PaginaTipo.Add('Professor: o computador de onde você controla a sala');
  PaginaTipo.Add('Aluno: uma máquina do laboratório');
  if (Tipo = TipoProfessor) or ((Tipo = '') and (TipoAnterior = TipoProfessor)) then
    PaginaTipo.SelectedValueIndex := 0
  else if (Tipo = TipoAluno) or ((Tipo = '') and (TipoAnterior = TipoAluno)) then
    PaginaTipo.SelectedValueIndex := 1;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := (PageID = PaginaTipo.ID) and (ExpandConstant('{param:TIPO|}') <> '');
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var
  Escolhido, Recusa: String;
begin
  Result := True;
  if CurPageID <> PaginaTipo.ID then
    Exit;

  if PaginaTipo.SelectedValueIndex = 0 then
    Escolhido := TipoProfessor
  else if PaginaTipo.SelectedValueIndex = 1 then
    Escolhido := TipoAluno
  else begin
    MsgBox('Escolha Professor ou Aluno.', mbError, MB_OK);
    Result := False;
    Exit;
  end;

  Recusa := RecusaOutroTipo(Escolhido);
  if Recusa <> '' then begin
    MsgBox(Recusa, mbError, MB_OK);
    Result := False;
    Exit;
  end;

  Tipo := Escolhido;
  WizardForm.DirEdit.Text := PastaDoTipo(Tipo);
end;

{ Aluno: stop the Midview that is there before its files are replaced. }
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ArquivoDeErro: String;
  Codigo: Integer;
begin
  Result := '';
  if not EhAluno then
    Exit;
  ExtractTemporaryFile('Comum.ps1');
  ExtractTemporaryFile('Parar-Aluno.ps1');
  ArquivoDeErro := ExpandConstant('{tmp}\parar-erro.txt');
  if not RodarScript(ExpandConstant('{tmp}\Parar-Aluno.ps1'), '', ArquivoDeErro, Codigo) or (Codigo <> 0) then
    Result := 'Não foi possível parar o Midview desta máquina: ' + DescreverFalha(ArquivoDeErro, Codigo);
end;

{ Aluno: once the files are in place, set up and start the service. }
procedure CurStepChanged(CurStep: TSetupStep);
var
  ArquivoDeErro: String;
  Codigo: Integer;
begin
  if (CurStep <> ssPostInstall) or not EhAluno then
    Exit;
  WizardForm.StatusLabel.Caption := 'Configurando o serviço do Midview...';
  ArquivoDeErro := ExpandConstant('{tmp}\configurar-erro.txt');
  if not RodarScript(ExpandConstant('{app}\instalador\Configurar-Aluno.ps1'), '', ArquivoDeErro, Codigo) or (Codigo <> 0) then begin
    FalhaAoConfigurar := True;
    SuppressibleMsgBox('Os arquivos foram copiados, mas o Midview não pôde ser configurado: ' +
      DescreverFalha(ArquivoDeErro, Codigo) + #13#10#13#10 + 'Corrija o problema e rode o instalador de novo.',
      mbError, MB_OK, IDOK);
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  if FalhaAoConfigurar then
    Result := CodigoFalhaAoConfigurar
  else
    Result := 0;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID <> wpFinished then
    Exit;
  if EhAluno and not FalhaAoConfigurar then
    WizardForm.FinishedLabel.Caption := 'O Midview Aluno está instalado e rodando nesta máquina.' + #13#10#13#10 +
      'Agora abra o Midview Professor no computador do professor e clique em "Máquinas da sala" > "Procurar na rede". ' +
      'Pareie esta máquina logo: até lá, qualquer professor da rede pode pareá-la.'
  else if EhProfessor then
    WizardForm.FinishedLabel.Caption := 'O Midview Professor está instalado: abra "Midview - Sala de aula" na Área de Trabalho.' + #13#10#13#10 +
      'Para trazer as máquinas, instale o Midview Aluno nelas e clique em "Máquinas da sala" > "Procurar na rede".';
end;

{ Aluno: undo what a lesson left and remove the service, before the files go. }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  Argumentos, ArquivoDeErro: String;
  Codigo: Integer;
begin
  if (CurUninstallStep <> usUninstall) or (TipoInstalado <> TipoAluno) then
    Exit;

  Argumentos := '';
  if not UninstallSilent then
    if MsgBox('Apagar também os professores pareados e a identidade desta máquina?' + #13#10#13#10 +
        'Escolha Não para manter: instalando de novo, ela continua pareada com os mesmos professores.',
        mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      Argumentos := ' -RemoverDados';

  ArquivoDeErro := AddBackslash(GetTempDir) + 'midview-desinstalar-erro.txt';
  if not RodarScript(ExpandConstant('{app}\instalador\Desinstalar-Aluno.ps1'), Argumentos, ArquivoDeErro, Codigo) or (Codigo <> 0) then
    SuppressibleMsgBox('A limpeza do Midview não foi completa: ' + DescreverFalha(ArquivoDeErro, Codigo) + #13#10#13#10 +
      'Confira se a internet, os sites e os programas estão liberados nesta máquina.', mbError, MB_OK, IDOK);
end;
```

- [ ] **Step 2: `installer/Gerar-Instalador.ps1`**

ASCII-only. Reuse `Gerar-Pacote.ps1`'s `Publish-Project`, `Copy-Text` and `Write-BuildManifest` exactly; give `Write-BuildManifest` a `[datetime]$When` parameter instead of calling `Get-Date` itself. The rest:

```powershell
# Builds Midview's setup program (docs/superpowers/specs/2026-10-05-installer-and-pairing-design.md):
#
#   powershell -ExecutionPolicy Bypass -File installer\Gerar-Instalador.ps1 [-Output <folder>] [-Iscc <ISCC.exe>]
#
# Needs the .NET 8 SDK and Inno Setup 6. The programs are published
# self-contained for win-x64, so no computer needs .NET installed. Output:
#   <Output>\Midview-Instalador-<yyyy.MM.dd-HHmm>.exe
#   <Output>\programa\aluno, professor, instalador   what went into it (the tests use them)
#
# This file stays ASCII-only: Windows PowerShell 5.1 reads a .ps1 without a
# BOM as ANSI.
[CmdletBinding()]
param(
    [string]$Output,
    [string]$Iscc
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Path $PSScriptRoot -Parent
if (-not $Output) {
    $Output = Join-Path $repository 'dist'
}
$programs = Join-Path $Output 'programa'

function Find-Iscc {
    if ($Iscc) {
        return $Iscc
    }
    foreach ($candidate in @(
            (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
            (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe'))) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }
    $command = Get-Command 'iscc.exe' -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }
    throw 'Inno Setup 6 not found: install it from https://jrsoftware.org/isdl.php or pass -Iscc <ISCC.exe>.'
}

# ... Publish-Project, Copy-Text, Write-BuildManifest (with -When) as in Gerar-Pacote.ps1 ...

$iscc = Find-Iscc
$when = Get-Date
$invariant = [Globalization.CultureInfo]::InvariantCulture
$version = $when.ToString('yyyy.MM.dd.HHmm', $invariant)
$fileVersion = $when.ToString('yyyy.MM.dd-HHmm', $invariant)

New-Item -ItemType Directory -Path $Output -Force | Out-Null
if (Test-Path -LiteralPath $programs) {
    Remove-Item -LiteralPath $programs -Recurse -Force
}
Get-ChildItem -LiteralPath $Output -Filter 'Midview-Instalador-*.exe' | Remove-Item -Force

# Agent and Session side by side: the Agent launches the Session from its
# own folder. The Session goes second on purpose: the few runtime files both
# ship (WindowsBase, System.Drawing, EventLog...) must be the Windows Desktop
# runtime's full, same-or-newer versions the Session needs; the Agent runs
# fine on those, while the Session would break on the Agent's stubs.
Publish-Project 'Midview.Agent' (Join-Path $programs 'aluno')
Publish-Project 'Midview.Session' (Join-Path $programs 'aluno')
Write-BuildManifest (Join-Path $programs 'aluno') $when
Publish-Project 'Midview.Master' (Join-Path $programs 'professor')

$scripts = New-Item -ItemType Directory -Path (Join-Path $programs 'instalador') -Force
foreach ($script in Get-ChildItem -Path (Join-Path $PSScriptRoot 'scripts') -Filter '*.ps1') {
    Copy-Text $script.FullName (Join-Path $scripts.FullName $script.Name) $true
}

# Compiled from a UTF-8-with-BOM copy, so Inno reads the Portuguese right.
$iss = Join-Path $programs 'Midview.iss'
Copy-Text (Join-Path $PSScriptRoot 'Midview.iss') $iss $true
& $iscc '/Q' "/DVersao=$version" "/DVersaoArquivo=$fileVersion" "/DPrograma=$programs" "/DSaida=$Output" $iss
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed (exit code $LASTEXITCODE)."
}

$setup = Join-Path $Output "Midview-Instalador-$fileVersion.exe"
Write-Host ''
Write-Host "Setup ready: $setup" -ForegroundColor Green
```

Delete `installer/Gerar-Pacote.ps1`.

- [ ] **Step 3: Build it and check it**

Run (PowerShell, with the SDK first on PATH so `dotnet publish` finds it):

```powershell
$env:PATH = "C:\Users\Neto\AppData\Local\Microsoft\dotnet;$env:PATH"; powershell.exe -NoProfile -ExecutionPolicy Bypass -File installer\Gerar-Instalador.ps1 -Output "$env:TEMP\midview-setup"
```

Expected: `Setup ready: ...\Midview-Instalador-<stamp>.exe`; ISCC reports no errors (fix Pascal compile errors it reports, if any).

Then: `powershell.exe -NoProfile -ExecutionPolicy Bypass -File installer\tests\Testar-Scripts.ps1 -Pacote "$env:TEMP\midview-setup"`
Expected: `All checks passed.`

Do **not** run the setup on this PC: it is the teacher's PC, and installing is part of the user's lab test.

---

### Task 16: The E2E tool, the real-Windows tests and CI

**Files:**
- Modify: `tests/Midview.E2E/Program.cs`, `tests/Midview.E2E/Midview.E2E.csproj`, `installer/tests/Testar-Atualizacao.ps1`, `installer/tests/Testar-Bloqueio.ps1`, `.github/workflows/pacote.yml`, `.github/workflows/atualizacao.yml`, `.github/workflows/bloqueio.yml`

**Interfaces:**
- Consumes: `PairingSession` (Task 11), `UpdatePackage.Identify/Create(studentFolder)` (Task 12), the setup (Task 15).
- Produces: E2E commands `pair <teacher.pfx> <machine.json>`, `status <machine.json> <teacher.pfx> [<expected student folder>]`, `push <machine.json> <new student folder> <teacher.pfx> <committed|rolledBack>`, `block <machine.json> <teacher.pfx> <mode> [programs]`, `block-status <machine.json> <teacher.pfx>`, `facts`, `launch`, `launch-raw`.

- [ ] **Step 1: The E2E tool**

`Midview.E2E.csproj`: replace the `InstallPackage.cs` link with `<Compile Include="..\..\src\Midview.Master\PairingSession.cs" Link="Master\PairingSession.cs" />`.

`Program.cs`:
- the header comment lists the new commands. `<machine.json>` is the machine as `pair` wrote it, a `KnownMachine` in JSON, reached over loopback;
- the dispatch:

```csharp
    ["pair", var pfx, var machineFile] => Pair(pfx, machineFile),
    ["status", var machineFile, var pfx] => Status(machineFile, pfx, null),
    ["status", var machineFile, var pfx, var expected] => Status(machineFile, pfx, expected),
    ["push", var machineFile, var update, var pfx, var expected] => Push(machineFile, update, pfx, expected),
    ["block", var machineFile, var pfx, var mode, .. var programs] => Block(machineFile, pfx, mode, programs),
    ["block-status", var machineFile, var pfx] => BlockStatus(machineFile, pfx),
```

- delete `Authorize`; `InstalledMachine(string machineFile)` becomes `JsonSerializer.Deserialize<KnownMachine>(File.ReadAllText(machineFile))! with { HostAddress = "127.0.0.1" }`;
- `Status`'s expected fingerprint and `Push`'s package use the folder directly (`UpdatePackage.Identify(expectedFolder)`, `UpdatePackage.Create(updateFolder, ...)`);
- add:

```csharp
// Creates a test teacher and pairs it with the Agent installed here, as the
// Master's "Parear" does, checking on the way that the code really is on
// the student's screen (a Midview.Session.exe showing it) and goes away.
static int Pair(string pfx, string machineFile)
{
    using var teacher = SelfSignedCertificate.Create("midview-e2e-teacher");
    File.WriteAllBytes(pfx, teacher.Export(X509ContentType.Pfx));
    using var session = PairingSession.Start("127.0.0.1", teacher, "E2E");
    Say($"code {session.CodeText} (agent {session.AgentThumbprint})");

    var shown = $"{PairingCodeArguments.CodeFlag} {session.Code:000000}";
    if (!WaitFor(() => SessionCommandLines().Any(line => line.Contains(shown)), TimeSpan.FromSeconds(20)))
    {
        Say($"no Midview.Session.exe is showing {shown}");
        session.Cancel();
        return 1;
    }
    Say("the code is on the student's screen");

    var machine = session.Confirm();
    File.WriteAllText(machineFile, JsonSerializer.Serialize(machine));
    Say($"paired with {machine.Name}: session {machine.SessionThumbprint}, mac {machine.MacAddress ?? "none"}");

    if (!WaitFor(() => !SessionCommandLines().Any(line => line.Contains(PairingCodeArguments.CodeFlag)), TimeSpan.FromSeconds(20)))
    {
        Say("the code window is still open after the pairing ended");
        return 1;
    }
    return 0;
}

static bool WaitFor(Func<bool> condition, TimeSpan timeout)
{
    var deadline = DateTime.UtcNow + timeout;
    while (!condition())
    {
        if (DateTime.UtcNow >= deadline)
        {
            return false;
        }
        Thread.Sleep(500);
    }
    return true;
}

// The command lines of the Midview.Session.exe processes on this machine
// (.NET can't read another process's command line itself).
static IReadOnlyList<string> SessionCommandLines()
{
    var start = new System.Diagnostics.ProcessStartInfo("powershell.exe")
    {
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    start.ArgumentList.Add("-NoProfile");
    start.ArgumentList.Add("-Command");
    start.ArgumentList.Add("Get-CimInstance Win32_Process -Filter \"Name='Midview.Session.exe'\" | ForEach-Object { $_.CommandLine }");
    using var process = System.Diagnostics.Process.Start(start)!;
    var output = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
```

- add `using System.Text.Json;`; update `Usage()`.

Build: `"$DOTNET" build tests/Midview.E2E`. Expected: success.

- [ ] **Step 2: `Testar-Atualizacao.ps1`**

Header: installs with the setup (silently, `/TIPO=aluno`), pairs a test teacher for real, updates over the network, reinstalls with the setup and uninstalls silently. New variables and functions:

```powershell
$setup = (Get-ChildItem -LiteralPath $Pacote -Filter 'Midview-Instalador-*.exe' | Select-Object -First 1).FullName
$student = Join-Path $Pacote 'programa\aluno'
$machine = Join-Path $work 'maquina.json'
$installedExe = Join-Path $env:ProgramFiles 'Midview\Midview.Agent.exe'
$uninstaller = Join-Path $env:ProgramFiles 'Midview\unins000.exe'

function Invoke-Setup([string[]]$Arguments) {
    $process = Start-Process -FilePath $setup -ArgumentList $Arguments -Wait -PassThru
    return $process.ExitCode
}

function Invoke-Uninstall([string]$Log) {
    $process = Start-Process -FilePath $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', "/LOG=$Log") -Wait -PassThru
    return $process.ExitCode
}
```

`New-Build` copies `$student` to `$work\$Name`, relabels its manifest, and returns that folder (no `programa\aluno` level). Delete `Invoke-Script`. The steps:

```powershell
    Step 'Instalar com o instalador (aluno, silencioso)'
    Check ($null -ne $setup) "the setup is in $Pacote"
    Check ((Invoke-Setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/TIPO=aluno', "/LOG=$Registros\instalar.log")) -eq 0) 'the setup finishes'
    $service = Get-Service-Config
    Check ($service.State -eq 'Running') "the service runs ($($service.State))"
    Check ($service.PathName -eq "`"$installedExe`"") "from the installed folder ($($service.PathName))"
    Check ((Invoke-Setup @('/VERYSILENT', '/SUPPRESSMSGBOXES')) -ne 0) 'a silent install without /TIPO is refused'
    Check ((Invoke-Setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/TIPO=professor')) -ne 0) 'installing the other kind is refused'
    Check (-not (Test-Path -LiteralPath (Join-Path $env:ProgramFiles 'Midview Professor'))) 'and nothing of it was installed'

    Step 'Parear um professor de teste (o codigo aparece na tela do aluno)'
    Check ((Invoke-Tool @('pair', $teacher, $machine)) -eq 0) 'the test teacher pairs with the machine'
    Check ((Invoke-Tool @('status', $machine, $teacher, $student)) -eq 0) "the Agent reports the setup's build to the paired teacher"
    (the existing update-folder ACL checks follow)

    Step 'Atualizar pela rede para uma versao nova (tem que confirmar)'
    $good = New-Build 'versao-boa' '2099-01-01 00:00 e2e-boa' $null
    Check ((Invoke-Tool @('push', $machine, $good, $teacher, 'committed')) -eq 0) 'the new build is committed and running'
    (rest as today)

    Step 'Atualizar para uma versao quebrada (tem que voltar sozinha)'
    (as today, with $machine and the folders)

    Step 'Reinstalar com o instalador por cima da versao da rede'
    Save-Logs 'antes-de-reinstalar'
    Check ((Invoke-Setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/TIPO=aluno', "/LOG=$Registros\reinstalar.log")) -eq 0) 'the setup finishes'
    (service back to $installedExe, Midview.update and the update record gone, as today)
    Check ((Invoke-Tool @('status', $machine, $teacher, $student)) -eq 0) 'the teacher is still paired after reinstalling'

    Step 'Desinstalar (silencioso)'
    Save-Logs 'antes-de-desinstalar'
    Check ((Invoke-Uninstall "$Registros\desinstalar.log") -eq 0) 'the uninstaller finishes'
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Service-Config) -and (Get-Date) -lt $deadline) { Start-Sleep -Seconds 2 }
    Check ($null -eq (Get-Service-Config)) 'the service is gone'
    Check (-not (Test-Path -LiteralPath (Split-Path -Parent $installedExe))) 'the program is gone'
    Check (-not (Test-Path -LiteralPath $versions)) 'Midview.update is gone'
    Check (Test-Path -LiteralPath (Join-Path $data 'trusted-masters.json')) 'a silent uninstall keeps the paired teachers'
    Remove-Item -LiteralPath $data -Recurse -Force
```

- [ ] **Step 3: `Testar-Bloqueio.ps1`**

The same `$setup`, `$student`, `$machine`, `Invoke-Setup`, `Invoke-Uninstall`; delete `Invoke-Script`. `Set-Block` and the `block-status` calls use `$machine` instead of `$Pacote`. The first step becomes:

```powershell
    Step 'Instalar com o instalador e parear um professor de teste'
    Check ((Invoke-Setup @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/TIPO=aluno', "/LOG=$Registros\instalar.log")) -eq 0) 'the setup finishes'
    Check ((Get-ServiceState) -eq 'Running') 'the service runs'
    Check ((Invoke-Tool @('pair', $teacher, $machine)).Code -eq 0) 'the test teacher pairs with the machine'
    Check ((Invoke-Tool @('status', $machine, $teacher, $student)).Code -eq 0) 'the Agent answers'
    Check ((Invoke-Tool @('block-status', $machine, $teacher)).Code -eq 0) 'the Agent knows program blocking'
```

and the uninstall step:

```powershell
    Check ((Invoke-Uninstall "$Registros\desinstalar.log") -eq 0) 'the uninstaller finishes'
    Check (-not (Test-Path -LiteralPath $blockFile)) 'the saved block is gone'
    Remove-Item -LiteralPath $data -Recurse -Force -ErrorAction SilentlyContinue
```

- [ ] **Step 4: Workflows**

In all three, replace the "Gerar o pacote" step with:

```yaml
      - name: Instalar o Inno Setup (se faltar)
        shell: pwsh
        run: if (-not (Test-Path "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe")) { choco install innosetup -y --no-progress }

      - name: Gerar o instalador (Windows PowerShell 5.1)
        shell: cmd
        run: powershell.exe -NoProfile -ExecutionPolicy Bypass -File installer\Gerar-Instalador.ps1 -Output "%RUNNER_TEMP%\Midview-Instalador"
```

and every `-Pacote "%RUNNER_TEMP%\Midview-Instalacao"` with `-Pacote "%RUNNER_TEMP%\Midview-Instalador"`. In `atualizacao.yml`'s path filters, replace `src/Midview.Master/InstallPackage.cs` with `src/Midview.Master/PairingSession.cs`.

`pacote.yml`:
- name `Instalador`; header comment: builds the setup, checks its scripts under 5.1, keeps the `.exe` for download 7 days on a manual run, and attaches it to a GitHub Release when a `v*` tag is pushed;
- `on.push` gets `tags: ['v*']` (GitHub ignores path filters for tag pushes);
- job-level `permissions: contents: write` (creating the release needs it), with a comment;
- the check step uses `-Pacote "%RUNNER_TEMP%\Midview-Instalador"`;
- the upload step uploads `${{ runner.temp }}/Midview-Instalador/Midview-Instalador-*.exe` as `Midview-Instalador`;
- a last step:

```yaml
      - name: Publicar na versão (tag v*)
        if: startsWith(github.ref, 'refs/tags/v')
        uses: softprops/action-gh-release@v2
        with:
          files: ${{ runner.temp }}/Midview-Instalador/Midview-Instalador-*.exe
```

- [ ] **Step 5: Check what can be checked here**

Run: `"$DOTNET" build tests/Midview.E2E` (success) and the parser loop from Task 14 Step 6 over `installer\tests\*.ps1` (0 errors). The real-Windows tests run only in CI. They run after the user's lab test, when they decide to push.

---

### Task 17: Docs and the final check

**Files:**
- Modify: `docs/superpowers/specs/2026-09-24-lab-installer-design.md` (status line), `docs/superpowers/specs/2026-10-05-installer-and-pairing-design.md` (status line)

- [ ] **Step 1: Status lines**

- Lab installer: `Status: Implemented 2026-09-24; replaced 2026-10-05 by 2026-10-05-installer-and-pairing-design.md (the setup program and pairing).`
- Installer and pairing: `Status: Implemented 2026-10-05 (not yet validated live)`.

- [ ] **Step 2: Everything, from clean**

Run: `"$DOTNET" build Midview.sln`, `"$DOTNET" test Midview.sln`, `"$DOTNET" build tests/Midview.E2E`, then Task 15 Step 3 again (setup builds, `Testar-Scripts.ps1` passes).
Expected: all green. Report the per-project test counts to the user.

- [ ] **Step 3: Hand over for the lab test**

Copy the setup to `C:\Users\Neto\Desktop\Midview-Instalador-<stamp>.exe` for the user. Give the user the live checklist from the spec's Testing section:
1. both kinds of install;
2. the other kind refused;
3. a silent install;
4. installing over a USB-stick install;
5. uninstalling with and without removing data;
6. the code window;
7. pairing the class;
8. discovery across the lab's switch;
9. a network update from the bundled program.

Nothing is committed: the user tests first.
