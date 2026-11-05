"""Adds the Disassembly document type to the starter packs, copied from Production's own rows (2026-11-04, the founder's design):
the product taken apart goes out (direction 2), what comes back comes in (3, cost mode 8: what was taken, shared by average cost), what is
lost carries only its value to the waste expense (5). Not reversed. Keys carry ':disassembly:' so base_production.py does not replace them.
Run it after base_production.py (it skips what is already there), then build_country.py."""
import glob, json, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

KINDS = {'RAW_MATERIAL': 'DISASSEMBLED_ITEM', 'PRODUCED_ITEM': 'RECOVERED_PART', 'WASTE': 'DISASSEMBLY_WASTE'}
DIRECTION = {'DISASSEMBLED_ITEM': 2, 'RECOVERED_PART': 3, 'DISASSEMBLY_WASTE': 5}
SORT = {'DISASSEMBLED_ITEM': 43.0, 'RECOVERED_PART': 44.0, 'DISASSEMBLY_WASTE': 45.0}
EN = {'DISASSEMBLY': 'Disassembly', 'DISASSEMBLED_ITEM': 'Product taken apart', 'RECOVERED_PART': 'Recovered component', 'DISASSEMBLY_WASTE': 'Disassembly waste'}
# names.tsv: sq, it, el; translations/<lang>.tsv: the rest (and sq, it, el too, as for production).
T = {
    'sq': ('Çmontim', 'Produkt i çmontuar', 'Përbërës i rikuperuar', 'Firo çmontimi'),
    'it': ('Smontaggio', 'Prodotto smontato', 'Componente recuperato', 'Scarto di smontaggio'),
    'el': ('Αποσυναρμολόγηση', 'Προϊόν που αποσυναρμολογήθηκε', 'Ανακτημένο συστατικό', 'Φύρα αποσυναρμολόγησης'),
    'bg': ('Разглобяване', 'Разглобен продукт', 'Възстановен компонент', 'Фира от разглобяване'),
    'cs': ('Demontáž', 'Demontovaný výrobek', 'Získaná součást', 'Ztráta při demontáži'),
    'da': ('Adskillelse', 'Adskilt produkt', 'Genvundet komponent', 'Spild ved adskillelse'),
    'de': ('Demontage', 'Zerlegtes Produkt', 'Zurückgewonnene Komponente', 'Demontageverlust'),
    'es': ('Desmontaje', 'Producto desmontado', 'Componente recuperado', 'Merma de desmontaje'),
    'et': ('Lahtivõtmine', 'Lahtivõetud toode', 'Taastatud komponent', 'Lahtivõtmise kadu'),
    'fi': ('Purku', 'Purettu tuote', 'Talteen otettu osa', 'Purkuhävikki'),
    'fr': ('Démontage', 'Produit démonté', 'Composant récupéré', 'Perte au démontage'),
    'hr': ('Rastavljanje', 'Rastavljeni proizvod', 'Vraćena komponenta', 'Kalo pri rastavljanju'),
    'hu': ('Szétszerelés', 'Szétszerelt termék', 'Visszanyert összetevő', 'Szétszerelési veszteség'),
    'lt': ('Išardymas', 'Išardytas gaminys', 'Atgauta sudedamoji dalis', 'Išardymo nuostoliai'),
    'lv': ('Demontāža', 'Demontēts produkts', 'Atgūta sastāvdaļa', 'Demontāžas zudumi'),
    'mk': ('Расклопување', 'Расклопен производ', 'Повратена компонента', 'Кало при расклопување'),
    'nl': ('Demontage', 'Gedemonteerd product', 'Teruggewonnen onderdeel', 'Demontageverlies'),
    'pl': ('Demontaż', 'Zdemontowany produkt', 'Odzyskany składnik', 'Ubytek przy demontażu'),
    'pt': ('Desmontagem', 'Produto desmontado', 'Componente recuperado', 'Quebra de desmontagem'),
    'ro': ('Dezasamblare', 'Produs dezasamblat', 'Componentă recuperată', 'Pierdere la dezasamblare'),
    'sk': ('Demontáž', 'Demontovaný výrobok', 'Získaná súčasť', 'Strata pri demontáži'),
    'sl': ('Razstavljanje', 'Razstavljen izdelek', 'Vrnjena sestavina', 'Kalo pri razstavljanju'),
    'sv': ('Demontering', 'Demonterad artikel', 'Återvunnen komponent', 'Spill vid demontering'),
}
CODES = {'sq': 'FCM', 'de': 'DEM', 'fr': 'DMT', 'cs': 'DEM', 'sk': 'DEM', 'es': 'DSM', 'pt': 'DSM', 'it': 'SMT', 'ro': 'DZA', 'mk': 'ДЕМ', 'el': 'ΑΠΣ'}

def load(f):
    text = open(f, encoding='utf-8').read()
    return text, json.loads(text)

def save(f, text, rows):
    nl = '\r\n' if '\r\n' in text else '\n'
    open(f, 'w', encoding='utf-8', newline='').write(json.dumps(rows, ensure_ascii=False, indent=1).replace('\n', nl) + nl)

def swap(v):
    """The Production row's references, pointed at Disassembly's."""
    if isinstance(v, str):
        if v == 'document_type:PRODUCTION':
            return 'document_type:DISASSEMBLY'
        for old, new in KINDS.items():
            if v == f'transaction_type:{old}':
                return f'transaction_type:{new}'
        if ':production:PRODUCTION' in v or v.startswith('scenario:posting:PRODUCTION:') or v.startswith('scenario_conf:posting:PRODUCTION:'):
            v = v.replace(':production:PRODUCTION', ':disassembly:DISASSEMBLY').replace('scenario:posting:PRODUCTION:', 'scenario:posting:DISASSEMBLY:') \
                 .replace('scenario_conf:posting:PRODUCTION:', 'scenario_conf:posting:DISASSEMBLY:')
            for old, new in KINDS.items():
                v = v.replace(f':{old}', f':{new}')
        return v
    if isinstance(v, dict):
        return {k: swap(x) for k, x in v.items()}
    if isinstance(v, list):
        return [swap(x) for x in v]
    return v

added = {}
for f in sorted(glob.glob('base/*.json')):
    text, rows = load(f)
    if not isinstance(rows, list) or any(isinstance(r, dict) and 'DISASSEMBLY' in (r.get('Key') or '') for r in rows):
        continue
    new = []
    for r in rows:
        if not isinstance(r, dict):
            continue
        k = r.get('Key') or ''
        own_doc = k == 'document_type:PRODUCTION' or ':production:PRODUCTION' in k or k.startswith('scenario:posting:PRODUCTION:') or k.startswith('scenario_conf:posting:PRODUCTION:')
        own_kind = k in (f'transaction_type:{o}' for o in KINDS) or k in (f'transaction_type_conf:production:{o}' for o in KINDS) \
            or k in (f'inter_transaction_type_category:production:{o}' for o in KINDS)
        if not (own_doc or own_kind):
            continue
        c = swap(r)
        if own_kind:
            old = next(o for o in KINDS if k.endswith(o))
            kind = KINDS[old]
            c['Key'] = k.replace(':production:', ':disassembly:').replace(old, kind)
            if 'Code' in c:
                c['Code'] = kind
            for field in ('Name', 'Description'):
                if isinstance(c.get(field), dict):
                    c[field] = {'en': EN[kind]}
            if 'SortOrder' in c and k.startswith('transaction_type:'):
                c['SortOrder'] = SORT[kind]
            if 'StockDirection' in c:
                c['StockDirection'] = DIRECTION[kind]
        if k == 'document_type:PRODUCTION':
            c['Code'] = 'DISASSEMBLY'
            c['Name'] = {'en': EN['DISASSEMBLY']}
            c['Description'] = {'en': EN['DISASSEMBLY']}
            for flag in ('SendsToProduction', 'SellsProducedProduct', 'ProducedOnProductionScreen', 'IsReversal', 'AllowsPartialReversal'):
                if flag in c:
                    c[flag] = False
            c['ProductionDocumentTypeId'] = None if 'ProductionDocumentTypeId' in c else c.get('ProductionDocumentTypeId')
            c['ReversedDocumentTypeId'] = None if 'ReversedDocumentTypeId' in c else c.get('ReversedDocumentTypeId')
            if 'SortOrder' in c and isinstance(c['SortOrder'], (int, float)):
                c['SortOrder'] = c['SortOrder'] + 0.5
        if k == 'documenttypeconfto:production:PRODUCTION:0':
            c['CostLayers'] = 8
        if k.startswith('scenario:posting:PRODUCTION:') and isinstance(c.get('Name'), dict):
            kind = KINDS[k.split(':')[3]]
            c['Name'] = {'en': EN[kind]}
        # A disassembly's waste is not taken out of stock: its value goes from the production account to the waste expense.
        if k.startswith('scenario_conf:posting:PRODUCTION:WASTE:') and c.get('ChartId') == 'role:goods_purchases':
            c['ChartId'] = 'role:production'
        new.append(c)
    if new:
        rows.extend(new)
        save(f, text, rows)
        added[f] = len(new)
print('rows added:', added)

# Names.
lines = open('base/names.tsv', encoding='utf-8').readlines()
if not any(l.startswith('document_type:DISASSEMBLY\t') for l in lines):
    for i, key in enumerate(('document_type:DISASSEMBLY', 'transaction_type:DISASSEMBLED_ITEM', 'transaction_type:RECOVERED_PART', 'transaction_type:DISASSEMBLY_WASTE')):
        code = key.split(':')[1]
        for field in ('Name', 'Description'):
            lines.append(f"{key}\t{field}\t{EN[code]}\t{T['sq'][i]}\t{T['it'][i]}\t{T['el'][i]}\n")
    open('base/names.tsv', 'w', encoding='utf-8', newline='\n').writelines(lines)
order = ('DISASSEMBLY', 'DISASSEMBLED_ITEM', 'RECOVERED_PART', 'DISASSEMBLY_WASTE')
for lang, texts in T.items():
    p = f'base/translations/{lang}.tsv'
    if not os.path.exists(p):
        continue
    kept = open(p, encoding='utf-8').readlines()
    have = {l.split('\t', 1)[0] for l in kept}
    kept += [f'{EN[c]}\t{t}\n' for c, t in zip(order, texts) if EN[c] not in have]
    open(p, 'w', encoding='utf-8', newline='\n').writelines(kept)

# A number code per country.
n = 0
for path in sorted(glob.glob('*/country.json')):
    text = open(path, encoding='utf-8').read()
    conf = json.loads(text)
    if 'DISASSEMBLY' in conf['document_codes']:
        continue
    conf['document_codes']['DISASSEMBLY'] = CODES.get(conf['language'], 'DIS')
    nl = '\r\n' if '\r\n' in text else '\n'
    indent = 2 if '\n  "' in text.replace('\r\n', '\n') else 1
    open(path, 'w', encoding='utf-8', newline='').write(json.dumps(conf, ensure_ascii=False, indent=indent).replace('\n', nl) + nl)
    n += 1
print('country codes added:', n)
