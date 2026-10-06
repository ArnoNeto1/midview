using Midview.Master;
using Xunit;

public class OpenOnceTests
{
    private sealed class Item;

    [Fact]
    public void TheSameActionAndMachines_FindTheOneOpen_InAnyOrderOrCase()
    {
        var open = new OpenOnce<Item>();
        var window = new Item();
        open.Add(OpenOnce<Item>.Key("mensagem", new[] { "ALUNO-02", "ALUNO-01" }), window);

        Assert.True(open.TryGet(OpenOnce<Item>.Key("mensagem", new[] { "aluno-01", "aluno-02" }), out var found));
        Assert.Same(window, found);
    }

    [Fact]
    public void AnotherActionOrOtherMachines_AreAnotherWindow()
    {
        var open = new OpenOnce<Item>();
        open.Add(OpenOnce<Item>.Key("mensagem", new[] { "ALUNO-01", "ALUNO-02" }), new Item());

        Assert.False(open.TryGet(OpenOnce<Item>.Key("mensagem", new[] { "ALUNO-01" }), out _));
        Assert.False(open.TryGet(OpenOnce<Item>.Key("arquivo", new[] { "ALUNO-01", "ALUNO-02" }), out _));
    }

    [Fact]
    public void Remove_OnlyTakesOutThatWindow()
    {
        var open = new OpenOnce<Item>();
        var key = OpenOnce<Item>.Key("tela", new[] { "ALUNO-03" });
        var first = new Item();
        var second = new Item();
        open.Add(key, first);
        open.Add(key, second);

        open.Remove(key, first); // the first closing late must not forget the second
        Assert.True(open.TryGet(key, out var found));
        Assert.Same(second, found);

        open.Remove(key, second);
        Assert.False(open.TryGet(key, out _));
    }

    // "Ver a tela" connects before its window exists: a second click meanwhile waits.
    [Fact]
    public void WhileOneIsBeingOpened_ASecondAskIsTurnedAway()
    {
        var open = new OpenOnce<Item>();
        var key = OpenOnce<Item>.Key("tela", new[] { "ALUNO-03" });

        Assert.True(open.TryStartOpening(key));
        Assert.False(open.TryStartOpening(key));

        open.Add(key, new Item());
        Assert.False(open.TryStartOpening(key)); // open now

        open.Remove(key, open.TryGet(key, out var item) ? item : null!);
        Assert.True(open.TryStartOpening(key));
        open.StopOpening(key); // the connection failed
        Assert.True(open.TryStartOpening(key));
    }
}
