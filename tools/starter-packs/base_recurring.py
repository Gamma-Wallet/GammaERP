"""
Journal types under the recurring sheets (H14, the founder 2026-10-30): each journal type of the journal, depreciation and revaluation sheets
gets a twin under the recurring sheet, with its reversal type where it has one. A twin is a copy of everything its journal has (sides, line
types, fills, VAT groups, properties, payment methods, posting scenarios and their lines), in the recurring sheet's category; it keeps its
journal's name (the sheet above it says it recurs). Each country's code is its journal's with the country's own mark for recurring (the one
its recurring journal sheet adds to its journal sheet's code); a reversal's follows the country's reversal codes. Every row this script made
before is replaced. Run it after base_posting.py, before base_cost_layers.py and build_country.py.
"""
import glob
import json
import os

TOOLS = os.path.dirname(os.path.abspath(__file__))
BASE = os.path.join(TOOLS, 'base')
MARK = ':recurring:'
# The sheets that have a reversal sheet: their recurring twin gets one too, and the twins' reversals go under it.
REVERSED_SHEETS = ['JOURNAL_SHEET', 'DEPRECIATION_SHEET']
SHEETS = {'JOURNAL_SHEET': 'RECURRING_JOURNAL_SHEET', 'DEPRECIATION_SHEET': 'RECURRING_DEPRECIATION_SHEET', 'REVALUATION_SHEET': 'RECURRING_REVALUATION_SHEET'}


def load(model):
    return json.load(open(os.path.join(BASE, f'{model}.json'), encoding='utf-8'))


def save(model, rows):
    with open(os.path.join(BASE, f'{model}.json'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(rows, ensure_ascii=False, indent=1) + '\n')


models = sorted(os.path.basename(f)[:-5] for f in glob.glob(os.path.join(BASE, '*.json')) if not os.path.basename(f).startswith('_'))
data = {m: [r for r in load(m) if MARK not in r['Key']] for m in models}
types = {t['Key']: t for t in data['DocumentType']}

# The new types: each sheet's journals, and their reversal types.
mapping = {}
new_types = []
for sheet, recurring in SHEETS.items():
    for journal in [t for t in data['DocumentType'] if t['IdSup'] == f'document_type:{sheet}']:
        code = journal['Code']
        key = f'document_type:RECURRING_{code}'
        mapping[journal['Key']] = key
        new_types.append({**journal, 'Key': key, 'Code': f'RECURRING_{code}', 'IdSup': f'document_type:{recurring}', 'SortOrder': journal['SortOrder'] + 100})
        for reversal in [t for t in data['DocumentType'] if t.get('ReversedDocumentTypeId') == journal['Key']]:
            rkey = f'document_type:REVERSAL_RECURRING_{code}'
            mapping[reversal['Key']] = rkey
            new_types.append({**reversal, 'Key': rkey, 'Code': f'REVERSAL_RECURRING_{code}', 'ReversedDocumentTypeId': key, 'SortOrder': reversal['SortOrder'] + 100,
                              'IdSup': f'document_type:REVERSAL_{recurring}' if sheet in REVERSED_SHEETS else None})
    if sheet in REVERSED_SHEETS:
        reversed_sheet = types[f'document_type:REVERSAL_{sheet}']
        rkey = f'document_type:REVERSAL_{recurring}'
        mapping[reversed_sheet['Key']] = rkey
        new_types.append({**reversed_sheet, 'Key': rkey, 'Code': f'REVERSAL_{recurring}', 'ReversedDocumentTypeId': f'document_type:{recurring}', 'SortOrder': reversed_sheet['SortOrder'] + 100})
data['DocumentType'] = [t for t in data['DocumentType'] if t['Key'] not in mapping.values()] + new_types

# Everything that hangs on a journal type (and, through it, on its links and scenarios) is copied onto its twin.
clone_models = [m for m in models if m not in ('DocumentType', 'InterDocumentTypeCategory')]
added = {m: [] for m in clone_models}
frontier = dict(mapping)
while frontier:
    found = {}
    for m in clone_models:
        for r in data[m] + added[m]:
            if r['Key'] in mapping:
                continue
            refs = [k for k, v in r.items() if k != 'Key' and isinstance(v, str) and v in frontier]
            if not refs:
                continue
            key = r['Key'].replace(':', MARK, 1) + ':' + frontier[r[refs[0]]].split(':', 1)[1]
            mapping[r['Key']] = found[r['Key']] = key
            copy = {k: (mapping.get(v, v) if k != 'Key' and isinstance(v, str) else v) for k, v in r.items()}
            copy['Key'] = key
            added[m].append(copy)
    frontier = found
for m in clone_models:
    # A copy made before all its targets were known points at them now.
    data[m] += [{k: (mapping.get(v, v) if k != 'Key' and isinstance(v, str) else v) for k, v in r.items()} for r in added[m]]

# Categories: a twin in the recurring sheets' category, a reversal twin in Reversals (as its original).
categories = [c for c in data['InterDocumentTypeCategory'] if MARK not in c['Key']]
for t in new_types:
    categories.append({'Key': f'inter_document_type_category{MARK}{t["Code"]}', 'IdSup': None, 'Active': True, 'TypeId': t['Key'],
                       'CategoryId': 'document_category:REVERSALS' if t.get('IsReversal') else 'document_category:RECURRING_JOURNAL'})
data['InterDocumentTypeCategory'] = categories
for m in models:
    if data[m] != load(m):
        save(m, data[m])

# Names: the twin's are its journal's.
source = {v: k for k, v in mapping.items() if k.startswith('document_type:')}
path = os.path.join(BASE, 'names.tsv')
lines = [l for l in open(path, encoding='utf-8') if l.split('\t', 1)[0] not in source]
names = {}
for l in lines:
    parts = l.split('\t', 2)
    if len(parts) == 3 and parts[0] in mapping:
        names.setdefault(parts[0], []).append(parts)
lines += [f'{new}\t{field}\t{rest}' for new, old in source.items() for _, field, rest in names.get(old, [])]
open(path, 'w', encoding='utf-8', newline='\n').writelines(lines)

# Profiles keep a twin where they keep its journal.
for f in glob.glob(os.path.join(BASE, 'profiles', '*.json')):
    profile = json.load(open(f, encoding='utf-8'))
    kept = [c for c in profile.get('DocumentType', []) if not c.startswith(('RECURRING_', 'REVERSAL_RECURRING_')) or c in SHEETS.values()]
    kept = [c for c in kept if c not in {t['Code'] for t in new_types}]
    profile['DocumentType'] = kept + [t['Code'] for t in new_types if types[source[t['Key']]]['Code'] in kept]
    with open(f, 'w', encoding='utf-8', newline='\n') as o:
        o.write(json.dumps(profile, ensure_ascii=False, indent=1) + '\n')


# Codes per country: the journal's code with the country's recurring mark; a reversal's in the country's reversal pattern.
def mark(plain, marked):
    """What a country adds to a code to mark it: (prefix, suffix), or None when the marked code is not the plain one with something around it."""
    i = marked.find(plain)
    return None if i < 0 else (marked[:i], marked[i + len(plain):])


for path in sorted(glob.glob(os.path.join(TOOLS, '*', 'country.json'))):
    text = open(path, encoding='utf-8').read()
    conf = json.loads(text)
    codes = conf['document_codes']
    for t in new_types:
        codes.pop(t['Code'], None)
    recurring = mark(codes['JOURNAL_SHEET'], codes['RECURRING_JOURNAL_SHEET']) or ('', 'R')
    reversal = mark(codes['JOURNAL_PAYMENT'], codes['REVERSAL_JOURNAL_PAYMENT']) or ('R', '')
    taken = set(codes.values())
    for t in new_types:
        journal = types[source[t['Key']]] if not t.get('IsReversal') else types[types[source[t['Key']]]['ReversedDocumentTypeId']]
        code = codes[t['ReversedDocumentTypeId'].split(':')[1]] if journal['Code'] in REVERSED_SHEETS else recurring[0] + codes[journal['Code']] + recurring[1]
        if t.get('IsReversal'):
            code = reversal[0] + code + reversal[1]
        while code in taken:
            code += recurring[1] or 'R'
        codes[t['Code']] = code
        taken.add(code)
    newline = '\r\n' if '\r\n' in text else '\n'
    indent = 2 if '\n  "' in text.replace('\r\n', '\n') else 1
    open(path, 'w', encoding='utf-8', newline='').write(json.dumps(conf, ensure_ascii=False, indent=indent).replace('\n', newline) + newline)
print(f'{len(new_types)} document types under the recurring sheets; {sum(len(v) for v in added.values())} rows copied')
