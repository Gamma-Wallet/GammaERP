"""
Cost layers of the English base (the founder's rule, 2026-10-30: first in, first out, per place; stock comes in only through a purchase
bill): what posting each document type does to the cost layers on its from / to side (DocumentTypeConfFrom / To.CostLayers), and the
"Transaction stock cost" amount (tbl_transaction.cost_lcu) the sales credit the product's value with instead of the sale value with VAT.
Run it, then build_country.py.

Modes: 0 none, 1 take the side's oldest layers, 2 make a layer (a purchase bill), 3 receive what the from side took, 4 put back what the
reversed line took, 5 take off the reversed line's own layer, 6 make a layer at what the document's raw materials cost (production), 7
receive what the from side took converted into the line's unit, each layer on its own with its unit cost changed by the same factor (a unit split).
"""
import json
import os

BASE = os.path.join(os.path.dirname(os.path.abspath(__file__)), 'base')
NONE, TAKE, MAKE, RECEIVE, PUT_BACK, TAKE_OFF, MAKE_FROM_USED, RECEIVE_CONVERTED = 0, 1, 2, 3, 4, 5, 6, 7
STOCK_COST = 'elements:195'

# document type: (from side, to side). Offers and contracts are never posted and keep 0.
LAYERS = {
    'SALES_INVOICE_GOODS': (TAKE, NONE), 'SALES_INVOICE_SERVICES': (TAKE, NONE), 'POS_RECEIPT': (TAKE, NONE),
    'CREDIT_NOTE_GOODS_SOLD': (NONE, PUT_BACK), 'CREDIT_NOTE_SERVICES_SOLD': (NONE, PUT_BACK), 'POS_REVERSAL': (NONE, PUT_BACK),
    'PURCHASE_INVOICE_GOODS': (NONE, MAKE), 'PURCHASE_INVOICE_SERVICES': (NONE, MAKE),
    'CREDIT_NOTE_GOODS_BOUGHT': (TAKE_OFF, NONE), 'CREDIT_NOTE_SERVICES_BOUGHT': (TAKE_OFF, NONE),
    'GOODS_TRANSFER': (TAKE, RECEIVE), 'GOODS_TRANSFER_TO_STAFF': (TAKE, RECEIVE), 'GOODS_TRANSFER_TO_CLIENT': (TAKE, RECEIVE),
    'GOODS_TRANSFER_FROM_CLIENT': (TAKE, RECEIVE), 'GOODS_TRANSFER_STAFF_TO_CLIENT': (TAKE, RECEIVE),
    # Each gets the place the goods come from or go back to (the founder, 2026-10-30), so goods never
    # appear or vanish without a purchase bill.
    'PRODUCTION': (TAKE, MAKE_FROM_USED), 'UNIT_SPLIT': (TAKE, RECEIVE_CONVERTED),
    'REVERSAL_UNIT_SPLIT': (TAKE_OFF, PUT_BACK),
    'POS_GOODS_IN': (TAKE, RECEIVE), 'GOODS_IN_FROM_STAFF': (TAKE, RECEIVE), 'POS_GOODS_OUT': (TAKE, RECEIVE), 'GOODS_OUT_TO_STAFF': (TAKE, RECEIVE),
}
# The product's value moves by what the goods cost (not what they were sold for): a sale credits it, its reversal debits it back, a reversal
# of a purchase credits what it takes off. Transfers stay within the company and leave it alone.
COST_VALUED = {(0, 'SALES_INVOICE_GOODS'), (0, 'SALES_INVOICE_SERVICES'), (0, 'POS_RECEIPT'), (1, 'CREDIT_NOTE_GOODS_SOLD'), (1, 'CREDIT_NOTE_SERVICES_SOLD'),
               (1, 'POS_REVERSAL'), (0, 'CREDIT_NOTE_GOODS_BOUGHT'), (0, 'CREDIT_NOTE_SERVICES_BOUGHT')} |     {(side, code) for side in (0, 1) for code in ('PRODUCTION', 'UNIT_SPLIT', 'REVERSAL_PRODUCTION', 'REVERSAL_UNIT_SPLIT')}


def load(model):
    return json.load(open(os.path.join(BASE, f'{model}.json'), encoding='utf-8'))


def save(model, rows):
    with open(os.path.join(BASE, f'{model}.json'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(json.dumps(rows, ensure_ascii=False, indent=1) + '\n')


elements = [e for e in load('Elements') if e['Key'] != STOCK_COST]
elements.append({'Key': STOCK_COST, 'Active': True, 'ElementsType': None, 'ElementsTypeId': 'elements_type:11', 'Name': {'en': 'Transaction stock cost'},
                 'Table': 'tbl_transaction', 'Record': 'cost_lcu', 'Code': 8})
elements[-1].pop('ElementsType')
save('Elements', elements)

# The place (a cost centre) those one-sided documents lacked.
PLACES = {0: ('POS_GOODS_OUT', 'GOODS_OUT_TO_STAFF'), 1: ('POS_GOODS_IN', 'GOODS_IN_FROM_STAFF')}

for side, model in ((0, 'DocumentTypeConfFrom'), (1, 'DocumentTypeConfTo')):
    rows = load(model)
    prefix = 'document_type_conf_from' if side == 0 else 'document_type_conf_to'
    for code in PLACES[side]:
        if not any(r['DocumentTypeId'] == f'document_type:{code}' for r in rows):
            rows.append({'Key': f'{prefix}:place:{code}', 'IdSup': None, 'Active': True, 'DocumentTypeId': f'document_type:{code}', 'ElementsId': 'elements:10'})
    for row in rows:
        code = row['DocumentTypeId'].split(':', 1)[1]
        row['CostLayers'] = LAYERS.get(code, (NONE, NONE))[side]
        if (side, code) in COST_VALUED:
            row['ProductValueElementsId'] = STOCK_COST
    missing = {c for c, modes in LAYERS.items() if modes[side] != NONE} - {r['DocumentTypeId'].split(':', 1)[1] for r in rows}
    assert not missing, (model, missing)
    save(model, rows)
print('cost layers set for', len(LAYERS), 'document types')
