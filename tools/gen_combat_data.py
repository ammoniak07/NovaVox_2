"""
Génère src/NovaVox.Core/Combat/CombatData.json (tableau « 🎯 Pénétration »)
à partir de StarCitizenWiki/scunpacked-data (ships.json + ship-items.json).
À relancer après un patch du jeu qui change les armes ou le blindage :
  python tools/gen_combat_data.py ../scunpacked-data 4.10.2 src/NovaVox.Core/Combat/CombatData.json
"""
import json, re, collections, sys
if len(sys.argv)<4:
    sys.exit('usage: gen_combat_data.py <dossier scunpacked-data> <version du jeu> <CombatData.json de sortie>')
D=sys.argv[1]
ships=json.load(open(D+'/ships.json',encoding='utf-8')); items=json.load(open(D+'/ship-items.json',encoding='utf-8'))
T=['Physical','Energy','Distortion']
def r(x,n=2): return round(float(x or 0),n)
bad_cls=re.compile(r'_(Collector|Teach|Showdown|BTALA|Display|BIS\d+|Tutorial|AI|PU_AI|Unmanned|NoInterior)(_|$)',re.I)
ITEM_TYPE={(i.get('className') or '').lower():i.get('type') for i in items if i.get('className')}
def clean(n): return re.sub(r'\s+',' ',(n or '').replace('\xa0',' ')).strip()
def weapon_entry(i):
    si=i.get('stdItem') or {}; typ=i.get('type'); sub=i.get('subType')
    if typ=='WeaponGun':
        w=si.get('Weapon') or {}; modes=w.get('Modes') or []
        al=((w.get('Damage') or {}).get('Alpha')) or {}
        if not modes or not sum(al.get(t,0) for t in T): return None
        m=modes[0]; pel=max(1,int(m.get('PelletsPerShot') or 1))
        e={'kind':'gun','size':si.get('Size'),'manufacturer':(si.get('Manufacturer') or {}).get('Name'),
           'itemType':(si.get('DescriptionData') or {}).get('Item Type') or sub,
           'alphaPerProjectile':[r(al.get(t,0)/pel) for t in T],'pellets':pel,
           'rpm':r(m.get('RoundsPerMinute'),1),'dps':r((w.get('Damage') or {}).get('DpsTotal'),1),
           'speed':r((si.get('Ammunition') or {}).get('Speed'),0),'range':r(w.get('EffectiveRange') or (si.get('Ammunition') or {}).get('Range'),0)}
    elif typ=='Missile' and sub in ('Missile','Torpedo','Rocket'):
        mi=si.get('Missile') or {}; dm=mi.get('Damage') or {}
        if not sum(dm.get(t,0) for t in T): return None
        e={'kind':'torpedo' if sub=='Torpedo' else 'missile','size':si.get('Size'),'manufacturer':(si.get('Manufacturer') or {}).get('Name'),
           'itemType':(si.get('DescriptionData') or {}).get('Item Type') or sub,
           'alphaPerProjectile':[r(dm.get(t,0)) for t in T],'pellets':1,'rpm':0,'dps':0,
           'speed':r((mi.get('GCS') or {}).get('LinearSpeed'),0),'range':r(mi.get('Distance'),0)}
    else: return None
    if e['manufacturer'] and 'PLACEHOLDER' in e['manufacturer']: e['manufacturer']=None
    return e
# Plusieurs objets du jeu portent souvent le même nom : l'arme normale
# (ex. GATS_BallisticCannon_S3) et des variantes propres à un vaisseau, à un
# PNJ ou à l'exosquelette ATLS (Bengal_..._S8, ..._Idris_M, ..._ATLS, AAgun...).
# On garde l'arme normale sous son nom ; une variante aux stats différentes
# devient une entrée à part, « Nom (variante) » ; une variante identique est
# simplement rattachée à l'arme normale.
CANON=re.compile(r'^[A-Z]{3,5}_[A-Za-z]+_S\d+$')
def canon_score(i):
    cls=i['className']; si=i.get('stdItem') or {}
    desc_size=str((si.get('DescriptionData') or {}).get('Size') or '').strip()
    score=0 if desc_size==str(si.get('Size')) else 4          # taille de la description = taille réelle
    if not CANON.match(cls): score+=1                           # nom de classe « standard » (GATS_BallisticCannon_S3)
    if re.search(r'(LowPoly|Turret|Idris|Bengal|SecurityNetwork|ATLS|AAgun|Mounted|Bespoke|Collector)',cls,re.I): score+=2
    return score
TYPE_WORD=re.compile(r'(Cannon|Repeater|Gatling|Scatter[Gg]un|MassDriver|Gun|Rifle|Laser|Ballistic|Plasma|Neutron|Tachyon|Distortion)',re.I)
def variant_label(cls,e,base):
    parts=[]
    for k,t in enumerate(cls.split('_')):
        if k==0 and re.match(r'^[A-Z]{3,5}$',t): continue
        if re.match(r'^S\d+$',t): continue
        t2=TYPE_WORD.sub('',t)
        if t2: parts.append(t2)
    if base and e['size']!=base['size']: parts.insert(0,f"S{e['size']}")
    return ' '.join(parts) or 'variante'
groups=collections.defaultdict(list)
for i in items:
    si=i.get('stdItem') or {}
    n=clean(si.get('Name'))
    if not n or 'PLACEHOLDER' in n or not i.get('className'): continue
    if re.search(r'Dummy',i['className'],re.I): continue
    e=weapon_entry(i)
    if e: groups[n].append((i,e))
def stats_key(e): return (tuple(e['alphaPerProjectile']),e['pellets'],e['rpm'],e['size'],e['kind'])
out_w={}; WEAPON_BY_CLASS={}; VARIANTS=set()
for n,variants in groups.items():
    variants.sort(key=lambda ie:(canon_score(ie[0]),len(ie[0]['className'])))
    variants=[(i['className'],e) for i,e in variants]
    base_cls,base=variants[0]
    out_w[n]=dict(name=n,**base); WEAPON_BY_CLASS[base_cls.lower()]=n
    seen={stats_key(base):n}
    for cls,e in variants[1:]:
        k=stats_key(e)
        if k in seen: WEAPON_BY_CLASS[cls.lower()]=seen[k]; continue
        vn=f"{n} ({variant_label(cls,e,base)})"
        while vn in out_w: vn+="'"
        out_w[vn]=dict(name=vn,**e); seen[k]=vn; WEAPON_BY_CLASS[cls.lower()]=vn; VARIANTS.add(vn)
# Générateurs de bouclier : tous ont la même absorption/résistance (vérifié),
# seuls les PV, la régénération et les délais changent.
out_sh={}; SHIELD_BY_CLASS={}
for i in items:
    if i.get('type')!='Shield' or not i.get('className'): continue
    si=i.get('stdItem') or {}; sh=si.get('Shield') or {}
    if not sh.get('MaxShieldHealth') or re.search(r'Template',i['className'],re.I): continue
    dd=si.get('DescriptionData') or {}
    n=clean(si.get('Name'))
    if not n or 'PLACEHOLDER' in n:
        m=re.match(r'^SHLD_[A-Z]+_S\d+_(.+?)_SCItem',i['className'])
        n=f"Bouclier {m.group(1).replace('_',' ')}" if m else i['className']
    e={'name':n,'size':si.get('Size') or 0,'grade':dd.get('Grade'),'class':dd.get('Class'),
       'manufacturer':(si.get('Manufacturer') or {}).get('Name'),'hp':r(sh['MaxShieldHealth'],0),'regen':r(sh.get('MaxShieldRegen'),0),
       'damagedDelay':r(sh.get('DamagedDelay')),'downedDelay':r(sh.get('DownedDelay'))}
    if e['manufacturer'] and 'PLACEHOLDER' in e['manufacturer']: e['manufacturer']=None
    key=n
    if key in out_sh and (out_sh[key]['hp'],out_sh[key]['regen'],out_sh[key]['size'])!=(e['hp'],e['regen'],e['size']):
        key=e['name']=f"{n} (S{e['size']})"
    if key in out_sh and (out_sh[key]['hp'],out_sh[key]['regen'])!=(e['hp'],e['regen']):
        key=e['name']=f"{n} ({i['className']})"
    out_sh.setdefault(key,e); SHIELD_BY_CLASS[i['className'].lower()]=key
out_s={}
for v in ships:
    if not v.get('IsSpaceship'): continue
    if bad_cls.search(v['ClassName']) or 'Wikelo' in v['Name'] or "Teach's" in v['Name'] or 'Best In Show' in v['Name']: continue
    a=v.get('Armor') or {}
    d=a.get('Deflection')
    if not d or not a.get('Health'): continue
    if max(d.get(t,0) for t in T)>=100000: continue
    man=(v.get('Manufacturer') or {}).get('Name') or ''
    name=re.sub(r'\s+',' ',v['Name']).strip()
    short=name.split(' ',1)[1] if ' ' in name else name
    sa=(v.get('ShieldsTotal') or {}).get('Absorption') or {}
    weap=[]
    def walk(o):
        if isinstance(o,dict):
            if 'Weapons' in o and isinstance(o['Weapons'],list):
                for w in o['Weapons']:
                    if w.get('Name'): weap.append(re.sub(r'\s+',' ',w['Name'].replace('\xa0',' ')).strip())
            for x in o.values(): walk(x)
        elif isinstance(o,list):
            for x in o: walk(x)
    walk(v.get('Weaponry') or {})
    # Équipement complet (tourelles, PDC, missiles, torpilles) : le résumé
    # « Weaponry » est vide pour beaucoup de gros vaisseaux (Polaris...).
    def walk_loadout(o):
        if isinstance(o,dict):
            c=(o.get('ClassName') or '').lower()
            if c in WEAPON_BY_CLASS: weap.append(WEAPON_BY_CLASS[c])
            for x in o.values(): walk_loadout(x)
        elif isinstance(o,list):
            for x in o: walk_loadout(x)
    walk_loadout(v.get('Loadout') or [])
    mounts=[]
    def pretty(h):
        h=re.sub(r'^hardpoint_','',h,flags=re.I)
        return re.sub(r'\s+',' ',h.replace('_',' ')).strip()
    def walk_mounts(o,labels):
        if isinstance(o,dict):
            hp=o.get('HardpointName'); ct=[c.get('Type') for c in o.get('CompatibleTypes') or []]
            cls=(o.get('ClassName') or '').lower(); it=ITEM_TYPE.get(cls)
            here=labels+([pretty(hp)] if hp and not re.match(r'(hardpoint_)?class_\d+$',hp,re.I) else [])
            children=[x for x in (o.get('Loadout') or []) if isinstance(x,dict)]
            if hp and (it=='WeaponGun' or (not cls and 'WeaponGun' in ct and not children)):
                mounts.append({'id':o.get('PortId') or hp,'label':' › '.join(here[-2:]) or hp,'kind':'gun',
                    'minSize':o.get('MinSize') or 0,'maxSize':o.get('MaxSize') or 0,'count':1,
                    'stock':WEAPON_BY_CLASS.get(cls),'editable':bool(o.get('Editable'))})
                return
            if hp and (it=='Shield' or (not cls and 'Shield' in ct and not children)):
                mounts.append({'id':o.get('PortId') or hp,'label':' › '.join(here[-2:]) or hp,'kind':'shield',
                    'minSize':o.get('MinSize') or 0,'maxSize':o.get('MaxSize') or 0,'count':1,
                    'stock':SHIELD_BY_CLASS.get(cls),'editable':bool(o.get('Editable'))})
                return
            if hp and it=='MissileLauncher':
                slots=[x for x in children if 'Missile' in [c.get('Type') for c in x.get('CompatibleTypes') or []]]
                if slots:
                    first=next((WEAPON_BY_CLASS.get((x.get('ClassName') or '').lower()) for x in slots if x.get('ClassName')),None)
                    mounts.append({'id':o.get('PortId') or hp,'label':' › '.join(here[-2:]),'kind':'missile',
                        'minSize':slots[0].get('MinSize') or 0,'maxSize':slots[0].get('MaxSize') or 0,'count':len(slots),
                        'stock':first,'editable':bool(o.get('Editable')) or any(x.get('Editable') for x in slots)})
                    return
            for k,x in o.items():
                if k!='CompatibleTypes': walk_mounts(x,here)
        elif isinstance(o,list):
            for x in o: walk_mounts(x,labels)
    walk_mounts(v.get('Loadout') or [],[])
    e={'name':short,'fullName':name,'manufacturer':man,'size':v.get('Size'),
       'deflection':[r(d.get(t)) for t in T],'armorHp':r(a['Health'],0),
       'armorDamageMultiplier':[r((a.get('DamageMultipliers') or {}).get(t,1)) for t in T],
       'shieldHp':r(v.get('ShieldHp'),0),'shieldRegen':r((v.get('ShieldsTotal') or {}).get('Regen'),0),
       'hullHp':r(sum(float(x.get('DestructionDamage') or x.get('HP') or 0) for x in (v.get('DamageBeforeDestruction') or [])),0),
       'activeShields':int(((v.get('PowerPools') or {}).get('Shield') or {}).get('Size') or 2),
       'shieldResistance':[[r((((v.get('ShieldsTotal') or {}).get('Resistance') or {}).get(t) or {}).get('Minimum',0)),r((((v.get('ShieldsTotal') or {}).get('Resistance') or {}).get(t) or {}).get('Maximum',0))] for t in T],
       'shieldAbsorption':[[r((sa.get(t) or {}).get('Minimum',1)),r((sa.get(t) or {}).get('Maximum',1))] for t in T],
       'stockWeapons':sorted(set(weap)),'mounts':mounts}
    key=short
    if key in out_s:
        # même nom : on garde la première (les variantes identiques sont fréquentes)
        continue
    out_s[key]=e
# Variantes que personne n'utilise (PNJ, modèles « LowPoly »...) : retirées de la liste.
used={n for e in out_s.values() for n in e['stockWeapons']}|{m['stock'] for e in out_s.values() for m in e['mounts'] if m['stock']}
for vn in sorted(VARIANTS-used): del out_w[vn]
gv=sys.argv[2]
# Garde-fous : si un patch (ou un changement du parsing de scunpacked)
# déplace une valeur sur un autre champ, on s'arrête au lieu de produire
# une base fausse. Les valeurs de référence sont aussi vérifiées par les
# tests (CombatReferenceValuesTests).
problems=[]
if len(out_s)<150: problems.append(f'seulement {len(out_s)} vaisseaux')
if len(out_w)<120: problems.append(f'seulement {len(out_w)} armes')
bad_def=[e['name'] for e in out_s.values() if not any(e['deflection']) or max(e['deflection'])>5000]
if len(bad_def)>len(out_s)*0.05: problems.append(f'déflexion suspecte sur {len(bad_def)} vaisseaux: {bad_def[:10]}')
no_mounts=[e['name'] for e in out_s.values() if not e['mounts']]
if len(no_mounts)>len(out_s)*0.15: problems.append(f'{len(no_mounts)} vaisseaux sans emplacement d\'arme')
guns=[w for w in out_w.values() if w['kind']=='gun']
bad_guns=[w['name'] for w in guns if not w['rpm'] or not w['speed']]
if len(bad_guns)>len(guns)*0.05: problems.append(f'cadence/vitesse manquante sur {len(bad_guns)} canons: {bad_guns[:10]}')
if len(out_sh)<40: problems.append(f'seulement {len(out_sh)} boucliers')
no_sh=[e['name'] for e in out_s.values() if e['shieldHp']>0 and not any(m['kind']=='shield' for m in e['mounts'])]
if len(no_sh)>len(out_s)*0.1: problems.append(f'{len(no_sh)} vaisseaux sans emplacement de bouclier: {no_sh[:10]}')
if problems:
    sys.exit('ARRET, données suspectes :\n- '+'\n- '.join(problems))
data={'gameVersion':gv,'ships':sorted(out_s.values(),key=lambda e:-e['armorHp']),'weapons':sorted(out_w.values(),key=lambda e:(e['kind'],e['size'] or 0,e['name'])),'shields':sorted(out_sh.values(),key=lambda e:(e['size'],-e['hp'],e['name']))}
json.dump(data,open(sys.argv[3],'w',encoding='utf-8'),ensure_ascii=False,separators=(',',':'))
print('ships',len(out_s),'weapons',len(out_w),collections.Counter(w['kind'] for w in out_w.values()))
known=set(out_w); miss=collections.Counter(n for s in out_s.values() for n in s['stockWeapons'] if n not in known); print('armes de série inconnues',len(miss),list(miss)[:15])
