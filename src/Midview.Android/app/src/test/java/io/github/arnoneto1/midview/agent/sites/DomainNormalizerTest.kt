package io.github.arnoneto1.midview.agent.sites

import org.junit.Assert.assertEquals
import org.junit.Test

// The vectors of tests/Midview.Agent.Tests/DomainNormalizerTests.cs, and
// more, each with what the Windows DomainNormalizer (Uri.CheckHostName on
// .NET 8) answers for it: the phone keeps the same sites as the PCs.
class DomainNormalizerTest {
    private fun check(vararg cases: Pair<String, String?>) {
        for ((input, expected) in cases) {
            assertEquals("for \"$input\"", expected, DomainNormalizer.normalize(input))
        }
    }

    @Test fun windowsValidVectors() = check(
        "youtube.com" to "youtube.com",
        "YouTube.com" to "youtube.com",
        " youtube.com " to "youtube.com",
        "https://youtube.com/watch?v=x" to "youtube.com",
        "http://youtube.com" to "youtube.com",
        "youtube.com:8080" to "youtube.com",
        "youtube.com/path" to "youtube.com",
    )

    @Test fun windowsInvalidVectors() = check(
        "" to null,
        "   " to null,
        "youtube.com\nSome-Injected-Line evil.example" to null,
        "not a domain" to null,
    )

    @Test fun cutsSchemePathQueryFragmentAndPort() = check(
        "youtube.com#frag" to "youtube.com",
        "youtube.com?x=1" to "youtube.com",
        "ftp://x.com:21/a" to "x.com",
        "://youtube.com" to "youtube.com",
        "http://" to null,
        "YOUTUBE.COM/" to "youtube.com",
        "HTTPS://WWW.Google.com.br/search?q=1" to "www.google.com.br",
        "https://site.com:443" to "site.com",
        // Cut at the first ':' as on Windows: what is left is the user name.
        "https://user:pw@site.com/" to "user",
        "user@youtube.com" to null,
    )

    @Test fun trimsWhatDotNetCallsWhiteSpace() = check(
        "\tyoutube.com\t" to "youtube.com",
        "youtube.com\r" to "youtube.com",
        "youtube.com\u00a0" to "youtube.com",
        "\u0085youtube.com" to "youtube.com",
        "\u3000youtube.com\u2028" to "youtube.com",
        // Not white space for .NET: kept, and refused.
        "\u001cyoutube.com" to null,
    )

    @Test fun labels() = check(
        "localhost" to "localhost",
        "youtube" to "youtube",
        "www.youtube.com" to "www.youtube.com",
        "m.youtube.com." to "m.youtube.com.",
        "EXAMPLE.ORG." to "example.org.",
        "com." to "com.",
        "a." to "a.",
        "a" to "a",
        "a1" to "a1",
        "1a.com" to "1a.com",
        "a.123" to "a.123",
        "123.a" to "123.a",
        "a.b-c.d" to "a.b-c.d",
        "a-b.c-d" to "a-b.c-d",
        "bad-.com" to "bad-.com",
        "abc-.def" to "abc-.def",
        "under_score.com" to "under_score.com",
        "x.y_z" to "x.y_z",
        "site.com.br" to "site.com.br",
        "a.b.c.d.e.f.g" to "a.b.c.d.e.f.g",
        "xn--caf-dma.com" to "xn--caf-dma.com",
        "-bad.com" to null,
        "_dmarc.example.com" to null,
        "x.-y.com" to null,
        "a..b" to null,
        ".com" to null,
        ".." to null,
        "." to null,
        "*.youtube.com" to null,
        "exa mple.com" to null,
        "example.com\\path" to null,
        "ex%41mple.com" to null,
        "a+b.com" to null,
        "a!b.com" to null,
        "a~b.com" to null,
        "a=b.com" to null,
        "a'b.com" to null,
        "a,b.com" to null,
        "a;b.com" to null,
        "a\u0000b.com" to null,
        "[::1]" to null,
        "::1" to null,
    )

    @Test fun labelLength() = check(
        "a".repeat(63) + ".com" to "a".repeat(63) + ".com",
        "a".repeat(64) + ".com" to null,
        // No limit on the whole name.
        List(5) { "a".repeat(63) }.joinToString(".") to List(5) { "a".repeat(63) }.joinToString("."),
    )

    @Test fun anIpv4AddressIsNotADomain() = check(
        "192.168.0.1" to null,
        "1.2.3.4" to null,
        "0.0.0.0" to null,
        "1.2.3" to null,
        "123" to null,
        "0" to null,
        "00" to null,
        "4294967295" to null,
        "0xffffffff" to null,
        "037777777777" to null,
        "0x7f.1" to null,
        "01.02.03.04" to null,
        "1.16777215" to null,
        "1.2.65535" to null,
        "1.2.3.0x10" to null,
        "1.2.3.010" to null,
        "1..2" to null,
        // Not an address (out of range, not octal, not hex, too many parts):
        // a name.
        "1.2.3.4.5" to "1.2.3.4.5",
        "999.1.1.1" to "999.1.1.1",
        "256.1" to "256.1",
        "1.16777216" to "1.16777216",
        "1.2.65536" to "1.2.65536",
        "0x100000000" to "0x100000000",
        "040000000000" to "040000000000",
        "08.1" to "08.1",
        "1.2.3.09" to "1.2.3.09",
        "0x.1" to "0x.1",
        "0x" to "0x",
        "0x7g.1" to "0x7g.1",
        "1.2.3.com" to "1.2.3.com",
        "1." to "1.",
        "1.2.3.4." to "1.2.3.4.",
    )

    @Test fun internationalNames() = check(
        "caf\u00e9.com" to "caf\u00e9.com",
        "CAF\u00c9.COM" to "caf\u00e9.com",
        "\u00c0\u00c9.com" to "\u00e0\u00e9.com",
        "A\u00e9.COM" to "a\u00e9.com",
        "K\u212a.com" to "kk.com",
        "\u0130x.com" to "\u0130x.com",
        "\u65e5\u672c.jp" to "\u65e5\u672c.jp",
        "\u65e5\u672c\u3002jp" to "\u65e5\u672c\u3002jp",
        "x\uff0ey" to "x\uff0ey",
        "x\uff61y" to "x\uff61y",
        "\u65e5\u672c-.jp" to "\u65e5\u672c-.jp",
        "x_\u65e5\u672c.jp" to "x_\u65e5\u672c.jp",
        "-\u65e5\u672c.jp" to null,
        "_\u65e5\u672c.jp" to null,
        "\u00e9" to "\u00e9",
        "\u00e9." to "\u00e9.",
        "a.\u00e9" to "a.\u00e9",
        "x.\u00e9." to "x.\u00e9.",
        "\u00e9-x.com" to "\u00e9-x.com",
        "x.\u00e9-" to "x.\u00e9-",
        "a\u00e9.b_c" to "a\u00e9.b_c",
        "\u00a1x.com" to "\u00a1x.com",
        "x\u00a0y.com" to "x\u00a0y.com",
        "a\u200bb.com" to "a\u200bb.com",
        "x\u2028.com" to "x\u2028.com",
        "x\u00ad.com" to "x\u00ad.com",
        "x\ufeff.com" to "x\ufeff.com",
        "x\ufffd.com" to "x\ufffd.com",
        "\ud83d\ude00.com" to "\ud83d\ude00.com",
        "\ud800.com" to "\ud800.com",
        "\u00e9:80" to "\u00e9",
        "\u00e9/x" to "\u00e9",
        "\u0080x.com" to null,
        "\u009fx.com" to null,
        "x\u0085.com" to null,
        "\u00e9..com" to null,
        "a\u00e9-.-b.com" to null,
        "a b\u00e9.com" to null,
        "\u00e9 .com" to null,
    )

    // A label with letters beyond ASCII is at most 59 long, those past
    // U+00FF counting twice (what .NET allows).
    @Test fun internationalLabelLength() = check(
        "\u00e9".repeat(59) + ".com" to "\u00e9".repeat(59) + ".com",
        "\u00e9".repeat(60) + ".com" to null,
        "a".repeat(58) + "\u00e9.com" to "a".repeat(58) + "\u00e9.com",
        "a".repeat(59) + "\u00e9.com" to null,
        "\u65e5".repeat(29) + ".com" to "\u65e5".repeat(29) + ".com",
        "\u65e5".repeat(30) + ".com" to null,
        "a".repeat(5) + "\u65e5".repeat(27) + ".com" to "a".repeat(5) + "\u65e5".repeat(27) + ".com",
        "a".repeat(5) + "\u65e5".repeat(28) + ".com" to null,
        "\ud83d\ude00".repeat(14) + ".com" to "\ud83d\ude00".repeat(14) + ".com",
        "\ud83d\ude00".repeat(15) + ".com" to null,
        "a".repeat(40) + "\u3002" + "a".repeat(40) to "a".repeat(40) + "\u3002" + "a".repeat(40),
        "a".repeat(70) + "\u3002x" to null,
        // A label of ASCII only stays at 63, even in a name with others.
        "a".repeat(63) + ".\u00e9" to "a".repeat(63) + ".\u00e9",
        "a".repeat(64) + ".\u00e9" to null,
        "\u00e9." + "a".repeat(63) to "\u00e9." + "a".repeat(63),
        "\u00e9." + "a".repeat(64) to null,
    )
}
