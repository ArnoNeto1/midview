namespace Midview.Protocol;

// ForgetOtherTeachers' answer (see MessageType): how many teachers the
// machine forgot, as the teacher reads it. The phone says the same
// (src/Midview.Android/.../pairing/PairingFlow.kt).
public static class OtherTeachers
{
    public const string NoneForgotten = "nenhum outro professor";

    public static string ForgottenText(int dropped) => dropped switch
    {
        0 => NoneForgotten,
        1 => "1 professor esquecido",
        _ => $"{dropped} professores esquecidos",
    };
}
