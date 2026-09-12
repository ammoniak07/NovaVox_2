using NovaVox.Core.Commands;

namespace NovaVox.App.Input;

/// <summary>
/// Port de Api._run_command_sequence (app.py) : exécute l'action
/// principale d'une commande (avec maintien/répétition), PUIS chacune de
/// ses actions supplémentaires dans l'ordre (jusqu'à
/// CommandStore.MaxCommandExtraSteps). Chaque action supplémentaire est un
/// simple appui bref, précédé du délai configuré. À appeler sur son
/// propre thread (voir Api._execute_command côté Python) pour ne jamais
/// geler la reconnaissance vocale pendant les délais.
/// </summary>
public sealed class CommandExecutor
{
    private readonly KeySimulator _keySimulator;

    public CommandExecutor(KeySimulator keySimulator)
    {
        _keySimulator = keySimulator;
    }

    public void Run(VoiceCommand command)
    {
        _keySimulator.PressKeysRepeated(command.Keys, command.Hold, command.RepeatCount, command.RepeatDelay);
        foreach (var step in command.ExtraSteps)
        {
            var delay = Math.Max(0.0, step.DelayBefore);
            if (delay > 0) Thread.Sleep(TimeSpan.FromSeconds(delay));
            _keySimulator.PressKeys(step.Keys, hold: false);
        }
    }
}
