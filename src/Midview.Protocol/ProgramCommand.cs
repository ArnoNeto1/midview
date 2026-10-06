namespace Midview.Protocol;

// What OpenProgram carries: a command line as the teacher typed it, as in
// Windows' Run box ("powershell", "notepad C:\aula.txt", "\"C:\Program Files\...\app.exe\" -a").
// The program is the first word, or what's between the opening quotes when
// its path has spaces; the rest are its arguments, passed on as typed.
// Variables like %USERPROFILE% are left for the student's machine to
// expand. The same split on both ends: the Master says what's wrong with
// what the teacher typed, the Session refuses anything else.
public static class ProgramCommand
{
    public const int MaxLength = TextMessage.MaxLength;

    public static bool TryParse(string? typed, out string program, out string arguments, out string problem)
    {
        program = arguments = string.Empty;
        var text = typed?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            problem = "Escreva o comando, como powershell ou notepad.";
            return false;
        }
        if (text.Length > MaxLength)
        {
            problem = "Comando longo demais.";
            return false;
        }

        int end;
        if (text[0] == '"')
        {
            var closing = text.IndexOf('"', 1);
            if (closing <= 1)
            {
                problem = "Feche as aspas em volta do caminho do programa, como \"C:\\Program Files\\Programa\\app.exe\".";
                return false;
            }
            program = text[1..closing];
            end = closing + 1;
        }
        else
        {
            end = 0;
            while (end < text.Length && !char.IsWhiteSpace(text[end]))
            {
                end++;
            }
            program = text[..end];
        }

        arguments = text[end..].Trim();
        problem = string.Empty;
        return true;
    }

    // Several programs at once, one per line (blank lines skipped). A
    // problem names its line when there's more than one.
    public static bool TryParseLines(string typed, out IReadOnlyList<string> commands, out string problem)
    {
        var lines = typed.Split('\n').Select((line, index) => (Text: line.Trim(), Number: index + 1))
            .Where(line => line.Text.Length > 0)
            .ToList();
        commands = lines.Select(line => line.Text).ToList();
        if (lines.Count == 0)
        {
            return TryParse(null, out _, out _, out problem);
        }

        foreach (var line in lines)
        {
            if (!TryParse(line.Text, out _, out _, out var lineProblem))
            {
                problem = lines.Count == 1 ? lineProblem : $"Linha {line.Number}: {lineProblem}";
                return false;
            }
        }
        problem = string.Empty;
        return true;
    }
}
