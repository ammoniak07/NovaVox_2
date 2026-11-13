"""
Garde les noms de fabricants d'une version à l'autre des données du jeu.

ScDataDumper (outil qui produit les fichiers façon scunpacked-data depuis
le Data.p4k, voir tools/LISEZMOI_extraction.md) ne nomme pas toujours les
fabricants comme scunpacked-data : « BEH » au lieu de « Behring Applied
Technology », « VNC » au lieu de « Vanduul Clans »... Ce script reprend,
fabricant par fabricant (même UUID), le nom de l'ANCIENNE version ; seuls
les fabricants inconnus de l'ancienne version gardent le nouveau nom.
Il réécrit les fichiers du nouveau dossier sur place.

  python tools/normaliser_fabricants.py <ancien dossier> <nouveau dossier>
"""
import json, sys, collections

FILES = ['ships.json', 'ship-items.json', 'items.json', 'fps-items.json']
ZERO = '00000000-0000-0000-0000-000000000000'


def walk(o, f):
    if isinstance(o, dict):
        m = o.get('Manufacturer')
        if isinstance(m, dict):
            f(m)
        for v in o.values():
            walk(v, f)
    elif isinstance(o, list):
        for v in o:
            walk(v, f)


def uuid_of(m):
    return m.get('UUID') or m.get('Reference')


def main(old_dir, new_dir):
    # Un même UUID peut porter des noms différents selon le fichier (ex.
    # « Mirai » dans ships.json, « Musashi... » dans les objets) : on
    # reprend d'abord le nom du MÊME fichier de l'ancienne version, puis
    # celui des autres fichiers à défaut.
    per_file = {}
    for fn in FILES:
        names = {}
        try:
            data = json.load(open(f'{old_dir}/{fn}', encoding='utf-8'))
        except FileNotFoundError:
            data = None
        walk(data, lambda m: names.setdefault(uuid_of(m), m.get('Name')))
        per_file[fn] = names
    # Fabricant de chaque objet de l'ancienne version, par className.
    by_class = {}
    for fn in FILES:
        try:
            data = json.load(open(f'{old_dir}/{fn}', encoding='utf-8'))
        except FileNotFoundError:
            continue
        for entry in (data if isinstance(data, list) else []):
            if not isinstance(entry, dict):
                continue
            cls = entry.get('className') or entry.get('ClassName')
            m = (entry.get('stdItem') or entry).get('Manufacturer')
            if cls and isinstance(m, dict) and uuid_of(m) not in (None, ZERO):
                by_class.setdefault(cls.lower(), m.get('Name'))
    changed = collections.Counter()

    for fn in FILES:
        path = f'{new_dir}/{fn}'
        try:
            data = json.load(open(path, encoding='utf-8'))
        except FileNotFoundError:
            continue
        order = [fn] + [f for f in FILES if f != fn]

        def fix(m, cls=None):
            u = uuid_of(m)
            old = next((per_file[f][u] for f in order if per_file[f].get(u)), None)
            # Fabricant perdu dans la nouvelle version (UUID nul, « Unknown
            # Manufacturer ») : on reprend celui de l'ancienne version du
            # MÊME objet (même className).
            if (not u or u == ZERO) and cls:
                old = by_class.get(cls.lower())
            if old and old != m.get('Name'):
                changed[(m.get('Name'), old)] += 1
                m['Name'] = old

        for entry in (data if isinstance(data, list) else []):
            cls = entry.get('className') or entry.get('ClassName') if isinstance(entry, dict) else None
            walk(entry, lambda m, c=cls: fix(m, c))
        json.dump(data, open(path, 'w', encoding='utf-8'), ensure_ascii=False)
    for (new, old), n in changed.most_common():
        print(f'{n:5d}  {new!r} -> {old!r}')


if __name__ == '__main__':
    if len(sys.argv) < 3:
        sys.exit(__doc__)
    main(sys.argv[1], sys.argv[2])
