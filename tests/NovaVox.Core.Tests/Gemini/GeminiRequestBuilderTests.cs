using System.Text.Json.Nodes;
using NovaVox.Core.Gemini;
using Xunit;

namespace NovaVox.Core.Tests.Gemini;

public class GeminiRequestBuilderTests
{
    [Fact]
    public void BuildRequestBody_MapsAssistantRoleToModel()
    {
        var history = new List<GeminiMessage>
        {
            new("user", "Salut"),
            new("assistant", "Bonjour !"),
        };
        var body = GeminiRequestBuilder.BuildRequestBody(history, "system prompt", "normal");
        var contents = (JsonArray)body["contents"]!;

        Assert.Equal("user", (string)contents[0]!["role"]!);
        Assert.Equal("model", (string)contents[1]!["role"]!);
        Assert.Equal("Bonjour !", (string)contents[1]!["parts"]![0]!["text"]!);
        Assert.Equal("system prompt", (string)body["systemInstruction"]!["parts"]![0]!["text"]!);
    }

    [Theory]
    [InlineData("short", 512, "minimal")]
    [InlineData("normal", 1024, "low")]
    [InlineData("long", 2048, "medium")]
    public void BuildRequestBody_SetsGenerationConfigByResponseLength(string length, int expectedTokens, string expectedThinking)
    {
        var body = GeminiRequestBuilder.BuildRequestBody(new List<GeminiMessage>(), "x", length);
        var config = body["generationConfig"]!;
        Assert.Equal(expectedTokens, (int)config["maxOutputTokens"]!);
        Assert.Equal(expectedThinking, (string)config["thinkingConfig"]!["thinkingLevel"]!);
    }

    [Fact]
    public void RequestUrl_BuildsGenerateContentEndpoint()
    {
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.6-flash:generateContent",
            GeminiRequestBuilder.RequestUrl("gemini-3.6-flash"));
    }

    [Fact]
    public void ExtractReplyText_ConcatenatesParts()
    {
        var response = JsonNode.Parse("""
        {"candidates": [{"content": {"parts": [{"text": "Bon"}, {"text": "jour"}]}}]}
        """);
        Assert.Equal("Bonjour", GeminiRequestBuilder.ExtractReplyText(response));
    }

    [Fact]
    public void ExtractReplyText_EmptyWhenNoCandidates()
    {
        Assert.Equal("", GeminiRequestBuilder.ExtractReplyText(JsonNode.Parse("""{"candidates": []}""")));
    }

    [Fact]
    public void ExtractErrorMessage_ReadsErrorMessageField()
    {
        var response = JsonNode.Parse("""{"error": {"code": 404, "message": "model not found"}}""");
        Assert.Equal("model not found", GeminiRequestBuilder.ExtractErrorMessage(response));
    }

    [Fact]
    public void TrimHistory_KeepsOnlyLastMaxMessages()
    {
        var history = Enumerable.Range(0, 30).Select(i => new GeminiMessage("user", i.ToString())).ToList();
        var trimmed = GeminiRequestBuilder.TrimHistory(history);
        Assert.Equal(20, trimmed.Count);
        Assert.Equal("10", trimmed[0].Content);
        Assert.Equal("29", trimmed[^1].Content);
    }
}
