using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Midview.Protocol;

namespace Midview.Master;

// The phone agent's APK that "Atualizar celulares" sends (as an
// IUpdatePayload, see PhoneUpdate): the one published on GitHub with this
// Master's version, kept in MasterData.Folder\celular so that the next
// update does not download it again; or a local file (FromFile, for tests on
// a real phone). The phone checks it again itself (package, signature and a
// higher version) before installing.
public sealed class PhoneAgentApk : IUpdatePayload, IDisposable
{
    public const string AssetName = "MidviewAgent.apk";
    public const string ReleaseUrlFormat = "https://api.github.com/repos/ArnoNeto1/midview/releases/tags/v{0}";

    // The connection dropped or stalled in the middle of the download.
    public const string DownloadUnfinished = "o download do app do celular não terminou (confira a internet deste PC)";

    // How long one read of the download may wait for data: the client from
    // TeacherUpdate never times out, so a stalled connection would hang.
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(60);

    private static readonly Regex Sha256Digest = new("^sha256:([0-9a-fA-F]{64})$", RegexOptions.CultureInvariant);

    private PhoneAgentApk(string path, long size, byte[] sha256)
    {
        ZipPath = path;
        ZipSize = size;
        Sha256 = sha256;
    }

    public string ZipPath { get; }

    public long ZipSize { get; }

    public byte[] Sha256 { get; }

    // Upper case, as the phone reports its own APK's hash.
    public string Sha256Hex => Convert.ToHexString(Sha256);

    public static string DefaultFolder => Path.Combine(MasterData.Folder, "celular");

    public static string KeptPath(string folder, Version version) => Path.Combine(folder, $"MidviewAgent-{version}.apk");

    // Already downloaded (and checked when it was).
    public static bool IsKept(string folder, Version version) => File.Exists(KeptPath(folder, version));

    // For a phone the APK is both the "fingerprint" and the "zip".
    public UpdateOffer Offer(byte[] attemptId) => new(attemptId, Sha256, ZipSize, Sha256, ZipSize);

    // The file stays: it is the kept APK, or the teacher's own.
    public void Dispose()
    {
    }

    // InvalidDataException, in the teacher's words, for a file that can't be sent.
    public static PhoneAgentApk FromFile(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length == 0)
        {
            throw new InvalidDataException("o arquivo do app do celular está vazio");
        }
        if (stream.Length > PhoneUpdate.MaxApkSize)
        {
            throw new InvalidDataException("o app do celular é grande demais (até 64 MB)");
        }
        return new PhoneAgentApk(path, stream.Length, SHA256.HashData(stream));
    }

    // The APK of that version on GitHub: asked of GitHub's API (only an asset
    // on github.com over HTTPS, with its SHA-256 published, and no bigger
    // than PhoneUpdate.MaxApkSize), reused from folder when the one kept
    // there has that SHA-256, downloaded and checked otherwise. With GitHub
    // out of reach, the one kept there (checked when it was saved) is used.
    // InvalidDataException, in the teacher's words, for anything else.
    // percent: 0 to 100 while downloading. readTimeout: ReadTimeout unless a
    // test shortens it.
    public static async Task<PhoneAgentApk> ForVersionAsync(
        Version version, HttpClient client, string folder, CancellationToken cancellation, IProgress<int>? percent = null,
        TimeSpan? readTimeout = null)
    {
        var kept = KeptPath(folder, version);
        Asset asset;
        try
        {
            asset = await FetchAssetAsync(version, client, cancellation);
        }
        catch (HttpRequestException) when (File.Exists(kept))
        {
            return FromFile(kept);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidDataException("não foi possível falar com o GitHub para baixar o app do celular (confira a internet deste PC)", ex);
        }
        catch (TaskCanceledException ex) when (!cancellation.IsCancellationRequested)
        {
            if (File.Exists(kept))
            {
                return FromFile(kept);
            }
            throw new InvalidDataException("não foi possível falar com o GitHub para baixar o app do celular (confira a internet deste PC)", ex);
        }

        if (File.Exists(kept))
        {
            try
            {
                var existing = FromFile(kept);
                if (string.Equals(existing.Sha256Hex, asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    return existing;
                }
            }
            catch (InvalidDataException)
            {
                // Downloaded again below.
            }
        }

        try
        {
            await DownloadAsync(client, asset, folder, kept, percent, readTimeout ?? ReadTimeout, cancellation);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException)
        {
            throw new InvalidDataException(DownloadUnfinished, ex);
        }
        catch (OperationCanceledException ex) when (!cancellation.IsCancellationRequested)
        {
            // A read that waited longer than readTimeout.
            throw new InvalidDataException(DownloadUnfinished, ex);
        }
        return FromFile(kept);
    }

    private sealed record Asset(Uri Url, long Size, string Sha256);

    private static async Task<Asset> FetchAssetAsync(Version version, HttpClient client, CancellationToken cancellation)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, string.Format(System.Globalization.CultureInfo.InvariantCulture, ReleaseUrlFormat, version));
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        NameTheProgram(client, request);
        using var response = await client.SendAsync(request, cancellation);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new InvalidDataException($"a versão {version} do Midview ainda não foi publicada no GitHub");
        }
        response.EnsureSuccessStatusCode();
        return ParseAsset(await response.Content.ReadAsStringAsync(cancellation), version);
    }

    private static Asset ParseAsset(string json, Version version)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            foreach (var asset in document.RootElement.GetProperty("assets").EnumerateArray())
            {
                if (asset.GetProperty("name").GetString() != AssetName)
                {
                    continue;
                }

                var url = asset.GetProperty("browser_download_url").GetString();
                if (!Uri.TryCreate(url, UriKind.Absolute, out var apkUrl) || apkUrl.Scheme != Uri.UriSchemeHttps
                    || !string.Equals(apkUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"o app do celular da versão {version} não está no GitHub");
                }
                var size = asset.GetProperty("size").GetInt64();
                if (size > PhoneUpdate.MaxApkSize)
                {
                    throw new InvalidDataException($"o app do celular da versão {version} é grande demais (até 64 MB)");
                }
                if (size <= 0)
                {
                    throw new InvalidDataException($"o app do celular da versão {version} tem um tamanho inválido");
                }
                var digest = asset.TryGetProperty("digest", out var value) && value.ValueKind == JsonValueKind.String
                    ? Sha256Digest.Match(value.GetString()!)
                    : Match.Empty;
                if (!digest.Success)
                {
                    throw new InvalidDataException($"o GitHub não informou a soma de verificação do app do celular da versão {version}");
                }
                return new Asset(apkUrl, size, digest.Groups[1].Value.ToLowerInvariant());
            }
            throw new InvalidDataException($"a versão {version} no GitHub não tem o app do celular ({AssetName})");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new InvalidDataException("a resposta do GitHub não pôde ser lida", ex);
        }
    }

    // Into a ".part" first: only an APK of the published size and SHA-256
    // ever gets the kept name.
    private static async Task DownloadAsync(
        HttpClient client, Asset asset, string folder, string kept, IProgress<int>? percent, TimeSpan readTimeout,
        CancellationToken cancellation)
    {
        Directory.CreateDirectory(folder);
        var partial = kept + ".part";
        try
        {
            using (var request = new HttpRequestMessage(HttpMethod.Get, asset.Url))
            {
                NameTheProgram(client, request);
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
                response.EnsureSuccessStatusCode();
                await using var source = await response.Content.ReadAsStreamAsync(cancellation);
                await using var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None);
                using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long total = 0;
                var reported = -1;
                using var stalled = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                while (true)
                {
                    stalled.CancelAfter(readTimeout);
                    var read = await source.ReadAsync(buffer, stalled.Token);
                    if (read == 0)
                    {
                        break;
                    }
                    total += read;
                    if (total > asset.Size)
                    {
                        throw new InvalidDataException("o app do celular baixado é maior que o publicado no GitHub");
                    }
                    sha256.AppendData(buffer, 0, read);
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellation);
                    var now = (int)(total * 100 / asset.Size);
                    if (now != reported)
                    {
                        reported = now;
                        percent?.Report(now);
                    }
                }
                if (total != asset.Size)
                {
                    throw new InvalidDataException("o download do app do celular veio incompleto");
                }
                if (!string.Equals(Convert.ToHexString(sha256.GetHashAndReset()), asset.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("o app do celular baixado não confere com o publicado no GitHub");
                }
            }
            File.Move(partial, kept, overwrite: true);
        }
        catch
        {
            TryDelete(partial);
            throw;
        }
    }

    // GitHub refuses requests that don't name their program (a client from
    // TeacherUpdate.CreateClient already does).
    private static void NameTheProgram(HttpClient client, HttpRequestMessage request)
    {
        if (client.DefaultRequestHeaders.UserAgent.Count == 0)
        {
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Midview", TeacherUpdate.CurrentVersion.ToString()));
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left; the next download overwrites it.
        }
    }
}

// Where the update window gets the APK: GitHub (the Master's own version,
// kept in PhoneAgentApk.DefaultFolder) or, for a test, a local file.
public sealed record PhoneApkSource(
    string Version, Func<bool> IsKept, Func<IProgress<int>?, CancellationToken, Task<PhoneAgentApk>> GetAsync)
{
    public static PhoneApkSource GitHub(Version version, HttpClient client, string folder) => new(
        version.ToString(),
        () => PhoneAgentApk.IsKept(folder, version),
        (percent, cancellation) => PhoneAgentApk.ForVersionAsync(version, client, folder, cancellation, percent));

    public static PhoneApkSource File(string path, string version) => new(
        version, () => true, (_, _) => Task.FromResult(PhoneAgentApk.FromFile(path)));
}
