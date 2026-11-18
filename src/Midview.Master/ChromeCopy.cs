using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Midview.Master;

// One file of a saved copy: its name in the copy's folder, its size and its
// SHA-256 (hex, lower case).
public sealed record ChromeCopyFile(string Name, long Size, string Sha256);

// What a saved copy holds: the app and its version, the processor it was
// built for (the phone's primaryCpuAbi; "" for an app with no native code)
// and the oldest Android it runs on, with its files.
public sealed record ChromeCopyManifest(
    string Package, string VersionName, long VersionCode, string Abi, int MinSdk, IReadOnlyList<ChromeCopyFile> Files);

// Chrome and Android System WebView as the Play Store left them on the first
// device, kept on this computer so every next device gets them over the cable
// (PhoneCable.InstallChromeCopy) with no Google account and no Play Store:
// <MasterData.Folder>\chrome-copia\<package>\<abi>-android<minSdk>\, the
// APK files and copia.json (before 0.0.62, <abi>\ alone). Only the newest
// copy of each app for each processor and oldest Android is kept: the Play
// Store sends a newer phone a Chrome for Android 12L and up only, with the
// same version as the tablets' Android 10 one, and that copy must never take
// the tablets' place. A copy that runs on as old an Android and is as new
// makes another one useless, which is then dropped.
// A copy is written in a folder of its own and moved into place only when
// complete, so a cable dropped halfway never leaves half a copy.
public sealed class ChromeCopy
{
    public const string FolderName = "chrome-copia";

    private const string ManifestName = "copia.json";
    // An app copied with no native code ("primaryCpuAbi=null").
    private const string AnyAbiFolder = "qualquer";
    private const string DraftPrefix = ".nova-";
    private const string OldPrefix = ".velha-";

    private static readonly Regex PackageName = new(@"^[A-Za-z0-9_]+(\.[A-Za-z0-9_]+)+$", RegexOptions.Compiled);
    private static readonly Regex AbiName = new(@"^[A-Za-z0-9_-]+$", RegexOptions.Compiled);
    private static readonly Regex FileName = new(@"^[A-Za-z0-9_.-]+\.apk$", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public ChromeCopy(string? folder = null)
    {
        Root = Path.Combine(folder ?? MasterData.Folder, FolderName);
    }

    public string Root { get; }

    // The APK file names a copy can hold: nothing that leaves its folder.
    public static bool IsFileName(string name) => FileName.IsMatch(name) && name != "." && name != "..";

    public static bool IsAbi(string abi) => abi.Length == 0 || AbiName.IsMatch(abi);

    // Every copy saved of the app, newest first. A folder whose copia.json
    // is missing, unreadable or not about this app is left out.
    public IReadOnlyList<(ChromeCopyManifest Manifest, string Folder)> Copies(string package)
    {
        var copies = new List<(ChromeCopyManifest, string)>();
        if (!PackageName.IsMatch(package))
        {
            return copies;
        }
        var folder = Path.Combine(Root, package);
        try
        {
            if (!Directory.Exists(folder))
            {
                return copies;
            }
            foreach (var dir in Directory.GetDirectories(folder))
            {
                if (Read(dir) is { } manifest && manifest.Package == package
                    && (Path.GetFileName(dir) == CopyFolder(manifest.Abi, manifest.MinSdk)
                        || Path.GetFileName(dir) == AbiFolder(manifest.Abi)))
                {
                    copies.Add((manifest, dir));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
        return copies.OrderByDescending(c => c.Item1.VersionCode).ToList();
    }

    // Whether a copy for that processor is already kept that is as new as
    // versionCode (or newer) and runs on an Android as old as minSdk (or older).
    public bool Holds(string package, string abi, int minSdk, long versionCode) =>
        Copies(package).Any(c => Covers(c.Manifest, abi, minSdk, versionCode));

    // An empty folder to pull a new copy into, beside the copies (the same
    // disk, so Keep only renames it).
    public string NewDraft()
    {
        var draft = Path.Combine(Root, DraftPrefix + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(draft);
        return draft;
    }

    public void Discard(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Left for CleanDrafts.
        }
    }

    // Drafts and replaced copies left by a Midview closed halfway.
    public void CleanDrafts()
    {
        try
        {
            if (!Directory.Exists(Root))
            {
                return;
            }
            foreach (var dir in Directory.GetDirectories(Root))
            {
                var name = Path.GetFileName(dir);
                if (name.StartsWith(DraftPrefix, StringComparison.Ordinal) || name.StartsWith(OldPrefix, StringComparison.Ordinal))
                {
                    Discard(dir);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    // The files pulled into draft become the copy of the app for that
    // processor and Android, in place of the copies it makes useless; the
    // draft is gone either way.
    // False when they don't (a copy as new is kept already, a name that is
    // not an app's, the disk refused).
    public bool Keep(string draft, string package, string versionName, long versionCode, string abi, int minSdk, IReadOnlyList<string> files)
    {
        try
        {
            if (!PackageName.IsMatch(package) || !IsAbi(abi) || files.Count == 0 || !files.All(IsFileName)
                || Holds(package, abi, minSdk, versionCode))
            {
                Discard(draft);
                return false;
            }
            var listed = files.Select(name =>
            {
                var path = Path.Combine(draft, name);
                return new ChromeCopyFile(name, new FileInfo(path).Length, Hash(path));
            }).ToList();
            var manifest = new ChromeCopyManifest(package, versionName, versionCode, abi, minSdk, listed);
            File.WriteAllText(Path.Combine(draft, ManifestName), JsonSerializer.Serialize(manifest, Json));

            var useless = Copies(package).Where(c => Covers(manifest, c.Manifest.Abi, c.Manifest.MinSdk, c.Manifest.VersionCode))
                .Select(c => c.Folder).ToList();
            var target = Path.Combine(Root, package, CopyFolder(abi, minSdk));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string? old = null;
            if (Directory.Exists(target))
            {
                old = Path.Combine(Root, OldPrefix + Guid.NewGuid().ToString("N"));
                Directory.Move(target, old);
            }
            Directory.Move(draft, target);
            if (old is not null)
            {
                Discard(old);
            }
            foreach (var folder in useless.Where(f => f != target))
            {
                Discard(folder);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Discard(draft);
            return false;
        }
    }

    // Whether the copy's files are still the ones saved: each there, with
    // its size and SHA-256.
    public bool Intact(ChromeCopyManifest manifest, string folder)
    {
        try
        {
            return manifest.Files.Count > 0 && manifest.Files.All(file =>
            {
                var path = Path.Combine(folder, file.Name);
                return IsFileName(file.Name) && File.Exists(path) && new FileInfo(path).Length == file.Size
                       && string.Equals(Hash(path), file.Sha256, StringComparison.OrdinalIgnoreCase);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // A damaged copy is dropped: the next device updated by the Play
    // Store saves a new one.
    public void Remove(string folder) => Discard(folder);

    private static string AbiFolder(string abi) => abi.Length == 0 ? AnyAbiFolder : abi;

    private static string CopyFolder(string abi, int minSdk) =>
        AbiFolder(abi) + "-android" + minSdk.ToString(System.Globalization.CultureInfo.InvariantCulture);

    // Whether copy is as good as one of that processor, Android and version:
    // as new, and running on as old an Android.
    private static bool Covers(ChromeCopyManifest copy, string abi, int minSdk, long versionCode) =>
        copy.Abi == abi && copy.MinSdk <= minSdk && copy.VersionCode >= versionCode;

    private static ChromeCopyManifest? Read(string folder)
    {
        try
        {
            var manifest = JsonSerializer.Deserialize<ChromeCopyManifest>(File.ReadAllText(Path.Combine(folder, ManifestName)));
            return manifest is { Package: not null, VersionName: not null, Abi: not null, Files: not null }
                   && IsAbi(manifest.Abi) && manifest.Files.Count > 0
                   && manifest.Files.All(f => f is { Name: not null, Sha256: not null } && IsFileName(f.Name))
                ? manifest
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
