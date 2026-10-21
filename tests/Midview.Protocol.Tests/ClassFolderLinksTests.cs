using System.Diagnostics;
using Midview.Protocol;
using Xunit;

// What counts as a link for the class folder, on both sides: a junction or a
// symbolic link, which could point anywhere, and nothing else. Other reparse
// points (a cloud files placeholder, OneDrive's) are ordinary files and folders.
public class ClassFolderLinksTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"midview-links-{Guid.NewGuid():N}");
    private readonly List<string> _links = new();

    public ClassFolderLinksTests()
    {
        Directory.CreateDirectory(Path.Combine(_root, "alvo"));
    }

    public void Dispose()
    {
        // Links first: deleting one removes the link, never what it points at.
        foreach (var link in _links.Where(Directory.Exists))
        {
            Directory.Delete(link);
        }
        Directory.Delete(_root, recursive: true);
    }

    private string MakeJunction(string name)
    {
        var link = Path.Combine(_root, name);
        using var process = Process.Start(
            new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{Path.Combine(_root, "alvo")}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            })!;
        process.WaitForExit();
        Assert.True(Directory.Exists(link), "mklink /J did not make the junction");
        _links.Add(link);
        return link;
    }

    private string MakeFile(string name)
    {
        var file = Path.Combine(_root, name);
        File.WriteAllText(file, "conteudo");
        return file;
    }

    [Fact]
    public void AJunction_IsALink()
    {
        var junction = MakeJunction("atalho");

        Assert.True(ClassFolderRules.IsLink(junction));
        Assert.True(ClassFolderRules.IsLink(new DirectoryInfo(junction)));
    }

    [Fact]
    public void AnOrdinaryFileOrFolder_IsNotALink()
    {
        var file = MakeFile("normal.txt");
        var folder = Path.Combine(_root, "alvo");

        Assert.False(ClassFolderRules.IsLink(file));
        Assert.False(ClassFolderRules.IsLink(new FileInfo(file)));
        Assert.False(ClassFolderRules.IsLink(folder));
        Assert.False(ClassFolderRules.IsLink(new DirectoryInfo(folder)));
    }

    [Fact]
    public void WhatIsNotThere_IsNotALink()
    {
        Assert.False(ClassFolderRules.IsLink(Path.Combine(_root, "nada")));
        Assert.False(ClassFolderRules.IsLink(Path.Combine(_root, "nada", "mais", "fundo")));
    }

    [Fact]
    public void AReparsePointThatIsNoLink_IsNotALink()
    {
        var file = MakeFile("nuvem.txt");
        NonLinkReparsePoint.Make(file);
        Assert.True((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0, "the test's reparse point");

        Assert.False(ClassFolderRules.IsLink(file));
        Assert.False(ClassFolderRules.IsLink(new FileInfo(file)));
    }
}
