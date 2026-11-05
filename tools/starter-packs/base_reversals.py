"""
Reversal types for the customs declarations and the goods transfers (the founder, 2026-11-04): each a copy of its original's rows, as the
other reversal types are made: marked as reversing it, in the Reversals category, the same sides, line kinds, line categories, VAT group and
line fills, and its original's posting lines mirrored (debit for credit, the from place for the to place, as base_posting.py mirrors). A
transfer that took and received stock is reversed by taking off what it brought and putting back what it took (as the unit split's
reversal); a declaration moves no stock and its reversal none. Run it after base_posting.py and base_cost_layers.py (it skips what is
already there), then build_country.py.
"""
import glob, json, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))

ORIGINALS = ['CUSTOMS_DECLARATION', 'CUSTOMS_DECLARATION_ASSETS', 'GOODS_TRANSFER', 'GOODS_TRANSFER_TO_STAFF', 'GOODS_TRANSFER_FROM_STAFF',
             'GOODS_TRANSFER_TO_CLIENT', 'GOODS_TRANSFER_FROM_CLIENT', 'GOODS_TRANSFER_STAFF_TO_CLIENT']
TAKE, RECEIVE, PUT_BACK, TAKE_OFF = 1, 3, 4, 5
LAYERS = {TAKE: TAKE_OFF, RECEIVE: PUT_BACK}
SWAP_PLACE = {1: 2, 2: 1, 3: 4, 4: 3}
SWAP_CENTRE = {1: 2, 2: 1}
# "Reversal of …" in each language, before the original's own name.
PREFIX = {'sq': 'Stornim', 'it': 'Storno', 'el': 'Αντιλογισμός', 'bg': 'Сторно', 'cs': 'Storno', 'da': 'Tilbageførsel', 'de': 'Storno', 'es': 'Anulación',
          'et': 'Tühistamine', 'fi': 'Peruutus', 'fr': 'Annulation', 'hr': 'Storno', 'hu': 'Sztornó', 'lt': 'Atšaukimas', 'lv': 'Stornēšana', 'mk': 'Сторно',
          'nl': 'Storno', 'pl': 'Storno', 'pt': 'Anulação', 'ro': 'Stornare', 'sk': 'Storno', 'sl': 'Storno', 'sv': 'Makulering'}
# The reversal's number code: the original's with the country's reversal letter in front (as the production codes).
CODE_PREFIX = {'sq': 'K', 'de': 'S', 'fr': 'A', 'cs': 'S', 'sk': 'S', 'es': 'A', 'it': 'S', 'mk': 'С', 'pt': 'A', 'ro': 'S', 'el': 'Α'}


def load(model):
    text = open(f'base/{model}.json', encoding='utf-8').read()
    return text, json.loads(text)


def save(model, text, rows):
    nl = '\r\n' if '\r\n' in text else '\n'
    open(f'base/{model}.json', 'w', encoding='utf-8', newline='').write(json.dumps(rows, ensure_ascii=False, indent=1).replace('\n', nl) + nl)


files = {m: load(m) for m in ('DocumentType', 'DocumentTypeConfFrom', 'DocumentTypeConfTo', 'InterDocumentTypeCategory', 'InterDocumentTypeTransactionCategory',
                               'InterDocumentTypeTransactionType', 'InterDocumentTypeVatGroup', 'TransactionFill', 'Scenario', 'ScenarioConf')}
rows = {m: r for m, (_, r) in files.items()}
types = {t['Code']: t for t in rows['DocumentType'] if t.get('Code')}
added = 0
names = {}
for code in ORIGINALS:
    rev = f'REVERSAL_{code}'
    if rev in types or code not in types:
        continue
    original = types[code]
    english = original['Name']['en']
    names[rev] = english
    rows['DocumentType'].append({**original, 'Key': f'document_type:{rev}', 'Code': rev, 'Name': {'en': f'Reversal of {english[0].lower()}{english[1:]}'},
                                 'Description': {'en': f'Reversal of {english[0].lower()}{english[1:]}'}, 'IsReversal': True,
                                 'ReversedDocumentTypeId': original['Key'], 'AllowsPartialReversal': False, 'NeverPosted': False,
                                 'SortOrder': (original.get('SortOrder') or 0) + 0.5})
    for model, layers_of in (('DocumentTypeConfFrom', lambda m: LAYERS.get(m, 0)), ('DocumentTypeConfTo', lambda m: LAYERS.get(m, 0))):
        for side in [r for r in rows[model] if r.get('DocumentTypeId') == original['Key']]:
            rows[model].append({**side, 'Key': f"{side['Key']}:reversal", 'DocumentTypeId': f'document_type:{rev}', 'CostLayers': layers_of(side.get('CostLayers') or 0)})
    rows['InterDocumentTypeCategory'].append({'Key': f'inter_document_type_category:reversals:{rev}', 'IdSup': None, 'Active': True, 'TypeId': f'document_type:{rev}',
                                              'CategoryId': 'document_category:REVERSALS'})
    for model in ('InterDocumentTypeTransactionCategory', 'InterDocumentTypeVatGroup'):
        for r in [r for r in rows[model] if r.get('DocumentTypeId') == original['Key']]:
            rows[model].append({**r, 'Key': f"{r['Key']}:reversal", 'DocumentTypeId': f'document_type:{rev}'})
    for link in [l for l in rows['InterDocumentTypeTransactionType'] if l.get('DocumentTypeId') == original['Key']]:
        kind = link['TransactionTypeId'].split(':', 1)[1]
        new_link = f'inter_document_type_transaction_type:reversals:{rev}:{kind}'
        rows['InterDocumentTypeTransactionType'].append({**link, 'Key': new_link, 'DocumentTypeId': f'document_type:{rev}'})
        for fill in [f for f in rows['TransactionFill'] if f.get('DocumentTypeTransactionTypeId') == link['Key']]:
            rows['TransactionFill'].append({**fill, 'Key': f'transaction_fill:reversals:{rev}:{kind}', 'DocumentTypeTransactionTypeId': new_link})
        for scenario in [s for s in rows['Scenario'] if s.get('DocumentTypeTransactionTypeId') == link['Key']]:
            key = f"scenario:posting:{rev}:{kind}:{scenario['ElementsId'].split(':', 1)[1]}"
            rows['Scenario'].append({**scenario, 'Key': key, 'DocumentTypeTransactionTypeId': new_link})
            for n, line in enumerate([c for c in rows['ScenarioConf'] if c.get('ScenarioId') == scenario['Key']], 1):
                rows['ScenarioConf'].append({**line, 'Key': f'{key}:{n}'.replace('scenario:', 'scenario_conf:', 1), 'ScenarioId': key, 'Side': not line.get('Side'),
                                             'SideValue': 'Debit' if line.get('Side') else 'Credit', 'Place': SWAP_PLACE.get(line.get('Place'), line.get('Place')),
                                             'CostCenterPlace': SWAP_CENTRE.get(line.get('CostCenterPlace'), line.get('CostCenterPlace'))})
    added += 1
for model, (text, _) in files.items():
    save(model, text, rows[model])
print('reversal types added:', added)

# Names: English, and in each language the reversal word before the original's own name.
lines = open('base/names.tsv', encoding='utf-8').readlines()
have = {l.split('\t', 2)[0] + l.split('\t', 2)[1] for l in lines if l.count('\t') >= 2}
own = {l.split('\t')[0]: l.rstrip('\n').split('\t') for l in lines if l.split('\t')[1:2] == ['Name']}
for rev, english in names.items():
    original = own.get(f"document_type:{rev[len('REVERSAL_'):]}")
    for field in ('Name', 'Description'):
        if f'document_type:{rev}{field}' in have:
            continue
        local = [f'{PREFIX[l]}: {t}' if t else '' for l, t in zip(('sq', 'it', 'el'), (original or ['', '', '', '', '', ''])[3:6])]
        lines.append('\t'.join([f'document_type:{rev}', field, f'Reversal of {english[0].lower()}{english[1:]}', *local]) + '\n')
open('base/names.tsv', 'w', encoding='utf-8', newline='\n').writelines(lines)
for path in glob.glob('base/translations/*.tsv'):
    lang = os.path.basename(path)[:-4]
    if lang not in PREFIX:
        continue
    kept = open(path, encoding='utf-8').readlines()
    table = {l.split('\t', 1)[0]: l.rstrip('\n').split('\t', 1)[1] for l in kept if '\t' in l}
    for rev, english in names.items():
        key = f'Reversal of {english[0].lower()}{english[1:]}'
        if key not in table and english in table:
            kept.append(f'{key}\t{PREFIX[lang]}: {table[english]}\n')
    open(path, 'w', encoding='utf-8', newline='\n').writelines(kept)

# A number code per country.
n = 0
for path in sorted(glob.glob('*/country.json')):
    text = open(path, encoding='utf-8').read()
    conf = json.loads(text)
    codes = conf['document_codes']
    letter = 'Α' if os.path.basename(os.path.dirname(path)) == 'GR' else CODE_PREFIX.get(conf['language'], 'R')
    changed = False
    for code in ORIGINALS:
        rev = f'REVERSAL_{code}'
        if rev in codes or code not in codes:
            continue
        short = letter + codes[code]
        while short in codes.values():
            short += '2'
        codes[rev] = short
        changed = True
    if changed:
        nl = '\r\n' if '\r\n' in text else '\n'
        indent = 2 if '\n  "' in text.replace('\r\n', '\n') else 1
        open(path, 'w', encoding='utf-8', newline='').write(json.dumps(conf, ensure_ascii=False, indent=indent).replace('\n', nl) + nl)
        n += 1
print('countries with codes added:', n)
