using System.Text.Json.Nodes;
using NovaVox.Core.Gemini;
using Xunit;

namespace NovaVox.Core.Tests.Gemini;

public class StarCitizenWikiTests
{
    [Fact]
    public void SearchUrl_EscapesTerm()
    {
        Assert.Equal(
            "https://starcitizen.tools/api.php?action=query&list=search&srsearch=Hull%20C&srlimit=1&format=json",
            StarCitizenWiki.SearchUrl("Hull C"));
    }

    [Fact]
    public void PageUrl_EscapesTitle()
    {
        Assert.Equal(
            "https://starcitizen.tools/api.php?action=parse&page=MicroTech%20%28planet%29&format=json&prop=text%7Ccategories&redirects=1",
            StarCitizenWiki.PageUrl("MicroTech (planet)"));
    }

    [Fact]
    public void BuildEntityExtractionPrompt_IncludesTranscriptAndQuestion()
    {
        var history = new List<GeminiMessage>
        {
            new("user", "Parle-moi du Hull C"),
            new("assistant", "C'est un vaisseau cargo."),
        };
        var prompt = StarCitizenWiki.BuildEntityExtractionPrompt(history, "il a combien de HP de bouclier ?");
        Assert.Contains("User: Parle-moi du Hull C", prompt);
        Assert.Contains("Assistant: C'est un vaisseau cargo.", prompt);
        Assert.Contains("reply with exactly: NONE", prompt);
    }

    [Fact]
    public void BuildEntityExtractionPrompt_FallsBackToQuestionWhenHistoryEmpty()
    {
        var prompt = StarCitizenWiki.BuildEntityExtractionPrompt(new List<GeminiMessage>(), "C'est quoi le Hull C ?");
        Assert.Contains("User: C'est quoi le Hull C ?", prompt);
    }

    [Theory]
    [InlineData("Hull C", "Hull C")]
    [InlineData("NONE", null)]
    [InlineData("none", null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    public void ExtractEntityTerm_HandlesNoneAndBlank(string replyText, string? expected)
    {
        var response = new JsonObject
        {
            ["candidates"] = new JsonArray(new JsonObject
            {
                ["content"] = new JsonObject
                {
                    ["parts"] = new JsonArray(new JsonObject { ["text"] = replyText }),
                },
            }),
        };
        Assert.Equal(expected, StarCitizenWiki.ExtractEntityTerm(response));
    }

    [Fact]
    public void ExtractSearchTitle_ReturnsFirstResultTitle()
    {
        var response = JsonNode.Parse("""{"query": {"search": [{"title": "Hull C"}, {"title": "Other"}]}}""");
        Assert.Equal("Hull C", StarCitizenWiki.ExtractSearchTitle(response));
    }

    [Fact]
    public void ExtractSearchTitle_NullWhenNoResults()
    {
        Assert.Null(StarCitizenWiki.ExtractSearchTitle(JsonNode.Parse("""{"query": {"search": []}}""")));
    }

    [Fact]
    public void IsDisambiguationPage_DetectsDisambigCategory()
    {
        var response = JsonNode.Parse("""
        {"parse": {"categories": [{"*": "Disambiguation pages"}]}}
        """);
        Assert.True(StarCitizenWiki.IsDisambiguationPage(response));
    }

    [Fact]
    public void ExtractPageText_NullForDisambiguationPage()
    {
        var response = JsonNode.Parse("""
        {"parse": {"categories": [{"*": "Disambiguation pages"}], "text": {"*": "<p>Some links</p>"}}}
        """);
        Assert.Null(StarCitizenWiki.ExtractPageText(response));
    }

    [Fact]
    public void ExtractPageText_NullForEmptyHtml()
    {
        var response = JsonNode.Parse("""{"parse": {"categories": [], "text": {"*": ""}}}""");
        Assert.Null(StarCitizenWiki.ExtractPageText(response));
    }

    [Fact]
    public void ExtractPageText_ConvertsHtmlAndTruncates()
    {
        var longText = new string('a', StarCitizenWiki.ExtractMaxChars + 500);
        var response = new JsonObject
        {
            ["parse"] = new JsonObject
            {
                ["categories"] = new JsonArray(),
                ["text"] = new JsonObject { ["*"] = $"<p>{longText}</p>" },
            },
        };
        var extract = StarCitizenWiki.ExtractPageText(response);
        Assert.NotNull(extract);
        Assert.Equal(StarCitizenWiki.ExtractMaxChars, extract!.Length);
    }

    [Fact]
    public void HtmlToText_StripsScriptsStylesTagsAndDecodesEntities()
    {
        const string html = "<div><style>.x{color:red}</style><script>alert(1)</script>" +
                             "<p>Hull C &amp; Caterpillar</p>\n\n<span>info</span></div>";
        Assert.Equal("Hull C & Caterpillar info", StarCitizenWiki.HtmlToText(html));
    }

    [Fact]
    public void BuildReferenceBlock_IncludesTitleAndExtract()
    {
        var block = StarCitizenWiki.BuildReferenceBlock("Hull C", "A cargo ship.");
        Assert.Contains("page \"Hull C\"", block);
        Assert.Contains("A cargo ship.", block);
        Assert.Contains("never switch to English", block);
    }
}
