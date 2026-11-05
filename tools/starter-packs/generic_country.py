"""
Writes the country.json of a country whose pack takes Gamma's generic chart (generic/chart-generic.tsv, its names in generic/<lang>.tsv).
Used by the country scripts under tools/starter-packs/<COUNTRY>/make.py; build_country.py then builds the pack.
"""
import json, os

TOOLS = os.path.dirname(os.path.abspath(__file__))
ROLES = {'clients': '2201', 'output_vat': '4401', 'suppliers': '4101', 'input_vat': '2401', 'employee_advances': '2303', 'goods_sales': '5001',
         'goods_purchases': '6001', 'client_advances': '4104', 'supplier_prepayments': '2302', 'wages_payable': '4203', 'rent_expense': '7101', 'services_sales': '5002', 'services_purchases': '7205',
         'asset_assets': '1103', 'asset_land': '1101', 'asset_buildings': '1102', 'asset_office': '1105', 'asset_vehicles': '1104', 'asset_computers': '1106', 'asset_furniture': '1105', 'asset_intangible': '1302'}
CLASS_ROLES = {'1': ['fixed_assets'], '5': ['income'], '6': ['inventories'], '7': ['expenses']}
ACCOUNTS = {'assets': ('7602', '1203'), 'land': (None, None), 'buildings': ('7601', '1202'), 'office': ('7604', '1205'), 'vehicles': ('7603', '1204'),
            'computers': ('7605', '1206'), 'furniture': ('7604', '1205'), 'intangible': ('7606', '1402')}


def write(country, language, currency, chart_name, class_word, vat, lives, codes, sources, uncertain, descriptions=None):
    """vat: [(rate, local name, standard)]; lives: {category: (months, rate)}; codes: {English document key: code}."""
    names = {}
    if language != 'en':
        for line in open(os.path.join(TOOLS, 'generic', f'{language}.tsv'), encoding='utf-8'):
            if line.strip() and not line.startswith('#'):
                c, n = line.rstrip('\n').split('\t')
                names[c] = n
    master = [l.rstrip('\n').split('\t') for l in open(os.path.join(TOOLS, 'generic', 'chart-generic.tsv'), encoding='utf-8') if l.strip() and not l.startswith('#')]
    depreciation = {}
    for category, (expense, accumulated) in ACCOUNTS.items():
        months, rate = lives[category]
        depreciation[category] = [months, rate, expense, accumulated]
    conf = {
        'country': country, 'language': language, 'currency': currency, 'sources': sources,
        'uncertain': ['The chart is Gamma\'s generic one on the IAS 1 captions, not an official chart.'] + uncertain,
        'chart': {'generic': True, 'category': {'code': 'GAMMA', 'name': chart_name},
                  'types': [{'code': c, 'name': f'{class_word} {c} – {names.get(c, en)}', 'prefixes': [c], 'roles': CLASS_ROLES.get(c, [])}
                            for c, en in master if len(c) == 1]},
        'roles': ROLES, 'cash_account': '2801', 'bank_account': '2803', 'services_account': '5002',
        'vat': [{'rate': r, 'name': n, 'en': f'VAT {r}%' if s else f'VAT {r}% / exempt', 'standard': s} for r, n, s in vat],
        'depreciation': depreciation, 'document_codes': codes, 'document_descriptions': descriptions or {},
    }
    os.makedirs(os.path.join(TOOLS, country), exist_ok=True)
    json.dump(conf, open(os.path.join(TOOLS, country, 'country.json'), 'w', encoding='utf-8'), ensure_ascii=False, indent=2)
    print(country, 'written')
