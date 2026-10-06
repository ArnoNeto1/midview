namespace Midview.Protocol;

// The Session's command line for showing a pairing code on the student's
// screen: Midview.Session.exe --pairing-code 482913 --from "PROF-01". The
// Agent builds it; the Session reads it. The teacher's name goes through
// Pairing.CleanTeacherName, so no quote can reach the command line.
public static class PairingCodeArguments
{
    public const string CodeFlag = "--pairing-code";
    public const string FromFlag = "--from";

    public static string Build(int code, string teacherName) =>
        $"{CodeFlag} {code:000000} {FromFlag} \"{Pairing.CleanTeacherName(teacherName)}\"";

    public static (int Code, string TeacherName)? Parse(string[] args) =>
        args is [CodeFlag, var code, FromFlag, var from] && code.Length == 6 && code.All(char.IsAsciiDigit)
            ? (int.Parse(code), Pairing.CleanTeacherName(from))
            : null;
}
