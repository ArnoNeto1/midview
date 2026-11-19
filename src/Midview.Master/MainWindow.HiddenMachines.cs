using System.IO;
using System.Windows;
using Midview.Protocol;

namespace Midview.Master;

// "Esconder da sala" (the tile menu) and "Telas" > "Escondidas" (bring it
// back): see HiddenMachines.
public partial class MainWindow
{
    // Saved with the roster (SaveRoster), never tiles.
    private readonly List<KnownMachine> _hiddenMachines = new();

    // The tile, or all the selected ones when it is part of the selection.
    private void OnHideMachineClicked(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is not MachineTile tile)
        {
            return;
        }

        var names = new List<string>();
        foreach (var target in TargetsOf(tile).ToList())
        {
            var index = _tiles.IndexOf(target);
            if (index < 0)
            {
                continue;
            }
            RemoveTileAt(index);
            _hiddenMachines.Add(target.Machine with { Hidden = true });
            names.Add(target.Machine.Name);
        }
        if (names.Count == 0)
        {
            return;
        }
        HiddenChanged(HiddenMachinesText.Hidden(names), "Escondida da grade");
    }

    private void ShowHiddenMachines(IReadOnlyList<KnownMachine> machines)
    {
        var names = new List<string>();
        foreach (var machine in machines)
        {
            if (!_hiddenMachines.Remove(machine))
            {
                continue;
            }
            InsertTile(_tiles.Count, machine with { Hidden = false });
            names.Add(machine.Name);
        }
        if (names.Count == 0)
        {
            return;
        }
        HiddenChanged(HiddenMachinesText.Shown(names), "De volta à grade");
    }

    private void HiddenChanged(string said, string done)
    {
        UpdateEmptyHint();
        ShowScreensBadge();
        try
        {
            SaveRoster();
            StatusTextBlock.Text = said;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowWarning($"{done}, mas a lista não pôde ser gravada em disco: {ErrorText.Describe(ex)}");
        }
    }
}
