using System.ComponentModel;
using System.IO;

namespace Midview.Master;

// The "conta-google" step: once each time it comes up, the cable opens the
// phone's accounts screen (PhoneCable.OpenAccounts), so the teacher only
// taps the Google account and "Remover conta". Nothing is said when it does
// not open (no cable, a phone that refused): the step's text says where it is.
public partial class PreparePhonesWindow
{
    private bool _accountsOpened;

    private void OpenAccountsOnce()
    {
        var onStep = _progress.Page == SetupPage.Step && _progress.StepId == "conta-google";
        if (!onStep)
        {
            _accountsOpened = false;
            return;
        }
        if (_accountsOpened)
        {
            return;
        }
        _accountsOpened = true;
        _ = OpenAccountsAsync();
    }

    private async Task OpenAccountsAsync()
    {
        try
        {
            if (_adb is null && !await FindToolAsync())
            {
                return;
            }
            var cable = Cable();
            await Task.Run(cable.OpenAccounts);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            // The step's text says where the screen is.
        }
    }
}
