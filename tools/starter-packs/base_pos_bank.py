"""
The point of sale's money can go to a bank (a payment method's link at the point of sale names where its money goes, card payments to the
bank account, say; the founder, 2026-11-04): the point-of-sale receipt of money gets the receipt's bank-account line (its posting lines and
line fill copied), and the point-of-sale refund, its reversal, that line mirrored (debit for credit, as base_posting.py mirrors). Run it
after base_posting.py and base_reversals.py (it skips what is already there), then build_country.py.
"""
import json, os
os.chdir(os.path.dirname(os.path.abspath(__file__)))
SWAP_PLACE = {1: 2, 2: 1, 3: 4, 4: 3}
SWAP_CENTRE = {1: 2, 2: 1}


def load(model):
    text = open(f'base/{model}.json', encoding='utf-8').read()
    return text, json.loads(text)


def save(model, text, rows):
    nl = '\r\n' if '\r\n' in text else '\n'
    open(f'base/{model}.json', 'w', encoding='utf-8', newline='').write(json.dumps(rows, ensure_ascii=False, indent=1).replace('\n', nl) + nl)


files = {m: load(m) for m in ('InterDocumentTypeTransactionType', 'Scenario', 'ScenarioConf', 'TransactionFill')}
links, scenarios, lines, fills = (files[m][1] for m in ('InterDocumentTypeTransactionType', 'Scenario', 'ScenarioConf', 'TransactionFill'))
source = next(l for l in links if l['DocumentTypeId'] == 'document_type:RECEIPT' and l['TransactionTypeId'] == 'transaction_type:BANK_ACCOUNT')
added = 0
for code, mirror in (('POS_MONEY_IN', False), ('POS_REFUND', True)):
    key = f'inter_document_type_transaction_type:pos_bank:{code}:BANK_ACCOUNT'
    if any(l['Key'] == key for l in links):
        continue
    links.append({**source, 'Key': key, 'DocumentTypeId': f'document_type:{code}'})
    for fill in [f for f in fills if f.get('DocumentTypeTransactionTypeId') == source['Key']]:
        fills.append({**fill, 'Key': f'transaction_fill:pos_bank:{code}:BANK_ACCOUNT', 'DocumentTypeTransactionTypeId': key})
    for scenario in [s for s in scenarios if s.get('DocumentTypeTransactionTypeId') == source['Key']]:
        new = f'scenario:posting:{code}:BANK_ACCOUNT'
        scenarios.append({**scenario, 'Key': new, 'DocumentTypeTransactionTypeId': key})
        for n, line in enumerate([c for c in lines if c.get('ScenarioId') == scenario['Key']], 1):
            copy = {**line, 'Key': f'scenario_conf:posting:{code}:BANK_ACCOUNT:{n}', 'ScenarioId': new}
            if mirror:
                copy.update({'Side': not line.get('Side'), 'SideValue': 'Debit' if line.get('Side') else 'Credit',
                             'Place': SWAP_PLACE.get(line.get('Place'), line.get('Place')), 'CostCenterPlace': SWAP_CENTRE.get(line.get('CostCenterPlace'), line.get('CostCenterPlace'))})
            lines.append(copy)
    added += 1
for model, (text, rows) in files.items():
    save(model, text, rows)
print('point-of-sale types given a bank line:', added)
