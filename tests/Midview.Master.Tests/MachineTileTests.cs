using System.ComponentModel;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Midview.Master;
using Xunit;

public class MachineTileTests
{
    private static MachineTile NewTile() => new(new KnownMachine("LAB-01", "192.0.2.1", "ANY", null));

    private static ImageSource AnImage()
    {
        var image = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, new byte[4], 4);
        image.Freeze();
        return image;
    }

    [Fact]
    public void ANewTile_IsStillConnecting() => Assert.Equal(TilePresence.Connecting, NewTile().Presence);

    [Fact]
    public void AThumbnail_MeansTheMachineIsOnline()
    {
        var tile = NewTile();

        tile.ShowThumbnail(AnImage());

        Assert.Equal(TilePresence.Online, tile.Presence);
    }

    // It answered: on, but its screen can't be captured now (e.g. locked).
    [Fact]
    public void AScreenThatCantBeCaptured_IsStillOnline()
    {
        var tile = NewTile();

        tile.ShowScreenUnavailable();

        Assert.Equal(TilePresence.Online, tile.Presence);
        Assert.Equal("Tela indisponível", tile.Status);
    }

    public static TheoryData<Exception, TilePresence> WhatThePollerReports => new()
    {
        // On, at the login screen: something answered the connection.
        { new SocketException((int)SocketError.ConnectionRefused), TilePresence.Online },
        // On, and answering, but not trusting this teacher.
        { new AuthenticationException("The remote certificate was rejected"), TilePresence.Online },
        { new SocketException((int)SocketError.TimedOut), TilePresence.Offline },
        { new OperationCanceledException(), TilePresence.Offline },
        { new SocketException((int)SocketError.HostNotFound), TilePresence.Offline },
        { new IOException("Unable to read data from the transport connection"), TilePresence.Offline },
        { new InvalidOperationException("anything else"), TilePresence.Offline },
    };

    [Theory]
    [MemberData(nameof(WhatThePollerReports))]
    public void AStatus_IsOnlineOnlyWhenTheMachineAnswered(Exception ex, TilePresence expected)
    {
        var tile = NewTile();

        tile.ShowStatus(FailureText.Status(ex));

        Assert.Equal(expected, tile.Presence);
    }

    [Fact]
    public void GoingOffline_IsAnnounced_ForTheGridToHideIt()
    {
        var tile = NewTile();
        tile.ShowThumbnail(AnImage());
        var changed = new List<string?>();
        tile.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        tile.ShowStatus("Offline");

        Assert.Contains(nameof(MachineTile.Presence), changed);
    }
}
