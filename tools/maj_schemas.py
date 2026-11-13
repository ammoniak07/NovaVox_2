"""
Met à jour les schémas de fabrication (SchemaDatabase.json) d'une version
du jeu à la suivante, à partir de deux exports façon scunpacked-data
(blueprints.json, items.json, ship-items.json, labels.json) : l'ancien
(celui qui a servi à la base actuelle) et le nouveau.

On part de la base actuelle (qui contient aussi des corrections faites à
la main) et on n'applique que les différences entre les deux versions :
  - schéma nouveau          -> fiche ajoutée (fabriquée depuis items.json) ;
  - schéma renommé          -> fiche refaite sous le nouveau nom ;
  - temps / ingrédients     -> mis à jour ;
  - schéma disparu          -> fiche retirée.
Les clés global.ini des noms ajoutés vont dans SchemaNameLocalizationKeys.json.
Les traductions (SchemaDatabase.<langue>.json) ne sont pas touchées : une
fiche sans traduction affiche la description anglaise.

Lancer d'abord tools/normaliser_fabricants.py sur le nouvel export.

  python tools/maj_schemas.py <ancien dossier> <nouveau dossier>
"""
import json, re, sys

DB = 'src/NovaVox.Core/GameLog/SchemaDatabase.json'
KEYS = 'src/NovaVox.Core/GameLog/SchemaNameLocalizationKeys.json'
COMPONENT = {'Shield', 'Cooler', 'PowerPlant', 'QuantumDrive', 'Radar', 'JumpDrive', 'MiningModifier'}
PUNCT = str.maketrans({'’': "'", '‘': "'", '“': '"', '”': '"', '–': '-', '—': '-'})


def ingredients(node, out):
    if isinstance(node, list):
        for x in node:
            ingredients(x, out)
    elif isinstance(node, dict):
        kind = node.get('Kind')
        if kind == 'resource':
            out.append({'name': node['Name'], 'amount': node.get('QuantityScu'), 'unit': 'SCU'})
        elif kind == 'item':
            out.append({'name': node['Name'], 'amount': node.get('Quantity'), 'unit': 'pcs'})
        for c in node.get('Children') or []:
            ingredients(c, out)
    return out


def clean_desc(d):
    # Retire le ou les blocs d'en-tête « Item Type: ... » (lignes courtes
    # « Libellé: valeur », séparés par des lignes vides), comme
    # SchemaDatabase.CleanIniDescription côté appli.
    blocks = (d or '').replace('\r', '').split('\n')
    lines = [l.rstrip() for l in blocks]
    i = 0
    while i < len(lines):
        if not lines[i].strip():
            i += 1
            continue
        end = i
        while end < len(lines) and lines[end].strip():
            end += 1
        if not all(len(l) <= 80 and re.match(r'^[^:]{1,40}:\s*\S', l.strip()) for l in lines[i:end]):
            break
        i = end
    return '\n'.join(lines[i:]).replace('…', '...').translate(PUNCT).strip()


def num(v):
    try:
        f = float(v)
        return int(f) if f.is_integer() else f
    except (TypeError, ValueError):
        return v


def build(bp, item):
    out = bp['Output']
    si = item.get('stdItem') or {}
    dd = dict(si.get('DescriptionData') or {})
    e = {'name': out['Name'], 'type': out.get('Type') or item.get('type'),
         'subtype': item.get('subType') or 'UNDEFINED',
         'manufacturer': (si.get('Manufacturer') or {}).get('Name'),
         'description': clean_desc(si.get('Description')),
         'craftTimeSeconds': bp['Tiers'][0].get('CraftTimeSeconds')}
    cap = dd.pop('Carrying Capacity', None)  # sacs à dos : « 180K µSCU » -> 180000
    m = re.match(r'([\d.]+)\s*K', str(cap or ''))
    if m:
        e['capacityMicroScu'] = int(float(m.group(1)) * 1000)
    if e['type'] in COMPONENT:
        if 'Size' in dd: e['size'] = num(dd['Size'])
        if 'Grade' in dd: e['grade'] = dd['Grade']
        if 'Class' in dd: e['componentClass'] = dd['Class']
    if 'Damage Reduction' in dd:
        e['damageReduction'] = num(str(dd['Damage Reduction']).rstrip('%'))
    if 'Item Type' in dd:
        e['itemType'] = dd['Item Type']
    if e['type'] not in COMPONENT:
        stats = [{'label': k, 'value': str(v)} for k, v in dd.items()
                 if k not in ('Item Type', 'Damage Reduction', 'Manufacturer')]
        if stats:
            e['stats'] = stats
    e['ingredients'] = recipe(bp)
    return e


def recipe(bp):
    return ingredients(bp['Tiers'][0]['Requirements'], [])


def blueprints(d):
    res = {}
    for bp in json.load(open(f'{d}/blueprints.json', encoding='utf-8')):
        out = bp.get('Output')
        if isinstance(out, dict) and out.get('Name') and 'PLACEHOLDER' not in out['Name']:
            res[bp['Key']] = bp
    return res


def main(old_dir, new_dir):
    old, new = blueprints(old_dir), blueprints(new_dir)
    items = {}
    for fn in ('items.json', 'ship-items.json', 'fps-items.json'):
        try:
            for it in json.load(open(f'{new_dir}/{fn}', encoding='utf-8')):
                items.setdefault((it.get('stdItem') or {}).get('UUID') or it.get('reference'), it)
        except FileNotFoundError:
            pass
    labels = json.load(open(f'{new_dir}/labels.json', encoding='utf-8'))
    db = json.load(open(DB, encoding='utf-8'))
    keys = json.load(open(KEYS, encoding='utf-8'))
    index = {e['name']: i for i, e in enumerate(db)}
    new_names = {bp['Output']['Name'] for bp in new.values()}
    known = sorted({e['manufacturer'] for e in db if e.get('manufacturer')}, key=len)
    log = []
    added = set()

    def add(bp):
        name = bp['Output']['Name']
        it = items.get(bp['Output'].get('UUID'))
        if it is None:
            log.append(f'  ! objet introuvable pour {name}, ignoré')
            return
        e = build(bp, it)
        if e['manufacturer'] in (None, '', 'Unknown Manufacturer'):
            # Fabricant perdu : on prend celui écrit dans la description
            # (« Manufacturer: Lightning Bolt Co »), sous le nom déjà utilisé
            # dans la base (« Lightning Bolt Co. »).
            hint = ((it.get('stdItem') or {}).get('DescriptionData') or {}).get('Manufacturer')
            if hint:
                e['manufacturer'] = next((x for x in known if x.startswith(hint)), hint)
        db.append(e)
        added.add(name)
        index[name] = len(db) - 1
        cls = (bp['Output'].get('Class') or '').lower()
        cands = [k for k, v in labels.items() if v == name and k.lower().startswith('item_name')]
        cands.sort(key=lambda k: (cls not in k.lower(), k.endswith(',P'), k))
        if cands and name not in keys:
            keys[name] = cands[0]
        log.append(f'  + {name}')

    for key, bp in new.items():
        name = bp['Output']['Name']
        before = old.get(key)
        before_name = before['Output']['Name'] if before else None
        if name in index:
            e = db[index[name]]
            t = bp['Tiers'][0].get('CraftTimeSeconds')
            if e.get('craftTimeSeconds') != t or e.get('ingredients') != recipe(bp):
                if before and (before['Tiers'][0].get('CraftTimeSeconds'), recipe(before)) == (t, recipe(bp)):
                    continue  # recette identique entre les deux versions : on garde la fiche telle quelle
                e['craftTimeSeconds'] = t
                e['ingredients'] = recipe(bp)
                log.append(f'  ~ {name} (temps/ingrédients)')
            continue
        if before_name and before_name != name:
            log.append(f'  ~ renommé : {before_name} -> {name}')
        add(bp)

    for key, bp in old.items():
        name = bp['Output']['Name']
        if key not in new and name not in new_names and name in index:
            db[index[name]] = None
            log.append(f'  - {name}')
    db = [e for e in db if e is not None]
    for e in db:
        if e['name'] in added and e.get('manufacturer') in (None, '', 'Unknown Manufacturer'):
            # Variante d'une fiche existante (« Palatino Arms Raider Camo »,
            # « Skarnov Rifle Battery ») : même fabricant que la fiche de base.
            words = e['name'].replace('"', '').split()
            for n in range(len(words) - 1, 0, -1):
                prefix = ' '.join(words[:n])
                base = next((x for x in db if x is not e and x['name'].replace('"', '').startswith(prefix)
                             and x.get('manufacturer') not in (None, '', 'Unknown Manufacturer')), None)
                if base:
                    e['manufacturer'] = base['manufacturer']
                    break

    json.dump(db, open(DB, 'w', encoding='utf-8'), ensure_ascii=False, separators=(',', ':'))
    json.dump(keys, open(KEYS, 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
    print('\n'.join(log) or '  aucun changement')
    print(f'{len(db)} schémas')


if __name__ == '__main__':
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
