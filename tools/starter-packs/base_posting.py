"""
Posting rules of the English base (the founder's plan, 2026-10-30): the scenarios and their lines for the document × line types the base
had none for, written into base/Scenario.json and base/ScenarioConf.json (every row this script made before is replaced). Accounts are named
by role ("role:clients"); each country's pack gives the role its account (country.json "roles"). Run it, then build_country.py.

Places (ScenarioConf.Place): 1 / 2 the from / to side, 3 the line's element is the account, 4 / 5 the account the from / to side's element
holds (a cash desk's or bank's), 6 the account the line's element holds (a cash desk or bank line). Side: false debit, true credit.

Phase B, cash and bank: a money document brings money in (to its "to" side) or pays it out (from its "from" side). Its cash desk or bank
line is the money (the account that cash desk or bank holds) against the document's counter account; a client, supplier or staff line is
the counter account (its role) against the money side's account. A general journal entry posts each line's own debit and credit.
Phase A, the point of sale: see POS_SALES. Phase C, service invoices: see SALES_INVOICES, PURCHASE_INVOICES. Phase D, fixed assets bought: see ASSET_SCOPES. Phase E, customs and transfers: see CUSTOMS, TRANSFERS. Phase F, production and unit splits: see PRODUCTION. Reversal types get their original type's lines mirrored (sides swapped, debit and credit swapped), unless they have their own.
"""
import json
import os

BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'base')
MARK = ':posting:'
VALUE, VAT, WITH_VAT, DEBIT, CREDIT, COST = 'elements:174', 'elements:176', 'elements:180', 'elements:178', 'elements:179', 'elements:195'
VALUE_NAMES = {VALUE: (1, 'Line value'), VAT: (3, 'Line VAT'), WITH_VAT: (7, 'Line value with VAT'), DEBIT: (None, 'Line debit'), CREDIT: (None, 'Line credit'), COST: (8, 'Line stock cost')}
FROM, TO, ON_ELEMENT, FROM_ACCOUNT, TO_ACCOUNT, ELEMENT_ACCOUNT = 1, 2, 3, 4, 5, 6
SWAP_PLACE = {FROM: TO, TO: FROM, FROM_ACCOUNT: TO_ACCOUNT, TO_ACCOUNT: FROM_ACCOUNT}
SWAP_CENTRE = {1: 2, 2: 1}

# Phase B: the money documents, the way their money goes, and the account the money is set against.
MONEY_IN, MONEY_OUT = 'in', 'out'
MONEY_DOCUMENTS = {
    'RECEIPT': (MONEY_IN, 'role:clients'), 'JOURNAL_RECEIPT': (MONEY_IN, 'role:clients'),
    'ADVANCE': (MONEY_IN, 'role:client_advances'), 'JOURNAL_ADVANCE': (MONEY_IN, 'role:client_advances'),
    'POS_MONEY_IN': (MONEY_IN, 'role:clients'), 'POS_MONEY_FROM_STAFF': (MONEY_IN, 'role:employee_advances'),
    'PAYMENT': (MONEY_OUT, 'role:suppliers'), 'JOURNAL_PAYMENT': (MONEY_OUT, 'role:suppliers'),
    'PREPAYMENT': (MONEY_OUT, 'role:supplier_prepayments'), 'JOURNAL_PREPAYMENT': (MONEY_OUT, 'role:supplier_prepayments'),
    'EMPLOYEE_ADVANCE': (MONEY_OUT, 'role:employee_advances'), 'JOURNAL_EMPLOYEE_ADVANCE': (MONEY_OUT, 'role:employee_advances'),
    'WAGE_PAYMENT': (MONEY_OUT, 'role:wages_payable'), 'RENT_PAYMENT': (MONEY_OUT, 'role:rent_expense'),
    'POS_MONEY_OUT': (MONEY_OUT, 'role:employee_advances'), 'POS_REFUND': (MONEY_OUT, 'role:clients'),
}
# A line that names a client, supplier or employee is set on that party's account.
PARTY_LINES = {'CLIENT': 'role:clients', 'SUPPLIER': 'role:suppliers', 'STAFF': 'role:employee_advances'}
MONEY_LINES = ('CASH_DESK_ACCOUNT', 'BANK_ACCOUNT')

# Phase A, the point of sale: a bill has no "from" side and often no named client, so its lines take fixed accounts (no place): the sale's
# revenue (goods or services) and output VAT against the clients' account, value with VAT.
POS_SALES = {'POS_RECEIPT': {'ITEM': 'role:goods_sales', 'SERVICE_ITEM': 'role:services_sales'}}

# Phase C, invoices of services (and the goods on them), as the goods invoices post: a sale credits the revenue and the output VAT on its
# "from" side (the company) and debits the client on its "to" side; a purchase debits the cost and the input VAT on its "to" side and
# credits the supplier on its "from" side.
SALES_INVOICES = {'SALES_INVOICE_SERVICES': {'ITEM': 'role:goods_sales', 'SERVICE_ITEM': 'role:services_sales'}}
PURCHASE_INVOICES = {'PURCHASE_INVOICE_SERVICES': {'ITEM': 'role:goods_purchases', 'SERVICE_ITEM': 'role:services_purchases'}}

# Phase D, fixed assets bought: a fixed-asset line of a purchase invoice debits the asset's own account, by the asset's category (each
# category's scope; the engine takes the element's own category first), and the input VAT, against the supplier. Selling a fixed asset is
# parked by the founder (its remaining value and the result of the sale).
ASSET_SCOPES = {'elements:187': 'assets', 'elements:188': 'land', 'elements:189': 'buildings', 'elements:190': 'office', 'elements:191': 'vehicles',
                'elements:192': 'computers', 'elements:193': 'furniture', 'elements:194': 'intangible'}
ASSET_PURCHASES = ('PURCHASE_INVOICE_GOODS', 'PURCHASE_INVOICE_SERVICES')

# Phase E, customs and transfers. The duties, transport and agency costs of an import are part of what was imported (the goods' stock
# account, or the asset's account by its category), with the import VAT, against the customs office, agent or carrier. A transfer moves
# goods or assets the company keeps (between its places, to staff or clients holding them): the same account on both sides, each side
# carrying its cost centre when it is one (1 / 2: the from / to side is a cost centre).
CUSTOMS = {'CUSTOMS_DECLARATION': ('CUSTOMS', 'TRANSPORT', 'CUSTOMS_AGENCY'), 'CUSTOMS_DECLARATION_ASSETS': ('CUSTOMS_ASSETS', 'TRANSPORT_ASSETS', 'CUSTOMS_AGENCY_ASSETS')}
TRANSFERS = {
    'GOODS_TRANSFER': (1, 2), 'GOODS_TRANSFER_TO_CLIENT': (1, None), 'GOODS_TRANSFER_FROM_CLIENT': (None, 2), 'GOODS_TRANSFER_TO_STAFF': (1, None),
    'GOODS_TRANSFER_FROM_STAFF': (None, 2), 'GOODS_OUT_TO_STAFF': (None, None), 'GOODS_IN_FROM_STAFF': (None, None), 'GOODS_TRANSFER_STAFF_TO_CLIENT': (None, None),
    'POS_GOODS_IN': (None, None), 'POS_GOODS_OUT': (None, None),
}
ASSET_KINDS = ('FIXED_ASSET', 'FIXED_ASSET_SERVICE', 'CUSTOMS_ASSETS', 'TRANSPORT_ASSETS', 'CUSTOMS_AGENCY_ASSETS')


def phase_e(document, kind, account):
    """The lines of a customs declaration's or a transfer's scenario on the given account, or None."""
    if kind in CUSTOMS.get(document, ()):
        return [(False, VALUE, dict(chart=account, place=TO, centre=2)), (False, VAT, dict(chart='role:input_vat', place=TO, centre=2)),
                (True, WITH_VAT, dict(chart='role:suppliers', place=FROM, centre=2))]
    if document == 'GENERAL_JOURNAL_ENTRY' and kind == 'FIXED_ASSET':
        return [(False, DEBIT, dict(chart=account)), (True, CREDIT, dict(chart=account))]
    if document in TRANSFERS and kind in ('ITEM', 'FIXED_ASSET', 'FIXED_ASSET_SERVICE'):
        credit_centre, debit_centre = TRANSFERS[document]
        return [(False, VALUE, dict(chart=account, centre=debit_centre)), (True, VALUE, dict(chart=account, centre=credit_centre))]
    return None


# Phase F, production and unit splits (H11): each raw material leaves its stock account for the production account, the product comes onto
# its stock account from it, at what the stock cost (first in, first out), so the production account nets to 0; waste leaves stock
# for the waste expense account (the founder, 2026-10-30). Each side carries its place.
PRODUCTION = ('PRODUCTION', 'UNIT_SPLIT')


def phase_f(document, kind):
    """The lines of a production's or a unit split's scenario for one line type, or None."""
    if document not in PRODUCTION:
        return None
    if kind == 'RAW_MATERIAL':
        return [(False, COST, dict(chart='role:production', centre=1)), (True, COST, dict(chart='role:goods_purchases', centre=1))]
    if kind == 'WASTE':
        return [(False, COST, dict(chart='role:waste', centre=1)), (True, COST, dict(chart='role:goods_purchases', centre=1))]
    if kind == 'PRODUCED_ITEM':
        return [(False, COST, dict(chart='role:goods_purchases', centre=2)), (True, COST, dict(chart='role:production', centre=2))]
    return None


def load(model):
    return json.load(open(os.path.join(BASE, f'{model}.json'), encoding='utf-8'))


def save(model, rows):
    with open(os.path.join(BASE, f'{model}.json'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(rows, ensure_ascii=False, indent=1) + '\n')


def line(scenario, n, credit, value, place=None, chart=None, centre=None):
    number, name = VALUE_NAMES[value]
    return {'Key': f'{scenario}:{n}'.replace('scenario:', 'scenario_conf:', 1), 'IdSup': None, 'Active': True, 'ScenarioId': scenario, 'ChartId': chart,
            'ChartName': None, 'Side': credit, 'SideValue': 'Credit' if credit else 'Debit', 'Value': number, 'ValueName': name, 'ValueElementsId': value,
            'Place': place, 'CostCenterPlace': centre}


def phase_a(document, kind):
    """The lines of a point-of-sale bill's scenario for one line type, or None."""
    revenue = POS_SALES.get(document, {}).get(kind)
    return None if revenue is None else [(True, VALUE, dict(chart=revenue)), (True, VAT, dict(chart='role:output_vat')), (False, WITH_VAT, dict(chart='role:clients'))]


def phase_c(document, kind):
    """The lines of a service invoice's scenario for one line type, or None."""
    if (revenue := SALES_INVOICES.get(document, {}).get(kind)) is not None:
        return [(True, VALUE, dict(chart=revenue, place=FROM, centre=1)), (True, VAT, dict(chart='role:output_vat', place=FROM, centre=1)),
                (False, WITH_VAT, dict(chart='role:clients', place=TO, centre=1))]
    if (cost := PURCHASE_INVOICES.get(document, {}).get(kind)) is not None:
        return [(False, VALUE, dict(chart=cost, place=TO, centre=2)), (False, VAT, dict(chart='role:input_vat', place=TO, centre=2)),
                (True, WITH_VAT, dict(chart='role:suppliers', place=FROM, centre=2))]
    return None


def phase_b(document, kind):
    """The lines of a money document's scenario for one line type, or None when the rules say nothing about it."""
    if document == 'GENERAL_JOURNAL_ENTRY':
        account = {'ACCOUNT': dict(place=ON_ELEMENT), 'CASH_DESK_ACCOUNT': dict(place=ELEMENT_ACCOUNT), 'BANK_ACCOUNT': dict(place=ELEMENT_ACCOUNT)}.get(kind) \
            or ({'chart': PARTY_LINES[kind]} if kind in PARTY_LINES else None)
        return None if account is None else [(False, DEBIT, account), (True, CREDIT, account)]
    if document not in MONEY_DOCUMENTS:
        return None
    way, counter = MONEY_DOCUMENTS[document]
    if kind in MONEY_LINES:
        money, other = dict(place=ELEMENT_ACCOUNT), dict(chart=counter)
    elif kind in PARTY_LINES:
        money = dict(place=TO_ACCOUNT, centre=2) if way == MONEY_IN else dict(place=FROM_ACCOUNT, centre=1)
        other = dict(chart=PARTY_LINES[kind])
    else:
        return None
    return [(False, VALUE, money), (True, VALUE, other)] if way == MONEY_IN else [(False, VALUE, other), (True, VALUE, money)]


def balanced(lines):
    """Debit = credit for any amounts: value with VAT counts as value + VAT; a journal line's debit and credit balance across the document."""
    weight = {VALUE: (1, 0), VAT: (0, 1), WITH_VAT: (1, 1), COST: (1, 0)}
    if all(l['ValueElementsId'] in (DEBIT, CREDIT) for l in lines):
        return True
    total = [0, 0]
    for l in lines:
        w = weight[l['ValueElementsId']]
        sign = -1 if l['Side'] else 1
        total = [total[0] + sign * w[0], total[1] + sign * w[1]]
    return total == [0, 0]


def build():
    scenarios = [s for s in load('Scenario') if MARK not in s['Key']]
    lines = [c for c in load('ScenarioConf') if MARK not in c['Key']]
    types = {t['Key']: t for t in load('DocumentType')}
    kinds = {t['Key']: t for t in load('TransactionType')}
    scope = {c['TransactionTypeId']: c['ElementsId'] for c in load('TransactionTypeConf')}
    links = load('InterDocumentTypeTransactionType')
    has = {(s['DocumentTypeTransactionTypeId'], s['ElementsId']) for s in scenarios}

    added = 0
    for link in links:
        document, kind = link['DocumentTypeId'].split(':', 1)[1], link['TransactionTypeId'].split(':', 1)[1]
        rule = phase_a(document, kind) or phase_b(document, kind) or phase_c(document, kind) or phase_f(document, kind)
        where = scope.get(link['TransactionTypeId'])
        if rule is None or where is None or (link['Key'], where) in has or types[link['DocumentTypeId']].get('NeverPosted'):
            continue
        key = f'scenario{MARK}{document}:{kind}'
        scenarios.append({'Key': key, 'IdSup': None, 'Active': True, 'Name': kinds[link['TransactionTypeId']]['Name'],
                          'DocumentTypeTransactionTypeId': link['Key'], 'ElementsId': where})
        lines.extend(line(key, n, credit, value, **target) for n, (credit, value, target) in enumerate(rule, 1))
        has.add((link['Key'], where))
        added += 1

    for link in links:
        document, kind = link['DocumentTypeId'].split(':', 1)[1], link['TransactionTypeId'].split(':', 1)[1]
        if document not in ASSET_PURCHASES or kind != 'FIXED_ASSET':
            continue
        for where, category in ASSET_SCOPES.items():
            if (link['Key'], where) in has:
                continue
            key = f'scenario{MARK}{document}:{kind}:{category}'
            scenarios.append({'Key': key, 'IdSup': None, 'Active': True, 'Name': {'en': f'Fixed assets, {category}'},
                              'DocumentTypeTransactionTypeId': link['Key'], 'ElementsId': where})
            rule = [(False, VALUE, dict(chart=f'role:asset_{category}', place=TO, centre=2)), (False, VAT, dict(chart='role:input_vat', place=TO, centre=2)),
                    (True, WITH_VAT, dict(chart='role:suppliers', place=FROM, centre=2))]
            lines.extend(line(key, n, credit, value, **target) for n, (credit, value, target) in enumerate(rule, 1))
            has.add((link['Key'], where))
            added += 1

    for link in links:
        document, kind = link['DocumentTypeId'].split(':', 1)[1], link['TransactionTypeId'].split(':', 1)[1]
        targets = [(scope.get(link['TransactionTypeId']), 'role:goods_purchases', kind)] if kind not in ASSET_KINDS else \
            [(where, f'role:asset_{category}', f'{kind}:{category}') for where, category in ASSET_SCOPES.items()]
        for where, account, name in targets:
            rule = phase_e(document, kind, account)
            if rule is None or where is None or (link['Key'], where) in has:
                continue
            key = f'scenario{MARK}{document}:{name}'
            scenarios.append({'Key': key, 'IdSup': None, 'Active': True, 'Name': kinds[link['TransactionTypeId']]['Name'],
                              'DocumentTypeTransactionTypeId': link['Key'], 'ElementsId': where})
            lines.extend(line(key, n, credit, value, **target) for n, (credit, value, target) in enumerate(rule, 1))
            has.add((link['Key'], where))
            added += 1

    # Reversal types: their original's lines mirrored, where they have none of their own.
    link_of = {(l['DocumentTypeId'], l['TransactionTypeId']): l['Key'] for l in links}
    by_link = {l['Key']: l for l in links}
    mirrored = 0
    for reversal in (t for t in types.values() if t.get('IsReversal')):
        for s in [s for s in scenarios if by_link[s['DocumentTypeTransactionTypeId']]['DocumentTypeId'] == reversal['ReversedDocumentTypeId']]:
            kind = by_link[s['DocumentTypeTransactionTypeId']]['TransactionTypeId']
            target = link_of.get((reversal['Key'], kind))
            if target is None or (target, s['ElementsId']) in has:
                continue
            key = f"scenario{MARK}{reversal['Key'].split(':', 1)[1]}:{kind.split(':', 1)[1]}:{s['ElementsId'].split(':', 1)[1]}"
            scenarios.append({**s, 'Key': key, 'DocumentTypeTransactionTypeId': target})
            for n, c in enumerate([c for c in lines if c['ScenarioId'] == s['Key']], 1):
                lines.append({**c, 'Key': f'{key}:{n}'.replace('scenario:', 'scenario_conf:', 1), 'ScenarioId': key, 'Side': not c['Side'],
                              'SideValue': 'Debit' if c['Side'] else 'Credit', 'Place': SWAP_PLACE.get(c['Place'], c['Place']),
                              'CostCenterPlace': SWAP_CENTRE.get(c['CostCenterPlace'], c['CostCenterPlace'])})
            has.add((target, s['ElementsId']))
            mirrored += 1

    # The money documents keep the balance of their cash desk or bank side too: the value with VAT, as the partner side does.
    elements = {e['Key']: e for e in load('Elements')}
    balances = 0
    for model in ('DocumentTypeConfFrom', 'DocumentTypeConfTo'):
        sides = load(model)
        for side in sides:
            if side['DocumentTypeId'].split(':', 1)[1] in MONEY_DOCUMENTS and side.get('BalanceValueElementsId') is None                     and elements.get(side.get('ElementsId'), {}).get('Table') == 'tbl_cost_center':
                side['BalanceValueElementsId'] = WITH_VAT
                balances += 1
        save(model, sides)

    # The point of sale moves stock as a goods sale does: its bill takes the goods out of the place it sells from (its line's place), at their
    # value; its return puts them back.
    for model, document, key in (('DocumentTypeConfFrom', 'document_type:POS_RECEIPT', 'document_type_conf_from:pos_receipt'),
                                 ('DocumentTypeConfTo', 'document_type:POS_REVERSAL', 'document_type_conf_to:pos_return')):
        sides = load(model)
        if not any(r['Key'] == key for r in sides):
            sides.append({'Key': key, 'IdSup': None, 'Active': True, 'DocumentTypeId': document, 'ElementsId': 'elements:10', 'ProductValueElementsId': WITH_VAT})
            save(model, sides)

    unbalanced = [s['Key'] for s in scenarios if MARK in s['Key'] and not balanced([c for c in lines if c['ScenarioId'] == s['Key']])]
    assert not unbalanced, f'unbalanced scenarios: {unbalanced}'
    save('Scenario', scenarios)
    save('ScenarioConf', lines)
    print(f'{added} scenarios added, {mirrored} mirrored for reversal types, {balances} cash desk / bank sides keep a balance; {len(scenarios)} scenarios, {len(lines)} lines in the base')


if __name__ == '__main__':
    build()
