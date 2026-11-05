# Opportunity on documents

A document whose posting books nothing, such as an offer or an order, can name the opportunity it belongs to. It is a plain link, set by hand, and carried over to documents made from it.

**Where:** Documents › the category › the document, field Opportunity. **Who:** the right to change the document. The choices are the opportunities you may read.

## Actions

### Link a document to an opportunity

Steps:
1. Open a document whose type books nothing. In the base pack these are:
   - sales and purchase offers,
   - sales and purchase contracts,
   - the purchase request, purchase order and sales order,
   - the point-of-sale contract and order,
   - the repair intake and repair offer.
2. Choose the Opportunity. It is searchable.
3. Save the document.

Result: the document keeps the link in `opportunity_id`. The opportunity and its stage do not change.

Refused when:
- The opportunity is not one of this company's: "The opportunity does not exist in this company."
- The document can no longer be changed.

API: `opportunityId` on `POST /documents`. Read with `GET /documents/{id}`.

### Carry the link to the next document

A document made from another one, by **Generate** or a flow, takes the source's opportunity. See [flows](../documents/flows.md).

### Remove the link

Steps: clear Opportunity and save the document.

API: `opportunityId: null` on `POST /documents`.

### See an opportunity's documents

Steps:
1. Open CRM › Opportunities and click the opportunity.
2. Open the **Documents** tab.

Result: the active documents linked to it, oldest document date first: number, type, date, and Posted or Draft. Only documents you may read are listed. The number opens the document. With none: "No documents are linked to this opportunity yet. Choose it on an offer, an order or a contract." Nothing changes.

Refused when: you may not read the opportunity: the call answers 404 and the panel shows the error.

API: `GET /opportunities/{id}/documents?language={code}`. Each row: `id`, `code`, `type`, `date`, `posted`, `categoryId`.

## Where the field shows

Only on document types marked "posting books nothing". Invoices and other posted documents do not show it. This is the document type's own setting, on System settings › Document types.


## Related

- [Opportunities](opportunities.md)
- [Pipeline](pipeline.md)
