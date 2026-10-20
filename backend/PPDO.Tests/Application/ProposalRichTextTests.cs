using PPDO.Application.Common;

namespace PPDO.Tests.Application;

/// <summary>
/// <see cref="ProposalRichText"/> (PPDO-235): characterization tests pinning the exact sanitized
/// output for realistic input, so an HtmlSanitizer / AngleSharp upgrade cannot change what gets
/// stored without a test saying so. Any expected value that changes here must be a reviewed,
/// deliberate change.
/// </summary>
public sealed class ProposalRichTextTests
{
    public static TheoryData<string, string, string?> Cases => new()
    {
        // ── The proposal editor's own (TipTap) output: kept byte for byte ──
        { "editor paragraphs",
          "<p>First <strong>bold</strong> and <em>italic</em>.</p><p>Second line<br>continued</p>",
          "<p>First <strong>bold</strong> and <em>italic</em>.</p><p>Second line<br>continued</p>" },
        { "editor lists",
          "<ul><li><p>One</p></li><li><p>Two <strong>b</strong></p><ol><li><p>Nested</p></li></ol></li></ul><ol><li><p>A</p></li></ol>",
          "<ul><li><p>One</p></li><li><p>Two <strong>b</strong></p><ol><li><p>Nested</p></li></ol></li></ul><ol><li><p>A</p></li></ol>" },
        { "editor empty paragraph", "<p></p>", null },
        { "editor only breaks and spaces", "<p><br></p><p>   </p>", null },
        { "entities",
          "<p>Fish &amp; chips &lt;tag&gt; &quot;q&quot; ₱1,000 &nbsp;x</p>",
          "<p>Fish &amp; chips &lt;tag&gt; \"q\" ₱1,000 &nbsp;x</p>" },
        { "unclosed tags are closed", "<p>open <strong>bold <em>both", "<p>open <strong>bold <em>both</em></strong></p>" },
        { "plain text", "just text with no tags", "just text with no tags" },

        // ── Pasted from Word / Google Docs: wrappers and styling go, text and structure stay ──
        { "Word paste",
          "<p class=MsoNormal style='margin-bottom:0in;line-height:normal'><b><span lang=EN-US style='font-size:12.0pt;font-family:\"Arial\",sans-serif'>Heading<o:p></o:p></span></b></p><p class=MsoListParagraph style='mso-list:l0 level1 lfo1'><![if !supportLists]><span style='font-family:Symbol'>·<span style='font:7.0pt \"Times New Roman\"'>&nbsp;&nbsp; </span></span><![endif]><span lang=EN-US>Item one</span></p>",
          "<p>Heading</p><p>·&nbsp;&nbsp; Item one</p>" },
        { "Google Docs paste",
          "<meta charset=\"utf-8\"><b style=\"font-weight:normal;\" id=\"docs-internal-guid-abc\"><p dir=\"ltr\" style=\"line-height:1.38;margin-top:0pt;\"><span style=\"font-size:11pt;font-family:Arial;font-weight:700;\">Bold run</span><span style=\"font-size:11pt;\"> plain run</span></p><ul style=\"margin-top:0;\"><li dir=\"ltr\" style=\"list-style-type:disc;\"><p dir=\"ltr\" role=\"presentation\"><span>Bullet</span></p></li></ul></b><br>",
          "<p>Bold run plain run</p><ul><li><p>Bullet</p></li></ul><br>" },
        { "nested spans", "<p><span><span style=\"color:red\"><strong><span>deep</span></strong></span></span> text</p>", "<p><strong>deep</strong> text</p>" },
        { "headings, tables, divs flatten to text", "<h1>Title</h1><table><tr><td>cell</td></tr></table><div>div text</div>", "Titlecelldiv text" },
        { "links and images", "<p><a href=\"javascript:alert(1)\">link</a> <img src=x onerror=alert(1)> <a href=\"https://example.com\" target=_blank>ok</a></p>", "<p>link  ok</p>" },
        { "attributes on allowed tags", "<p onclick=\"x()\" class=\"c\" style=\"color:red\" id=\"i\">attrs</p><strong onmouseover=\"y()\">b</strong>", "<p>attrs</p><strong>b</strong>" },

        // ── Code-holding elements: removed together with their content ──
        // svg/math: SVG and MathML elements keep lower-case NodeNames, so these leaked their text
        // (e.g. a nested <script>'s "alert(1)") until the drop list became case-insensitive.
        { "script", "<p>before</p><script>alert('xss')</script><p>after</p>", "<p>before</p><p>after</p>" },
        { "style", "<style>p{color:red}</style><p>styled</p>", "<p>styled</p>" },
        { "svg", "<p>a</p><svg><script>alert(1)</script><text>svgtext</text></svg><p>b</p>", "<p>a</p><p>b</p>" },
        { "math", "<p>a</p><math><mi>x</mi><mtext>mt</mtext></math><p>b</p>", "<p>a</p><p>b</p>" },
        { "iframe, object, embed", "<iframe src=\"https://evil\">fr</iframe><object data=x>obj</object><embed src=y><p>ok</p>", "<p>ok</p>" },
        { "template, noscript", "<template><p>tpl</p></template><noscript><p>ns</p></noscript><p>ok</p>", "<p>ok</p>" },
        { "title, textarea, select", "<title>t</title><textarea>ta</textarea><select><option>o</option></select><p>ok</p>", "<p>ok</p>" },

        // ── mXSS payloads, including GHSA-pgww-w46g-26qg's annotation-xml integration point ──
        { "advisory annotation-xml", "<math><annotation-xml encoding=\"text/html\"><style><img src=x onerror=alert(1)></style></annotation-xml></math>", null },
        // AngleSharp 1.x follows the current HTML spec: "</p>" inside <svg> breaks out of foreign
        // content, so <style> and the <img> parse as HTML. The <img> (and its onerror) is still
        // removed; only the inert '">' text remains. Under AngleSharp 0.17.1 this was null.
        { "mXSS svg/style", "<svg></p><style><a id=\"</style><img src=1 onerror=alert(1)>\">", "<p></p>\"&gt;" },
        { "mXSS math/mtext/mglyph", "<math><mtext><table><mglyph><style><img src=x onerror=alert(1)></style></mglyph></table></mtext></math>", null },
        { "mXSS noscript attribute", "<noscript><p title=\"</noscript><img src=x onerror=alert(1)>\"></noscript>", "\"&gt;" },
        { "comment breakout", "<p>a<!-- hidden --><!--><img src=x onerror=alert(1)>--></p>", "<p>a--&gt;</p>" },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Sanitize_RealisticInput_ProducesPinnedOutput(string scenario, string input, string? expected)
    {
        _ = scenario;   // shown in the test name only
        Assert.Equal(expected, ProposalRichText.Sanitize(input));
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void Sanitize_Output_HasNoExecutableMarkup(string scenario, string input, string? _)
    {
        string clean = ProposalRichText.Sanitize(input) ?? string.Empty;
        Assert.False(clean.Contains("<script", StringComparison.OrdinalIgnoreCase), scenario);
        Assert.False(clean.Contains("<img", StringComparison.OrdinalIgnoreCase), scenario);
        Assert.False(clean.Contains("<style", StringComparison.OrdinalIgnoreCase), scenario);
        Assert.False(clean.Contains("<svg", StringComparison.OrdinalIgnoreCase), scenario);
        Assert.False(clean.Contains("<math", StringComparison.OrdinalIgnoreCase), scenario);
        Assert.False(clean.Contains("onerror", StringComparison.OrdinalIgnoreCase) && clean.Contains('<'), scenario);
    }

    [Fact]
    public void Sanitize_IsIdempotent_OnEditorOutput()
    {
        const string html = "<ul><li><p>One <strong>b</strong></p></li></ul><p>x<br>y</p>";
        Assert.Equal(ProposalRichText.Sanitize(html), ProposalRichText.Sanitize(ProposalRichText.Sanitize(html)));
    }

    [Fact]
    public void FromPlainText_EncodesAndSplitsParagraphs()
        => Assert.Equal("<p>Line one<br>Line two</p><p>Para two &lt;b&gt;&amp;</p>",
            ProposalRichText.FromPlainText("Line one\nLine two\n\nPara two <b>&"));
}
