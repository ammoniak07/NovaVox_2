"""
Génère src/NovaVox.Core/GameLog/ResourceQualityBands.json : pour chaque
ingrédient de fabrication, les tranches de qualité du jeu
(CraftingQualityQuantization) — une qualité de minerai comprise entre
« début » et « fin » compte comme la valeur « retenue ».

    {"Aslarite": [[0, 399, 287], [400, 599, 575], ...], ...}

Sources (voir tools/LISEZMOI_extraction.md) :
  - <crafting> : dossier Data/libs/foundry/records/crafting extrait par
    StarBreaker (fichiers qualityquantization/quantization_*.xml) ;
  - <resources> : dossier export/resources de ScDataDumper (resources.json
    pour les qualités atteignables de chaque minerai brut, commodities.json
    pour le lien minerai brut -> version raffinée utilisée dans les recettes).

Chaque minerai brut est rattaché à LA table dont les valeurs retenues
contiennent toutes ses qualités atteignables (vérification, pas de devinette
sur les noms de fichiers) ; l'ingrédient prend le nom de la version raffinée
(« Aslarite (Raw) » -> « Aslarite »), ou du minerai lui-même s'il n'est pas
raffiné (gemmes : Hadanite, Dolivine...).

  python tools/gen_tranches_qualite.py <crafting> <resources>
"""
import glob, json, os, re, sys

OUT = 'src/NovaVox.Core/GameLog/ResourceQualityBands.json'


def main(crafting, resources):
    tables = {}
    for f in glob.glob(os.path.join(crafting, 'qualityquantization', 'quantization_*.xml')):
        key = os.path.basename(f)[len('quantization_'):-4]
        if key == 'template':
            continue
        bands = [[int(a), int(b), int(m)] for a, b, m in
                 re.findall(r'start="(\d+)" end="(\d+)" mappedValue="(\d+)"', open(f, encoding='utf-8').read())]
        if bands:
            tables[key] = sorted(bands)
    commodities = {c['UUID']: c for c in json.load(open(os.path.join(resources, 'commodities.json'), encoding='utf-8'))}
    reachable = {}

    def walk(o):
        if isinstance(o, dict):
            if o.get('ResourceTypeUUID') and o.get('QualityQuantization'):
                reachable.setdefault(o['ResourceTypeUUID'], set()).update(o['QualityQuantization'])
            for v in o.values():
                walk(v)
        elif isinstance(o, list):
            for v in o:
                walk(v)

    walk(json.load(open(os.path.join(resources, 'resources.json'), encoding='utf-8')))
    out, problems = {}, []
    for uuid, values in sorted(reachable.items(), key=lambda kv: commodities.get(kv[0], {}).get('Name', '')):
        raw = commodities.get(uuid)
        if raw is None:
            problems.append(f'ressource inconnue {uuid}')
            continue
        refined = commodities.get(raw.get('RefinedVersionUUID'))
        name = refined['Name'] if refined else raw['Name']
        match = [k for k, b in tables.items() if values <= {m for _, _, m in b}]
        if len(match) != 1:
            problems.append(f'{raw["Name"]} : {len(match)} tables compatibles {match}')
            continue
        out[name] = tables[match[0]]
    # Tables restantes sans minerai minable connu (ex. Yormandi Eye) : par nom de fichier.
    used = {json.dumps(v) for v in out.values()}
    for key, bands in sorted(tables.items()):
        if json.dumps(bands) not in used:
            out.setdefault('#' + key, bands)
    json.dump(out, open(OUT, 'w', encoding='utf-8'), ensure_ascii=False, indent=1)
    print(f'{sum(1 for k in out if not k.startswith("#"))} ingrédients, {sum(1 for k in out if k.startswith("#"))} tables par nom de fichier')
    for p in problems:
        print('  !', p)


if __name__ == '__main__':
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
