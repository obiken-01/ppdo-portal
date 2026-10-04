using System.Net;
using Ganss.Xss;

namespace PPDO.Application.Common;

/// <summary>
/// The investment proposal's rich text (Investment_Proposal_Spec.md decision 20): bold, italic,
/// bulleted and numbered lists, nothing else. Everything outside the allow-list is stripped on save.
///
/// <para>
/// The same <c>HtmlSanitizer</c> package Announcements uses, but configured much tighter: six
/// tags, <b>no attributes, no CSS, no URL schemes</b>. A proposal is a printed form, so links,
/// images, colours and inline styles have nowhere to go, and allowing them would only give the Word
/// renderer (PPDO-158) more to defend against.
/// </para>
/// </summary>
public static class ProposalRichText
{
    /// <summary>The allow-list, word for word from decision 20.</summary>
    public static readonly IReadOnlyList<string> AllowedTags = ["p", "strong", "em", "ul", "ol", "li", "br"];

    /// <summary>Elements removed together with everything inside them (AngleSharp upper-case names).</summary>
    private static readonly HashSet<string> DropWithContent =
        ["SCRIPT", "STYLE", "TEMPLATE", "NOSCRIPT", "IFRAME", "OBJECT", "EMBED", "SVG", "MATH", "TITLE", "TEXTAREA", "SELECT"];

    // HtmlSanitizer is thread-safe once configured; Announcements shares one instance the same way.
    private static readonly HtmlSanitizer Sanitizer = Build();

    private static HtmlSanitizer Build()
    {
        HtmlSanitizer s = new();
        s.AllowedTags.Clear();
        foreach (string tag in AllowedTags) s.AllowedTags.Add(tag);
        s.AllowedAttributes.Clear();
        s.AllowedCssProperties.Clear();
        s.AllowedSchemes.Clear();
        s.AllowedAtRules.Clear();
        s.AllowedClasses.Clear();
        s.KeepChildNodes = true;   // <span><strong>x</strong></span> keeps the bold text, not nothing
        // ⚠️ KeepChildNodes applies to EVERY removed tag, so a <script>'s code would survive as
        // visible text. These elements hold code or markup, never prose: empty them before they go.
        s.RemovingTag += (_, e) =>
        {
            if (DropWithContent.Contains(e.Tag.NodeName)) e.Tag.TextContent = string.Empty;
        };
        return s;
    }

    /// <summary>Sanitized HTML, or null when nothing but whitespace and empty tags remains.</summary>
    public static string? Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;
        string clean = Sanitizer.Sanitize(html).Trim();
        return HasText(clean) ? clean : null;
    }

    /// <summary>
    /// Plain text from the AIP (a project's description or objective) as proposal HTML: encoded,
    /// one <c>&lt;p&gt;</c> per blank-line-separated paragraph, line breaks kept as <c>&lt;br&gt;</c>.
    /// Used once, at creation (decisions 11 and 12).
    /// </summary>
    public static string? FromPlainText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        IEnumerable<string> paragraphs = text.Replace("\r\n", "\n").Split("\n\n")
            .Select(p => p.Trim())
            .Where(p => p.Length > 0)
            .Select(p => "<p>" + string.Join("<br>", p.Split('\n').Select(WebUtility.HtmlEncode)) + "</p>");
        return string.Concat(paragraphs);
    }

    private static bool HasText(string html)
    {
        // Strip tags and entities' whitespace: "<p><br></p>" is empty, "<p>a</p>" is not.
        string text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", string.Empty);
        return !string.IsNullOrWhiteSpace(WebUtility.HtmlDecode(text));
    }
}
