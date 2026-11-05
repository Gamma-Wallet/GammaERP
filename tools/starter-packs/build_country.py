"""
Builds a country's starter pack (src/backend/Gamma.Erp.Seed/Packs/<COUNTRY>/) from the English base (tools/starter-packs/base/, the
founder's rule 2026-10-29: English is the base, every country is built from it, Albania too) and the country's own parts in
tools/starter-packs/<COUNTRY>/country.json:
- its chart (in English, the default, and its own language, where the chart's file has the English), its classes as chart types, each naming the base's classes of accounts it holds (income, fixed
  assets, inventories, expenses), so the account-line scenarios are scoped on them;
- the account of each role the scenarios name (clients, output VAT, suppliers, input VAT, advances to employees, goods sales and purchases);
- its VAT rates (the base's standard kind becomes every taxed rate, the zero kind the untaxed ones), its base currency;
- its code (and description) for each document type, by the type's English key;
- its depreciation lives, rates and accounts per asset category;
- every name in its language and English (base/names.tsv).
Usage: python tools/starter-packs/build_country.py AL IT CY
"""
import glob, json, os, sys

TOOLS = os.path.dirname(os.path.abspath(__file__))
BASE = os.path.join(TOOLS, 'base')
PACKS = os.path.join(TOOLS, '..', '..', 'src', 'backend', 'Gamma.Erp.Seed', 'Packs')


def add_cost_of_sales(pack, code, lang):
    """A sale takes its goods out of the accounts at what they cost, and a reversal puts them back: two more lines on each item scenario of the types that sell stock."""
    purchase = 'document_type:PURCHASE_INVOICE_GOODS'
    link = next(l for l in pack['InterDocumentTypeTransactionType'] if l['DocumentTypeId'] == purchase and l['TransactionTypeId'] == 'transaction_type:ITEM')
    bought = next(r for r in pack['Scenario'] if r['DocumentTypeTransactionTypeId'] == link['Key'])
    stock = next(c for c in pack['ScenarioConf'] if c['ScenarioId'] == bought['Key'] and not c['Side'] and c['Value'] == 1)
    made = next(c for c in pack['ScenarioConf'] if c['ValueElementsId'] == 'elements:195')
    account = next(r for r in pack['Chart'] if r['Code'] == code)
    sides_from = {r['DocumentTypeId']: r for r in pack['DocumentTypeConfFrom']}
    sides_to = {r['DocumentTypeId']: r for r in pack['DocumentTypeConfTo']}
    for row in pack['DocumentType']:
        key = row['Key']
        out, into = sides_from.get(key, {}), sides_to.get(key, {})
        sale = out.get('CostLayers') == 1 and out.get('ProductValueElementsId') == 'elements:195' and into.get('CostLayers') == 0
        back = into.get('CostLayers') == 4 and out.get('CostLayers') == 0
        if not sale and not back:
            continue
        place = 1 if sale else 2
        links = [l['Key'] for l in pack['InterDocumentTypeTransactionType'] if l['DocumentTypeId'] == key and l['TransactionTypeId'] == 'transaction_type:ITEM']
        for scenario in [r for r in pack['Scenario'] if r['DocumentTypeTransactionTypeId'] in links]:
            for n, (chart, name, credit) in enumerate(((account['Key'], account['Name'].get(lang), not sale), (stock['ChartId'], stock['ChartName'], sale))):
                pack['ScenarioConf'].append({'Key': f"scenario_conf:cost:{scenario['Key']}:{n}", 'IdSup': None, 'Active': True, 'ScenarioId': scenario['Key'], 'ChartId': chart, 'ChartName': name, 'Side': credit,
                                             'SideValue': 'Credit' if credit else 'Debit', 'Value': made['Value'], 'ValueName': made['ValueName'], 'ValueElementsId': 'elements:195', 'Place': place, 'CostCenterPlace': place})


def add_opening_stock(pack, offset, lang):
    """A document type that brings stock in at the file's cost against the opening account, and its reversal: the inventory account is the one a goods purchase debits."""
    purchase = 'document_type:PURCHASE_INVOICE_GOODS'
    link = next(l for l in pack['InterDocumentTypeTransactionType'] if l['DocumentTypeId'] == purchase and l['TransactionTypeId'] == 'transaction_type:ITEM')
    scenario = next(r for r in pack['Scenario'] if r['DocumentTypeTransactionTypeId'] == link['Key'])
    debit = next(c for c in pack['ScenarioConf'] if c['ScenarioId'] == scenario['Key'] and not c['Side'] and c['Value'] == 1)
    account = next(r for r in pack['Chart'] if r['Code'] == offset)
    zero = next(v for v in pack['VatGroup'] if v['Rate'] == 0)
    name = lambda en, local: {lang: local if lang == 'sq' else en, 'en': en}
    def add(model, **row):
        pack.setdefault(model, []).append({'IdSup': None, 'Active': True, **row})
    types = (('OPENING_STOCK', 'CGJ', name('Opening stock', 'Çelje e gjendjes së mallrave'), False, None, 'document_category:TRANSFERS'),
             ('OPENING_STOCK_REVERSAL', 'KCGJ', name('Reversal of opening stock', 'Kthim i çeljes së gjendjes'), True, 'document_type:OPENING_STOCK', 'document_category:REVERSALS'))
    for key, code, text, reversal, reverses, category in types:
        type_key = f'document_type:{key}'
        add('DocumentType', Key=type_key, IsElement=False, Code=code, CodeStart=0, CodeEnd=9999, CodeCurrent=0, Name=text, Description=None, SortOrder=99.0, MenuId=None, AllowsPartialReversal=False,
            IsReversal=reversal, ReversedDocumentTypeId=reverses, NeverPosted=False)
        add('InterDocumentTypeCategory', Key=f'inter_document_type_category:{key}', TypeId=type_key, CategoryId=category)
        add('InterDocumentTypeVatGroup', Key=f'inter_document_type_vat_group:{key}', DocumentTypeId=type_key, VatGroupId=zero['Key'])
        add('InterDocumentTypeTransactionType', Key=f'inter_document_type_transaction_type:{key}', DocumentTypeId=type_key, TransactionTypeId='transaction_type:ITEM', ElementDebitValueElementsId=None, ElementCreditValueElementsId=None)
        add('Scenario', Key=f'scenario:{key}', Name=name('Opening stock', 'Çelje e gjendjes'), DocumentTypeTransactionTypeId=f'inter_document_type_transaction_type:{key}', ElementsId=scenario['ElementsId'])
        place = 1 if reversal else 2
        for n, (chart, chart_name, side) in enumerate(((debit['ChartId'], debit['ChartName'], reversal), (account['Key'], account['Name'].get(lang), not reversal))):
            add('ScenarioConf', Key=f'scenario_conf:{key}:{n}', ScenarioId=f'scenario:{key}', ChartId=chart, ChartName=chart_name, Side=side, SideValue='Credit' if side else 'Debit', Value=debit['Value'], ValueName=debit['ValueName'],
                ValueElementsId=debit['ValueElementsId'], Place=place, CostCenterPlace=place)
    add('DocumentTypeConfTo', Key='document_type_conf_to:OPENING_STOCK', DocumentTypeId='document_type:OPENING_STOCK', ElementsId='elements:136', ProductValueElementsId=debit['ValueElementsId'], CostLayers=2)
    add('DocumentTypeConfFrom', Key='document_type_conf_from:OPENING_STOCK_REVERSAL', DocumentTypeId='document_type:OPENING_STOCK_REVERSAL', ElementsId='elements:136', ProductValueElementsId='elements:195', CostLayers=5)


def build(country):
    conf = json.load(open(os.path.join(TOOLS, country, 'country.json'), encoding='utf-8'))
    lang = conf['language']
    pack = {os.path.basename(f)[:-5]: json.load(open(f, encoding='utf-8')) for f in glob.glob(os.path.join(BASE, '*.json')) if not os.path.basename(f).startswith('_')}

    def row(key_, **values):
        return {'Key': key_, 'IdSup': values.pop('IdSup', None), 'Active': True} | values

    # ---------- Names ----------
    names = {}
    for line in open(os.path.join(BASE, 'names.tsv'), encoding='utf-8'):
        if line.strip() and not line.startswith('#'):
            key_, field, en, sq, it, el = line.rstrip('\n').split('\t')
            names[(key_, field)] = {'en': en, 'sq': sq, 'it': it, 'el': el}
    # A language that is not a column of names.tsv has its own file, keyed by the English name (base/translations/<lang>.tsv).
    extra = {}
    path = os.path.join(BASE, 'translations', f'{lang}.tsv')
    if os.path.exists(path):
        for line in open(path, encoding='utf-8'):
            if line.strip() and not line.startswith('#'):
                en, text_ = line.rstrip('\n').split('\t')
                extra[en] = text_
    missing = set()
    for model, rows in pack.items():
        for r in rows:
            for field in ('Name', 'Description'):
                n = names.get((r['Key'], field))
                if n:
                    local = n['en'] if model == 'Currency' else n.get(lang) if lang in ('sq', 'it', 'el') else extra.get(n['en'])
                    if local is None and lang != 'en':
                        missing.add(n['en'])
                    r[field] = {'en': n['en'], lang: local or n['en']} if lang != 'en' else {'en': n['en']}
    if missing:
        sys.exit(f'{country}: no {lang} name for {sorted(missing)}')

    # ---------- The chart ----------
    chart = conf['chart']
    if chart.get('generic'):
        # Gamma's generic chart (generic/chart-generic.tsv), its names in the country's language (generic/<lang>.tsv, by account code).
        master = [l.rstrip('\n').split('\t') for l in open(os.path.join(TOOLS, 'generic', 'chart-generic.tsv'), encoding='utf-8') if l.strip() and not l.startswith('#')]
        local = {}
        if lang != 'en':
            for l in open(os.path.join(TOOLS, 'generic', f'{lang}.tsv'), encoding='utf-8'):
                if l.strip() and not l.startswith('#'):
                    c, n = l.rstrip('\n').split('\t')
                    local[c] = n
            missing_codes = [c for c, _ in master if c not in local]
            if missing_codes:
                sys.exit(f'{country}: generic/{lang}.tsv has no name for {missing_codes}')
        lines = [(c, local.get(c, en), en) for c, en in master]
    else:
        lines = [l.rstrip('\n').split('\t') for l in open(os.path.join(TOOLS, country, chart['file']), encoding='utf-8') if l.strip() and not l.startswith('#')]
    codes = [c for c, _, _ in lines]
    if len(codes) != len(set(codes)):
        sys.exit(f'{country}: a code repeats in the chart')

    def parent(code):
        return next((code[:n] for n in range(len(code) - 1, 0, -1) if code[:n] in codes), None)

    def type_of(code):
        matches = [(len(p), t['code']) for t in chart['types'] for p in t['prefixes'] if code.startswith(p)]
        if not matches:
            sys.exit(f'{country}: account {code} is in no class of the chart')
        return max(matches)[1]

    def both(local, english):
        return {'en': english, lang: local} if english and lang != 'en' else {lang: local}

    # A class of the chart in English: its own, or the name of the account with its code.
    def class_en(t):
        en = t.get('en') or next((e for c, _, e in lines if c == t['code'] and e), None)
        return f'Class {t["code"]} – {en}' if en and not t.get('en') else en

    category = 'chart_category:STATUTORY'
    pack['ChartCategory'] = [row(category, IsElement=True, Code=chart['category']['code'], Name=both(chart['category']['name'], chart['category'].get('en') or f"Chart of accounts ({chart['category']['code']})"), IsStatutory=True, SortOrder=10)]
    pack['ChartType'] = [row(f'chart_type:{t["code"]}', IsElement=True, Code=t['code'], CodeStart=0, CodeEnd=99999, CodeCurrent=0, Name=both(t['name'], class_en(t)),
                             SortOrder=(i + 1) * 10) for i, t in enumerate(chart['types'])]
    pack['InterChartTypeCategory'] = [row(f'inter_chart_type_category:{t["code"]}', TypeId=f'chart_type:{t["code"]}', CategoryId=category) for t in chart['types']]
    pack['Chart'] = [row(f'chart:{c}', IdSup=f'chart:{parent(c)}' if parent(c) else None, TypeId=f'chart_type:{type_of(c)}', Code=c, Name=both(n, en), IsComposite=False)
                     for c, n, en in lines]
    pack['InterChartCategory'] = [row(f'inter_chart_category:{c}', ElementId=f'chart:{c}', CategoryId=category) for c in codes]
    # A country's statement lines, where it has its own (<country>/statements.json); the others get theirs with the configuration pass.
    statements = os.path.join(TOOLS, country, 'statements.json')
    statement_lines = json.load(open(statements, encoding='utf-8')) if os.path.exists(statements) else {}
    pack['ChartAssetsLiabilities'], pack['ChartProfitLoss'] = statement_lines.get('ChartAssetsLiabilities', []), statement_lines.get('ChartProfitLoss', [])
    groups = {parent(c) for c in codes if parent(c)}
    chart_name = {f'chart:{c}': n for c, n, _ in lines}

    def account(code, why):
        if code not in codes:
            sys.exit(f'{country}, {why}: the chart has no account {code}')
        if code in groups:
            sys.exit(f'{country}, {why}: {code} has accounts under it; name one of those')
        return f'chart:{code}'

    # ---------- Scenarios: the base's classes of accounts on the country's classes, the roles on its accounts ----------
    type_of_class = {cls: t for t in chart['types'] for cls in t.get('roles', [])}
    scope_type = {}
    for e in pack['Elements']:
        if e['Key'].startswith('elements:chart_class:'):
            cls = e['Key'].rsplit(':', 1)[1]
            t = type_of_class.get(cls) or sys.exit(f'{country}: no class of the chart holds the {cls} accounts')
            scope_type[e['Key']] = t['code']
            e['TypeId'], e['Name'] = f'chart_type:{t["code"]}', both(t['name'], class_en(t))
    seen, dropped = set(), set()
    for s in pack['Scenario']:
        if s['ElementsId'] in scope_type:
            k = (s['DocumentTypeTransactionTypeId'], scope_type[s['ElementsId']])
            if k in seen:
                dropped.add(s['Key'])  # two classes of the base in one class of the country: one scenario is enough
            seen.add(k)
            s['Name'] = next(e['Name'] for e in pack['Elements'] if e['Key'] == s['ElementsId'])
    pack['Scenario'] = [s for s in pack['Scenario'] if s['Key'] not in dropped]
    pack['ScenarioConf'] = [c for c in pack['ScenarioConf'] if c['ScenarioId'] not in dropped]
    used = {s['ElementsId'] for s in pack['Scenario']}
    pack['Elements'] = [e for e in pack['Elements'] if e['Key'] not in scope_type or e['Key'] in used]
    for c in pack['ScenarioConf']:
        if (c.get('ChartId') or '').startswith('role:'):
            role = c['ChartId'][len('role:'):]
            if role not in conf['roles']:
                sys.exit(f'{country}: no account for the role {role}')
            c['ChartId'] = account(conf['roles'][role], f'role {role}')
            c['ChartName'] = chart_name[c['ChartId']]

    # ---------- Depreciation ----------
    for s in pack['DepreciationScenario']:
        months, rate, _, _ = conf['depreciation'][s['Key'].split(':')[1]]
        s['Months'], s['LinearPercentage'], s['DecliningPercentage'] = months, rate, 0
    for c in pack['DepreciationScenarioConf']:
        _, _, code, side = c['ChartId'].split(':')
        _, _, expense, accumulated = conf['depreciation'][code]
        c['ChartId'] = account(expense if side == 'expense' else accumulated, f'depreciation {code}')
        c['ChartName'] = chart_name[c['ChartId']]

    # ---------- The account a new cash desk / bank account holds when none is chosen (a soft link: empty without the chart group) ----------
    proposed = {'cost_center_type:CASH_DESK': account(conf['cash_account'], 'cash account'), 'cost_center_type:BANK': account(conf['bank_account'], 'bank account')}
    # The currency a new cost centre holds: the country's base currency. The cash desk's and bank's own "Name" text is the cost centre's name,
    # so it is not asked again.
    currency_scopes = {e['Key'] for e in pack['Elements'] if e.get('Table') == 'tbl_currency'}
    for p in pack['CostCenterTypeInfo']:
        if p['TypeId'] in proposed and p.get('ElementsId') == 'elements:181':
            p['DefaultElementId'] = proposed[p['TypeId']]
        elif p.get('ElementsId') in currency_scopes and p['Kind'] == 9:
            p['DefaultElementId'] = f'currency:{conf["currency"]}'
        elif p['TypeId'] in proposed and p['Kind'] == 1 and p['Name'].get('en') == 'Name':
            p['Required'] = False

    # ---------- VAT and currency ----------
    vat_key = {v['rate']: f'vat_group:{country}_{v["rate"]}' for v in conf['vat']}
    pack['VatGroup'] = [row(vat_key[v['rate']], Code=None, Name={lang: v['name'], 'en': v['en']}, Description=None, Rate=float(v['rate']), Depreciation=False)
                        for v in conf['vat']]
    pack['InterDocumentTypeVatGroup'] = [{**l, 'Key': f'{l["Key"]}:{v["rate"]}', 'VatGroupId': vat_key[v['rate']]} for l in pack['InterDocumentTypeVatGroup']
                                         for v in conf['vat'] if v['standard'] == (l['VatGroupId'] == 'vat_group:STANDARD')]
    # ---------- The VAT return form (its boxes and what fills them; only countries that have one) ----------
    for n, b in enumerate(conf.get('vat_return', [])):
        box = f'vat_return_box:{b["code"]}'
        pack.setdefault('VatReturnBox', []).append(row(box, Code=b['code'], Name={lang: b['name'], 'en': b['en']}, SortOrder=float(n), IsInput=b['input'], Amount=b['amount']))
        for m, r in enumerate(b['rules']):
            pack.setdefault('VatReturnBoxRule', []).append(row(
                f'vat_return_box_rule:{b["code"]}:{m}', BoxId=box, DocumentCategoryId=f'document_category:{r["category"]}' if 'category' in r else None,
                DocumentTypeId=f'document_type:{r["type"]}' if 'type' in r else None, TransactionTypeId=f'transaction_type:{r["line"]}' if 'line' in r else None,
                VatGroupId=vat_key[r['vat']] if 'vat' in r else None))

    for r in pack['Currency']:
        r['IsBase'] = r['Code'] == conf['currency']
    if not any(r['IsBase'] for r in pack['Currency']):
        sys.exit(f'{country}: no currency {conf["currency"]}')

    # ---------- Document codes and descriptions ----------
    code_of = {}
    for r in pack['DocumentType']:
        key_ = r['Code']
        if key_ not in conf['document_codes']:
            sys.exit(f'{country}: no code for the document type {key_}')
        r['Code'] = code_of[key_] = conf['document_codes'][key_]
        if key_ in conf.get('document_descriptions', {}):
            r['Description'] = {lang: conf['document_descriptions'][key_], 'en': conf['document_descriptions'][key_]}

    # A country's opening account is an account of its own chart: without one its opening steps say so.
    codes = {r.get('Code') for r in pack['Chart']}
    if conf.get('roles', {}).get('cost_of_sales'):
        add_cost_of_sales(pack, conf['roles']['cost_of_sales'], lang)
    offset = next((r['OffsetAccount'] for r in pack.get('MigrationItem', []) if r.get('OffsetAccount') in codes), None)
    if offset:
        add_opening_stock(pack, offset, lang)
    for r in pack.get('MigrationItem', []):
        if r.get('OffsetAccount') not in codes:
            r['OffsetAccount'] = None
            if r['Kind'] == 'OpeningStock':
                r['DocumentTypeId'] = None

    # ---------- Every text in English first, then the country's language ----------
    if lang != 'en':
        local_of = {n['en']: n.get(lang) for n in names.values() if n.get(lang)} | extra
        english_of = {t['name']: class_en(t) for t in chart['types'] if class_en(t)}
        unnamed = set()
        for rows in pack.values():
            for r in rows:
                for field in ('Name', 'Description'):
                    text_ = r.get(field)
                    if not isinstance(text_, dict) or not any(text_.values()):
                        continue
                    en, local = text_.get('en') or english_of.get(text_.get(lang)), text_.get(lang) or local_of.get(text_.get('en'))
                    if not en or not local:
                        unnamed.add(text_.get('en') or text_.get(lang))
                    r[field] = {k: v for k, v in (('en', en), (lang, local)) if v}
        if unnamed:
            print(f'{country}: {len(unnamed)} texts in one language only: {sorted(unnamed)[:12]}')

    # ---------- Write ----------
    out = os.path.join(PACKS, country)
    os.makedirs(os.path.join(out, 'profiles'), exist_ok=True)
    for f in glob.glob(os.path.join(out, '*.json')) + glob.glob(os.path.join(out, 'profiles', '*.json')):
        os.remove(f)
    with open(os.path.join(out, '_external.json'), 'w', encoding='utf-8', newline='\n') as f:
        f.write(open(os.path.join(BASE, '_external.json'), encoding='utf-8').read())
    for model, items in sorted(pack.items()):
        with open(os.path.join(out, f'{model}.json'), 'w', encoding='utf-8', newline='\n') as f:
            json.dump(items, f, ensure_ascii=False, indent=1)
    for f in glob.glob(os.path.join(BASE, 'profiles', '*.json')):
        profile = json.load(open(f, encoding='utf-8'))
        profile['DocumentType'] = sorted({code_of[k] for k in profile['DocumentType']})
        with open(os.path.join(out, 'profiles', os.path.basename(f)), 'w', encoding='utf-8', newline='\n') as o:
            o.write(json.dumps(profile, ensure_ascii=False, indent=1) + '\n')
    print(country, {m: len(pack[m]) for m in ('Chart', 'Scenario', 'ScenarioConf', 'DocumentType', 'VatGroup', 'DepreciationScenario')})


for c in sys.argv[1:]:
    build(c.upper())
