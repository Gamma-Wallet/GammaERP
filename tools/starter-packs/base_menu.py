"""
The base menu (H15, the founder 2026-10-30: the menu comes from the database, given to roles and users): base/Menu.json, one tree of folders
and the items that open a screen, and its names in base/names.tsv (English, Albanian, Italian, Greek; the other languages in
base/translations/<lang>.tsv, keyed by the English name). A documents item lists the document types of its category the user was given, so it
has the category's names. Nothing is given to a role here: until an administrator gives nodes to roles or users, only administrators see the menu.
The tree is by area, not by kind of screen (the founder, 2026-11-01); Documents stays one menu, its items are the document categories. Every row this script made before is replaced. Run it before build_country.py.
"""
import json
import os

TOOLS = os.path.dirname(os.path.abspath(__file__))
BASE = os.path.join(TOOLS, 'base')

# (code, parent, screen, parameter, icon, (en, sq, it, el)); a folder has no screen. A documents item names its category instead of its names.
TREE = [
    ('DASHBOARD', None, 'dashboard', None, 'pie-chart', ('Dashboard', 'Paneli', 'Cruscotto', 'Πίνακας ελέγχου')),
    ('DOCUMENTS', None, None, None, 'file-text', ('Documents', 'Dokumente', 'Documenti', 'Παραστατικά')),
    *[(f'DOCUMENTS_{c}', 'DOCUMENTS', 'documents', None, icon, c) for c, icon in [
        ('SALES', 'receipt'), ('PURCHASES', 'cart'), ('TRANSFERS', 'truck'), ('PRODUCTION', 'factory'), ('CASH_DESK', 'wallet'), ('BANK', 'bank'),
        ('OFFERS', 'tag'), ('ORDERS', 'list'), ('REPAIRS', 'settings'), ('CONTRACTS', 'handshake'), ('JOURNAL', 'book'), ('RECURRING_JOURNAL', 'repeat'), ('REVERSALS', 'undo')]],
    ('CRM', None, None, None, 'users', ('CRM', 'CRM', 'CRM', 'CRM')),
    ('OPPORTUNITIES', 'CRM', 'entities', 'opportunities', 'handshake', ('Opportunities', 'Mundësitë', 'Opportunità', 'Ευκαιρίες')),
    ('PIPELINE', 'CRM', 'opportunity-board', None, 'trending-up', ('Pipeline', 'Tubacioni i shitjeve', 'Pipeline', 'Αγωγός πωλήσεων')),
    ('OPPORTUNITY_FORECAST', 'CRM', 'report', 'opportunity-forecast', 'trending-up', ('Forecast', 'Parashikimi', 'Previsione', 'Πρόβλεψη')),
    ('OPPORTUNITY_DOCUMENTS', 'CRM', 'report', 'opportunity-documents', 'list', ('Opportunities and documents', 'Mundësitë dhe dokumentet', 'Opportunità e documenti', 'Ευκαιρίες και έγγραφα')),
    ('OPPORTUNITY_STAGES', 'CRM', 'config', 'opportunity-stages', 'settings', ('Opportunity stages', 'Fazat e mundësive', 'Fasi delle opportunità', 'Στάδια ευκαιριών')),
    ('SETUP_OPPORTUNITIES', 'CRM', 'setup', 'opportunities', 'folder', ('Opportunity types and categories', 'Llojet dhe kategoritë e mundësive', 'Tipi e categorie delle opportunità', 'Τύποι και κατηγορίες ευκαιριών')),
    ('CONTRACTS_AREA', None, None, None, 'handshake', ('Contracts', 'Kontratat', 'Contratti', 'Συμβόλαια')),
    ('INVOICE_RUN', 'CONTRACTS_AREA', 'invoice-run', None, 'repeat', ('Invoice run', 'Faturimi i kontratave', 'Fatturazione contratti', 'Τιμολόγηση συμβολαίων')),
    ('RENEWALS', 'CONTRACTS_AREA', 'renewals', None, 'calendar', ('Renewals', 'Rinovimet', 'Rinnovi', 'Ανανεώσεις')),
    ('BANK_STATEMENTS', 'DOCUMENTS', 'bank-statements', None, 'coins', ('Bank statements', 'Pasqyrat bankare', 'Estratti conto', 'Κινήσεις τράπεζας')),
    ('APPROVALS', 'DOCUMENTS', 'approvals', None, 'check', ('Approvals', 'Miratimet', 'Approvazioni', 'Εγκρίσεις')),
    ('PARTNERS', None, 'entities', 'partners', 'users', ('Clients and suppliers', 'Klientë dhe furnitorë', 'Clienti e fornitori', 'Πελάτες και προμηθευτές')),

    ('REPAIRS_AREA', None, None, None, 'settings', ('Repairs', 'Riparimet', 'Riparazioni', 'Επισκευές')),
    ('REPAIR_BOARD', 'REPAIRS_AREA', 'repairs', None, 'grid', ('Repair board', 'Tabela e riparimeve', 'Tabellone riparazioni', 'Πίνακας επισκευών')),
    ('REPAIR_STAGES', 'REPAIRS_AREA', 'config', 'repair-stages', 'settings', ('Repair stages', 'Fazat e riparimit', 'Fasi di riparazione', 'Στάδια επισκευής')),
    ('POS', None, None, None, 'shop', ('Point of sale', 'Pikë shitje', 'Punto vendita', 'Σημείο πώλησης')),
    ('POINT_OF_SALE', 'POS', 'pos', 'sme', 'shop', ('Open the point of sale', 'Hap pikën e shitjes', 'Apri il punto vendita', 'Άνοιγμα σημείου πώλησης')),
    ('PRODUCTION_DISPLAY', 'POS', 'production-display', None, 'factory', ('Production point display', 'Ekrani i pikës së prodhimit', 'Schermo del punto di produzione', 'Οθόνη σημείου παραγωγής')),
    ('PRODUCTION_ROUTES', 'POS', 'config', 'production-routes', 'route', ('Production routes', 'Rrugët e prodhimit', 'Percorsi di produzione', 'Διαδρομές παραγωγής')),
    ('POS_SHIFTS', 'POS', 'pos-shifts', None, 'receipt', ('Shifts', 'Turnet', 'Turni', 'Βάρδιες')),
    ('POS_SETTINGS', 'POS', 'config', 'point-of-sale-settings', 'settings', ('Point-of-sale settings', 'Cilësimet e pikës së shitjes', 'Impostazioni del punto vendita', 'Ρυθμίσεις σημείου πώλησης')),
    ('POS_SHOWS', 'POS', 'config', 'pos-shows', 'calendar', ('Shows', 'Shfaqjet', 'Spettacoli', 'Παραστάσεις')),

    ('PRODUCTS_AREA', None, None, None, 'package', ('Products and services', 'Produkte dhe shërbime', 'Prodotti e servizi', 'Προϊόντα και υπηρεσίες')),
    ('PRODUCTS', 'PRODUCTS_AREA', 'entities', 'products', 'package', ('Products and services', 'Produkte dhe shërbime', 'Prodotti e servizi', 'Προϊόντα και υπηρεσίες')),
    ('SALE_PRICES', 'PRODUCTS_AREA', 'config', 'sale-prices', 'tag', ('Sale prices', 'Çmimet e shitjes', 'Prezzi di vendita', 'Τιμές πώλησης')),
    ('SALE_PRICE_TYPES', 'PRODUCTS_AREA', 'config', 'sale-price-types', 'tag', ('Sale prices by partner', 'Çmimet e shitjes sipas partnerit', 'Prezzi di vendita per partner', 'Τιμές πώλησης ανά συνεργάτη')),
    ('SALE_MENUS', 'PRODUCTS_AREA', 'config', 'sale-menus', 'menu', ('Sale menus', 'Menutë e shitjes', 'Menu di vendita', 'Μενού πώλησης')),
    ('SALE_MENU_CATEGORIES', 'PRODUCTS_AREA', 'config', 'sale-menu-categories', 'menu', ('Sale menu categories', 'Kategoritë e menuve të shitjes', 'Categorie dei menu di vendita', 'Κατηγορίες μενού πώλησης')),
    ('UNITS', 'PRODUCTS_AREA', 'entities', 'units', 'ruler', ('Units of measure', 'Njësi matëse', 'Unità di misura', 'Μονάδες μέτρησης')),
    ('UNIT_CONVERSIONS', 'PRODUCTS_AREA', 'config', 'unit-conversions', 'ruler', ('Unit conversions', 'Konvertimet e njësive', 'Conversioni tra unità', 'Μετατροπές μονάδων')),

    ('STOCK_AREA', None, None, None, 'factory', ('Stock and production', 'Magazina dhe prodhimi', 'Magazzino e produzione', 'Απόθεμα και παραγωγή')),
    ('ORDERS_TO_PRODUCE', 'STOCK_AREA', 'production', None, 'factory', ('Orders to produce', 'Porosi për prodhim', 'Ordini da produrre', 'Παραγγελίες για παραγωγή')),
    ('LOTS', 'STOCK_AREA', 'lots', None, 'list', ('Lots', 'Lotet', 'Lotti', 'Παρτίδες')),
    ('SERIAL_NUMBERS', 'STOCK_AREA', 'serials', None, 'list', ('Serial numbers', 'Numrat serialë', 'Numeri di serie', 'Σειριακοί αριθμοί')),
    ('RECIPES', 'STOCK_AREA', 'config', 'product-recipes', 'chef-hat', ('Recipes', 'Recetat', 'Ricette', 'Συνταγές')),
    ('CONVERT_STOCK', 'STOCK_AREA', 'stock-convert', None, 'scale', ('Convert stock between units', 'Konvertim i gjendjes në një njësi tjetër', 'Conversione delle giacenze tra unità', 'Μετατροπή αποθέματος μεταξύ μονάδων')),

    ('ACCOUNTING_SETUP', None, None, None, 'book-open', ('Accounting setup', 'Konfigurimi kontabël', 'Impostazioni contabili', 'Λογιστικές ρυθμίσεις')),
    ('CHART', 'ACCOUNTING_SETUP', 'entities', 'charts', 'list', ('Chart of accounts', 'Plani kontabël', 'Piano dei conti', 'Λογιστικό σχέδιο')),
    ('BALANCE_SHEET_LINES', 'ACCOUNTING_SETUP', 'config', 'balance-sheet-lines', 'list', ('Balance sheet lines', 'Rreshtat e bilancit', 'Righe dello stato patrimoniale', 'Γραμμές ισολογισμού')),
    ('PROFIT_LOSS_LINES', 'ACCOUNTING_SETUP', 'config', 'profit-loss-lines', 'list', ('Profit and loss lines', 'Rreshtat e fitimit dhe humbjes', 'Righe del conto economico', 'Γραμμές αποτελεσμάτων')),
    ('SETUP_CHARTS', 'ACCOUNTING_SETUP', 'setup', 'charts', 'folder', ('Chart types and categories', 'Llojet dhe kategoritë e planit', 'Tipi e categorie del piano', 'Τύποι και κατηγορίες σχεδίου')),
    ('POSTING_SCENARIOS', 'ACCOUNTING_SETUP', 'config', 'scenarios', 'git-branch', ('Posting scenarios', 'Skenarët e kontabilizimit', 'Scenari di contabilizzazione', 'Σενάρια λογιστικών εγγραφών')),
    ('SCENARIO_LINES', 'ACCOUNTING_SETUP', 'config', 'scenario-lines', 'rows', ('Scenario lines', 'Rreshtat e skenarëve', 'Righe degli scenari', 'Γραμμές σεναρίων')),
    ('UNBALANCED_SCENARIOS', 'ACCOUNTING_SETUP', 'report', 'unbalanced-scenarios', 'scale', ('Unbalanced scenarios', 'Skenarë të pabalancuar', 'Scenari non bilanciati', 'Μη ισοσκελισμένα σενάρια')),
    ('CHART_MAPPINGS', 'ACCOUNTING_SETUP', 'config', 'chart-mappings', 'git-branch', ('Account mapping', 'Lidhja e llogarive', 'Corrispondenza dei conti', 'Αντιστοίχιση λογαριασμών')),
    ('YEAR_CLOSING', 'ACCOUNTING_SETUP', 'year-closing', None, 'key', ('Close the year', 'Mbyll vitin', "Chiudi l'esercizio", 'Κλείσιμο χρήσης')),
    ('CLOSED_PERIODS', 'ACCOUNTING_SETUP', 'config', 'closed-periods', 'key', ('Closed periods', 'Periudhat e mbyllura', 'Periodi chiusi', 'Κλειστές περίοδοι')),
    ('VAT_GROUPS', 'ACCOUNTING_SETUP', 'config', 'vat-groups', 'percent', ('VAT groups', 'Grupet e TVSH-së', 'Gruppi IVA', 'Ομάδες ΦΠΑ')),
    ('VAT_RETURN_BOXES', 'ACCOUNTING_SETUP', 'config', 'vat-return-boxes', 'grid', ('VAT return boxes', 'Kutitë e deklaratës së TVSH-së', 'Righi della dichiarazione IVA', 'Πεδία δήλωσης ΦΠΑ')),
    ('VAT_RETURN_BOX_RULES', 'ACCOUNTING_SETUP', 'config', 'vat-return-box-rules', 'grid', ('VAT return box rules', 'Rregullat e kutive të deklaratës', 'Regole dei righi della dichiarazione', 'Κανόνες πεδίων δήλωσης')),

    ('FIXED_ASSETS', None, None, None, 'trending-down', ('Fixed assets', 'Aktive të qëndrueshme', 'Immobilizzazioni', 'Πάγια')),
    ('DEPRECIATION', 'FIXED_ASSETS', 'depreciation', None, 'trending-down', ('Depreciation', 'Amortizimi', 'Ammortamento', 'Αποσβέσεις')),
    ('DEPRECIATION_BOOKS', 'FIXED_ASSETS', 'config', 'depreciation-books', 'book-open', ('Depreciation books', 'Librat e amortizimit', 'Libri degli ammortamenti', 'Βιβλία αποσβέσεων')),
    ('DEPRECIATION_METHODS', 'FIXED_ASSETS', 'config', 'depreciation-methods', 'list', ('Depreciation methods', 'Metodat e amortizimit', 'Metodi di ammortamento', 'Μέθοδοι απόσβεσης')),
    ('DEPRECIATION_SCENARIOS', 'FIXED_ASSETS', 'config', 'depreciation-scenarios', 'git-branch', ('Depreciation scenarios', 'Skenarët e amortizimit', 'Scenari di ammortamento', 'Σενάρια απόσβεσης')),
    ('DEPRECIATION_SCENARIO_LINES', 'FIXED_ASSETS', 'config', 'depreciation-scenario-lines', 'rows', ('Depreciation scenario lines', 'Rreshtat e skenarëve të amortizimit', 'Righe degli scenari di ammortamento', 'Γραμμές σεναρίων απόσβεσης')),
    ('ASSET_VALUE', 'FIXED_ASSETS', 'asset-value', None, 'coins', ('Remaining asset value', 'Vlera e mbetur e aktivit', 'Valore residuo del cespite', 'Αναπόσβεστη αξία παγίου')),

    ('CURRENCIES_AREA', None, None, None, 'coins', ('Currencies and payments', 'Monedhat dhe pagesat', 'Valute e pagamenti', 'Νομίσματα και πληρωμές')),
    ('CURRENCIES', 'CURRENCIES_AREA', 'currencies', None, 'coins', ('Currencies and exchange rates', 'Monedha dhe kurse këmbimi', 'Valute e tassi di cambio', 'Νομίσματα και συναλλαγματικές ισοτιμίες')),
    ('PAYMENT_METHODS', 'CURRENCIES_AREA', 'config', 'payment-methods', 'credit-card', ('Payment methods', 'Mënyrat e pagesës', 'Metodi di pagamento', 'Τρόποι πληρωμής')),
    ('BANK_STATEMENT_FORMATS', 'CURRENCIES_AREA', 'config', 'bank-statement-formats', 'coins', ('Bank statement formats', 'Formatet e pasqyrave bankare', 'Formati estratti conto', 'Μορφές κινήσεων τράπεζας')),
    ('PAYMENT_METHOD_COST_CENTERS', 'CURRENCIES_AREA', 'config', 'payment-method-cost-centers', 'credit-card', ('Payment methods by cost centre', 'Mënyrat e pagesës sipas qendrës së kostos', 'Metodi di pagamento per centro di costo', 'Τρόποι πληρωμής ανά κέντρο κόστους')),

    ('REPORTS', None, None, None, 'bar-chart', ('Reports', 'Raporte', 'Report', 'Αναφορές')),
    ('DASHBOARD_ITEMS', 'REPORTS', 'config', 'dashboard-items', 'list', ('Dashboard items', 'Elementet e panelit', 'Elementi del cruscotto', 'Στοιχεία πίνακα ελέγχου')),
    ('FINANCIAL_REPORTS', 'REPORTS', None, None, 'pie-chart', ('Financial', 'Financiare', 'Finanziari', 'Οικονομικά')),
    ('BALANCE_SHEET', 'FINANCIAL_REPORTS', 'report', 'balance-sheet', 'scale', ('Balance sheet', 'Bilanci', 'Stato patrimoniale', 'Ισολογισμός')),
    ('PROFIT_LOSS', 'FINANCIAL_REPORTS', 'report', 'profit-loss', 'trending-up', ('Profit and loss', 'Fitimi dhe humbja', 'Conto economico', 'Αποτελέσματα')),
    ('TRIAL_BALANCE', 'FINANCIAL_REPORTS', 'report', 'trial-balance', 'scale', ('Trial balance', 'Bilanci i verifikimit', 'Bilancio di verifica', 'Ισοζύγιο')),
    ('LEDGER', 'FINANCIAL_REPORTS', 'report', 'ledger', 'book-open', ('Account ledger', 'Kartela e llogarisë', "Partitario del conto", 'Καρτέλα λογαριασμού')),
    ('AGEING', 'FINANCIAL_REPORTS', 'report', 'ageing', 'calendar', ('Ageing', 'Vjetërsia e detyrimeve', 'Scadenzario', 'Ενηλικίωση υπολοίπων')),
    ('OPEN_ITEMS', 'FINANCIAL_REPORTS', 'report', 'open-items', 'receipt', ('Open invoices', 'Fatura të hapura', 'Fatture aperte', 'Ανοιχτά τιμολόγια')),
    ('PARTNER_LEDGER', 'FINANCIAL_REPORTS', 'report', 'partner-ledger', 'book-open', ('Client and supplier ledger', 'Kartela e klientit dhe e furnitorit', 'Partitario clienti e fornitori', 'Καρτέλα πελατών και προμηθευτών')),
    ('STAFF_LEDGER', 'FINANCIAL_REPORTS', 'report', 'staff-ledger', 'book-open', ('Staff ledger', 'Kartela e personelit', 'Partitario del personale', 'Καρτέλα προσωπικού')),
    ('COST_CENTER_LEDGER', 'FINANCIAL_REPORTS', 'report', 'cost-center-ledger', 'book-open', ('Cost centre ledger', 'Kartela e qendrës së kostos', 'Partitario del centro di costo', 'Καρτέλα κέντρου κόστους')),
    ('VAT_REPORTS', 'REPORTS', None, None, 'percent', ('VAT', 'TVSH', 'IVA', 'ΦΠΑ')),
    ('VAT_REGISTER', 'VAT_REPORTS', 'report', 'vat-register', 'percent', ('VAT books', 'Librat e TVSH-së', 'Registri IVA', 'Βιβλία ΦΠΑ')),
    ('VAT_RETURN', 'VAT_REPORTS', 'report', 'vat-return', 'percent', ('VAT return', 'Deklarata e TVSH-së', 'Dichiarazione IVA', 'Δήλωση ΦΠΑ')),
    ('VAT_RETURN_FORM', 'VAT_REPORTS', 'report', 'vat-return-form', 'grid', ('VAT return by boxes', 'Deklarata e TVSH-së sipas kutive', 'Dichiarazione IVA per righi', 'Δήλωση ΦΠΑ ανά πεδίο')),
    ('STOCK_REPORTS', 'REPORTS', None, None, 'boxes', ('Stock and sales', 'Magazina dhe shitjet', 'Magazzino e vendite', 'Αποθήκη και πωλήσεις')),
    ('STOCK', 'STOCK_REPORTS', 'report', 'stock', 'boxes', ('Stock', 'Gjendja e magazinës', 'Giacenze', 'Απόθεμα')),
    ('ITEM_TRACKING', 'STOCK_REPORTS', 'report', 'item-tracking', 'route', ('Item movements', 'Lëvizjet e artikullit', "Movimenti dell'articolo", 'Κινήσεις είδους')),
    ('DOCUMENT_LINES', 'STOCK_REPORTS', 'report', 'lines', 'table', ('Sales and purchases by line', 'Shitje dhe blerje sipas rreshtave', 'Vendite e acquisti per riga', 'Πωλήσεις και αγορές ανά γραμμή')),
    ('LINES_BY_USER', 'STOCK_REPORTS', 'report', 'lines-by-user', 'table', ('Sales by waiter', 'Shitje sipas kamarierit', 'Vendite per cameriere', 'Πωλήσεις ανά σερβιτόρο')),
    ('LINES_BY_PARTNER', 'STOCK_REPORTS', 'report', 'lines-by-partner', 'table', ('Sales and purchases by client', 'Shitje dhe blerje sipas klientit', 'Vendite e acquisti per cliente', 'Πωλήσεις και αγορές ανά πελάτη')),
    ('LINES_BY_COST_CENTER', 'STOCK_REPORTS', 'report', 'lines-by-cost-center', 'table', ('Sales and purchases by cost centre', 'Shitje dhe blerje sipas qendrës së kostos', 'Vendite e acquisti per centro di costo', 'Πωλήσεις και αγορές ανά κέντρο κόστους')),
    ('TRANSFERS_REPORT', 'STOCK_REPORTS', 'report', 'transfers', 'route', ('Transfers', 'Transferimet', 'Trasferimenti', 'Μεταφορές')),
    ('PURCHASE_PRICES', 'STOCK_REPORTS', 'report', 'purchase-prices', 'trending-up', ('Purchase price history', 'Historiku i çmimeve të blerjes', 'Storico dei prezzi di acquisto', 'Ιστορικό τιμών αγοράς')),
    ('DOCUMENT_TOTALS', 'STOCK_REPORTS', 'report', 'document-totals', 'list', ('Document totals', 'Totalet e dokumenteve', 'Totali dei documenti', 'Σύνολα παραστατικών')),
    ('CONTRACTS_REPORT', 'STOCK_REPORTS', 'report', 'contracts', 'file-text', ('Contracts', 'Kontratat', 'Contratti', 'Συμβάσεις')),
    ('PARTNER_STATISTICS', 'STOCK_REPORTS', 'report', 'partner-statistics', 'trending-up', ('Client statistics', 'Statistikat e klientëve', 'Statistiche dei clienti', 'Στατιστικά πελατών')),

    ('COMPANY', None, None, None, 'building', ('Company', 'Kompania', 'Azienda', 'Εταιρεία')),
    ('COST_CENTERS', 'COMPANY', 'entities', 'cost-centers', 'building', ('Cost centres', 'Qendra kostoje', 'Centri di costo', 'Κέντρα κόστους')),
    ('EMPLOYEES', 'COMPANY', 'entities', 'employees', 'id-card', ('Employees', 'Punonjës', 'Dipendenti', 'Εργαζόμενοι')),
    ('GROUP_PURCHASES', 'COMPANY', 'config', 'group-purchase-settings', 'cart', ('Purchases from the group', 'Blerjet nga grupi', 'Acquisti dal gruppo', 'Αγορές από τον όμιλο')),
    ('FILES', 'COMPANY', 'files', None, 'folder', ('Files', 'Skedarët', 'File', 'Αρχεία')),

    ('USERS_AREA', None, None, None, 'shield', ('Users and rights', 'Përdoruesit dhe të drejtat', 'Utenti e permessi', 'Χρήστες και δικαιώματα')),
    ('USERS', 'USERS_AREA', 'entities', 'users', 'user', ('Users', 'Përdoruesit', 'Utenti', 'Χρήστες')),
    ('USER_RIGHTS', 'USERS_AREA', 'rights', None, 'key', ('Rights', 'Të drejtat', 'Permessi', 'Δικαιώματα')),
    ('ROLES', 'USERS_AREA', 'config', 'user-roles', 'key', ('Roles', 'Rolet', 'Ruoli', 'Ρόλοι')),
    ('APPROVAL_LIMITS', 'USERS_AREA', 'config', 'approval-limits', 'key', ('Approval limits', 'Kufijtë e miratimit', 'Limiti di approvazione', 'Όρια έγκρισης')),
    ('USER_ACTIVITY', 'USERS_AREA', 'user-activity', None, 'list', ('User activity', 'Veprimtaria e përdoruesve', 'Attività degli utenti', 'Δραστηριότητα χρηστών')),

    ('SYSTEM_SETTINGS', None, None, None, 'settings', ('System settings', 'Cilësimet e sistemit', 'Impostazioni di sistema', 'Ρυθμίσεις συστήματος')),
    ('MIGRATION', 'SYSTEM_SETTINGS', 'migration', None, 'database', ('Migration', 'Migrimi', 'Migrazione', 'Μετάπτωση')),
    ('DOCUMENT_TYPES', 'SYSTEM_SETTINGS', 'document-types', None, 'file-cog', ('Document types', 'Llojet e dokumenteve', 'Tipi di documento', 'Τύποι παραστατικών')),
    ('MIGRATION_ITEMS', 'SYSTEM_SETTINGS', 'config', 'migration-items', 'list', ('Migration steps', 'Hapat e migrimit', 'Passi della migrazione', 'Βήματα μετάπτωσης')),
    ('LINE_TYPES', 'SYSTEM_SETTINGS', 'entities', 'transactions', 'rows', ('Transaction types', 'Llojet e transaksioneve', 'Tipi di transazione', 'Τύποι συναλλαγών')),
    ('LINE_SCOPES', 'SYSTEM_SETTINGS', 'config', 'transaction-type-scopes', 'rows', ('Transaction scopes', 'Fushat e transaksioneve', 'Ambiti delle transazioni', 'Πεδία συναλλαγών')),
    # After the transactions: a flow needs the same transactions on both document types.
    ('DOCUMENT_FLOWS', 'SYSTEM_SETTINGS', 'config', 'document-type-flows', 'repeat', ('Document flows', 'Rrjedhat e dokumenteve', 'Flussi dei documenti', 'Ροές παραστατικών')),
    ('TYPES_AND_CATEGORIES', 'SYSTEM_SETTINGS', None, None, 'folder', ('Types and categories', 'Llojet dhe kategoritë', 'Tipi e categorie', 'Τύποι και κατηγορίες')),
    ('SETUP_PARTNERS', 'TYPES_AND_CATEGORIES', 'setup', 'partners', 'users', ('Clients and suppliers', 'Klientë dhe furnitorë', 'Clienti e fornitori', 'Πελάτες και προμηθευτές')),
    ('SETUP_EMPLOYEES', 'TYPES_AND_CATEGORIES', 'setup', 'employees', 'id-card', ('Employees', 'Punonjës', 'Dipendenti', 'Εργαζόμενοι')),
    ('SETUP_PRODUCTS', 'TYPES_AND_CATEGORIES', 'setup', 'products', 'package', ('Products and services', 'Produkte dhe shërbime', 'Prodotti e servizi', 'Προϊόντα και υπηρεσίες')),
    ('SETUP_COST_CENTERS', 'TYPES_AND_CATEGORIES', 'setup', 'cost-centers', 'building', ('Cost centres', 'Qendra kostoje', 'Centri di costo', 'Κέντρα κόστους')),
    ('SETUP_UNITS', 'TYPES_AND_CATEGORIES', 'setup', 'units', 'ruler', ('Units of measure', 'Njësi matëse', 'Unità di misura', 'Μονάδες μέτρησης')),
    ('SETUP_USERS', 'TYPES_AND_CATEGORIES', 'setup', 'users', 'user', ('Users and roles', 'Përdoruesit dhe rolet', 'Utenti e ruoli', 'Χρήστες και ρόλοι')),
    ('TEMPLATES', 'SYSTEM_SETTINGS', 'config', 'templates', 'printer', ('Print templates', 'Modelet e printimit', 'Modelli di stampa', 'Πρότυπα εκτύπωσης')),
    ('SKINS', 'SYSTEM_SETTINGS', 'skins', None, 'palette', ('Skins', 'Pamjet', 'Temi grafici', 'Θέματα εμφάνισης')),
    ('MENU', 'SYSTEM_SETTINGS', 'config', 'menus', 'menu', ('Menu', 'Menuja', 'Menu', 'Μενού')),
]

rows, names, order = [], [], {}
category_names = {}
path = os.path.join(BASE, 'names.tsv')
lines = [l for l in open(path, encoding='utf-8') if not l.startswith('menu:')]
for l in lines:
    parts = l.rstrip('\n').split('\t')
    if len(parts) == 6 and parts[0].startswith('document_category:') and parts[1] == 'Name':
        category_names[parts[0].split(':', 1)[1]] = parts[2:]
# Shown only to administrators, and not offered on Rights.
ADMIN_ONLY = {'USER_RIGHTS', 'MENU', 'ROLES', 'SKINS'}
for code, parent, screen, parameter, icon, text in TREE:
    order[parent] = order.get(parent, 0) + 10
    key = f'menu:{code}'
    category = text if isinstance(text, str) else None
    rows.append({'Key': key, 'IdSup': f'menu:{parent}' if parent else None, 'Active': True, 'IsElement': screen is not None, 'Code': code,
                 'Name': {'en': (category_names[category][0] if category else text[0])}, 'SortOrder': order[parent], 'Screen': screen,
                 'Parameter': parameter, 'DocumentCategoryId': f'document_category:{category}' if category else None, 'Icon': icon, 'AdminOnly': code in ADMIN_ONLY})
    names.append('\t'.join([key, 'Name', *(category_names[category] if category else text)]) + '\n')
with open(os.path.join(BASE, 'Menu.json'), 'w', encoding='utf-8', newline='\n') as f:
    f.write(json.dumps(rows, ensure_ascii=False, indent=1) + '\n')
open(path, 'w', encoding='utf-8', newline='\n').writelines(lines + names)

# Every language file must name each new English name.
missing = {}
for f in sorted(os.listdir(os.path.join(BASE, 'translations'))):
    known = {l.split('\t', 1)[0] for l in open(os.path.join(BASE, 'translations', f), encoding='utf-8') if '\t' in l}
    gone = sorted({r['Name']['en'] for r in rows} - known)
    if gone:
        missing[f] = gone
print(f'{len(rows)} menu nodes;', 'translations missing: ' + json.dumps(missing, ensure_ascii=False)[:600] if missing else 'every language names them')
