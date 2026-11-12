using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Midview.Master;

// What the Master hands the phone through the cable: the number, the Master's
// certificate thumbprint and the Wi-Fi. One string, Base64 URL without "=", of
// the UTF-8 JSON {"number","master","ssid","password"} in that key order.
public static class CableSetupPayload
{
    public static string Json(string number, string masterThumbprint, string ssid, string password)
    {
        using var stream = new MemoryStream();
        // Relaxed escaping keeps letters like "é" and ' as they are; only quotes,
        // backslashes and control characters become escapes. Same text every time.
        var options = new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        using (var writer = new Utf8JsonWriter(stream, options))
        {
            writer.WriteStartObject();
            writer.WriteString("number", number);
            writer.WriteString("master", masterThumbprint.ToUpperInvariant());
            writer.WriteString("ssid", ssid);
            writer.WriteString("password", password);
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static string Encode(string number, string masterThumbprint, string ssid, string password) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(Json(number, masterThumbprint, ssid, password)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
