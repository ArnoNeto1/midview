using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Midview.Master;

// One file the Midview keeps out of Google's Platform-Tools zip: its name in
// the zip's "platform-tools" folder, its size and its SHA-256 (lower case).
public sealed record AdbToolFile(string Name, long Size, string Sha256);

// The Platform-Tools zip the Midview trusts: where it is, its size and its
// SHA-256, and the files taken out of it.
public sealed record AdbToolPin(Uri Url, long Size, string Sha256, IReadOnlyList<AdbToolFile> Files)
{
    // Android SDK Platform-Tools 37.0.1 for Windows, the stable one in
    // Google's own list (https://dl.google.com/android/repository/repository2-3.xml),
    // which gives the zip's size and its SHA-1,
    // e03e78b1d80b396f1c3358e31251cb31740e1110. The SHA-256s were computed
    // from that zip. adb.exe needs only the two DLLs beside it.
    public static AdbToolPin Google { get; } = new(
        new Uri("https://dl.google.com/android/repository/platform-tools_r37.0.1-win.zip"),
        8044989,
        "45f4d63113e895ebde0c90f194099a4676b6ac653bd28d54314a9e022bbc1a99",
        new AdbToolFile[]
        {
            new("adb.exe", 8273560, "b4a6b455702684652cccf7b46258b29e653538904359a58fd4931cf3ef286b3f"),
            new("AdbWinApi.dll", 108184, "c1d653030b4bde65d3e07e4d0b0979e17be56df1436cdd15528630f27808050d"),
            new("AdbWinUsbApi.dll", 73368, "0710e894d9b40f71a670c13c694079d564c92c1279da382cfe4850983aaebe1b"),
        });
}

// Google's adb, the phone-cable tool. The Midview doesn't ship it (its
// license doesn't let others pass it on): the first time a phone is
// prepared, the teacher asks for it and it is downloaded from Google, the
// zip checked against AdbToolPin, and only the pinned files taken out into
// a folder of the teacher's own (MasterData.Folder\adb), where it stays.
// Every file is checked again each time adb is about to be used; an adb an
// older Midview installed beside the program is used only when it is the
// same, file by file.
public sealed class AdbTool
{
    // The page of Google's Platform-Tools, with its terms.
    public const string TermsUrl = "https://developer.android.com/tools/releases/platform-tools";

    // How long one read of the download may wait for data.
    public static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(60);

    // In the teacher's words, after "Não deu para baixar a ferramenta do Google: ".
    public const string Unreachable = "sem internet neste computador, ou o Google não respondeu. Confira a internet e tente de novo.";
    public const string Blocked = "o download foi recusado. A rede da escola pode estar bloqueando dl.google.com.";
    public const string Unfinished = "o download não terminou. Confira a internet e tente de novo.";
    public const string Mismatch = "o arquivo baixado não confere com o publicado pelo Google, e não foi usado. Tente de novo; se repetir, a rede da escola pode estar trocando o arquivo.";

    private const string ZipFolder = "platform-tools/";

    private readonly HttpClient _http;
    private readonly string _folder;
    private readonly string? _programFolder;
    private readonly AdbToolPin _pin;

    // folder: where it is kept. programFolder: where an older Midview put
    // it, beside the program (null: none looked at).
    public AdbTool(HttpClient http, string folder, string? programFolder, AdbToolPin pin)
    {
        _http = http;
        _folder = folder;
        _programFolder = programFolder;
        _pin = pin;
    }

    public static string DefaultFolder => Path.Combine(MasterData.Folder, "adb");

    // Where the Midview up to 0.0.51 installed it.
    public static string ProgramFolder => Path.Combine(AppContext.BaseDirectory, "adb");

    public static AdbTool ForThisComputer(HttpClient http) => new(http, DefaultFolder, ProgramFolder, AdbToolPin.Google);

    public string Folder => _folder;

    // The adb.exe to run: the one kept, else the one an older Midview left
    // beside the program, each only when all its files are the pinned ones.
    // Null: it has to be downloaded.
    public string? Find()
    {
        foreach (var folder in new[] { _folder, _programFolder })
        {
            if (folder is not null && Matches(folder))
            {
                return Path.Combine(folder, _pin.Files[0].Name);
            }
        }
        return null;
    }

    // Downloads the zip from Google, checks its size and SHA-256 and keeps
    // the pinned files; returns adb.exe. InvalidDataException, in the
    // teacher's words (Unreachable, Blocked, Unfinished, Mismatch), when
    // nothing could be kept; IOException or UnauthorizedAccessException when
    // the folder can't be written. percent: 0 to 100 while downloading.
    public async Task<string> DownloadAsync(IProgress<int>? percent, CancellationToken cancellation, TimeSpan? readTimeout = null)
    {
        Directory.CreateDirectory(_folder);
        var zip = Path.Combine(_folder, "platform-tools.zip.part");
        try
        {
            await DownloadZipAsync(zip, percent, readTimeout ?? ReadTimeout, cancellation);
            // A server left running from this folder would hold the files.
            AdbProcess.StopLeftovers(_folder, TimeSpan.FromSeconds(5));
            Extract(zip);
        }
        finally
        {
            TryDelete(zip);
        }
        if (!Matches(_folder))
        {
            throw new InvalidDataException(Mismatch);
        }
        return Path.Combine(_folder, _pin.Files[0].Name);
    }

    private async Task DownloadZipAsync(string zip, IProgress<int>? percent, TimeSpan readTimeout, CancellationToken cancellation)
    {
        HttpResponseMessage response;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _pin.Url);
            if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            {
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("Midview", TeacherUpdate.CurrentVersion.ToString()));
            }
            response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidDataException(Unreachable, ex);
        }
        catch (TaskCanceledException ex) when (!cancellation.IsCancellationRequested)
        {
            throw new InvalidDataException(Unreachable, ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidDataException(Blocked);
            }
            if (response.Content.Headers.ContentLength is { } length && length != _pin.Size)
            {
                throw new InvalidDataException(Mismatch);
            }
            try
            {
                await CopyCheckedAsync(response, zip, percent, readTimeout, cancellation);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException)
            {
                throw new InvalidDataException(Unfinished, ex);
            }
            catch (OperationCanceledException ex) when (!cancellation.IsCancellationRequested)
            {
                // A read that waited longer than readTimeout.
                throw new InvalidDataException(Unfinished, ex);
            }
        }
    }

    private async Task CopyCheckedAsync(
        HttpResponseMessage response, string zip, IProgress<int>? percent, TimeSpan readTimeout, CancellationToken cancellation)
    {
        await using var source = await response.Content.ReadAsStreamAsync(cancellation);
        await using var target = new FileStream(zip, FileMode.Create, FileAccess.Write, FileShare.None);
        using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stalled = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var buffer = new byte[81920];
        long total = 0;
        var reported = -1;
        while (true)
        {
            stalled.CancelAfter(readTimeout);
            var read = await source.ReadAsync(buffer, stalled.Token);
            if (read == 0)
            {
                break;
            }
            total += read;
            if (total > _pin.Size)
            {
                throw new InvalidDataException(Mismatch);
            }
            sha256.AppendData(buffer, 0, read);
            await target.WriteAsync(buffer.AsMemory(0, read), cancellation);
            var now = (int)(total * 100 / _pin.Size);
            if (now != reported)
            {
                reported = now;
                percent?.Report(now);
            }
        }
        if (total != _pin.Size)
        {
            throw new InvalidDataException(total < _pin.Size ? Unfinished : Mismatch);
        }
        if (!string.Equals(Convert.ToHexString(sha256.GetHashAndReset()), _pin.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(Mismatch);
        }
    }

    // Only the pinned files, each into a ".part" first and given its name
    // only once it is the pinned one.
    private void Extract(string zip)
    {
        using var archive = ZipFile.OpenRead(zip);
        foreach (var file in _pin.Files)
        {
            var entry = archive.GetEntry(ZipFolder + file.Name);
            if (entry is null || entry.Length != file.Size)
            {
                throw new InvalidDataException(Mismatch);
            }
            var kept = Path.Combine(_folder, file.Name);
            var partial = kept + ".part";
            try
            {
                entry.ExtractToFile(partial, overwrite: true);
                if (!FileMatches(partial, file))
                {
                    throw new InvalidDataException(Mismatch);
                }
                File.Move(partial, kept, overwrite: true);
            }
            finally
            {
                TryDelete(partial);
            }
        }
    }

    private bool Matches(string folder)
    {
        try
        {
            return _pin.Files.All(file => FileMatches(Path.Combine(folder, file.Name), file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool FileMatches(string path, AdbToolFile file)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != file.Size)
        {
            return false;
        }
        using var stream = File.OpenRead(path);
        return string.Equals(Convert.ToHexString(SHA256.HashData(stream)), file.Sha256, StringComparison.OrdinalIgnoreCase);
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
