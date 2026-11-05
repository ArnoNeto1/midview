namespace Midview.Master;

// One line of "what happens next" in a question: a glyph from the icon font
// and the sentence beside it.
public sealed record QuestionFact(string Icon, string Text);

// How a QuestionWindow lays out its two buttons.
public enum QuestionButtons
{
    // A question in its own words ("Agora não | Atualizar agora"): the action
    // on the right, in the accent color.
    ActionOnRight,

    // A message box in Windows' order ("Sim | Não"), "Sim" in the accent color.
    YesFirst,

    // The same, with "Sim" in the danger style: an answer with no way back.
    YesFirstDanger,
}

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
    bool NoIsDefault = false,
    QuestionButtons Buttons = QuestionButtons.ActionOnRight);
