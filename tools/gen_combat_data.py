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
WEAPON_BY_CLASS={}
for i in items:
    si=i.get('stdItem') or {}
    if i.get('type') in ('WeaponGun','Missile') and i.get('className') and si.get('Name'):
        WEAPON_BY_CLASS[i['className'].lower()]=re.sub(r'\s+',' ',si['Name'].replace('\xa0',' ')).strip()
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
    e={'name':short,'fullName':name,'manufacturer':man,'size':v.get('Size'),
       'deflection':[r(d.get(t)) for t in T],'armorHp':r(a['Health'],0),
       'armorDamageMultiplier':[r((a.get('DamageMultipliers') or {}).get(t,1)) for t in T],
       'shieldHp':r(v.get('ShieldHp'),0),
       'shieldAbsorption':[[r((sa.get(t) or {}).get('Minimum',1)),r((sa.get(t) or {}).get('Maximum',1))] for t in T],
       'stockWeapons':sorted(set(weap))}
    key=short
    if key in out_s:
        # même nom : on garde la première (les variantes identiques sont fréquentes)
        continue
    out_s[key]=e
out_w={}
for i in items:
    si=i.get('stdItem') or {}
    n=re.sub(r'\s+',' ',(si.get('Name') or '').replace('\xa0',' ')).strip()
    if not n or 'PLACEHOLDER' in n: continue
    typ=i.get('type'); sub=i.get('subType')
    if typ=='WeaponGun':
        w=si.get('Weapon') or {}; modes=w.get('Modes') or []
        al=((w.get('Damage') or {}).get('Alpha')) or {}
        if not modes or not sum(al.get(t,0) for t in T): continue
        m=modes[0]; pel=max(1,int(m.get('PelletsPerShot') or 1))
        e={'name':n,'kind':'gun','size':si.get('Size'),'manufacturer':(si.get('Manufacturer') or {}).get('Name'),
           'itemType':(si.get('DescriptionData') or {}).get('Item Type') or sub,
           'alphaPerProjectile':[r(al.get(t,0)/pel) for t in T],'pellets':pel,
           'rpm':r(m.get('RoundsPerMinute'),1),'dps':r((w.get('Damage') or {}).get('DpsTotal'),1),
           'speed':r((si.get('Ammunition') or {}).get('Speed'),0),'range':r(w.get('EffectiveRange') or (si.get('Ammunition') or {}).get('Range'),0)}
    elif typ=='Missile' and sub in ('Missile','Torpedo','Rocket'):
        mi=si.get('Missile') or {}; dm=mi.get('Damage') or {}
        if not sum(dm.get(t,0) for t in T): continue
        e={'name':n,'kind':'torpedo' if sub=='Torpedo' else 'missile','size':si.get('Size'),'manufacturer':(si.get('Manufacturer') or {}).get('Name'),
           'itemType':(si.get('DescriptionData') or {}).get('Item Type') or sub,
           'alphaPerProjectile':[r(dm.get(t,0)) for t in T],'pellets':1,'rpm':0,'dps':0,
           'speed':r((mi.get('GCS') or {}).get('LinearSpeed'),0),'range':r(mi.get('Distance'),0)}
    else: continue
    if e['manufacturer'] and 'PLACEHOLDER' in e['manufacturer']: e['manufacturer']=None
    if n not in out_w: out_w[n]=e
gv=sys.argv[2]
data={'gameVersion':gv,'ships':sorted(out_s.values(),key=lambda e:-e['armorHp']),'weapons':sorted(out_w.values(),key=lambda e:(e['kind'],e['size'] or 0,e['name']))}
json.dump(data,open(sys.argv[3],'w',encoding='utf-8'),ensure_ascii=False,separators=(',',':'))
print('ships',len(out_s),'weapons',len(out_w),collections.Counter(w['kind'] for w in out_w.values()))
known=set(out_w); miss=collections.Counter(n for s in out_s.values() for n in s['stockWeapons'] if n not in known); print('armes de série inconnues',len(miss),list(miss)[:15])
