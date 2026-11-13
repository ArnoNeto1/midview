using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace Midview.Master;

// One element of a uiautomator dump with the elements inside it: what the
// launcher's home screen is made of (a home page holds its icons, folders
// and widgets as the children of one cell layout), where it is on the
// screen, and whether it is checked (the launcher's mode cards).
internal sealed class HomeScreenNode
{
    private static readonly Regex Bounds = new(@"^\[(-?\d+),(-?\d+)\]\[(-?\d+),(-?\d+)\]$", RegexOptions.Compiled);

    public string Id { get; private init; } = "";

    public string Class { get; private init; } = "";

    public string Package { get; private init; } = "";

    public string Text { get; private init; } = "";

    public string Description { get; private init; } = "";

    public bool Checked { get; private init; }

    public int Left { get; private init; }

    public int Top { get; private init; }

    public int Right { get; private init; }

    public int Bottom { get; private init; }

    public IReadOnlyList<HomeScreenNode> Children { get; private init; } = Array.Empty<HomeScreenNode>();

    public int X => (Left + Right) / 2;

    public int Y => (Top + Bottom) / 2;

    public int Width => Right - Left;

    public int Height => Bottom - Top;

    // Drawn on the screen: a hidden title has no size.
    public bool Shown => Width > 0 && Height > 0;

    public IEnumerable<HomeScreenNode> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var below in child.Descendants())
            {
                yield return below;
            }
        }
    }

    // The first element with this resource id ("com.miui.home:id/workspace"), this one included.
    public HomeScreenNode? Find(string id) => Id == id ? this : Descendants().FirstOrDefault(n => n.Id == id);

    public bool Has(string id) => Find(id) is not null;

    // Inside this one's bounds (the drawer's list, the dock), whole.
    public bool Holds(HomeScreenNode node) =>
        node.Left >= Left && node.Right <= Right && node.Top >= Top && node.Bottom <= Bottom;

    // The name the launcher gives an app, compared without case or accents:
    // a dump's "Câmera" is the same app as "Camera".
    public static string Plain(string name)
    {
        var decomposed = name.Trim().Normalize(NormalizationForm.FormD);
        var plain = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        {
            plain.Append(char.ToLowerInvariant(c));
        }
        return plain.ToString();
    }

    // The dump's root, or null when the text holds none (the dump failed:
    // "could not get idle state", the cable dropped).
    public static HomeScreenNode? Parse(string text)
    {
        var start = text.IndexOf("<hierarchy", StringComparison.Ordinal);
        var end = text.LastIndexOf("</hierarchy>", StringComparison.Ordinal);
        if (start < 0 || end < start)
        {
            return null;
        }
        try
        {
            return Read(XElement.Parse(text[start..(end + "</hierarchy>".Length)]));
        }
        catch (XmlException)
        {
            return null;
        }
    }

    private static HomeScreenNode Read(XElement element)
    {
        var bounds = Bounds.Match((string?)element.Attribute("bounds") ?? "");
        int At(int i) => bounds.Success ? int.Parse(bounds.Groups[i].Value, CultureInfo.InvariantCulture) : 0;
        return new HomeScreenNode
        {
            Id = (string?)element.Attribute("resource-id") ?? "",
            Class = (string?)element.Attribute("class") ?? "",
            Package = (string?)element.Attribute("package") ?? "",
            Text = (string?)element.Attribute("text") ?? "",
            Description = (string?)element.Attribute("content-desc") ?? "",
            Checked = (string?)element.Attribute("checked") == "true",
            Left = At(1),
            Top = At(2),
            Right = At(3),
            Bottom = At(4),
            Children = element.Elements("node").Select(Read).ToList(),
        };
    }
}
