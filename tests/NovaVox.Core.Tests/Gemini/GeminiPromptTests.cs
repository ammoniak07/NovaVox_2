using NovaVox.Core.Gemini;
using Xunit;

namespace NovaVox.Core.Tests.Gemini;

public class GeminiPromptTests
{
    [Fact]
    public void BuildSystemPrompt_FrenchIncludesNameAndLengthInstruction()
    {
        var prompt = GeminiPrompt.BuildSystemPrompt("Gemini", null, null, "short", "fr");
        Assert.Contains("Tu es Gemini", prompt);
        Assert.Contains("une seule phrase très courte", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_EnglishUsesEnglishInstructions()
    {
        var prompt = GeminiPrompt.BuildSystemPrompt("Gemini", null, null, "long", "en");
        Assert.Contains("You are Gemini", prompt);
        Assert.Contains("go into more detail", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_UnknownLanguageFallsBackToFrench()
    {
        var prompt = GeminiPrompt.BuildSystemPrompt("Gemini", null, null, "normal", "xx");
        Assert.Contains("Tu es Gemini", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_AppendsUserNameSection()
    {
        var prompt = GeminiPrompt.BuildSystemPrompt("Gemini", null, "Ammoniak", "normal", "fr");
        Assert.Contains("L'utilisateur s'appelle Ammoniak", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_AppendsCustomContextSection()
    {
        var prompt = GeminiPrompt.BuildSystemPrompt("Gemini", "Notre organisation s'appelle Foo Corp.", null, "normal", "fr");
        Assert.Contains("Foo Corp", prompt);
        Assert.Contains("STRICTEMENT", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_UnknownResponseLengthFallsBackToNormal()
    {
        var prompt = GeminiPrompt.BuildSystemPrompt("Gemini", null, null, "bogus", "fr");
        Assert.Contains("Réponds de façon concise", prompt);
    }
}
