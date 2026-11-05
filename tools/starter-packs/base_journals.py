"""
Sheets and their journals (the founder, 2026-11-01). Run it after base_cost_layers.py, then base_recurring.py, before build_country.py.

A journal row moves the balance of what it is on: each journal type names, per line type, the amounts that go
on the element's own balance (client, supplier, cash desk, bank, staff). The journals' sides carry no balance: they would count the row twice.

A sheet is reversed by a reversal sheet holding the reversals of its journals, so the sheets get reversal types, General journal entry gets
one, and the journals' reversal types go under the reversal sheet. Each country's code follows its reversal pattern.
"""
import glob
import json
import os

TOOLS = os.path.dirname(os.path.abspath(__file__))
BASE = os.path.join(TOOLS, 'base')
MARK = ':journal-reversal:'
JOURNALS = ['JOURNAL_PAYMENT', 'JOURNAL_RECEIPT', 'JOURNAL_PREPAYMENT', 'JOURNAL_ADVANCE', 'JOURNAL_EMPLOYEE_ADVANCE', 'GENERAL_JOURNAL_ENTRY']
ON_ELEMENT = ['SUPPLIER', 'CLIENT', 'STAFF', 'CASH_DESK_ACCOUNT', 'BANK_ACCOUNT']
DEBIT, CREDIT = 'elements:178', 'elements:179'
SHEETS = {'JOURNAL_SHEET': 'Reversal of journal sheet', 'DEPRECIATION_SHEET': 'Reversal of depreciation sheet'}
GENERAL = 'GENERAL_JOURNAL_ENTRY'


def load(model):
    return json.load(open(os.path.join(BASE, f'{model}.json'), encoding='utf-8'))


def save(model, rows):
    with open(os.path.join(BASE, f'{model}.json'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(rows, ensure_ascii=False, indent=1) + '\n')


models = sorted(os.path.basename(f)[:-5] for f in glob.glob(os.path.join(BASE, '*.json')) if not os.path.basename(f).startswith('_'))
data = {m: [r for r in load(m) if MARK not in r['Key']] for m in models}

# The reversal types.
types = data['DocumentType'] = [t for t in data['DocumentType'] if t['Key'] not in {f'document_type:REVERSAL_{c}' for c in [*SHEETS, GENERAL]}]
by_key = {t['Key']: t for t in types}
template = by_key['document_type:REVERSAL_JOURNAL_PAYMENT']
new_types = []
for sheet, name in SHEETS.items():
    new_types.append({**template, 'Key': f'document_type:REVERSAL_{sheet}', 'IdSup': None, 'Code': f'REVERSAL_{sheet}', 'Name': {'en': name}, 'Description': {'en': name},
                      'ReversedDocumentTypeId': f'document_type:{sheet}', 'IsElement': True, 'SortOrder': by_key[f'document_type:{sheet}']['SortOrder'] + 200})
general = {**template, 'Key': f'document_type:REVERSAL_{GENERAL}', 'IdSup': 'document_type:REVERSAL_JOURNAL_SHEET', 'Code': f'REVERSAL_{GENERAL}',
           'Name': {'en': 'Reversal of general journal entry'}, 'Description': {'en': 'Reversal of general journal entry'}, 'ReversedDocumentTypeId': f'document_type:{GENERAL}',
           'IsElement': True, 'SortOrder': by_key[f'document_type:{GENERAL}']['SortOrder'] + 200}
new_types.append(general)
types += new_types
for t in types:
    if t['Key'] in {f'document_type:REVERSAL_{c}' for c in JOURNALS}:
        t['IdSup'] = 'document_type:REVERSAL_JOURNAL_SHEET'
    if t['Key'] == 'document_type:REVERSAL_DEPRECIATION_JOURNAL':
        t['IdSup'] = 'document_type:REVERSAL_DEPRECIATION_SHEET'

# Everything that hangs on General journal entry is copied onto its reversal, as base_recurring.py copies a journal onto its twin.
mapping = {f'document_type:{GENERAL}': general['Key']}
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
    data[m] += [{k: (mapping.get(v, v) if k != 'Key' and isinstance(v, str) else v) for k, v in r.items()} for r in added[m]]

data['InterDocumentTypeCategory'] += [{'Key': f'inter_document_type_category{MARK}{t["Code"]}', 'IdSup': None, 'Active': True, 'TypeId': t['Key'],
                                       'CategoryId': 'document_category:REVERSALS'} for t in new_types]

# The balances: on the element, none on the sides.
journal_keys = {f'document_type:{c}' for c in JOURNALS} | {f'document_type:REVERSAL_{c}' for c in JOURNALS}
for link in data['InterDocumentTypeTransactionType']:
    moves = link['DocumentTypeId'] in journal_keys and link['TransactionTypeId'].split(':')[1] in ON_ELEMENT
    link['ElementDebitValueElementsId'] = DEBIT if moves else None
    link['ElementCreditValueElementsId'] = CREDIT if moves else None
for model in ('DocumentTypeConfFrom', 'DocumentTypeConfTo'):
    for conf in data[model]:
        if conf['DocumentTypeId'] in journal_keys:
            conf.pop('BalanceValueElementsId', None)

for m in models:
    if data[m] != load(m):
        save(m, data[m])

# Names.
sq = {'Reversal of journal sheet': 'Storno i fletës kontabël', 'Reversal of depreciation sheet': 'Storno i fletës së amortizimit', 'Reversal of general journal entry': 'Storno i veprimit të përgjithshëm'}
it = {'Reversal of journal sheet': 'Storno del foglio contabile', 'Reversal of depreciation sheet': 'Storno del foglio di ammortamento', 'Reversal of general journal entry': 'Storno della scrittura generale'}
el = {'Reversal of journal sheet': 'Αντιλογισμός λογιστικού φύλλου', 'Reversal of depreciation sheet': 'Αντιλογισμός φύλλου αποσβέσεων', 'Reversal of general journal entry': 'Αντιλογισμός γενικής εγγραφής'}
path = os.path.join(BASE, 'names.tsv')
keys = {t['Key'] for t in new_types}
lines = [l for l in open(path, encoding='utf-8', newline='').read().split('\n') if l.split('\t', 1)[0] not in keys]
while lines and not lines[-1]:
    lines.pop()
for t in new_types:
    name = t['Name']['en']
    for field in ('Name', 'Description'):
        lines.append(f'{t["Key"]}\t{field}\t{name}\t{sq[name]}\t{it[name]}\t{el[name]}')
open(path, 'w', encoding='utf-8', newline='').write('\n'.join(lines) + '\n')

WORDS = {
    'Reversal of journal sheet': {'bg': 'Сторно на счетоводен лист', 'cs': 'Storno účetního listu', 'da': 'Tilbageførsel af kassekladde', 'de': 'Storno des Buchungsblatts', 'es': 'Anulación de hoja contable',
                                  'et': 'Pearaamatulehe tühistamine', 'fi': 'Tositelehden peruutus', 'fr': "Extourne de la feuille comptable", 'hr': 'Storno knjigovodstvenog lista', 'hu': 'Könyvelési lap sztornója',
                                  'lt': 'Apskaitos lapo atšaukimas', 'lv': 'Grāmatojuma lapas stornēšana', 'mk': 'Сторно на сметководствен лист', 'nl': 'Terugboeking van boekingsblad', 'pl': 'Storno arkusza księgowego',
                                  'pt': 'Estorno da folha contabilística', 'ro': 'Stornare a fișei contabile', 'sk': 'Storno účtovného listu', 'sl': 'Storno knjigovodskega lista', 'sv': 'Återföring av bokföringsblad'},
    'Reversal of depreciation sheet': {'bg': 'Сторно на амортизационен лист', 'cs': 'Storno odpisového listu', 'da': 'Tilbageførsel af afskrivningsark', 'de': 'Storno des Abschreibungsblatts', 'es': 'Anulación de hoja de amortización',
                                       'et': 'Amortisatsioonilehe tühistamine', 'fi': 'Poistolehden peruutus', 'fr': "Extourne de la feuille d'amortissement", 'hr': 'Storno lista amortizacije', 'hu': 'Értékcsökkenési lap sztornója',
                                       'lt': 'Nusidėvėjimo lapo atšaukimas', 'lv': 'Nolietojuma lapas stornēšana', 'mk': 'Сторно на амортизационен лист', 'nl': 'Terugboeking van afschrijvingsblad', 'pl': 'Storno arkusza amortyzacji',
                                       'pt': 'Estorno da folha de amortização', 'ro': 'Stornare a fișei de amortizare', 'sk': 'Storno odpisového listu', 'sl': 'Storno lista amortizacije', 'sv': 'Återföring av avskrivningsblad'},
    'Reversal of general journal entry': {'bg': 'Сторно на обща счетоводна статия', 'cs': 'Storno obecného účetního zápisu', 'da': 'Tilbageførsel af generel postering', 'de': 'Storno der allgemeinen Buchung', 'es': 'Anulación de asiento general',
                                          'et': 'Üldkande tühistamine', 'fi': 'Yleisen kirjauksen peruutus', 'fr': "Extourne de l'écriture générale", 'hr': 'Storno općeg knjiženja', 'hu': 'Általános könyvelési tétel sztornója',
                                          'lt': 'Bendrojo įrašo atšaukimas', 'lv': 'Vispārējā ieraksta stornēšana', 'mk': 'Сторно на општо книжење', 'nl': 'Terugboeking van algemene boeking', 'pl': 'Storno zapisu ogólnego',
                                          'pt': 'Estorno do lançamento geral', 'ro': 'Stornare a înregistrării generale', 'sk': 'Storno všeobecného zápisu', 'sl': 'Storno splošnega knjiženja', 'sv': 'Återföring av allmän verifikation'},
}
for lang in WORDS['Reversal of journal sheet']:
    path = os.path.join(BASE, 'translations', f'{lang}.tsv')
    rows = open(path, encoding='utf-8', newline='').read().split('\n')
    rows = [r for r in rows if r.split('\t', 1)[0] not in WORDS]
    while rows and not rows[-1]:
        rows.pop()
    rows += [f'{english}\t{words[lang]}' for english, words in WORDS.items()]
    open(path, 'w', encoding='utf-8', newline='').write('\n'.join(rows) + '\n')

# Profiles keep a reversal where they keep what it reverses.
for f in glob.glob(os.path.join(BASE, 'profiles', '*.json')):
    profile = json.load(open(f, encoding='utf-8'))
    kept = [c for c in profile.get('DocumentType', []) if c not in {t['Code'] for t in new_types}]
    kept += [t['Code'] for t in new_types if t['ReversedDocumentTypeId'].split(':')[1] in kept]
    if 'DocumentType' in profile:
        profile['DocumentType'] = kept
    with open(f, 'w', encoding='utf-8', newline='\n') as o:
        o.write(json.dumps(profile, ensure_ascii=False, indent=1) + '\n')


# Codes per country, in its reversal pattern.
def mark(plain, marked):
    i = marked.find(plain)
    return None if i < 0 else (marked[:i], marked[i + len(plain):])


for path in sorted(glob.glob(os.path.join(TOOLS, '*', 'country.json'))):
    text = open(path, encoding='utf-8').read()
    conf = json.loads(text)
    codes = conf['document_codes']
    for t in new_types:
        codes.pop(t['Code'], None)
    reversal = mark(codes['JOURNAL_PAYMENT'], codes['REVERSAL_JOURNAL_PAYMENT']) or ('R', '')
    taken = set(codes.values())
    for t in new_types:
        code = reversal[0] + codes[t['ReversedDocumentTypeId'].split(':')[1]] + reversal[1]
        while code in taken:
            code += 'R'
        codes[t['Code']] = code
        taken.add(code)
    newline = '\r\n' if '\r\n' in text else '\n'
    indent = 2 if '\n  "' in text.replace('\r\n', '\n') else 1
    open(path, 'w', encoding='utf-8', newline='').write(json.dumps(conf, ensure_ascii=False, indent=indent).replace('\n', newline) + newline)
print(f'{len(new_types)} reversal types; {sum(len(v) for v in added.values())} rows copied onto the general reversal')
