namespace NovaVox.App.ViewModels;

/// <summary>Une ligne du panneau "journal système" — port de appendLog(msg, kind) (gui/script.js).</summary>
public sealed class LogEntryVm
{
    public required string Time { get; init; }
    public required string Message { get; init; }

    /// <summary>"info" | "success" | "error" | "warning" — voir les styles .log-* (gui/style.css).</summary>
    public required string Kind { get; init; }
}
