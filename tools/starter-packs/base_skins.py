"""
The skins every company starts with (the founder, 2026-11-04): Harbour, in use, and Nightshift, the dark skin, each for the ERP and for the point of
sale. Reads base/skins/<name>.json ({name, kind, dark, tokens}) and writes base/Skin.json (one row per file). Safe to run again: it rewrites only
Skin.json. Then run build_country.py for every country.
"""
import glob, json, os

BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'base')
IN_USE = {'harbour'}

rows = []
for path in sorted(glob.glob(os.path.join(BASE, 'skins', '*.json'))):
    stem = os.path.basename(path)[:-5]
    skin = json.load(open(path, encoding='utf-8'))
    kind = skin.get('kind', 'erp')
    family = stem[:-4] if stem.endswith('-pos') else stem
    rows.append({'Key': f'skin:{stem.upper()}', 'Active': True, 'Name': skin['name'], 'Kind': kind, 'Dark': bool(skin.get('dark')),
                 'InUse': family in IN_USE, 'Tokens': json.dumps(skin['tokens'], ensure_ascii=False, separators=(',', ':'))})
with open(os.path.join(BASE, 'Skin.json'), 'w', encoding='utf-8', newline='\n') as f:
    f.write(json.dumps(rows, ensure_ascii=False, indent=1) + '\n')
print(f'{len(rows)} skins:', ', '.join(f"{r['Name']} {r['Kind']}{' (in use)' if r['InUse'] else ''}" for r in rows))
