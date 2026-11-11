namespace Midview.Master;

// The QR on show in "Preparar celulares": for which number and Wi-Fi, its
// one-use token, and until when the token is valid.
internal sealed record ShownQr(string Number, string Ssid, string Password, string Token, DateTimeOffset ExpiresAt);

// Whether the QR on show can be shown again or a new one (a new token) is due.
// Showing it again matters: the phone may have read it already, and a new
// token would replace the one it carries.
internal static class PhoneQrReuse
{
    // A token this close to expiring is replaced instead of shown again.
    public static readonly TimeSpan FreshEnough = TimeSpan.FromMinutes(10);

    public static bool Reuse(ShownQr? shown, string number, string ssid, string password, DateTimeOffset now) =>
        shown is not null
        && shown.Number == number
        && shown.Ssid == ssid
        && shown.Password == password
        && shown.ExpiresAt - now > FreshEnough;
}
