using System;
using Midview.Session;
using Xunit;

public class ClipboardBridgeTests
{
    private DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private readonly FakeClipboard _clipboard = new();

    private ClipboardBridge Bridge() => new(_clipboard, () => _now);

    private void Advance(double seconds) => _now = _now.AddSeconds(seconds);

    [Fact]
    public void ApplyRemote_PutsTheTextOnTheClipboard()
    {
        Bridge().ApplyRemote("do professor");
        Assert.Equal("do professor", _clipboard.Text);
    }

    [Fact]
    public void ApplyRemote_ThenPollLocal_DoesNotEchoItBack()
    {
        var bridge = Bridge();
        bridge.Controlling = true;
        bridge.ApplyRemote("mesmo texto");
        Advance(1);

        // The student's clipboard now holds what the teacher just sent; it
        // must not be sent straight back.
        Assert.Null(bridge.PollLocal());
    }

    [Fact]
    public void PollLocal_WhenNotControlling_ReturnsNull()
    {
        _clipboard.Text = "copiado no aluno";
        Advance(1);
        Assert.Null(Bridge().PollLocal()); // Controlling defaults to false
    }

    [Fact]
    public void PollLocal_WhenControlling_ReturnsNewTextOnceThenNull()
    {
        var bridge = Bridge();
        bridge.Controlling = true;
        _clipboard.Text = "aluno copiou";

        Advance(1);
        Assert.Equal("aluno copiou", bridge.PollLocal());

        Advance(1);
        Assert.Null(bridge.PollLocal()); // unchanged since

        _clipboard.Text = "algo novo";
        Advance(1);
        Assert.Equal("algo novo", bridge.PollLocal());
    }

    [Fact]
    public void PollLocal_ThrottlesRapidPolls()
    {
        var bridge = Bridge();
        bridge.Controlling = true;
        _clipboard.Text = "a";
        Advance(1);
        Assert.Equal("a", bridge.PollLocal());

        _clipboard.Text = "b";
        Advance(0.1); // sooner than the min interval
        Assert.Null(bridge.PollLocal());

        Advance(1); // now past it
        Assert.Equal("b", bridge.PollLocal());
    }

    [Fact]
    public void PollLocal_OversizedText_IsNotSent()
    {
        var bridge = Bridge();
        bridge.Controlling = true;
        _clipboard.Text = new string('x', Midview.Protocol.ClipboardText.MaxBytes + 1);
        Advance(1);
        Assert.Null(bridge.PollLocal());
    }

    [Fact]
    public void PollLocal_LoneSurrogateText_IsSkipped_NotThrown()
    {
        var bridge = Bridge();
        bridge.Controlling = true;
        _clipboard.Text = "meio \uD800 caractere"; // invalid UTF-16
        Advance(1);
        Assert.Null(bridge.PollLocal()); // skipped, and no exception
    }

    [Fact]
    public void PollLocal_EmptyClipboard_IsNotSent()
    {
        var bridge = Bridge();
        bridge.Controlling = true;
        _clipboard.Text = "";
        Advance(1);
        Assert.Null(bridge.PollLocal());
    }

    [Fact]
    public void ApplyRemote_WhenTheClipboardIsBusy_DoesNotThrow()
    {
        _clipboard.FailSet = true;
        Bridge().ApplyRemote("texto"); // must swallow the failure
    }

    [Fact]
    public void PollLocal_WhenTheClipboardIsBusy_ReturnsNull()
    {
        var bridge = Bridge();
        bridge.Controlling = true;
        _clipboard.FailGet = true;
        Advance(1);
        Assert.Null(bridge.PollLocal());
    }

    private sealed class FakeClipboard : IClipboard
    {
        public string? Text;
        public bool FailGet;
        public bool FailSet;

        public string? GetText() => FailGet ? throw new InvalidOperationException("busy") : Text;

        public void SetText(string text)
        {
            if (FailSet) throw new InvalidOperationException("busy");
            Text = text;
        }
    }
}
