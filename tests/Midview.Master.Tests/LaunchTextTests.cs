using Midview.Master;
using Xunit;

public class LaunchTextTests
{
    [Fact]
    public void Combine_OneCommand_IsItsOwnAnswer()
    {
        var result = LaunchText.Combine("ALUNO-01", new[] { ("powershell", true, "aberto") });

        Assert.Equal(new LaunchResult("ALUNO-01", true, "aberto"), result);
    }

    [Fact]
    public void Combine_SeveralCommands_SaysEach_AndFailsIfOneFails()
    {
        var result = LaunchText.Combine("ALUNO-01", new[]
        {
            ("powershell", true, "aberto"),
            ("notepadd", false, "programa não encontrado: notepadd"),
        });

        Assert.Equal(new LaunchResult("ALUNO-01", false, "powershell: aberto; notepadd: programa não encontrado: notepadd"), result);
    }

    [Fact]
    public void Summary_OneMachine()
    {
        Assert.Equal("YouTube: aberto em ALUNO-03.",
            LaunchText.Summary("YouTube", new[] { new LaunchResult("ALUNO-03", true, "aberto") }));
        Assert.Equal("YouTube: não abriu em ALUNO-03 (Offline).",
            LaunchText.Summary("YouTube", new[] { new LaunchResult("ALUNO-03", false, "Offline") }));
    }

    [Fact]
    public void Summary_EveryMachine()
    {
        var results = Enumerable.Range(1, 25).Select(n => new LaunchResult($"ALUNO-{n:00}", true, "aberto")).ToList();

        Assert.Equal("YouTube: aberto nas 25 máquinas.", LaunchText.Summary("YouTube", results));
    }

    [Fact]
    public void Summary_SomeFailed_NamesThem()
    {
        var results = new[]
        {
            new LaunchResult("ALUNO-01", true, "aberto"),
            new LaunchResult("ALUNO-07", false, "Offline"),
            new LaunchResult("ALUNO-12", false, "Midview antigo nesta máquina: atualize-a para usar isto"),
        };

        Assert.Equal(
            "YouTube: aberto em 1 de 3. Não abriu em ALUNO-07 (Offline); ALUNO-12 (Midview antigo nesta máquina: atualize-a para usar isto).",
            LaunchText.Summary("YouTube", results));
    }

    [Fact]
    public void Summary_ManyFailed_NamesTheFirstFive()
    {
        var results = Enumerable.Range(1, 8).Select(n => new LaunchResult($"ALUNO-{n:00}", false, "Offline")).ToList();

        Assert.EndsWith("ALUNO-05 (Offline) e mais 3.", LaunchText.Summary("YouTube", results));
        Assert.StartsWith("YouTube: aberto em 0 de 8.", LaunchText.Summary("YouTube", results));
    }

    [Fact]
    public void Lines_OnePerMachine()
    {
        var lines = LaunchText.Lines(new[] { new LaunchResult("ALUNO-01", true, "aberto"), new LaunchResult("ALUNO-02", false, "Offline") });

        Assert.Equal("✓ ALUNO-01: aberto\n✗ ALUNO-02: Offline", lines);
    }
}
