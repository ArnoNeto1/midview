using Midview.Master;
using Midview.Protocol;
using Xunit;

public class CollectedFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"midview-recolhidos-{Guid.NewGuid():N}");

    public void Dispose()
    {
        foreach (var directory in new[] { _root, _root + "-lixeira" })
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void EveryCollection_GoesToTheRecycleBin()
    {
        Directory.CreateDirectory(Path.Combine(_root, "2026-10-05 14-00-00", "ALUNO-25"));
        File.WriteAllText(Path.Combine(_root, "2026-10-05 14-00-00", "ALUNO-25", "trabalho.docx"), "x");
        Directory.CreateDirectory(Path.Combine(_root, "2026-10-05 15-00-00"));
        var bin = new MovingRecycleBin(_root + "-lixeira");

        var (recycled, problems) = CollectedFiles.RecycleAll(_root, bin);

        Assert.Equal(2, recycled);
        Assert.Empty(problems);
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public void NothingCollectedYet_IsNothingToDo()
    {
        var (recycled, problems) = CollectedFiles.RecycleAll(_root, new MovingRecycleBin(_root + "-lixeira"));

        Assert.Equal(0, recycled);
        Assert.Empty(problems);
    }

    [Fact]
    public void WhatCantGo_IsNamed()
    {
        Directory.CreateDirectory(Path.Combine(_root, "aberta"));
        var bin = new MovingRecycleBin(_root + "-lixeira", refused: "aberta");

        var (recycled, problems) = CollectedFiles.RecycleAll(_root, bin);

        Assert.Equal(0, recycled);
        Assert.Equal(new[] { "aberta (o arquivo está em uso por outro programa)" }, problems);
    }

    private sealed class MovingRecycleBin : IRecycleBin
    {
        private readonly string _bin;
        private readonly string? _refused;

        public MovingRecycleBin(string bin, string? refused = null)
        {
            _bin = bin;
            _refused = refused;
            Directory.CreateDirectory(bin);
        }

        public void Recycle(string path)
        {
            if (Path.GetFileName(path) == _refused)
            {
                throw new IOException("in use", unchecked((int)0x80070020));
            }
            Directory.Move(path, Path.Combine(_bin, Path.GetFileName(path)));
        }
    }
}
