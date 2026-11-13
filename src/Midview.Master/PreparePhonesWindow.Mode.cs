namespace Midview.Master;

// What the assistant prepares, chosen in "Máquinas da sala" ("Configurar
// celular" or "Configurar tablet"): a phone or a tablet, and "Midview
// otimizado" (PhoneCable lightens the device) or "Só o Midview". The kind
// changes only the name the device gets ("Tablet 07") and the titles that
// show it; every step stays the same.
public partial class PreparePhonesWindow
{
    private bool _optimize = true;

    internal AndroidDeviceKind Kind => _progress.Kind;

    internal bool Optimize => _optimize;

    // Asked again with the window open, for another kind or mode: it changes
    // only while no device is named or on the cable (before the number is
    // typed, or after "Preparar o próximo"), so a device half done is never
    // renamed nor prepared differently from what the teacher saw. False when
    // it cannot change now; the window then keeps what it had (ModeBusyText).
    internal bool SetMode(AndroidDeviceKind kind, bool optimize)
    {
        if (kind == _progress.Kind && optimize == _optimize)
        {
            return true;
        }
        if (_preparing || (kind != _progress.Kind && _progress.Number is not null))
        {
            return false;
        }
        _progress.Kind = kind;
        _optimize = optimize;
        ShowMode();
        ShowPage();
        return true;
    }

    // Why SetMode said no, for a message over the open assistant.
    internal string ModeBusyText =>
        $"O assistente já está com o {_progress.Name}. Termine este {DeviceWord} (ou feche o assistente) e escolha de novo.";

    // "celular" or "tablet", for the texts that name the device being prepared.
    private string DeviceWord => _progress.Kind == AndroidDeviceKind.Tablet ? "tablet" : "celular";

    private void ShowMode()
    {
        var heading = _progress.Kind == AndroidDeviceKind.Tablet ? "Preparar tablets" : "Preparar celulares";
        Title = heading;
        Header.Heading = heading;
        NumberNameHint.Text =
            $"O número vira o nome do {DeviceWord} no Midview: o 7 aparece como \"{PhoneNumber.NameOf("07", _progress.Kind)}\".";
        NumberLabel.Text = $"Número do {DeviceWord}";
    }
}
