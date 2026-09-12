using Vosk;

namespace NovaVox.App.Speech;

/// <summary>
/// Réutilise le modèle Vosk déjà chargé tant que le dossier sélectionné
/// n'a pas changé — port du cache self._vosk_model/_vosk_model_path
/// (app.py) : recharger Model(modelPath) à chaque activation de l'écoute
/// est coûteux (plusieurs secondes) alors que rien ne change entre deux
/// activations successives.
/// </summary>
public sealed class VoskModelCache : IDisposable
{
    private Model? _model;
    private string? _modelPath;

    public Model GetOrLoad(string modelPath, out bool reused)
    {
        if (_model is not null && _modelPath == modelPath)
        {
            reused = true;
            return _model;
        }

        _model?.Dispose();
        _model = new Model(modelPath);
        _modelPath = modelPath;
        reused = false;
        return _model;
    }

    public void Dispose() => _model?.Dispose();
}
