using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Text.RegularExpressions;
using System.Windows.Controls;
using Midview.Master;
using Midview.Protocol;
using Xunit;

public class PhoneAppBlockWindowTests
{
    // The work area at 1366x768, or at 1080p with 150 % scaling, under a
    // Windows 11 taskbar: (1080 - 72) / 1.5 DIP.
    private const double SmallWorkAreaHeight = 672;

    // A small screen's work area is shorter than the window would like to be:
    // the window is held to it (its list of apps scrolls inside), never taller.
    [Fact]
    public void TheWindow_NeverOpensTallerThanTheWorkArea()
    {
        var xaml = File.ReadAllText(Path.Combine(RepoRoot(), "src", "Midview.Master", "PhoneAppBlockWindow.xaml"));

        Assert.Contains("MaxHeight=\"{Binding Source={x:Static SystemParameters.WorkArea}, Path=Height}\"", xaml);
        var minHeight = double.Parse(Regex.Match(xaml, "MinHeight=\"(\\d+)\"").Groups[1].Value, CultureInfo.InvariantCulture);
        Assert.True(minHeight <= SmallWorkAreaHeight, $"MinHeight {minHeight} would not fit a small screen");
    }

    [Fact]
    public void A_phone_tiles_menu_offers_to_block_apps()
    {
        var pc = new KnownMachine("PC 01", "192.0.2.1", "s", "a");
        var phone = new KnownMachine("Cel 01", "192.0.2.2", "s", "a", Kind: DeviceKind.Android);

        InSta(() =>
        {
            var menu = new ContextMenu();
            var programs = new MenuItem { Header = "Bloquear programas..." };
            DeviceActions.SetAction(programs, "BlockPrograms");
            var sites = new MenuItem { Header = "Bloquear sites..." };
            DeviceActions.SetAction(sites, "BlockSites");
            menu.Items.Add(programs);
            menu.Items.Add(sites);

            MainWindow.NameBlockProgramsItem(menu, new[] { phone });
            Assert.Equal("Bloquear apps...", programs.Header);
            Assert.Equal("Bloquear sites...", sites.Header);

            // PCs among them: the PCs' window opens (and says the phones were left out).
            MainWindow.NameBlockProgramsItem(menu, new[] { phone, pc });
            Assert.Equal("Bloquear programas...", programs.Header);
        });
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Midview.sln")))
        {
            dir = dir.Parent;
        }
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static void InSta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); } catch (Exception e) { failure = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
