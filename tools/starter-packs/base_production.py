"""
Production and unit split as financial documents (H11, the founder's rule 2026-10-30): the document types "Production" and "Unit split" with
their reversal types, in a "Production" category, from one place (the raw materials) to one place (the product, the same or another), and
line types on products: "Raw material used" (out of the from side only, stock direction 2), "Produced item" (into the to side only, 3) and,
on a production only, "Waste" (out of the from side only and not part of what is made, 4). The rest of each type's configuration is the goods transfer's (its line fill, VAT group, line category). Every row this script made
before is replaced. Run it, then base_posting.py, base_cost_layers.py and build_country.py.
"""
import json
import os

BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'base')
MARK = ':production:'

# A production is not reversed (the founder, 2026-11-04): a mistaken one is corrected by a disassembly.
TYPES = [('PRODUCTION', 'Production', None),
         ('UNIT_SPLIT', 'Unit split', None), ('REVERSAL_UNIT_SPLIT', 'Reversal of unit split', 'UNIT_SPLIT')]
LINES = [('RAW_MATERIAL', 'Raw material used', 2), ('PRODUCED_ITEM', 'Produced item', 3), ('WASTE', 'Waste', 4)]
# Waste is lost in making: only a production has it, and it is an expense, not part of what is made (the founder, 2026-10-30).
ONLY = {'WASTE': ('PRODUCTION',)}
TEMPLATE = 'document_type:GOODS_TRANSFER'


def load(model):
    return json.load(open(os.path.join(BASE, f'{model}.json'), encoding='utf-8'))


def save(model, rows):
    with open(os.path.join(BASE, f'{model}.json'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(rows, ensure_ascii=False, indent=1) + '\n')


def fresh(model, keys=()):
    """The model's rows without what this script made before (by key mark, or by pointing at one of its keys)."""
    return [r for r in load(model) if MARK not in r['Key'] and not any(v in keys for v in r.values() if isinstance(v, str))]


def key(model, name):
    return f'{model}{MARK}{name}'


doc_keys = {f'document_type:{code}' for code, _, _ in TYPES}
line_keys = {f'transaction_type:{code}' for code, _, _ in LINES}
made = doc_keys | line_keys | {'document_category:PRODUCTION'}

# Document types: the transfer's numbering and flags; the reversal types reverse their original, whole or in part.
template = next(t for t in load('DocumentType') if t['Key'] == TEMPLATE)
types = fresh('DocumentType', made)
types = [t for t in types if t['Key'] not in doc_keys]
for n, (code, name, reverses) in enumerate(TYPES):
    types.append({**template, 'Key': f'document_type:{code}', 'Code': code, 'Name': {'en': name}, 'Description': {'en': name}, 'SortOrder': 90.0 + n,
                  'AllowsPartialReversal': False,  # a production is never reversed (the founder, 2026-11-04)
                  'IsReversal': reverses is not None,
                  'ReversedDocumentTypeId': f'document_type:{reverses}' if reverses else None, 'NeverPosted': False})
save('DocumentType', types)

categories = [c for c in load('DocumentCategory') if c['Key'] != 'document_category:PRODUCTION']
categories.append({'Key': 'document_category:PRODUCTION', 'IdSup': None, 'Active': True, 'IsElement': True, 'Code': 'PRODUCTION', 'Name': {'en': 'Production'},
                   'Description': {'en': 'Production'}, 'SortOrder': 11.0})
save('DocumentCategory', categories)

rows = fresh('InterDocumentTypeCategory', made)
rows += [{'Key': key('inter_document_type_category', code), 'IdSup': None, 'Active': True, 'TypeId': f'document_type:{code}',
          'CategoryId': 'document_category:REVERSALS' if reverses else 'document_category:PRODUCTION'} for code, _, reverses in TYPES]
save('InterDocumentTypeCategory', rows)

# What the transfer has per document type (its sides, line category, VAT group), copied onto each new type.
for model in ('DocumentTypeConfFrom', 'DocumentTypeConfTo', 'InterDocumentTypeTransactionCategory', 'InterDocumentTypeVatGroup'):
    rows = fresh(model, made)
    field = 'DocumentTypeId'
    for code, _, _ in TYPES:
        rows += [{**r, 'Key': key(model.lower(), f'{code}:{n}'), field: f'document_type:{code}'} for n, r in enumerate(x for x in load(model) if x.get(field) == TEMPLATE)]
    save(model, rows)

# The two line types, on products, each moving stock one way only.
item = next(t for t in load('TransactionType') if t['Key'] == 'transaction_type:ITEM')
kinds = fresh('TransactionType', made)
kinds = [t for t in kinds if t['Key'] not in line_keys]
kinds += [{**item, 'Key': f'transaction_type:{code}', 'Code': code, 'Name': {'en': name}, 'Description': {'en': name}, 'SortOrder': 40.0 + n}
          for n, (code, name, _) in enumerate(LINES)]
save('TransactionType', kinds)
item_conf = next(c for c in load('TransactionTypeConf') if c['TransactionTypeId'] == 'transaction_type:ITEM')
rows = fresh('TransactionTypeConf', made)
rows += [{**item_conf, 'Key': key('transaction_type_conf', code), 'TransactionTypeId': f'transaction_type:{code}', 'StockDirection': direction} for code, _, direction in LINES]
save('TransactionTypeConf', rows)
rows = fresh('InterTransactionTypeCategory', made)
rows += [{'Key': key('inter_transaction_type_category', code), 'IdSup': None, 'Active': True, 'TypeId': f'transaction_type:{code}', 'CategoryId': 'transaction_category:LINES'}
         for code, _, _ in LINES]
save('InterTransactionTypeCategory', rows)

# Every new document type takes both line types, filled as the transfer's goods lines are.
fill = next(f for f in load('TransactionFill') if f['DocumentTypeTransactionTypeId'] == 'inter_document_type_transaction_type:32')
links, fills = fresh('InterDocumentTypeTransactionType', made), fresh('TransactionFill')
for code, _, _ in TYPES:
    for line, _, _ in LINES:
        if line in ONLY and code not in ONLY[line]:
            continue
        link = key('inter_document_type_transaction_type', f'{code}:{line}')
        links.append({'Key': link, 'IdSup': None, 'Active': True, 'DocumentTypeId': f'document_type:{code}', 'TransactionTypeId': f'transaction_type:{line}'})
        fills.append({**fill, 'Key': key('transaction_fill', f'{code}:{line}'), 'DocumentTypeTransactionTypeId': link})
save('InterDocumentTypeTransactionType', links)
save('TransactionFill', fills)

# Names in every pack language (names.tsv: en, sq, it, el; base/translations/<lang>.tsv for the others).
NAMES = {
    'Production': ('Prodhim', 'Produzione', 'Παραγωγή'),
    'Reversal of production': ('Kthim prodhimi', 'Storno produzione', 'Αντιλογισμός παραγωγής'),
    'Unit split': ('Ndarje në njësi tjetër', 'Conversione unità', 'Μετατροπή μονάδας'),
    'Reversal of unit split': ('Kthim ndarjeje në njësi', 'Storno conversione unità', 'Αντιλογισμός μετατροπής μονάδας'),
    'Raw material used': ('Lëndë e parë e përdorur', 'Materia prima utilizzata', 'Πρώτη ύλη που χρησιμοποιήθηκε'),
    'Produced item': ('Produkt i prodhuar', 'Prodotto finito', 'Παραχθέν προϊόν'),
    'Waste': ('Firo', 'Scarto di produzione', 'Φύρα'),
}
TRANSLATIONS = {
    'bg': ('Производство', 'Сторно на производство', 'Преобразуване на мерна единица', 'Сторно на преобразуване на мерна единица', 'Вложена суровина', 'Произведен продукт', 'Брак'),
    'cs': ('Výroba', 'Storno výroby', 'Převod měrné jednotky', 'Storno převodu měrné jednotky', 'Spotřebovaný materiál', 'Vyrobený výrobek', 'Odpad'),
    'da': ('Produktion', 'Tilbageførsel af produktion', 'Enhedsomregning', 'Tilbageførsel af enhedsomregning', 'Forbrugt råvare', 'Produceret vare', 'Spild'),
    'de': ('Produktion', 'Storno Produktion', 'Einheitenumrechnung', 'Storno Einheitenumrechnung', 'Verbrauchter Rohstoff', 'Hergestelltes Erzeugnis', 'Schwund'),
    'es': ('Producción', 'Anulación de producción', 'Conversión de unidad', 'Anulación de conversión de unidad', 'Materia prima consumida', 'Producto fabricado', 'Merma'),
    'et': ('Tootmine', 'Tootmise tühistamine', 'Ühiku teisendus', 'Ühiku teisenduse tühistamine', 'Kasutatud tooraine', 'Toodetud toode', 'Kadu'),
    'fi': ('Valmistus', 'Valmistuksen peruutus', 'Yksikön muunto', 'Yksikön muunnon peruutus', 'Käytetty raaka-aine', 'Valmistettu tuote', 'Hukka'),
    'fr': ('Production', 'Annulation de production', "Conversion d'unité", "Annulation de conversion d'unité", 'Matière première consommée', 'Produit fabriqué', 'Freinte'),
    'hr': ('Proizvodnja', 'Storno proizvodnje', 'Pretvorba jedinice mjere', 'Storno pretvorbe jedinice mjere', 'Utrošena sirovina', 'Proizvedeni proizvod', 'Kalo'),
    'hu': ('Gyártás', 'Gyártás sztornója', 'Mértékegység-átváltás', 'Mértékegység-átváltás sztornója', 'Felhasznált alapanyag', 'Gyártott termék', 'Selejt'),
    'lt': ('Gamyba', 'Gamybos storno', 'Matavimo vieneto keitimas', 'Matavimo vieneto keitimo storno', 'Sunaudota žaliava', 'Pagamintas produktas', 'Nuostoliai'),
    'lv': ('Ražošana', 'Ražošanas stornēšana', 'Mērvienības pārrēķins', 'Mērvienības pārrēķina stornēšana', 'Izlietotā izejviela', 'Saražotais produkts', 'Zudumi'),
    'mk': ('Производство', 'Сторно производство', 'Претворање на мерна единица', 'Сторно претворање на мерна единица', 'Потрошена суровина', 'Произведен производ', 'Кало'),
    'nl': ('Productie', 'Storno productie', 'Eenheidsomrekening', 'Storno eenheidsomrekening', 'Verbruikte grondstof', 'Geproduceerd artikel', 'Derving'),
    'pl': ('Produkcja', 'Storno produkcji', 'Przeliczenie jednostki', 'Storno przeliczenia jednostki', 'Zużyty surowiec', 'Wyprodukowany wyrób', 'Ubytek'),
    'pt': ('Produção', 'Anulação de produção', 'Conversão de unidade', 'Anulação de conversão de unidade', 'Matéria-prima consumida', 'Produto fabricado', 'Quebra'),
    'ro': ('Producție', 'Stornare producție', 'Conversie unitate de măsură', 'Stornare conversie unitate de măsură', 'Materie primă consumată', 'Produs fabricat', 'Pierdere tehnologică'),
    'sk': ('Výroba', 'Storno výroby', 'Prevod mernej jednotky', 'Storno prevodu mernej jednotky', 'Spotrebovaný materiál', 'Vyrobený výrobok', 'Strata'),
    'sl': ('Proizvodnja', 'Storno proizvodnje', 'Pretvorba merske enote', 'Storno pretvorbe merske enote', 'Porabljena surovina', 'Izdelan izdelek', 'Kalo'),
    'sv': ('Tillverkning', 'Makulering av tillverkning', 'Enhetsomvandling', 'Makulering av enhetsomvandling', 'Förbrukad råvara', 'Tillverkad artikel', 'Spill'),
}
named = [(f'document_type:{c}', n) for c, n, _ in TYPES] + [(f'transaction_type:{c}', n) for c, n, _ in LINES] + [('document_category:PRODUCTION', 'Production')]
path = os.path.join(BASE, 'names.tsv')
GONE = {'document_type:REVERSAL_PRODUCTION'}  # removed 2026-11-04: a production is not reversed
kept = [l for l in open(path, encoding='utf-8') if l.split('\t', 1)[0] not in {k for k, _ in named} | GONE]
kept += [f'{k}\t{field}\t{n}\t' + '\t'.join(NAMES[n]) + '\n' for k, n in named for field in ('Name', 'Description')]
open(path, 'w', encoding='utf-8', newline='\n').writelines(kept)
english = list(NAMES)
for lang, texts in TRANSLATIONS.items():
    path = os.path.join(BASE, 'translations', f'{lang}.tsv')
    kept = [l for l in open(path, encoding='utf-8') if l.split('\t', 1)[0] not in english]
    kept += [f'{en}\t{text}\n' for en, text in zip(english, texts)]
    open(path, 'w', encoding='utf-8', newline='\n').writelines(kept)
print(f'{len(TYPES)} document types, {len(LINES)} line types')

# Each country's codes for the four types (its own style: its reversal prefix, its words).
CODES = {
    'sq': ('FPR', 'KFPR', 'FNJ', 'KFNJ'), 'de': ('PRD', 'SPRD', 'EUM', 'SEUM'), 'fr': ('PRD', 'APRD', 'CVU', 'ACVU'), 'cs': ('VYR', 'SVYR', 'PMJ', 'SPMJ'),
    'sk': ('VYR', 'SVYR', 'PMJ', 'SPMJ'), 'es': ('PRD', 'APRD', 'CUM', 'ACUM'), 'it': ('PRD', 'SPRD', 'CUM', 'SCUM'), 'mk': ('ПРО', 'СПРО', 'ПЕМ', 'СПЕМ'),
    'pt': ('PRD', 'APRD', 'CUM', 'ACUM'), 'ro': ('PRD', 'SPRD', 'CUM', 'SCUM'),
}
GREEK = ('ΠΑΡ', 'ΑΠΑΡ', 'ΜΜΜ', 'ΑΜΜΜ')
OTHERS = ('PRD', 'RPRD', 'UNS', 'RUNS')
import glob  # noqa: E402
for path in sorted(glob.glob(os.path.join(os.path.dirname(BASE), '*', 'country.json'))):
    text = open(path, encoding='utf-8').read()
    conf = json.loads(text)
    country = os.path.basename(os.path.dirname(path))
    codes = GREEK if country == 'GR' else CODES.get(conf['language'], OTHERS) if conf['document_codes'].get('JOURNAL_PAYMENT') != 'PAY' else OTHERS
    made = {code for code, _, _ in TYPES}
    for code, short in zip(('PRODUCTION', 'REVERSAL_PRODUCTION', 'UNIT_SPLIT', 'REVERSAL_UNIT_SPLIT'), codes):
        if code in made:
            conf['document_codes'][code] = short
    conf['document_codes'].pop('REVERSAL_PRODUCTION', None)
    newline = '\r\n' if '\r\n' in text else '\n'
    indent = 2 if '\n  "' in text.replace('\r\n', '\n') else 1
    open(path, 'w', encoding='utf-8', newline='').write(json.dumps(conf, ensure_ascii=False, indent=indent).replace('\n', newline) + newline)
print('codes in every country')
