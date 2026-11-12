using System;
using System.Text;
using System.Text.Json;
using Midview.Master;
using Xunit;

namespace Midview.Master.Tests;

public sealed class CableSetupPayloadTests
{
    // The vector the phone app's tests (CableSetupPayload.decode) copy as is.
    private const string FixedVector =
        "eyJudW1iZXIiOiIwNyIsIm1hc3RlciI6IkFCMTJDRDM0RUY1NkFCMTJDRDM0RUY1NkFCMTJDRDM0RUY1NkFCMTIiLCJzc2lkIjoiTGFiIFwiMVwiOyDDqSIsInBhc3N3b3JkIjoicyduaEAgMTsifQ";

    private const string FixedJson =
        "{\"number\":\"07\",\"master\":\"AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12\",\"ssid\":\"Lab \\\"1\\\"; é\",\"password\":\"s'nh@ 1;\"}";

    private static string Fixed() =>
        CableSetupPayload.Encode("07", "ab12cd34ef56ab12cd34ef56ab12cd34ef56ab12", "Lab \"1\"; é", "s'nh@ 1;");

    private static string Decode(string text)
    {
        var base64 = text.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(base64));
    }

    [Fact]
    public void Fixed_vector_is_exactly_this_string()
    {
        Assert.Equal(FixedVector, Fixed());
    }

    [Fact]
    public void Fixed_vector_decodes_to_the_four_values()
    {
        var json = Decode(Fixed());
        Assert.Equal(FixedJson, json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("07", root.GetProperty("number").GetString());
        Assert.Equal("AB12CD34EF56AB12CD34EF56AB12CD34EF56AB12", root.GetProperty("master").GetString());
        Assert.Equal("Lab \"1\"; é", root.GetProperty("ssid").GetString());
        Assert.Equal("s'nh@ 1;", root.GetProperty("password").GetString());
    }

    [Fact]
    public void Encoding_is_url_safe_without_padding()
    {
        var text = CableSetupPayload.Encode("7", "ab", "???>>>", "~~~???");
        Assert.DoesNotContain('=', text);
        Assert.DoesNotContain('+', text);
        Assert.DoesNotContain('/', text);
    }
}
