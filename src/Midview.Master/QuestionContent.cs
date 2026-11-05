namespace Midview.Master;

// One line of "what happens next" in a question: a glyph from the icon font
// and the sentence beside it.
public sealed record QuestionFact(string Icon, string Text);

// Everything a QuestionWindow shows. Icon is a glyph of the icon font ("" for
// none); Versions draws "from -> to" as two chips under the heading; No is
// null for a message that only has an OK button.
public sealed record QuestionContent(
    string Title,
    string Icon,
    string Heading,
    string Yes,
    string? No,
    string? Body = null,
    IReadOnlyList<QuestionFact>? Facts = null,
    (string From, string To)? Versions = null,
    bool NoIsDefault = false);
