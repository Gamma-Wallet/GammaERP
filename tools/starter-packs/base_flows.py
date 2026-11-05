"""
Document flows (the founder, 2026-11-01): offer to contract to invoice, offer to invoice, contract to invoice, for sales and purchases, and the
point-of-sale contract to its receipt. An offer is taken up once; a contract is billed again and again until its end date. External number is a
field of every document, so the property of that name goes. Run it before base_recurring.py and build_country.py.
"""
import json
import os

TOOLS = os.path.dirname(os.path.abspath(__file__))
BASE = os.path.join(TOOLS, 'base')


def load(model):
    return json.load(open(os.path.join(BASE, f'{model}.json'), encoding='utf-8'))


def save(model, rows):
    with open(os.path.join(BASE, f'{model}.json'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(rows, ensure_ascii=False, indent=1) + '\n')


info = load('DocumentTypeInfo')
kept = [r for r in info if r['Name'].get('en') != 'External number']
gone = {r['Key'] for r in info} - {r['Key'] for r in kept}
save('DocumentTypeInfo', kept)
path = os.path.join(BASE, 'names.tsv')
lines = [l for l in open(path, encoding='utf-8', newline='').read().split('\n') if l.split('\t', 1)[0] not in gone]
open(path, 'w', encoding='utf-8', newline='').write('\n'.join(lines))

end_date = {r['TypeId']: r['Key'] for r in kept if r['Name'].get('en') == 'End date'}
rows = []


def flow(source, target, once):
    key = f'document_type:{source}'
    rows.append({'Key': f'document_type_flow:{source}:{target}', 'IdSup': None, 'Active': True, 'SourceTypeId': key, 'TargetTypeId': f'document_type:{target}', 'Once': once,
                 'ValidUntilInfoId': end_date.get(key)})


for side, invoice in (('SALES', 'SALES_INVOICE'), ('PURCHASE', 'PURCHASE_INVOICE')):
    for kind in ('GOODS', 'SERVICES'):
        flow(f'{side}_OFFER_{kind}', f'{side}_CONTRACT_{kind}', True)
        flow(f'{side}_OFFER_{kind}', f'{invoice}_{kind}', True)
        flow(f'{side}_CONTRACT_{kind}', f'{invoice}_{kind}', False)
flow('POS_CONTRACT', 'POS_RECEIPT', False)
save('DocumentTypeFlow', rows)
print(f'{len(rows)} flows; {len(gone)} External number properties gone')
