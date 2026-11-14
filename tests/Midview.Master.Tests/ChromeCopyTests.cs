using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Midview.Master;
using Xunit;

namespace Midview.Master.Tests;

// The copy of Chrome and WebView kept on this computer: written whole or not
// at all, checked by SHA-256, and only the newest of each app and processor.
public sealed class ChromeCopyTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "midview-chrome-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    // A copy of the app with these files (each holding its own name and the version) kept in store.
    internal static bool Store(ChromeCopy store, string package, long versionCode, string abi = "armeabi-v7a",
        int minSdk = 29, string versionName = "157.0.8103.51", params string[] files)
    {
        files = files.Length == 0 ? new[] { "base.apk" } : files;
        var draft = store.NewDraft();
        foreach (var name in files)
        {
            File.WriteAllText(Path.Combine(draft, name), name + " " + versionCode);
        }
        return store.Keep(draft, package, versionName, versionCode, abi, minSdk, files);
    }

    [Fact]
    public void A_kept_copy_lists_its_files_with_size_and_sha256()
    {
        var store = new ChromeCopy(_folder);

        Assert.True(Store(store, PhoneCable.ChromePackage, 810305100, files: new[] { "base.apk", "split_chrome.apk" }));

        var (manifest, folder) = Assert.Single(store.Copies(PhoneCable.ChromePackage));
        Assert.Equal(Path.Combine(_folder, "chrome-copia", PhoneCable.ChromePackage, "armeabi-v7a"), folder);
        Assert.Equal(("157.0.8103.51", 810305100L, "armeabi-v7a", 29), (manifest.VersionName, manifest.VersionCode, manifest.Abi, manifest.MinSdk));
        Assert.Equal(new[] { "base.apk", "split_chrome.apk" }, manifest.Files.Select(f => f.Name));
        var bytes = Encoding.UTF8.GetBytes("base.apk 810305100");
        Assert.Equal(bytes.Length, manifest.Files[0].Size);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), manifest.Files[0].Sha256);
        Assert.True(store.Intact(manifest, folder));
        // Nothing left beside the copies.
        Assert.Equal(new[] { PhoneCable.ChromePackage }, Directory.GetDirectories(store.Root).Select(Path.GetFileName));
    }

    [Fact]
    public void A_changed_or_missing_file_is_not_intact()
    {
        var store = new ChromeCopy(_folder);
        Store(store, PhoneCable.WebViewPackage, 810304001, files: new[] { "base.apk", "split_a.apk" });
        var (manifest, folder) = store.Copies(PhoneCable.WebViewPackage)[0];

        File.WriteAllText(Path.Combine(folder, "base.apk"), "base.apk 810304002");
        Assert.False(store.Intact(manifest, folder));

        File.WriteAllText(Path.Combine(folder, "base.apk"), "base.apk 810304001");
        Assert.True(store.Intact(manifest, folder));
        File.Delete(Path.Combine(folder, "split_a.apk"));
        Assert.False(store.Intact(manifest, folder));
    }

    [Fact]
    public void Only_the_newest_copy_of_each_app_and_processor_is_kept()
    {
        var store = new ChromeCopy(_folder);
        Store(store, PhoneCable.ChromePackage, 700000000, versionName: "140.0.1.2");

        Assert.True(Store(store, PhoneCable.ChromePackage, 810305100));
        Assert.False(Store(store, PhoneCable.ChromePackage, 810305100));
        Assert.False(Store(store, PhoneCable.ChromePackage, 700000000));
        Assert.True(Store(store, PhoneCable.ChromePackage, 810305000, abi: "arm64-v8a"));

        var copies = store.Copies(PhoneCable.ChromePackage);
        Assert.Equal(new[] { (810305100L, "armeabi-v7a"), (810305000L, "arm64-v8a") }, copies.Select(c => (c.Manifest.VersionCode, c.Manifest.Abi)));
        Assert.True(store.Holds(PhoneCable.ChromePackage, "armeabi-v7a", 810305100));
        Assert.False(store.Holds(PhoneCable.ChromePackage, "armeabi-v7a", 810305101));
        Assert.False(store.Holds(PhoneCable.ChromePackage, "x86_64", 1));
        // The refused drafts and the replaced copy are gone.
        Assert.Equal(new[] { PhoneCable.ChromePackage }, Directory.GetDirectories(store.Root).Select(Path.GetFileName));
        Assert.True(store.Intact(copies[0].Manifest, copies[0].Folder));
    }

    [Fact]
    public void An_app_with_no_native_code_is_kept_for_any_processor()
    {
        var store = new ChromeCopy(_folder);

        Assert.True(Store(store, PhoneCable.WebViewPackage, 810304001, abi: ""));

        var (manifest, folder) = Assert.Single(store.Copies(PhoneCable.WebViewPackage));
        Assert.Equal("", manifest.Abi);
        Assert.Equal("qualquer", Path.GetFileName(folder));
    }

    [Fact]
    public void A_half_pulled_draft_is_never_a_copy_and_is_cleaned_later()
    {
        var store = new ChromeCopy(_folder);
        var draft = store.NewDraft();
        File.WriteAllText(Path.Combine(draft, "base.apk"), "half");

        Assert.Empty(store.Copies(PhoneCable.ChromePackage));
        store.CleanDrafts();
        Assert.False(Directory.Exists(draft));
    }

    [Theory]
    [InlineData("../base.apk")]
    [InlineData("..\\base.apk")]
    [InlineData("base.txt")]
    public void A_file_name_that_leaves_the_copy_is_refused(string name)
    {
        var store = new ChromeCopy(_folder);
        var draft = store.NewDraft();

        Assert.False(store.Keep(draft, PhoneCable.ChromePackage, "157.0.8103.51", 1, "armeabi-v7a", 29, new[] { name }));
        Assert.False(Directory.Exists(draft));
        Assert.Empty(store.Copies(PhoneCable.ChromePackage));
    }

    [Fact]
    public void A_manifest_naming_a_file_outside_its_folder_is_ignored()
    {
        var store = new ChromeCopy(_folder);
        Store(store, PhoneCable.ChromePackage, 810305100);
        var folder = store.Copies(PhoneCable.ChromePackage)[0].Folder;
        var manifest = Path.Combine(folder, "copia.json");
        File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("\"base.apk\"", "\"..\\\\..\\\\base.apk\""));

        Assert.Empty(store.Copies(PhoneCable.ChromePackage));
    }

    [Fact]
    public void An_empty_store_has_no_copies()
    {
        var store = new ChromeCopy(_folder);

        Assert.Empty(store.Copies(PhoneCable.ChromePackage));
        Assert.Null(store.Find(PhoneCable.ChromePackage, "armeabi-v7a"));
        Assert.False(store.Holds(PhoneCable.ChromePackage, "armeabi-v7a", 0));
        store.CleanDrafts();
    }
}
