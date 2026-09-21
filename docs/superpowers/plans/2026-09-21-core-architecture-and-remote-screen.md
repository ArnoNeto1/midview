# TreezeSuite Core Architecture & Remote Screen Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

> **Progress note (2026-09-21):** Tasks 1-15 are implemented, committed, and passing all automated tests (22/22 across the three test projects) while the project owner was away. Task 16 (end-to-end manual validation) is deliberately not yet done -- it requires actually engaging the input lock and injecting input on a real machine, and doing that unsupervised while the only person who could intervene if the untested lock/unlock code failed was away was judged too risky. Do this task once the project owner is present.

**Goal:** Build the three-process foundation (TreezeAgent, TreezeSession, TreezeMaster) and a working remote screen view/control pipeline with a heartbeat-based input lock, as an independent C#/.NET codebase sharing no source with the GPL-licensed Treezeview fork.

**Architecture:** A shared `TreezeSuite.Protocol` library (message framing, mutual-TLS secure channel, tile diffing, JPEG tile codec) is consumed by three executables: `TreezeSuite.Agent` (Windows Service; supervises the session process), `TreezeSuite.Session` (per-user-session process; screen capture, input injection, input lock, network server), and `TreezeSuite.Master` (WPF console; connects, views, and controls).

**Tech Stack:** C# / .NET 8 (Windows-only, `net8.0-windows`), WPF for the Master UI, `System.Drawing.Common` for capture/encoding (GDI-based, avoids any third-party imaging license), built-in `SslStream` for transport security, xUnit for tests.

**Spec:** `C:\Users\Neto\Desktop\TreezeSuite\docs\superpowers\specs\2026-09-21-core-architecture-and-remote-screen-design.md`

## Global Constraints

- Windows only. No Linux code paths anywhere in this plan.
- LAN only. No relay/NAT-traversal code.
- No GPL or other copyleft-licensed source or libraries. Any third-party package must be MIT/BSD/Apache-2.0, or LGPL used only via dynamic linking.
- Solo-maintainable: prefer the simplest approach that meets the requirement over a more "impressive" one (see Task 3's GDI-over-WinRT-capture decision and Task 4's System.Drawing-over-ImageSharp decision below — both deliberate deviations from the spec's wording, made for exactly this reason, and called out for the project owner's awareness).
- Every stuck-input-lock class of bug found in Treezeview must be structurally prevented here, not just patched: the heartbeat-based auto-unlock in Task 8 is the mechanism that does this.

---

## Two notes on deviating from the spec

The spec names "Windows Graphics Capture API" for screen capture. This plan uses **GDI `Graphics.CopyFromScreen` (BitBlt)** instead: it is simpler to implement correctly, was already proven working live during the Treezeview debugging session, and is fully sufficient for tile-diffed JPEG streaming. Windows Graphics Capture (lower latency, hardware-accelerated) is a valid future optimization once this simpler pipeline is working end-to-end, not a first-version requirement.

The spec doesn't name an imaging library, but a reasonable default (SixLabors.ImageSharp) has licensing terms (the Six Labors Split License, since ImageSharp v3) that require a paid commercial license above a revenue threshold — exactly the kind of licensing entanglement this whole rewrite exists to avoid. This plan uses **`System.Drawing.Common`** instead: it's part of the MIT-licensed .NET runtime, wraps Windows' own GDI+ JPEG codec, and requires no third-party license at all, which fits a Windows-only project naturally.

---

### Task 1: Solution scaffolding

**Files:**
- Create: `TreezeSuite.sln`
- Create: `src/TreezeSuite.Protocol/TreezeSuite.Protocol.csproj`
- Create: `src/TreezeSuite.Session/TreezeSuite.Session.csproj`
- Create: `src/TreezeSuite.Agent/TreezeSuite.Agent.csproj`
- Create: `src/TreezeSuite.Master/TreezeSuite.Master.csproj`
- Create: `tests/TreezeSuite.Protocol.Tests/TreezeSuite.Protocol.Tests.csproj`
- Create: `tests/TreezeSuite.Session.Tests/TreezeSuite.Session.Tests.csproj`
- Create: `tests/TreezeSuite.Agent.Tests/TreezeSuite.Agent.Tests.csproj`
- Create: `.gitignore`

**Interfaces:**
- Produces: a buildable, empty solution every later task adds files into.

- [x] **Step 1: Create the .gitignore**

```
bin/
obj/
*.user
.vs/
```

- [x] **Step 2: Create `src/TreezeSuite.Protocol/TreezeSuite.Protocol.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="System.Drawing.Common" Version="8.0.8" />
  </ItemGroup>
</Project>
```

- [x] **Step 3: Create `src/TreezeSuite.Session/TreezeSuite.Session.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\TreezeSuite.Protocol\TreezeSuite.Protocol.csproj" />
  </ItemGroup>
</Project>
```

- [x] **Step 4: Create `src/TreezeSuite.Agent/TreezeSuite.Agent.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Hosting.WindowsServices" Version="8.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\TreezeSuite.Protocol\TreezeSuite.Protocol.csproj" />
  </ItemGroup>
</Project>
```

- [x] **Step 5: Create `src/TreezeSuite.Master/TreezeSuite.Master.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <UseWPF>true</UseWPF>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\TreezeSuite.Protocol\TreezeSuite.Protocol.csproj" />
  </ItemGroup>
</Project>
```

- [x] **Step 6: Create the three test projects** (identical shape, different `ProjectReference`)

`tests/TreezeSuite.Protocol.Tests/TreezeSuite.Protocol.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.11.1" />
    <PackageReference Include="xunit" Version="2.9.0" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\TreezeSuite.Protocol\TreezeSuite.Protocol.csproj" />
  </ItemGroup>
</Project>
```

`tests/TreezeSuite.Session.Tests/TreezeSuite.Session.Tests.csproj` — same body, but the `ProjectReference` points to `..\..\src\TreezeSuite.Session\TreezeSuite.Session.csproj` **and** `..\..\src\TreezeSuite.Protocol\TreezeSuite.Protocol.csproj` (two references).

`tests/TreezeSuite.Agent.Tests/TreezeSuite.Agent.Tests.csproj` — same body, `ProjectReference` pointing to `..\..\src\TreezeSuite.Agent\TreezeSuite.Agent.csproj`.

- [x] **Step 7: Create the solution and add every project**

```bash
dotnet new sln -n TreezeSuite
dotnet sln add src/TreezeSuite.Protocol/TreezeSuite.Protocol.csproj
dotnet sln add src/TreezeSuite.Session/TreezeSuite.Session.csproj
dotnet sln add src/TreezeSuite.Agent/TreezeSuite.Agent.csproj
dotnet sln add src/TreezeSuite.Master/TreezeSuite.Master.csproj
dotnet sln add tests/TreezeSuite.Protocol.Tests/TreezeSuite.Protocol.Tests.csproj
dotnet sln add tests/TreezeSuite.Session.Tests/TreezeSuite.Session.Tests.csproj
dotnet sln add tests/TreezeSuite.Agent.Tests/TreezeSuite.Agent.Tests.csproj
```

- [x] **Step 8: Verify it builds**

Run: `dotnet build`
Expected: succeeds with 0 errors (projects are empty except for placeholder `Program.cs`/`App.xaml` files `dotnet new` style templates don't create automatically for a bare `Sdk="Microsoft.NET.Sdk"` csproj with `OutputType=Exe` — if the build complains about a missing entry point, add a one-line `Program.cs` with `Console.WriteLine("TreezeSuite.Session placeholder");` to `Session` and `Agent`, and a minimal `App.xaml`/`App.xaml.cs` pair to `Master` per the standard WPF template; these get replaced by real code in later tasks).

- [x] **Step 9: Commit**

```bash
git add -A
git commit -m "Scaffold TreezeSuite solution: Protocol, Session, Agent, Master projects"
```

---

### Task 2: Protocol — message types and framing

**Files:**
- Create: `src/TreezeSuite.Protocol/MessageType.cs`
- Create: `src/TreezeSuite.Protocol/Message.cs`
- Create: `src/TreezeSuite.Protocol/MessageFramer.cs`
- Test: `tests/TreezeSuite.Protocol.Tests/MessageFramerTests.cs`

**Interfaces:**
- Produces: `MessageType` enum, `Message(MessageType Type, byte[] Payload)`, `MessageFramer.Write(Stream, Message)`, `MessageFramer.Read(Stream) -> Message`. Every later task that sends/receives anything over the wire uses these three types.

- [x] **Step 1: Write the failing tests**

```csharp
// tests/TreezeSuite.Protocol.Tests/MessageFramerTests.cs
using TreezeSuite.Protocol;
using Xunit;

public class MessageFramerTests
{
    [Fact]
    public void WriteThenRead_RoundTripsMessage()
    {
        var stream = new MemoryStream();
        var original = new Message(MessageType.Heartbeat, new byte[] { 1, 2, 3 });

        MessageFramer.Write(stream, original);
        stream.Position = 0;
        var result = MessageFramer.Read(stream);

        Assert.Equal(MessageType.Heartbeat, result.Type);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Payload);
    }

    [Fact]
    public void Read_EmptyPayload_RoundTrips()
    {
        var stream = new MemoryStream();
        MessageFramer.Write(stream, new Message(MessageType.Hello, Array.Empty<byte>()));
        stream.Position = 0;

        var result = MessageFramer.Read(stream);

        Assert.Equal(MessageType.Hello, result.Type);
        Assert.Empty(result.Payload);
    }

    [Fact]
    public void Read_TruncatedStream_ThrowsEndOfStream()
    {
        var stream = new MemoryStream();
        MessageFramer.Write(stream, new Message(MessageType.Heartbeat, new byte[] { 1, 2, 3 }));
        stream.SetLength(stream.Length - 2);
        stream.Position = 0;

        Assert.Throws<EndOfStreamException>(() => MessageFramer.Read(stream));
    }
}
```

- [x] **Step 2: Run the tests to verify they fail (types don't exist yet)**

Run: `dotnet test tests/TreezeSuite.Protocol.Tests`
Expected: FAIL to compile — `MessageType`, `Message`, `MessageFramer` not found.

- [x] **Step 3: Implement `MessageType.cs`**

```csharp
namespace TreezeSuite.Protocol;

public enum MessageType : byte
{
    Hello = 1,
    Heartbeat = 2,
    StartControl = 3,
    StopControl = 4,
    FrameTile = 5,
    InputEvent = 6,
}
```

- [x] **Step 4: Implement `Message.cs`**

```csharp
namespace TreezeSuite.Protocol;

public sealed class Message
{
    public MessageType Type { get; }
    public byte[] Payload { get; }

    public Message(MessageType type, byte[] payload)
    {
        Type = type;
        Payload = payload;
    }
}
```

- [x] **Step 5: Implement `MessageFramer.cs`**

```csharp
using System.Buffers.Binary;

namespace TreezeSuite.Protocol;

public static class MessageFramer
{
    public static void Write(Stream stream, Message message)
    {
        var length = message.Payload.Length + 1;
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, length);
        stream.Write(header, 0, 4);
        stream.WriteByte((byte)message.Type);
        stream.Write(message.Payload, 0, message.Payload.Length);
        stream.Flush();
    }

    public static Message Read(Stream stream)
    {
        var header = ReadExact(stream, 4);
        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 1)
        {
            throw new InvalidDataException($"Invalid message length {length}");
        }

        var typeAndPayload = ReadExact(stream, length);
        var type = (MessageType)typeAndPayload[0];
        var payload = typeAndPayload[1..];
        return new Message(type, payload);
    }

    private static byte[] ReadExact(Stream stream, int count)
    {
        var buffer = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(buffer, offset, count - offset);
            if (read == 0)
            {
                throw new EndOfStreamException("Connection closed while reading message");
            }
            offset += read;
        }
        return buffer;
    }
}
```

- [x] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/TreezeSuite.Protocol.Tests`
Expected: PASS (3/3)

- [x] **Step 7: Commit**

```bash
git add src/TreezeSuite.Protocol/MessageType.cs src/TreezeSuite.Protocol/Message.cs src/TreezeSuite.Protocol/MessageFramer.cs tests/TreezeSuite.Protocol.Tests/MessageFramerTests.cs
git commit -m "Add message framing protocol (type + length-prefixed payload)"
```

---

### Task 3: Protocol — trust store and mutual-TLS secure channel

**Files:**
- Create: `src/TreezeSuite.Protocol/SelfSignedCertificate.cs`
- Create: `src/TreezeSuite.Protocol/TrustStore.cs`
- Create: `src/TreezeSuite.Protocol/SecureChannel.cs`
- Test: `tests/TreezeSuite.Protocol.Tests/SecureChannelTests.cs`

**Interfaces:**
- Consumes: `Message`, `MessageFramer` from Task 2.
- Produces: `SelfSignedCertificate.Create(string subjectName) -> X509Certificate2`, `TrustStore(IEnumerable<string> trustedThumbprints)` with `IsTrusted(X509Certificate) -> bool`, `SecureChannel.AuthenticateAsServer(TcpClient, X509Certificate2, TrustStore) -> SecureChannel`, `SecureChannel.AuthenticateAsClient(TcpClient, string targetHost, X509Certificate2, TrustStore) -> SecureChannel`, instance methods `Send(Message)` / `Receive() -> Message`. `TreezeSuite.Session`'s server (Task 12) and `TreezeSuite.Master`'s client (Task 15) both build directly on this.

- [x] **Step 1: Write the failing tests**

```csharp
// tests/TreezeSuite.Protocol.Tests/SecureChannelTests.cs
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using TreezeSuite.Protocol;
using Xunit;

public class SecureChannelTests
{
    [Fact]
    public void AuthenticateAsClientAndServer_TrustedCertificates_ExchangesMessage()
    {
        using var serverCert = SelfSignedCertificate.Create("session-under-test");
        using var clientCert = SelfSignedCertificate.Create("master-under-test");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        Message? received = null;
        var serverTask = Task.Run(() =>
        {
            using var tcpClient = listener.AcceptTcpClient();
            using var channel = SecureChannel.AuthenticateAsServer(
                tcpClient, serverCert, new TrustStore(new[] { clientCert.Thumbprint }));
            channel.Send(new Message(MessageType.Hello, new byte[] { 42 }));
        });

        using var connectingClient = new TcpClient();
        connectingClient.Connect(IPAddress.Loopback, port);
        using var clientChannel = SecureChannel.AuthenticateAsClient(
            connectingClient, "session-under-test", clientCert,
            new TrustStore(new[] { serverCert.Thumbprint }));

        received = clientChannel.Receive();
        serverTask.Wait(TimeSpan.FromSeconds(5));

        listener.Stop();
        Assert.Equal(MessageType.Hello, received.Type);
        Assert.Equal(new byte[] { 42 }, received.Payload);
    }

    [Fact]
    public void AuthenticateAsServer_UntrustedClientCertificate_Throws()
    {
        using var serverCert = SelfSignedCertificate.Create("session-under-test");
        using var clientCert = SelfSignedCertificate.Create("untrusted-master");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        var serverTask = Task.Run(() =>
        {
            using var tcpClient = listener.AcceptTcpClient();
            Assert.Throws<AuthenticationException>(() =>
                SecureChannel.AuthenticateAsServer(tcpClient, serverCert, new TrustStore(Array.Empty<string>())));
        });

        using var connectingClient = new TcpClient();
        connectingClient.Connect(IPAddress.Loopback, port);
        Assert.ThrowsAny<Exception>(() =>
            SecureChannel.AuthenticateAsClient(connectingClient, "session-under-test", clientCert,
                new TrustStore(new[] { serverCert.Thumbprint })));

        serverTask.Wait(TimeSpan.FromSeconds(5));
        listener.Stop();
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/TreezeSuite.Protocol.Tests`
Expected: FAIL to compile — `SelfSignedCertificate`, `TrustStore`, `SecureChannel` not found.

- [x] **Step 3: Implement `SelfSignedCertificate.cs`**

```csharp
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace TreezeSuite.Protocol;

public static class SelfSignedCertificate
{
    public static X509Certificate2 Create(string subjectName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            $"CN={subjectName}", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(10));
        return new X509Certificate2(certificate.Export(X509ContentType.Pfx));
    }
}
```

- [x] **Step 4: Implement `TrustStore.cs`**

```csharp
using System.Security.Cryptography.X509Certificates;

namespace TreezeSuite.Protocol;

public sealed class TrustStore
{
    private readonly HashSet<string> _trustedThumbprints;

    public TrustStore(IEnumerable<string> trustedThumbprints)
    {
        _trustedThumbprints = trustedThumbprints
            .Select(t => t.ToUpperInvariant())
            .ToHashSet();
    }

    public bool IsTrusted(X509Certificate certificate)
    {
        using var certificate2 = new X509Certificate2(certificate);
        return _trustedThumbprints.Contains(certificate2.Thumbprint.ToUpperInvariant());
    }
}
```

- [x] **Step 5: Implement `SecureChannel.cs`**

```csharp
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

namespace TreezeSuite.Protocol;

public sealed class SecureChannel : IDisposable
{
    private readonly SslStream _sslStream;

    private SecureChannel(SslStream sslStream)
    {
        _sslStream = sslStream;
    }

    public static SecureChannel AuthenticateAsServer(
        TcpClient client, X509Certificate2 serverCertificate, TrustStore clientTrustStore)
    {
        var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, cert, _, _) =>
                cert is not null && clientTrustStore.IsTrusted(cert));

        sslStream.AuthenticateAsServer(serverCertificate, clientCertificateRequired: true,
            checkCertificateRevocation: false);

        return new SecureChannel(sslStream);
    }

    public static SecureChannel AuthenticateAsClient(
        TcpClient client, string targetHost, X509Certificate2 clientCertificate, TrustStore serverTrustStore)
    {
        var sslStream = new SslStream(client.GetStream(), leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, cert, _, _) =>
                cert is not null && serverTrustStore.IsTrusted(cert));

        var certificates = new X509CertificateCollection { clientCertificate };
        sslStream.AuthenticateAsClient(targetHost, certificates, SslProtocols.Tls13, checkCertificateRevocation: false);

        return new SecureChannel(sslStream);
    }

    public void Send(Message message) => MessageFramer.Write(_sslStream, message);

    public Message Receive() => MessageFramer.Read(_sslStream);

    public void Dispose() => _sslStream.Dispose();
}
```

- [x] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/TreezeSuite.Protocol.Tests`
Expected: PASS (5/5 including Task 2's tests)

- [x] **Step 7: Commit**

```bash
git add src/TreezeSuite.Protocol/SelfSignedCertificate.cs src/TreezeSuite.Protocol/TrustStore.cs src/TreezeSuite.Protocol/SecureChannel.cs tests/TreezeSuite.Protocol.Tests/SecureChannelTests.cs
git commit -m "Add trust-store-validated mutual TLS secure channel"
```

---

### Task 4: Protocol — tile diffing

**Files:**
- Create: `src/TreezeSuite.Protocol/TileCoord.cs`
- Create: `src/TreezeSuite.Protocol/TileDiff.cs`
- Test: `tests/TreezeSuite.Protocol.Tests/TileDiffTests.cs`

**Interfaces:**
- Produces: `TileCoord(int Row, int Column)`, `TileDiff.TileSize` (const), `TileDiff.FindChangedTiles(Bitmap previous, Bitmap current) -> IReadOnlyList<TileCoord>`, `TileDiff.TileRect(int row, int column, int imageWidth, int imageHeight) -> Rectangle`. Task 10's `FramePipeline` calls `FindChangedTiles` every frame and `TileRect` to know what to pass to `TileCodec` (Task 5).

- [x] **Step 1: Write the failing tests**

```csharp
// tests/TreezeSuite.Protocol.Tests/TileDiffTests.cs
using System.Drawing;
using TreezeSuite.Protocol;
using Xunit;

public class TileDiffTests
{
    [Fact]
    public void FindChangedTiles_IdenticalBitmaps_ReturnsEmpty()
    {
        using var a = new Bitmap(128, 128);
        using var b = new Bitmap(128, 128);
        FillSolid(a, Color.Red);
        FillSolid(b, Color.Red);

        var changed = TileDiff.FindChangedTiles(a, b);

        Assert.Empty(changed);
    }

    [Fact]
    public void FindChangedTiles_OneTileDifferent_ReturnsOnlyThatTile()
    {
        using var a = new Bitmap(128, 128);
        using var b = new Bitmap(128, 128);
        FillSolid(a, Color.Red);
        FillSolid(b, Color.Red);
        for (var y = 0; y < 64; y++)
        {
            for (var x = 64; x < 128; x++)
            {
                b.SetPixel(x, y, Color.Blue);
            }
        }

        var changed = TileDiff.FindChangedTiles(a, b);

        Assert.Equal(new[] { new TileCoord(0, 1) }, changed);
    }

    [Fact]
    public void FindChangedTiles_DifferentSizes_Throws()
    {
        using var a = new Bitmap(128, 128);
        using var b = new Bitmap(64, 64);

        Assert.Throws<ArgumentException>(() => TileDiff.FindChangedTiles(a, b));
    }

    private static void FillSolid(Bitmap bitmap, Color color)
    {
        using var g = Graphics.FromImage(bitmap);
        g.Clear(color);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/TreezeSuite.Protocol.Tests`
Expected: FAIL to compile — `TileCoord`, `TileDiff` not found.

- [x] **Step 3: Implement `TileCoord.cs`**

```csharp
namespace TreezeSuite.Protocol;

public readonly record struct TileCoord(int Row, int Column);
```

- [x] **Step 4: Implement `TileDiff.cs`**

```csharp
using System.Drawing;

namespace TreezeSuite.Protocol;

public static class TileDiff
{
    public const int TileSize = 64;

    public static IReadOnlyList<TileCoord> FindChangedTiles(Bitmap previous, Bitmap current)
    {
        if (previous.Width != current.Width || previous.Height != current.Height)
        {
            throw new ArgumentException("Bitmaps must be the same size to diff");
        }

        var changed = new List<TileCoord>();
        var columns = (int)Math.Ceiling(current.Width / (double)TileSize);
        var rows = (int)Math.Ceiling(current.Height / (double)TileSize);

        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var rect = TileRect(row, column, current.Width, current.Height);
                if (!TilesEqual(previous, current, rect))
                {
                    changed.Add(new TileCoord(row, column));
                }
            }
        }

        return changed;
    }

    public static Rectangle TileRect(int row, int column, int imageWidth, int imageHeight)
    {
        var x = column * TileSize;
        var y = row * TileSize;
        var width = Math.Min(TileSize, imageWidth - x);
        var height = Math.Min(TileSize, imageHeight - y);
        return new Rectangle(x, y, width, height);
    }

    // NOTE: GetPixel-based comparison is correct but slow (unsuitable for a
    // final low-latency pipeline). This is intentional for a first working
    // version -- switch to LockBits + unsafe pointer scanning once the rest
    // of the pipeline works end-to-end and this is confirmed as the
    // bottleneck, not before (YAGNI).
    private static bool TilesEqual(Bitmap a, Bitmap b, Rectangle rect)
    {
        for (var y = rect.Top; y < rect.Bottom; y++)
        {
            for (var x = rect.Left; x < rect.Right; x++)
            {
                if (a.GetPixel(x, y) != b.GetPixel(x, y))
                {
                    return false;
                }
            }
        }
        return true;
    }
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/TreezeSuite.Protocol.Tests`
Expected: PASS (8/8)

- [x] **Step 6: Commit**

```bash
git add src/TreezeSuite.Protocol/TileCoord.cs src/TreezeSuite.Protocol/TileDiff.cs tests/TreezeSuite.Protocol.Tests/TileDiffTests.cs
git commit -m "Add tile-based frame diffing"
```

---

### Task 5: Protocol — JPEG tile codec

**Files:**
- Create: `src/TreezeSuite.Protocol/TileCodec.cs`
- Test: `tests/TreezeSuite.Protocol.Tests/TileCodecTests.cs`

**Interfaces:**
- Consumes: `TileDiff.TileRect` output (a `Rectangle`) from Task 4.
- Produces: `TileCodec.EncodeTile(Bitmap source, Rectangle rect, long quality = 75L) -> byte[]`, `TileCodec.DecodeTile(byte[] jpegBytes) -> Bitmap`. Task 10's `FramePipeline` calls `EncodeTile` for each changed tile; `TreezeMaster`'s `ViewControlWindow` (Task 15) calls `DecodeTile` on receipt.

- [x] **Step 1: Write the failing tests**

```csharp
// tests/TreezeSuite.Protocol.Tests/TileCodecTests.cs
using System.Drawing;
using TreezeSuite.Protocol;
using Xunit;

public class TileCodecTests
{
    [Fact]
    public void EncodeThenDecode_RoundTripsApproximately()
    {
        using var source = new Bitmap(64, 64);
        using (var g = Graphics.FromImage(source))
        {
            g.Clear(Color.FromArgb(200, 30, 90));
        }

        var encoded = TileCodec.EncodeTile(source, new Rectangle(0, 0, 64, 64));
        using var decoded = TileCodec.DecodeTile(encoded);

        Assert.Equal(64, decoded.Width);
        Assert.Equal(64, decoded.Height);
        var originalPixel = source.GetPixel(32, 32);
        var decodedPixel = decoded.GetPixel(32, 32);
        Assert.InRange(Math.Abs(originalPixel.R - decodedPixel.R), 0, 15);
        Assert.InRange(Math.Abs(originalPixel.G - decodedPixel.G), 0, 15);
        Assert.InRange(Math.Abs(originalPixel.B - decodedPixel.B), 0, 15);
    }

    [Fact]
    public void EncodeTile_SmallerRegionThanBitmap_EncodesOnlyThatRegion()
    {
        using var source = new Bitmap(128, 128);
        using (var g = Graphics.FromImage(source))
        {
            g.Clear(Color.Black);
            g.FillRectangle(Brushes.White, 64, 0, 64, 64);
        }

        var encoded = TileCodec.EncodeTile(source, new Rectangle(64, 0, 64, 64));
        using var decoded = TileCodec.DecodeTile(encoded);

        Assert.Equal(64, decoded.Width);
        Assert.Equal(64, decoded.Height);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/TreezeSuite.Protocol.Tests`
Expected: FAIL to compile — `TileCodec` not found.

- [x] **Step 3: Implement `TileCodec.cs`**

```csharp
using System.Drawing;
using System.Drawing.Imaging;

namespace TreezeSuite.Protocol;

public static class TileCodec
{
    public static byte[] EncodeTile(Bitmap source, Rectangle rect, long quality = 75L)
    {
        using var tile = source.Clone(rect, source.PixelFormat);
        using var stream = new MemoryStream();
        var jpegEncoder = ImageCodecInfo.GetImageEncoders()
            .First(codec => codec.FormatID == ImageFormat.Jpeg.Guid);
        using var encoderParams = new EncoderParameters(1);
        encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        tile.Save(stream, jpegEncoder, encoderParams);
        return stream.ToArray();
    }

    public static Bitmap DecodeTile(byte[] jpegBytes)
    {
        using var stream = new MemoryStream(jpegBytes);
        return new Bitmap(stream);
    }
}
```

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test tests/TreezeSuite.Protocol.Tests`
Expected: PASS (10/10)

- [x] **Step 5: Commit**

```bash
git add src/TreezeSuite.Protocol/TileCodec.cs tests/TreezeSuite.Protocol.Tests/TileCodecTests.cs
git commit -m "Add JPEG tile codec using System.Drawing.Common (no third-party imaging license)"
```

---

### Task 6: Decide the input-blocking mechanism (research task, no code)

This task is a decision gate, not an implementation step — Task 7 depends on its outcome.

- [x] **Step 1: Check the Interception driver's license terms** (`https://github.com/oblitum/Interception`, the same driver vendored in Treezeview) for compatibility with a closed-source/commercial product. Read the actual `LICENSE` file in that repository, not a summary — license text is what matters here, not a project's informal description of itself.
- [x] **Step 2: Record the decision** in `docs/superpowers/specs/2026-09-21-core-architecture-and-remote-screen-design.md`, replacing the "Open item to verify" paragraph with the actual finding and chosen mechanism:
  - **If compatible:** Task 7 vendors the Interception driver the same way, and implements `DisableInputDevices`/`EnableInputDevices` via `interception_create_context`/`interception_set_filter`/`interception_destroy_context`, called through P/Invoke — mirroring Treezeview's `WindowsInputDeviceFunctions`, written independently against the driver's public C API (the API itself is not GPL-encumbered even if some wrapper code referencing it might be; only Treezeview's own C++ wrapper code must not be copied).
  - **If not compatible:** Task 7 instead implements `DisableInputDevices`/`EnableInputDevices` using only public Win32 APIs — `SetupDiGetClassDevs`/`SetupDiSetClassInstallParams` with `DIF_PROPERTYCHANGE`/`DICS_DISABLE`/`DICS_ENABLE` to disable/enable the keyboard and mouse HID devices via Device Manager's own mechanism. This is weaker (it's a device-manager-level disable, not a live driver-level filter) but requires no third-party dependency at all, since these are plain public Windows APIs.
- [x] **Step 3: Commit the spec update**

```bash
git add docs/superpowers/specs/2026-09-21-core-architecture-and-remote-screen-design.md
git commit -m "Record input-blocking mechanism decision after license review"
```

---

### Task 7: Session — Windows input device controller

**Files:**
- Create: `src/TreezeSuite.Session/IInputDeviceController.cs`
- Create: `src/TreezeSuite.Session/WindowsInputDeviceController.cs`

**Interfaces:**
- Produces: `IInputDeviceController` with `DisableInputDevices()` / `EnableInputDevices()`, and `WindowsInputDeviceController` implementing it. Task 8's `InputLockManager` is constructed with an `IInputDeviceController`; production code passes `WindowsInputDeviceController`, tests pass a fake.

- [x] **Step 1: Define the interface**

```csharp
// src/TreezeSuite.Session/IInputDeviceController.cs
namespace TreezeSuite.Session;

public interface IInputDeviceController
{
    void DisableInputDevices();
    void EnableInputDevices();
}
```

- [x] **Step 2: Implement `WindowsInputDeviceController.cs` per Task 6's decision**

If Task 6 chose the SetupDi fallback, implement it as:

```csharp
using System.Runtime.InteropServices;

namespace TreezeSuite.Session;

public sealed class WindowsInputDeviceController : IInputDeviceController
{
    // GUID_DEVCLASS_KEYBOARD and GUID_DEVCLASS_MOUSE
    private static readonly Guid KeyboardClassGuid = new("4D36E96B-E325-11CE-BFC1-08002BE10318");
    private static readonly Guid MouseClassGuid = new("4D36E96F-E325-11CE-BFC1-08002BE10318");

    public void DisableInputDevices()
    {
        SetClassDevicesState(KeyboardClassGuid, enable: false);
        SetClassDevicesState(MouseClassGuid, enable: false);
    }

    public void EnableInputDevices()
    {
        SetClassDevicesState(KeyboardClassGuid, enable: true);
        SetClassDevicesState(MouseClassGuid, enable: true);
    }

    private static void SetClassDevicesState(Guid classGuid, bool enable)
    {
        const uint DIGCF_PRESENT = 0x02;
        var deviceInfoSet = SetupDiGetClassDevs(ref classGuid, null, IntPtr.Zero, DIGCF_PRESENT);
        if (deviceInfoSet == IntPtr.Zero || deviceInfoSet.ToInt64() == -1)
        {
            return;
        }

        try
        {
            var deviceInfoData = new SP_DEVINFO_DATA { cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>() };
            for (uint index = 0; SetupDiEnumDeviceInfo(deviceInfoSet, index, ref deviceInfoData); index++)
            {
                var parameters = new SP_PROPCHANGE_PARAMS
                {
                    ClassInstallHeader = new SP_CLASSINSTALL_HEADER
                    {
                        cbSize = Marshal.SizeOf<SP_CLASSINSTALL_HEADER>(),
                        InstallFunction = DIF_PROPERTYCHANGE,
                    },
                    StateChange = enable ? DICS_ENABLE : DICS_DISABLE,
                    Scope = DICS_FLAG_GLOBAL,
                    HwProfile = 0,
                };

                SetupDiSetClassInstallParams(deviceInfoSet, ref deviceInfoData, ref parameters,
                    Marshal.SizeOf<SP_PROPCHANGE_PARAMS>());
                SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, deviceInfoSet, ref deviceInfoData);
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(deviceInfoSet);
        }
    }

    private const uint DIF_PROPERTYCHANGE = 0x12;
    private const uint DICS_ENABLE = 1;
    private const uint DICS_DISABLE = 2;
    private const uint DICS_FLAG_GLOBAL = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_DEVINFO_DATA
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_CLASSINSTALL_HEADER
    {
        public int cbSize;
        public uint InstallFunction;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SP_PROPCHANGE_PARAMS
    {
        public SP_CLASSINSTALL_HEADER ClassInstallHeader;
        public uint StateChange;
        public uint Scope;
        public uint HwProfile;
    }

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        ref Guid classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiSetClassInstallParams(
        IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
        ref SP_PROPCHANGE_PARAMS classInstallParams, int classInstallParamsSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiCallClassInstaller(
        uint installFunction, IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
```

(If Task 6 instead chose to keep the Interception driver, implement the same two public methods using `interception_create_context`/`interception_set_filter`/`interception_destroy_context` P/Invokes against the vendored driver's header, following the same shape used in Treezeview's `WindowsInputDeviceFunctions::enableInterception`/`disableInterception` for reference on *which native calls to make* — write the C# wrapper code itself from scratch, don't copy the C++.)

- [x] **Step 3: Manually verify** (no automated test possible for real hardware device state)

Add a temporary `Console.WriteLine` call in `Program.cs`: call `DisableInputDevices()`, wait 5 seconds (`Thread.Sleep(5000)`), confirm the keyboard/mouse stop responding, then call `EnableInputDevices()`, confirm they respond again. Remove the temporary call once confirmed — this logic gets wired into `InputLockManager` in Task 8 instead.

- [x] **Step 4: Commit**

```bash
git add src/TreezeSuite.Session/IInputDeviceController.cs src/TreezeSuite.Session/WindowsInputDeviceController.cs
git commit -m "Add Windows input device controller (disable/enable keyboard+mouse)"
```

---

### Task 8: Session — input lock manager with heartbeat auto-unlock

**Files:**
- Create: `src/TreezeSuite.Session/ISystemClock.cs`
- Create: `src/TreezeSuite.Session/SystemClock.cs`
- Create: `src/TreezeSuite.Session/InputLockManager.cs`
- Test: `tests/TreezeSuite.Session.Tests/InputLockManagerTests.cs`

**Interfaces:**
- Consumes: `IInputDeviceController` from Task 7.
- Produces: `InputLockManager(IInputDeviceController, ISystemClock, TimeSpan heartbeatTimeout)` with `StartLock()`, `StopLock()`, `OnHeartbeatReceived()`, `CheckHeartbeatTimeout()`, `IsLocked`. Task 12's `SessionServer` calls `StartLock`/`StopLock` on `StartControl`/`StopControl` messages, `OnHeartbeatReceived` on `Heartbeat` messages, and polls `CheckHeartbeatTimeout` on a timer.

- [x] **Step 1: Write the failing tests**

```csharp
// tests/TreezeSuite.Session.Tests/InputLockManagerTests.cs
using TreezeSuite.Session;
using Xunit;

public class FakeClock : ISystemClock
{
    public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
}

public class FakeInputDeviceController : IInputDeviceController
{
    public int DisableCalls { get; private set; }
    public int EnableCalls { get; private set; }
    public void DisableInputDevices() => DisableCalls++;
    public void EnableInputDevices() => EnableCalls++;
}

public class InputLockManagerTests
{
    [Fact]
    public void StartLock_DisablesInputDevicesOnceEvenIfCalledTwice()
    {
        var controller = new FakeInputDeviceController();
        var manager = new InputLockManager(controller, new FakeClock(), TimeSpan.FromSeconds(5));

        manager.StartLock();
        manager.StartLock();

        Assert.Equal(1, controller.DisableCalls);
        Assert.True(manager.IsLocked);
    }

    [Fact]
    public void StopLock_EnablesInputDevices()
    {
        var controller = new FakeInputDeviceController();
        var manager = new InputLockManager(controller, new FakeClock(), TimeSpan.FromSeconds(5));
        manager.StartLock();

        manager.StopLock();

        Assert.Equal(1, controller.EnableCalls);
        Assert.False(manager.IsLocked);
    }

    [Fact]
    public void CheckHeartbeatTimeout_NoHeartbeatPastTimeout_AutoUnlocks()
    {
        var controller = new FakeInputDeviceController();
        var clock = new FakeClock();
        var manager = new InputLockManager(controller, clock, TimeSpan.FromSeconds(5));
        manager.StartLock();

        clock.UtcNow = clock.UtcNow.AddSeconds(6);
        manager.CheckHeartbeatTimeout();

        Assert.False(manager.IsLocked);
        Assert.Equal(1, controller.EnableCalls);
    }

    [Fact]
    public void CheckHeartbeatTimeout_HeartbeatReceivedInTime_StaysLocked()
    {
        var controller = new FakeInputDeviceController();
        var clock = new FakeClock();
        var manager = new InputLockManager(controller, clock, TimeSpan.FromSeconds(5));
        manager.StartLock();

        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        manager.OnHeartbeatReceived();
        clock.UtcNow = clock.UtcNow.AddSeconds(3);
        manager.CheckHeartbeatTimeout();

        Assert.True(manager.IsLocked);
        Assert.Equal(0, controller.EnableCalls);
    }

    [Fact]
    public void CheckHeartbeatTimeout_NotLocked_DoesNothing()
    {
        var controller = new FakeInputDeviceController();
        var clock = new FakeClock();
        var manager = new InputLockManager(controller, clock, TimeSpan.FromSeconds(5));

        manager.CheckHeartbeatTimeout();

        Assert.Equal(0, controller.EnableCalls);
        Assert.Equal(0, controller.DisableCalls);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/TreezeSuite.Session.Tests`
Expected: FAIL to compile — types not found.

- [x] **Step 3: Implement `ISystemClock.cs` and `SystemClock.cs`**

```csharp
// src/TreezeSuite.Session/ISystemClock.cs
namespace TreezeSuite.Session;

public interface ISystemClock
{
    DateTimeOffset UtcNow { get; }
}
```

```csharp
// src/TreezeSuite.Session/SystemClock.cs
namespace TreezeSuite.Session;

public sealed class SystemClock : ISystemClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
```

- [x] **Step 4: Implement `InputLockManager.cs`**

```csharp
namespace TreezeSuite.Session;

public sealed class InputLockManager
{
    private readonly IInputDeviceController _deviceController;
    private readonly ISystemClock _clock;
    private readonly TimeSpan _heartbeatTimeout;
    private DateTimeOffset? _lastHeartbeat;

    public InputLockManager(IInputDeviceController deviceController, ISystemClock clock, TimeSpan heartbeatTimeout)
    {
        _deviceController = deviceController;
        _clock = clock;
        _heartbeatTimeout = heartbeatTimeout;
    }

    public bool IsLocked { get; private set; }

    public void StartLock()
    {
        if (IsLocked)
        {
            return;
        }
        _deviceController.DisableInputDevices();
        IsLocked = true;
        _lastHeartbeat = _clock.UtcNow;
    }

    public void StopLock()
    {
        if (!IsLocked)
        {
            return;
        }
        _deviceController.EnableInputDevices();
        IsLocked = false;
        _lastHeartbeat = null;
    }

    public void OnHeartbeatReceived()
    {
        if (IsLocked)
        {
            _lastHeartbeat = _clock.UtcNow;
        }
    }

    public void CheckHeartbeatTimeout()
    {
        if (!IsLocked || _lastHeartbeat is null)
        {
            return;
        }
        if (_clock.UtcNow - _lastHeartbeat.Value > _heartbeatTimeout)
        {
            StopLock();
        }
    }
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/TreezeSuite.Session.Tests`
Expected: PASS (5/5)

- [x] **Step 6: Commit**

```bash
git add src/TreezeSuite.Session/ISystemClock.cs src/TreezeSuite.Session/SystemClock.cs src/TreezeSuite.Session/InputLockManager.cs tests/TreezeSuite.Session.Tests/InputLockManagerTests.cs
git commit -m "Add input lock manager with heartbeat-based auto-unlock safety net"
```

---

### Task 9: Session — screen capture (GDI)

**Files:**
- Create: `src/TreezeSuite.Session/IScreenCapture.cs`
- Create: `src/TreezeSuite.Session/GdiScreenCapture.cs`

**Interfaces:**
- Produces: `IScreenCapture` with `CaptureFrame() -> Bitmap`, and `GdiScreenCapture` implementing it against the primary screen's bounds. Task 10's `FramePipeline` is constructed with an `IScreenCapture`; production code passes `GdiScreenCapture`, tests pass a fake that returns pre-made bitmaps.

- [x] **Step 1: Define the interface**

```csharp
// src/TreezeSuite.Session/IScreenCapture.cs
using System.Drawing;

namespace TreezeSuite.Session;

public interface IScreenCapture
{
    Bitmap CaptureFrame();
}
```

- [x] **Step 2: Implement `GdiScreenCapture.cs`**

```csharp
using System.Drawing;
using System.Windows.Forms;

namespace TreezeSuite.Session;

public sealed class GdiScreenCapture : IScreenCapture
{
    public Bitmap CaptureFrame()
    {
        var bounds = Screen.PrimaryScreen!.Bounds;
        var bitmap = new Bitmap(bounds.Width, bounds.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        return bitmap;
    }
}
```

This needs a reference to `System.Windows.Forms` (for `Screen.PrimaryScreen`) — add `<UseWindowsForms>true</UseWindowsForms>` to `src/TreezeSuite.Session/TreezeSuite.Session.csproj`'s `PropertyGroup`.

- [x] **Step 3: Manually verify**

Add a temporary call in `Program.cs`: `new GdiScreenCapture().CaptureFrame().Save("test-capture.png")`, run the executable, open `test-capture.png`, confirm it's a real screenshot of the current desktop. Delete the temporary call and the test file afterward.

- [x] **Step 4: Commit**

```bash
git add src/TreezeSuite.Session/IScreenCapture.cs src/TreezeSuite.Session/GdiScreenCapture.cs src/TreezeSuite.Session/TreezeSuite.Session.csproj
git commit -m "Add GDI-based screen capture"
```

---

### Task 10: Session — frame pipeline

**Files:**
- Create: `src/TreezeSuite.Session/EncodedTile.cs`
- Create: `src/TreezeSuite.Session/FramePipeline.cs`
- Test: `tests/TreezeSuite.Session.Tests/FramePipelineTests.cs`

**Interfaces:**
- Consumes: `IScreenCapture` (Task 9), `TileDiff` and `TileCodec` (Tasks 4–5) from `TreezeSuite.Protocol`.
- Produces: `EncodedTile(int Row, int Column, byte[] JpegBytes)`, `FramePipeline(IScreenCapture)` with `CaptureNextFrame() -> IReadOnlyList<EncodedTile>` (empty list on the very first call, since there's no previous frame to diff against — the caller is responsible for a full-frame send on first connect, handled in Task 12). Task 12's `SessionServer` calls `CaptureNextFrame` on a timer and turns each `EncodedTile` into a `FrameTile` message.

- [x] **Step 1: Write the failing tests**

```csharp
// tests/TreezeSuite.Session.Tests/FramePipelineTests.cs
using System.Drawing;
using TreezeSuite.Session;
using Xunit;

public class FakeScreenCapture : IScreenCapture
{
    private readonly Queue<Bitmap> _frames;

    public FakeScreenCapture(params Bitmap[] frames)
    {
        _frames = new Queue<Bitmap>(frames);
    }

    public Bitmap CaptureFrame() => _frames.Dequeue();
}

public class FramePipelineTests
{
    [Fact]
    public void CaptureNextFrame_FirstCall_ReturnsEmpty()
    {
        var frame = SolidBitmap(128, 128, Color.Red);
        var pipeline = new FramePipeline(new FakeScreenCapture(frame));

        var tiles = pipeline.CaptureNextFrame();

        Assert.Empty(tiles);
    }

    [Fact]
    public void CaptureNextFrame_SecondCallSameContent_ReturnsEmpty()
    {
        var frame1 = SolidBitmap(128, 128, Color.Red);
        var frame2 = SolidBitmap(128, 128, Color.Red);
        var pipeline = new FramePipeline(new FakeScreenCapture(frame1, frame2));
        pipeline.CaptureNextFrame();

        var tiles = pipeline.CaptureNextFrame();

        Assert.Empty(tiles);
    }

    [Fact]
    public void CaptureNextFrame_SecondCallOneTileChanged_ReturnsThatTileEncoded()
    {
        var frame1 = SolidBitmap(128, 128, Color.Red);
        var frame2 = SolidBitmap(128, 128, Color.Red);
        using (var g = Graphics.FromImage(frame2))
        {
            g.FillRectangle(Brushes.Blue, 64, 0, 64, 64);
        }
        var pipeline = new FramePipeline(new FakeScreenCapture(frame1, frame2));
        pipeline.CaptureNextFrame();

        var tiles = pipeline.CaptureNextFrame();

        Assert.Single(tiles);
        Assert.Equal(0, tiles[0].Row);
        Assert.Equal(1, tiles[0].Column);
        Assert.NotEmpty(tiles[0].JpegBytes);
    }

    private static Bitmap SolidBitmap(int width, int height, Color color)
    {
        var bitmap = new Bitmap(width, height);
        using var g = Graphics.FromImage(bitmap);
        g.Clear(color);
        return bitmap;
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/TreezeSuite.Session.Tests`
Expected: FAIL to compile — types not found.

- [x] **Step 3: Implement `EncodedTile.cs`**

```csharp
namespace TreezeSuite.Session;

public sealed record EncodedTile(int Row, int Column, byte[] JpegBytes);
```

- [x] **Step 4: Implement `FramePipeline.cs`**

```csharp
using System.Drawing;
using TreezeSuite.Protocol;

namespace TreezeSuite.Session;

public sealed class FramePipeline
{
    private readonly IScreenCapture _capture;
    private Bitmap? _previousFrame;

    public FramePipeline(IScreenCapture capture)
    {
        _capture = capture;
    }

    public IReadOnlyList<EncodedTile> CaptureNextFrame()
    {
        var currentFrame = _capture.CaptureFrame();

        if (_previousFrame is null)
        {
            _previousFrame = currentFrame;
            return Array.Empty<EncodedTile>();
        }

        var changedTiles = TileDiff.FindChangedTiles(_previousFrame, currentFrame);
        var encoded = new List<EncodedTile>(changedTiles.Count);
        foreach (var tile in changedTiles)
        {
            var rect = TileDiff.TileRect(tile.Row, tile.Column, currentFrame.Width, currentFrame.Height);
            var jpegBytes = TileCodec.EncodeTile(currentFrame, rect);
            encoded.Add(new EncodedTile(tile.Row, tile.Column, jpegBytes));
        }

        _previousFrame.Dispose();
        _previousFrame = currentFrame;
        return encoded;
    }
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/TreezeSuite.Session.Tests`
Expected: PASS (3/3)

- [x] **Step 6: Commit**

```bash
git add src/TreezeSuite.Session/EncodedTile.cs src/TreezeSuite.Session/FramePipeline.cs tests/TreezeSuite.Session.Tests/FramePipelineTests.cs
git commit -m "Add frame pipeline: capture, diff, and JPEG-encode only changed tiles"
```

---

### Task 11: Session — input injection

**Files:**
- Create: `src/TreezeSuite.Session/InputEvent.cs`
- Create: `src/TreezeSuite.Session/IInputInjector.cs`
- Create: `src/TreezeSuite.Session/SendInputInjector.cs`

**Interfaces:**
- Produces: `InputEvent` (a small DTO covering mouse move/click and key down/up), `IInputInjector` with `Inject(InputEvent)`, `SendInputInjector` implementing it via `user32.dll`'s `SendInput`. Task 12's `SessionServer` decodes `InputEvent` messages and calls `Inject`.

- [x] **Step 1: Define `InputEvent.cs`**

```csharp
namespace TreezeSuite.Session;

public enum InputEventKind
{
    MouseMove,
    MouseLeftDown,
    MouseLeftUp,
    KeyDown,
    KeyUp,
}

public sealed record InputEvent(InputEventKind Kind, int X, int Y, ushort VirtualKeyCode);
```

- [x] **Step 2: Define `IInputInjector.cs`**

```csharp
namespace TreezeSuite.Session;

public interface IInputInjector
{
    void Inject(InputEvent inputEvent);
}
```

- [x] **Step 3: Implement `SendInputInjector.cs`**

```csharp
using System.Runtime.InteropServices;

namespace TreezeSuite.Session;

public sealed class SendInputInjector : IInputInjector
{
    public void Inject(InputEvent inputEvent)
    {
        var input = inputEvent.Kind switch
        {
            InputEventKind.MouseMove => MouseInput(inputEvent.X, inputEvent.Y, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE),
            InputEventKind.MouseLeftDown => MouseInput(inputEvent.X, inputEvent.Y, MOUSEEVENTF_LEFTDOWN | MOUSEEVENTF_ABSOLUTE),
            InputEventKind.MouseLeftUp => MouseInput(inputEvent.X, inputEvent.Y, MOUSEEVENTF_LEFTUP | MOUSEEVENTF_ABSOLUTE),
            InputEventKind.KeyDown => KeyboardInput(inputEvent.VirtualKeyCode, keyUp: false),
            InputEventKind.KeyUp => KeyboardInput(inputEvent.VirtualKeyCode, keyUp: true),
            _ => throw new ArgumentOutOfRangeException(nameof(inputEvent)),
        };

        var inputs = new[] { input };
        SendInput(1, inputs, Marshal.SizeOf<INPUT>());
    }

    private static INPUT MouseInput(int x, int y, uint flags) => new()
    {
        type = INPUT_MOUSE,
        u = new InputUnion
        {
            mi = new MOUSEINPUT
            {
                dx = NormalizeToAbsolute(x, SystemMetric.SM_CXSCREEN),
                dy = NormalizeToAbsolute(y, SystemMetric.SM_CYSCREEN),
                dwFlags = flags,
            },
        },
    };

    private static INPUT KeyboardInput(ushort virtualKeyCode, bool keyUp) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = virtualKeyCode,
                dwFlags = keyUp ? KEYEVENTF_KEYUP : 0,
            },
        },
    };

    private static int NormalizeToAbsolute(int coordinate, SystemMetric metric)
    {
        var screenSize = GetSystemMetrics((int)metric);
        return (int)(coordinate * 65535.0 / screenSize);
    }

    private enum SystemMetric { SM_CXSCREEN = 0, SM_CYSCREEN = 1 }

    private const uint INPUT_MOUSE = 0;
    private const uint INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint numberOfInputs, INPUT[] inputs, int structSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
}
```

- [x] **Step 4: Manually verify**

Add a temporary call in `Program.cs`: inject a `MouseMove` to the screen center, then a `KeyDown`/`KeyUp` for a harmless key (e.g. Shift, virtual key code `0x10`), confirm the cursor moves and the key registers (e.g. by focusing Notepad first and checking Caps behavior, or watching a keyboard-test website). Remove the temporary call afterward.

- [x] **Step 5: Commit**

```bash
git add src/TreezeSuite.Session/InputEvent.cs src/TreezeSuite.Session/IInputInjector.cs src/TreezeSuite.Session/SendInputInjector.cs
git commit -m "Add SendInput-based mouse/keyboard input injection"
```

---

### Task 12: Session — server tying capture, injection, and lock together

**Files:**
- Create: `src/TreezeSuite.Session/SessionServer.cs`
- Modify: `src/TreezeSuite.Session/Program.cs`

**Interfaces:**
- Consumes: `SecureChannel` (Task 3), `FramePipeline`/`EncodedTile` (Task 10), `IInputInjector`/`InputEvent` (Task 11), `InputLockManager` (Task 8), `MessageType`/`Message` (Task 2).
- Produces: `SessionServer(TcpListener, X509Certificate2, TrustStore, FramePipeline, IInputInjector, InputLockManager)` with `Run(CancellationToken)`. `Program.cs` constructs and runs it; Task 15's `TreezeMaster` is the client this task's server talks to.

- [x] **Step 1: Implement `SessionServer.cs`**

```csharp
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using TreezeSuite.Protocol;

namespace TreezeSuite.Session;

public sealed class SessionServer
{
    private const int HeartbeatCheckIntervalMs = 1000;
    private const int FrameIntervalMs = 66; // ~15 FPS

    private readonly TcpListener _listener;
    private readonly X509Certificate2 _serverCertificate;
    private readonly TrustStore _masterTrustStore;
    private readonly FramePipeline _framePipeline;
    private readonly IInputInjector _inputInjector;
    private readonly InputLockManager _inputLockManager;

    public SessionServer(
        TcpListener listener, X509Certificate2 serverCertificate, TrustStore masterTrustStore,
        FramePipeline framePipeline, IInputInjector inputInjector, InputLockManager inputLockManager)
    {
        _listener = listener;
        _serverCertificate = serverCertificate;
        _masterTrustStore = masterTrustStore;
        _framePipeline = framePipeline;
        _inputInjector = inputInjector;
        _inputLockManager = inputLockManager;
    }

    public void Run(CancellationToken cancellationToken)
    {
        _listener.Start();
        while (!cancellationToken.IsCancellationRequested)
        {
            var tcpClient = _listener.AcceptTcpClient();
            HandleClient(tcpClient, cancellationToken);
        }
    }

    private void HandleClient(TcpClient tcpClient, CancellationToken cancellationToken)
    {
        using var channel = SecureChannel.AuthenticateAsServer(tcpClient, _serverCertificate, _masterTrustStore);
        var isControlling = false;

        var heartbeatTimer = new Timer(_ => _inputLockManager.CheckHeartbeatTimeout(),
            null, HeartbeatCheckIntervalMs, HeartbeatCheckIntervalMs);

        try
        {
            while (!cancellationToken.IsCancellationRequested && tcpClient.Connected)
            {
                var message = channel.Receive();
                switch (message.Type)
                {
                    case MessageType.StartControl:
                        isControlling = true;
                        _inputLockManager.StartLock();
                        break;

                    case MessageType.StopControl:
                        isControlling = false;
                        _inputLockManager.StopLock();
                        break;

                    case MessageType.Heartbeat:
                        _inputLockManager.OnHeartbeatReceived();
                        break;

                    case MessageType.InputEvent:
                        if (isControlling)
                        {
                            _inputInjector.Inject(InputEventCodec.Decode(message.Payload));
                        }
                        break;
                }

                foreach (var tile in _framePipeline.CaptureNextFrame())
                {
                    channel.Send(new Message(MessageType.FrameTile, TileMessageCodec.Encode(tile)));
                }
            }
        }
        finally
        {
            heartbeatTimer.Dispose();
            _inputLockManager.StopLock(); // safety net: connection ended for any reason
        }
    }
}
```

Note: `HandleClient`'s loop calls `channel.Receive()`, which blocks until a message arrives — this means frame capture only happens once per received message from the Master (heartbeat, input event, etc.), not on its own independent timer. That's a known simplification for this task; Task 13 (end-to-end validation) will surface whether frame updates feel responsive enough purely off the Master's own heartbeat cadence, or whether a separate frame-sending thread is needed — don't add one preemptively (YAGNI) until that's confirmed.

- [x] **Step 2: Add `InputEventCodec` and `TileMessageCodec`** (small serializers `SessionServer` depends on — add to the same file, above the `SessionServer` class)

```csharp
internal static class InputEventCodec
{
    public static InputEvent Decode(byte[] payload)
    {
        var kind = (InputEventKind)payload[0];
        var x = BitConverter.ToInt32(payload, 1);
        var y = BitConverter.ToInt32(payload, 5);
        var vk = BitConverter.ToUInt16(payload, 9);
        return new InputEvent(kind, x, y, vk);
    }

    public static byte[] Encode(InputEvent inputEvent)
    {
        var buffer = new byte[11];
        buffer[0] = (byte)inputEvent.Kind;
        BitConverter.GetBytes(inputEvent.X).CopyTo(buffer, 1);
        BitConverter.GetBytes(inputEvent.Y).CopyTo(buffer, 5);
        BitConverter.GetBytes(inputEvent.VirtualKeyCode).CopyTo(buffer, 9);
        return buffer;
    }
}

internal static class TileMessageCodec
{
    public static byte[] Encode(EncodedTile tile)
    {
        var buffer = new byte[8 + tile.JpegBytes.Length];
        BitConverter.GetBytes(tile.Row).CopyTo(buffer, 0);
        BitConverter.GetBytes(tile.Column).CopyTo(buffer, 4);
        tile.JpegBytes.CopyTo(buffer, 8);
        return buffer;
    }

    public static (int Row, int Column, byte[] JpegBytes) Decode(byte[] payload)
    {
        var row = BitConverter.ToInt32(payload, 0);
        var column = BitConverter.ToInt32(payload, 4);
        var jpegBytes = payload[8..];
        return (row, column, jpegBytes);
    }
}
```

- [x] **Step 3: Wire up `Program.cs`**

```csharp
using System.Net;
using System.Net.Sockets;
using TreezeSuite.Protocol;
using TreezeSuite.Session;

var serverCertificate = SelfSignedCertificate.Create(Environment.MachineName);
Console.WriteLine($"This machine's certificate thumbprint (give this to the teacher's TreezeMaster): {serverCertificate.Thumbprint}");

// TODO (later task, not this one): load the actual trusted master thumbprint(s)
// from a config file instead of accepting a command-line argument.
var masterThumbprint = args.Length > 0 ? args[0] : throw new ArgumentException(
    "Pass the trusted TreezeMaster certificate thumbprint as the first argument for this manual test.");

var server = new SessionServer(
    new TcpListener(IPAddress.Any, 5901),
    serverCertificate,
    new TrustStore(new[] { masterThumbprint }),
    new FramePipeline(new GdiScreenCapture()),
    new SendInputInjector(),
    new InputLockManager(new WindowsInputDeviceController(), new SystemClock(), TimeSpan.FromSeconds(5)));

server.Run(CancellationToken.None);
```

- [x] **Step 4: Verify the project builds**

Run: `dotnet build src/TreezeSuite.Session`
Expected: succeeds with 0 errors.

- [x] **Step 5: Commit**

```bash
git add src/TreezeSuite.Session/SessionServer.cs src/TreezeSuite.Session/Program.cs
git commit -m "Add session server tying together capture, injection, and input lock"
```

---

### Task 13: Agent — session process supervisor

**Files:**
- Create: `src/TreezeSuite.Agent/ISessionProcessLauncher.cs`
- Create: `src/TreezeSuite.Agent/SessionSupervisor.cs`
- Test: `tests/TreezeSuite.Agent.Tests/SessionSupervisorTests.cs`

**Interfaces:**
- Produces: `ISessionProcessLauncher` with `TryLaunch(out int processId) -> bool` and `IsRunning(int processId) -> bool`; `SessionSupervisor(ISessionProcessLauncher, TimeSpan pollInterval)` with `EnsureRunning()`. Task 14's Windows Service host calls `EnsureRunning()` on a timer.

- [x] **Step 1: Write the failing tests**

```csharp
// tests/TreezeSuite.Agent.Tests/SessionSupervisorTests.cs
using TreezeSuite.Agent;
using Xunit;

public class FakeSessionProcessLauncher : ISessionProcessLauncher
{
    private readonly Queue<bool> _launchResults;
    private readonly HashSet<int> _runningProcessIds = new();
    private int _nextProcessId = 1;

    public FakeSessionProcessLauncher(params bool[] launchResults)
    {
        _launchResults = new Queue<bool>(launchResults);
    }

    public int LaunchAttempts { get; private set; }

    public bool TryLaunch(out int processId)
    {
        LaunchAttempts++;
        processId = 0;
        if (_launchResults.Count == 0 || !_launchResults.Dequeue())
        {
            return false;
        }
        processId = _nextProcessId++;
        _runningProcessIds.Add(processId);
        return true;
    }

    public bool IsRunning(int processId) => _runningProcessIds.Contains(processId);

    public void SimulateCrash(int processId) => _runningProcessIds.Remove(processId);
}

public class SessionSupervisorTests
{
    [Fact]
    public void EnsureRunning_NoProcessYet_LaunchesOne()
    {
        var launcher = new FakeSessionProcessLauncher(true);
        var supervisor = new SessionSupervisor(launcher, TimeSpan.FromSeconds(5));

        supervisor.EnsureRunning();

        Assert.Equal(1, launcher.LaunchAttempts);
    }

    [Fact]
    public void EnsureRunning_ProcessStillRunning_DoesNotRelaunch()
    {
        var launcher = new FakeSessionProcessLauncher(true);
        var supervisor = new SessionSupervisor(launcher, TimeSpan.FromSeconds(5));
        supervisor.EnsureRunning();

        supervisor.EnsureRunning();

        Assert.Equal(1, launcher.LaunchAttempts);
    }

    [Fact]
    public void EnsureRunning_ProcessCrashed_RelaunchesIt()
    {
        var launcher = new FakeSessionProcessLauncher(true, true);
        var supervisor = new SessionSupervisor(launcher, TimeSpan.FromSeconds(5));
        supervisor.EnsureRunning();
        launcher.SimulateCrash(1);

        supervisor.EnsureRunning();

        Assert.Equal(2, launcher.LaunchAttempts);
    }

    [Fact]
    public void EnsureRunning_LaunchFails_DoesNotThrow()
    {
        var launcher = new FakeSessionProcessLauncher(false);
        var supervisor = new SessionSupervisor(launcher, TimeSpan.FromSeconds(5));

        supervisor.EnsureRunning();

        Assert.Equal(1, launcher.LaunchAttempts);
    }
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/TreezeSuite.Agent.Tests`
Expected: FAIL to compile — types not found.

- [x] **Step 3: Implement `ISessionProcessLauncher.cs`**

```csharp
namespace TreezeSuite.Agent;

public interface ISessionProcessLauncher
{
    bool TryLaunch(out int processId);
    bool IsRunning(int processId);
}
```

- [x] **Step 4: Implement `SessionSupervisor.cs`**

```csharp
namespace TreezeSuite.Agent;

public sealed class SessionSupervisor
{
    private readonly ISessionProcessLauncher _launcher;
    private int? _currentProcessId;

    public SessionSupervisor(ISessionProcessLauncher launcher, TimeSpan pollInterval)
    {
        _launcher = launcher;
        PollInterval = pollInterval;
    }

    public TimeSpan PollInterval { get; }

    public void EnsureRunning()
    {
        if (_currentProcessId is int pid && _launcher.IsRunning(pid))
        {
            return;
        }

        _currentProcessId = _launcher.TryLaunch(out var newPid) ? newPid : null;
    }
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test tests/TreezeSuite.Agent.Tests`
Expected: PASS (4/4)

- [x] **Step 6: Commit**

```bash
git add src/TreezeSuite.Agent/ISessionProcessLauncher.cs src/TreezeSuite.Agent/SessionSupervisor.cs tests/TreezeSuite.Agent.Tests/SessionSupervisorTests.cs
git commit -m "Add session process supervisor with crash-restart logic"
```

---

### Task 14: Agent — Windows Service host

**Files:**
- Create: `src/TreezeSuite.Agent/UserSessionProcessLauncher.cs`
- Create: `src/TreezeSuite.Agent/TreezeAgentWorker.cs`
- Modify: `src/TreezeSuite.Agent/Program.cs`

**Interfaces:**
- Consumes: `ISessionProcessLauncher` (Task 13's interface), `SessionSupervisor` (Task 13).
- Produces: `UserSessionProcessLauncher` implementing `ISessionProcessLauncher` against the real Windows session APIs; `TreezeAgentWorker : BackgroundService` wiring `SessionSupervisor` into `Microsoft.Extensions.Hosting`. Nothing later in this plan depends on this task; it's the deployable entry point.

- [x] **Step 1: Implement `UserSessionProcessLauncher.cs`**

```csharp
using System.Diagnostics;
using System.Runtime.InteropServices;
using TreezeSuite.Agent;

namespace TreezeSuite.Agent;

public sealed class UserSessionProcessLauncher : ISessionProcessLauncher
{
    private readonly string _executablePath;

    public UserSessionProcessLauncher(string executablePath)
    {
        _executablePath = executablePath;
    }

    public bool TryLaunch(out int processId)
    {
        processId = 0;
        var sessionId = WTSGetActiveConsoleSessionId();
        if (sessionId == 0xFFFFFFFF)
        {
            return false; // no active session, e.g. sitting at the logon screen
        }

        if (!WTSQueryUserToken(sessionId, out var userToken))
        {
            return false;
        }

        try
        {
            if (!DuplicateTokenEx(userToken, 0x02000000, IntPtr.Zero,
                    SECURITY_IMPERSONATION_LEVEL.SecurityIdentification, TOKEN_TYPE.TokenPrimary, out var primaryToken))
            {
                return false;
            }

            try
            {
                var startupInfo = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
                var created = CreateProcessAsUser(
                    primaryToken, _executablePath, null, IntPtr.Zero, IntPtr.Zero, false,
                    0, IntPtr.Zero, null, ref startupInfo, out var processInfo);

                if (!created)
                {
                    return false;
                }

                processId = processInfo.dwProcessId;
                CloseHandle(processInfo.hThread);
                CloseHandle(processInfo.hProcess);
                return true;
            }
            finally
            {
                CloseHandle(primaryToken);
            }
        }
        finally
        {
            CloseHandle(userToken);
        }
    }

    public bool IsRunning(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private enum SECURITY_IMPERSONATION_LEVEL { SecurityIdentification = 1 }
    private enum TOKEN_TYPE { TokenPrimary = 1 }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFO
    {
        public int cb;
        public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        IntPtr existingToken, uint desiredAccess, IntPtr tokenAttributes,
        SECURITY_IMPERSONATION_LEVEL impersonationLevel, TOKEN_TYPE tokenType, out IntPtr newToken);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUser(
        IntPtr token, string applicationName, string? commandLine, IntPtr processAttributes,
        IntPtr threadAttributes, bool inheritHandles, uint creationFlags, IntPtr environment,
        string? currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInfo);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}
```

- [x] **Step 2: Implement `TreezeAgentWorker.cs`**

```csharp
using Microsoft.Extensions.Hosting;

namespace TreezeSuite.Agent;

public sealed class TreezeAgentWorker : BackgroundService
{
    private readonly SessionSupervisor _supervisor;

    public TreezeAgentWorker(SessionSupervisor supervisor)
    {
        _supervisor = supervisor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            _supervisor.EnsureRunning();
            await Task.Delay(_supervisor.PollInterval, stoppingToken);
        }
    }
}
```

- [x] **Step 3: Wire up `Program.cs`**

```csharp
using Microsoft.Extensions.Hosting;
using TreezeSuite.Agent;

var sessionExecutablePath = Path.Combine(AppContext.BaseDirectory, "..", "TreezeSuite.Session", "TreezeSuite.Session.exe");

var host = Host.CreateDefaultBuilder(args)
    .UseWindowsService(options => options.ServiceName = "TreezeAgent")
    .ConfigureServices(services =>
    {
        services.AddSingleton<ISessionProcessLauncher>(
            new UserSessionProcessLauncher(sessionExecutablePath));
        services.AddSingleton(provider => new SessionSupervisor(
            provider.GetRequiredService<ISessionProcessLauncher>(), TimeSpan.FromSeconds(5)));
        services.AddHostedService<TreezeAgentWorker>();
    })
    .Build();

host.Run();
```

- [x] **Step 4: Verify the project builds**

Run: `dotnet build src/TreezeSuite.Agent`
Expected: succeeds with 0 errors.

- [x] **Step 5: Manually verify** (needs an actual service install — this cannot be automated in a unit test)

```bash
dotnet publish src/TreezeSuite.Agent -c Release -o publish/Agent
sc.exe create TreezeAgent binPath= "C:\path\to\publish\Agent\TreezeSuite.Agent.exe" start= auto
sc.exe start TreezeAgent
```

Confirm in Task Manager that `TreezeSuite.Session.exe` appears in your interactive session shortly after starting the service. Then end that process manually and confirm the Agent relaunches it within ~5 seconds (matching `PollInterval`). Clean up afterward: `sc.exe stop TreezeAgent`, `sc.exe delete TreezeAgent`.

- [x] **Step 6: Commit**

```bash
git add src/TreezeSuite.Agent/UserSessionProcessLauncher.cs src/TreezeSuite.Agent/TreezeAgentWorker.cs src/TreezeSuite.Agent/Program.cs
git commit -m "Add TreezeAgent Windows Service hosting the session supervisor"
```

---

### Task 15: Master — minimal console and view/control window

**Files:**
- Create: `src/TreezeSuite.Master/MainWindow.xaml`
- Create: `src/TreezeSuite.Master/MainWindow.xaml.cs`
- Create: `src/TreezeSuite.Master/ViewControlWindow.xaml`
- Create: `src/TreezeSuite.Master/ViewControlWindow.xaml.cs`
- Create: `src/TreezeSuite.Master/ConnectionClient.cs`

**Interfaces:**
- Consumes: `SecureChannel`/`SelfSignedCertificate`/`TrustStore` (Task 3), `MessageType`/`Message` (Task 2), the `TileMessageCodec`/`InputEventCodec` shapes defined in Task 12 (duplicated here as the Master's own copies, since Master and Session don't share an assembly beyond `TreezeSuite.Protocol` — the wire format is the contract, not shared code).
- Produces: a runnable WPF app. This is the last task before end-to-end validation (Task 16).

- [x] **Step 1: Implement `ConnectionClient.cs`**

```csharp
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using TreezeSuite.Protocol;

namespace TreezeSuite.Master;

public sealed class ConnectionClient : IDisposable
{
    private readonly SecureChannel _channel;

    private ConnectionClient(SecureChannel channel)
    {
        _channel = channel;
    }

    public static ConnectionClient Connect(
        string hostAddress, int port, string targetThumbprint, X509Certificate2 masterCertificate)
    {
        var tcpClient = new TcpClient();
        tcpClient.Connect(hostAddress, port);
        var channel = SecureChannel.AuthenticateAsClient(
            tcpClient, hostAddress, masterCertificate, new TrustStore(new[] { targetThumbprint }));
        return new ConnectionClient(channel);
    }

    public void SendStartControl() => _channel.Send(new Message(MessageType.StartControl, Array.Empty<byte>()));

    public void SendStopControl() => _channel.Send(new Message(MessageType.StopControl, Array.Empty<byte>()));

    public void SendHeartbeat() => _channel.Send(new Message(MessageType.Heartbeat, Array.Empty<byte>()));

    public void SendInputEvent(byte[] encodedEvent) =>
        _channel.Send(new Message(MessageType.InputEvent, encodedEvent));

    public Message ReceiveNext() => _channel.Receive();

    public void Dispose() => _channel.Dispose();
}
```

- [x] **Step 2: Implement `MainWindow.xaml`** — a hardcoded single-entry list for this first version (a config-file-backed computer list is a later sub-project, not this one)

```xml
<Window x:Class="TreezeSuite.Master.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="TreezeMaster" Height="200" Width="400">
    <StackPanel Margin="16">
        <TextBlock Text="Host address:" Margin="0,0,0,4" />
        <TextBox x:Name="HostAddressTextBox" Text="localhost" Margin="0,0,0,8" />
        <TextBlock Text="Trusted certificate thumbprint:" Margin="0,0,0,4" />
        <TextBox x:Name="ThumbprintTextBox" Margin="0,0,0,8" />
        <Button Content="Connect" Click="OnConnectClicked" />
    </StackPanel>
</Window>
```

- [x] **Step 3: Implement `MainWindow.xaml.cs`**

```csharp
using System.Windows;
using TreezeSuite.Protocol;

namespace TreezeSuite.Master;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnConnectClicked(object sender, RoutedEventArgs e)
    {
        var masterCertificate = SelfSignedCertificate.Create("TreezeMaster");
        var client = ConnectionClient.Connect(
            HostAddressTextBox.Text, 5901, ThumbprintTextBox.Text, masterCertificate);

        var viewControlWindow = new ViewControlWindow(client);
        viewControlWindow.Show();
    }
}
```

- [x] **Step 4: Implement `ViewControlWindow.xaml`**

```xml
<Window x:Class="TreezeSuite.Master.ViewControlWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="TreezeMaster - Controlling" Height="720" Width="1280"
        Closed="OnWindowClosed">
    <Grid>
        <Image x:Name="ScreenImage" Stretch="None"
               MouseMove="OnMouseMove" MouseLeftButtonDown="OnMouseLeftDown" MouseLeftButtonUp="OnMouseLeftUp"
               KeyDown="OnKeyDown" KeyUp="OnKeyUp" Focusable="True" />
    </Grid>
</Window>
```

- [x] **Step 5: Implement `ViewControlWindow.xaml.cs`**

```csharp
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TreezeSuite.Protocol;

namespace TreezeSuite.Master;

public partial class ViewControlWindow : Window
{
    private readonly ConnectionClient _client;
    private readonly DispatcherTimer _heartbeatTimer;
    private WriteableBitmap? _screenBitmap;

    public ViewControlWindow(ConnectionClient client)
    {
        InitializeComponent();
        _client = client;

        _client.SendStartControl();

        _heartbeatTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _heartbeatTimer.Tick += (_, _) => _client.SendHeartbeat();
        _heartbeatTimer.Start();

        Task.Run(ReceiveLoop);
    }

    private void ReceiveLoop()
    {
        while (true)
        {
            Message message;
            try
            {
                message = _client.ReceiveNext();
            }
            catch (IOException)
            {
                return; // connection closed
            }

            if (message.Type != MessageType.FrameTile)
            {
                continue;
            }

            var row = BitConverter.ToInt32(message.Payload, 0);
            var column = BitConverter.ToInt32(message.Payload, 4);
            var jpegBytes = message.Payload[8..];

            Dispatcher.Invoke(() => DrawTile(row, column, jpegBytes));
        }
    }

    private void DrawTile(int row, int column, byte[] jpegBytes)
    {
        const int tileSize = 64; // must match TreezeSuite.Protocol.TileDiff.TileSize

        using var stream = new MemoryStream(jpegBytes);
        var decoder = new JpegBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var tileFrame = decoder.Frames[0];

        _screenBitmap ??= new WriteableBitmap(1920, 1080, 96, 96, PixelFormats.Bgra32, null);

        var rect = new Int32Rect(column * tileSize, row * tileSize, tileFrame.PixelWidth, tileFrame.PixelHeight);
        var stride = tileFrame.PixelWidth * 4;
        var pixels = new byte[stride * tileFrame.PixelHeight];
        tileFrame.CopyPixels(pixels, stride, 0);
        _screenBitmap.WritePixels(rect, pixels, stride, 0);

        ScreenImage.Source = _screenBitmap;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        var position = e.GetPosition(ScreenImage);
        SendInput(InputEventKind.MouseMove, (int)position.X, (int)position.Y, 0);
    }

    private void OnMouseLeftDown(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(ScreenImage);
        SendInput(InputEventKind.MouseLeftDown, (int)position.X, (int)position.Y, 0);
    }

    private void OnMouseLeftUp(object sender, MouseButtonEventArgs e)
    {
        var position = e.GetPosition(ScreenImage);
        SendInput(InputEventKind.MouseLeftUp, (int)position.X, (int)position.Y, 0);
    }

    private void OnKeyDown(object sender, KeyEventArgs e) =>
        SendInput(InputEventKind.KeyDown, 0, 0, (ushort)KeyInterop.VirtualKeyFromKey(e.Key));

    private void OnKeyUp(object sender, KeyEventArgs e) =>
        SendInput(InputEventKind.KeyUp, 0, 0, (ushort)KeyInterop.VirtualKeyFromKey(e.Key));

    private void SendInput(InputEventKind kind, int x, int y, ushort virtualKeyCode)
    {
        var buffer = new byte[11];
        buffer[0] = (byte)kind;
        BitConverter.GetBytes(x).CopyTo(buffer, 1);
        BitConverter.GetBytes(y).CopyTo(buffer, 5);
        BitConverter.GetBytes(virtualKeyCode).CopyTo(buffer, 9);
        _client.SendInputEvent(buffer);
    }

    private void OnWindowClosed(object sender, EventArgs e)
    {
        _heartbeatTimer.Stop();
        _client.SendStopControl();
        _client.Dispose();
    }

    private enum InputEventKind : byte { MouseMove = 0, MouseLeftDown = 1, MouseLeftUp = 2, KeyDown = 3, KeyUp = 4 }
}
```

Note: `OnWindowClosed` sends `StopControl` before disposing the channel — this is the Master-side half of the lesson from Treezeview's stuck-lock bug (send the stop message from the window's own close handler, before teardown). The heartbeat in Task 8 is the independent second line of defense if this message is ever lost anyway.

- [x] **Step 6: Verify the project builds**

Run: `dotnet build src/TreezeSuite.Master`
Expected: succeeds with 0 errors.

- [x] **Step 7: Commit**

```bash
git add src/TreezeSuite.Master/
git commit -m "Add minimal TreezeMaster console and view/control window"
```

---

### Task 16: End-to-end manual validation

No new files — this task runs the whole pipeline and confirms the sub-project's goal is actually met.

- [ ] **Step 1: Run `TreezeSuite.Session` locally**, noting the printed certificate thumbprint and passing your own machine's `TreezeMaster` certificate thumbprint as its argument (generate one ad hoc with `SelfSignedCertificate.Create(...)` in a scratch console app, or temporarily log it from `MainWindow.OnConnectClicked` on first run).
- [ ] **Step 2: Run `TreezeSuite.Master`**, enter `localhost` and the Session's printed thumbprint, click Connect.
- [ ] **Step 3: Confirm the view/control window shows the live desktop**, updating as you change what's on screen (open a window, move it, type somewhere) — this validates capture → diff → encode → transport → decode → render end-to-end.
- [ ] **Step 4: Confirm mouse/keyboard control works**: click and type inside the view/control window, confirm it actually moves the mouse / types on the Session machine.
- [ ] **Step 5: Confirm the input lock engages**: while controlling, try to use the Session machine's own physical keyboard/mouse directly — it should not respond.
- [ ] **Step 6: Confirm the input lock releases on close**: close the `ViewControlWindow`, confirm physical keyboard/mouse work again immediately.
- [ ] **Step 7: Confirm the heartbeat safety net independently**: reconnect, start controlling, then kill the `TreezeSuite.Master` process directly from Task Manager (not a graceful window close) instead of closing the window. Confirm the Session's physical input unlocks on its own within a few seconds (bounded by the heartbeat timeout), with no manual intervention — this is the single most important check in this whole plan, since it's the exact failure mode chased at length in Treezeview.
- [ ] **Step 8: If any of the above fails**, use `superpowers:systematic-debugging` before making changes — don't patch symptoms.
- [ ] **Step 9: Once all checks pass, update the spec's status line** from "Approved for planning" to "Implemented and validated", and commit:

```bash
git add docs/superpowers/specs/2026-09-21-core-architecture-and-remote-screen-design.md
git commit -m "Mark core architecture and remote screen engine as implemented and validated"
```

---

## Self-review notes

- **Spec coverage:** three-process architecture (Tasks 1, 12, 14, 15), TCP+TLS with trusted-key auth (Task 3), tile-diffed JPEG streaming (Tasks 4, 5, 10), input injection (Task 11), input lock with heartbeat auto-unlock (Task 8), minimal console (Task 15), the Interception-license open item (Task 6), and the "live testing on real hardware" testing approach (Task 16) are all covered.
- **Deviations from the spec's exact wording** (GDI capture instead of Windows Graphics Capture; `System.Drawing.Common` instead of an unnamed default image library) are called out explicitly above, with reasoning, rather than silently substituted.
- **Type consistency checked:** `EncodedTile(Row, Column, JpegBytes)` from Task 10 matches the fields `TileMessageCodec` (Task 12) encodes and `ViewControlWindow.DrawTile` (Task 15) decodes. `InputEvent(Kind, X, Y, VirtualKeyCode)` from Task 11 matches `InputEventCodec` (Task 12) and the Master's own encoding in `ViewControlWindow.SendInput` (Task 15). `IInputDeviceController`/`ISystemClock` from Tasks 7–8 match `InputLockManager`'s constructor. `ISessionProcessLauncher` from Task 13 matches `UserSessionProcessLauncher` (Task 14).
