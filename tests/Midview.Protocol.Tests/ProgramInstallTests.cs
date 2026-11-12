using System.Buffers.Binary;
using System.Text;
using Midview.Protocol;
using Xunit;

public class ProgramInstallTests
{
    [Fact]
    public void ARequest_RoundTrips()
    {
        var request = new InstallRequest("Instalador do GeoGebra 6.msi", 123_456_789, "ALLUSERS=1 INSTALLDIR=\"C:\\GeoGebra\"");

        Assert.True(ProgramInstall.TryDecodeRequest(ProgramInstall.EncodeRequest(request), out var decoded));

        Assert.Equal(request, decoded);
    }

    [Fact]
    public void NoArguments_RoundTrips()
    {
        var request = new InstallRequest("setup.exe", 1, "");

        Assert.True(ProgramInstall.TryDecodeRequest(ProgramInstall.EncodeRequest(request), out var decoded));

        Assert.Equal(request, decoded);
    }

    [Theory]
    [InlineData("..\\setup.exe")]
    [InlineData("C:\\Windows\\setup.exe")]
    [InlineData("setup.exe:stream")]
    [InlineData("notas.pdf")]
    [InlineData("setup")]
    [InlineData("CON.exe")]
    public void ANameThatIsntAPlainInstaller_IsRefused(string name)
    {
        Assert.False(ProgramInstall.TryDecodeRequest(Payload(10, "", name), out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ProgramInstall.MaxFileSize + 1)]
    public void ASizeOutOfRange_IsRefused(long size)
    {
        Assert.False(ProgramInstall.TryDecodeRequest(Payload(size, "", "setup.exe"), out _));
    }

    [Fact]
    public void ArgumentsOnMoreThanOneLine_AreRefused()
    {
        Assert.False(ProgramInstall.TryDecodeRequest(Payload(10, "/S\r\ncalc", "setup.exe"), out _));
    }

    [Fact]
    public void ArgumentsTooLong_AreRefused()
    {
        Assert.False(ProgramInstall.TryDecodeRequest(Payload(10, new string('a', ProgramInstall.MaxArgumentsBytes + 1), "setup.exe"), out _));
    }

    [Fact]
    public void ATruncatedPayload_IsRefused()
    {
        var payload = ProgramInstall.EncodeRequest(new InstallRequest("setup.exe", 10, "/S"));

        Assert.False(ProgramInstall.TryDecodeRequest(payload[..9], out _));
        Assert.False(ProgramInstall.TryDecodeRequest(payload[..12], out _));
    }

    [Theory]
    [InlineData("setup.exe", true)]
    [InlineData("SETUP.EXE", true)]
    [InlineData("pacote.msi", true)]
    [InlineData("script.bat", true)]
    [InlineData("SCRIPT.CMD", true)]
    [InlineData("pacote.zip", false)]
    [InlineData("setup.exe.txt", false)]
    public void WhatCanBeInstalled(string name, bool installer)
    {
        Assert.Equal(installer, ProgramInstall.IsInstallerName(name));
    }

    // A phone takes an Android app; a PC's Agent never does.
    [Theory]
    [InlineData("ola.apk", true)]
    [InlineData("OLA.APK", true)]
    [InlineData("ola.apk.exe", false)]
    [InlineData("ola.xapk", false)]
    [InlineData("setup.exe", false)]
    public void WhatAPhoneInstalls(string name, bool apk)
    {
        Assert.Equal(apk, ProgramInstall.IsApkName(name));
        Assert.False(ProgramInstall.IsInstallerName("ola.apk"));
        Assert.Equal(200L * 1024 * 1024, ProgramInstall.MaxApkSize);
    }

    // The phone decodes the request as a PC's Agent does: the bytes PhoneInstall sends.
    [Fact]
    public void AnApkRequest_HasNoArguments()
    {
        var payload = ProgramInstall.EncodeRequest(new InstallRequest("ola.apk", 1000, string.Empty));
        Assert.Equal(Convert.FromHexString("00000000000003E8" + "0000" + "6F6C612E61706B"), payload);
    }

    // Built by hand, so a request the Master would never send can be tried.
    private static byte[] Payload(long size, string arguments, string name)
    {
        var argumentBytes = Encoding.UTF8.GetBytes(arguments);
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var payload = new byte[10 + argumentBytes.Length + nameBytes.Length];
        BinaryPrimitives.WriteInt64BigEndian(payload, size);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(8), (ushort)argumentBytes.Length);
        argumentBytes.CopyTo(payload, 10);
        nameBytes.CopyTo(payload, 10 + argumentBytes.Length);
        return payload;
    }
}
