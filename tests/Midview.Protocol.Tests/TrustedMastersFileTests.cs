using Midview.Protocol;
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

    [Fact]
    public void KeepOnly_LeavesThatTeacherAlone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "[\"AAAA\", \"bbbb\", \"CCCC\"]");

            Assert.Equal(2, TrustedMastersFile.KeepOnly(path, " bbbb"));
            Assert.Equal(new[] { "BBBB" }, TrustedMastersFile.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void KeepOnly_WithNoOtherTeacher_LeavesTheFileAsItIs()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "[\"bbbb\"]");
            var written = File.GetLastWriteTimeUtc(path);

            Assert.Equal(0, TrustedMastersFile.KeepOnly(path, "BBBB"));
            Assert.Equal("[\"bbbb\"]", File.ReadAllText(path));
            Assert.Equal(written, File.GetLastWriteTimeUtc(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // Forgotten a moment before by another teacher: it must not come back
    // as the only one.
    [Fact]
    public void KeepOnly_ATeacherNoLongerOnTheList_ChangesNothing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "[\"AAAA\"]");

            Assert.Null(TrustedMastersFile.KeepOnly(path, "BBBB"));
            Assert.Equal("[\"AAAA\"]", File.ReadAllText(path));
            Assert.Null(TrustedMastersFile.KeepOnly(path + ".none", "BBBB"));
            Assert.False(File.Exists(path + ".none"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void KeepOnly_AListThatCantBeRead_IsLeftAlone()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(path, "isto não é json");

            Assert.Throws<InvalidDataException>(() => TrustedMastersFile.KeepOnly(path, "CCCC"));
            Assert.Equal("isto não é json", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    // TrustStore reads the file again when it changes, so the teachers a
    // machine forgot are no longer trusted by the Agent nor the Session.
    [Fact]
    public void KeepOnly_IsSeenByATrustStoreReadingTheFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"midview-trusted-{Guid.NewGuid()}.json");
        try
        {
            TrustedMastersFile.Add(path, "AAAA");
            TrustedMastersFile.Add(path, "BBBB");
            var store = TrustStore.FromFile(path);
            Assert.True(store.IsTrusted("AAAA"));

            TrustedMastersFile.KeepOnly(path, "BBBB");

            Assert.False(store.IsTrusted("AAAA"));
            Assert.True(store.IsTrusted("BBBB"));
            Assert.Equal(1, store.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
