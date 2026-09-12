namespace NovaVox.App.ViewModels;

/// <summary>Une ligne du panneau de discussion Gemini (Réglages > 🌟 Assistant Gemini > fenêtre d'utilisation).</summary>
public sealed class GeminiMessageVm
{
    /// <summary>"user" | "assistant" | "error".</summary>
    public required string Role { get; init; }
    public required string Text { get; init; }
}
