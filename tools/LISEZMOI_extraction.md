# Mettre à jour les données du jeu (dégâts et schémas) après un patch

Les fichiers `src/NovaVox.Core/Combat/CombatData.json` (tableau 🎯 Pénétration)
et `src/NovaVox.Core/GameLog/SchemaDatabase.json` (📐 Schémas) viennent des
fichiers du jeu, au format de `StarCitizenWiki/scunpacked-data`.

## 1. Obtenir les données

**Si scunpacked-data a publié la version** : prendre `ships.json`,
`ship-items.json`, `items.json`, `fps-items.json`, `blueprints.json` et
`labels.json` sur https://github.com/StarCitizenWiki/scunpacked-data.

**Sinon, les extraire soi-même sur le PC où le jeu est installé** (fait
pour la 4.10.3) :

1. **StarBreaker** (https://github.com/diogotr7/StarBreaker), version avec
   `dcb extract --combined` (la v0.3.2 publiée ne l'a pas : il a fallu le
   compiler depuis le code, commit 7d3f01e du 15/10/2026). Dans `C:\scdata` :
   ```
   .\starbreaker.exe dcb extract --format unp4k --combined -o C:\scdata\import\Data
   .\starbreaker.exe p4k extract -o C:\scdata\import --regex "\.(xml|ini)$" --convert cryxml
   ```
2. **ScDataDumper** (https://github.com/octfx/ScDataDumper), avec PHP 8.4+
   pour Windows (zip « VS17 x64 Non Thread Safe » dézippé dans
   `C:\scdata\php`, extensions `zip` et `mbstring` activées, `memory_limit = -1`).
   **Correctif Windows obligatoire**, sinon aucun vaisseau n'est exporté :
   dans `src/Services/VehicleService.php`, remplacer
   `DIRECTORY_SEPARATOR.'spaceships'`, `DIRECTORY_SEPARATOR.'groundvehicles'`
   et `DIRECTORY_SEPARATOR.'actors'` par `'/spaceships'`, `'/groundvehicles'`
   et `'/actors'` (les chemins du cache sont écrits avec des `/`).
   Créer `C:\scdata\export` (il n'est pas créé tout seul), puis :
   ```
   php -c php-windows.ini cli.php load:data C:\scdata\import C:\scdata\export
   ```

## 2. Régénérer

`<ancien>` = les données qui ont servi à la version actuelle, `<nouveau>` =
celles du patch.

```
python tools/normaliser_fabricants.py <ancien> <nouveau>
python tools/gen_combat_data.py <nouveau> <version du jeu> src/NovaVox.Core/Combat/CombatData.json
python tools/maj_schemas.py <ancien> <nouveau>
python tools/gen_tranches_qualite.py C:\scdata\import\Data\libs\foundry\records\crafting C:\scdata\export\resources
dotnet test tests/NovaVox.Core.Tests
```

- `normaliser_fabricants.py` garde les noms de fabricants de l'ancienne
  version (ScDataDumper écrit parfois « BEH » au lieu de « Behring Applied
  Technology »).
- `maj_schemas.py` n'applique que les différences (schémas ajoutés,
  renommés, retirés, recettes changées) : la base actuelle et ses
  corrections manuelles sont conservées.
- `gen_tranches_qualite.py` relit les tranches de qualité des minerais
  (`qualityquantization/*.xml` : une qualité de 0 à 399 compte comme 287...)
  et les rattache aux ingrédients des recettes, vérifiées par les qualités
  atteignables de chaque minerai (`resources.json`).
- Les tests `CombatReferenceValuesTests` contiennent des valeurs de
  référence d'une version précise : un écart après un patch peut être un
  vrai changement du jeu (ex. Polaris en 4.10.3).
