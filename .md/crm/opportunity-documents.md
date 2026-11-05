# Opportunities and documents

Each document of each opportunity: what was offered, ordered, contracted and invoiced.

**Where:** CRM › Opportunities and documents. **Who:** the report in your menu.

## Actions

### Read the report

Steps:
1. Open the report.
2. Set **Period** (by the document's date) and, if wanted, **Stage**.

Result: one row per document, by opportunity and date:

| Column | Holds |
|--------|-------|
| Opportunity | The opportunity's name |
| Stage | Its stage now |
| Partner | Its client |
| Document | The document's number |
| Type | The document type |
| Date | The document date |
| State | Invoice, Posted or Draft |
| Total | The document's total with VAT, in the company's currency; negative for a reversal |

A document counts when it is linked to the opportunity (see [opportunity-on-documents.md](opportunity-on-documents.md)), and so does the reversal of one. State is Invoice for a posted document whose type holds an open item, and for the posted reversal of one. Only the opportunities the Opportunities register shows you, and the documents you see, are listed. A row opens its document.

Refused when: the report is not in your menu: "this screen is not in your menu."

API: `GET /opportunities/documents-report?from=&to=&stage=`. **Excel**: `GET /opportunities/documents-report/xlsx` with the same filters. It has no PDF.

## Related

- [Pipeline](pipeline.md)
- [Forecast](forecast.md)
- [Opportunity on documents](opportunity-on-documents.md)
