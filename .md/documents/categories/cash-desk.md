# Cash desk documents

Money in and out of a cash desk. Payment and Receipt can cover open invoices.

**Where:** Documents › Cash desk. **Who:** see [who may do what](../life-cycle.md#who-may-do-what).

## Types in the base pack

| Type | Code | From | To | Covers invoices | Reversed by |
|------|------|------|----|-----------------|-------------|
| Payment | `PAYMENT` | Cost centre | Client or supplier | Yes | Reversal of payment |
| Receipt | `RECEIPT` | Client or supplier | Cost centre | Yes | Reversal of receipt |
| Prepayment | `PREPAYMENT` | Cost centre | Client or supplier | No | Reversal of prepayment |
| Advance | `ADVANCE` | Client or supplier | Cost centre | No | Reversal of advance |
| Employee advance | `EMPLOYEE_ADVANCE` | Cost centre | Staff | No | Reversal of employee advance |
| Wage payment | `WAGE_PAYMENT` | Cost centre | Staff | No | None |
| Rent payment | `RENT_PAYMENT` | Cost centre | Client or supplier | No | None |
| Point-of-sale receipt of money | `POS_MONEY_IN` | Client or supplier | None | No | `POS_REFUND` |
| Point-of-sale payment | `POS_MONEY_OUT` | None | Staff | No | None |
| Point-of-sale receipt from staff | `POS_MONEY_FROM_STAFF` | Staff | Cost centre | No | None |
| Point-of-sale refund | `POS_REFUND` | None | Client or supplier | No | Is a reversal |

Payment and Receipt take lines of kind Account, Cash desk account, Bank account, Client, Supplier and Staff. Prepayment takes Account, Cash desk account, Bank account, Supplier. Advance takes the same with Client. Employee advance with Staff. Wage and Rent payment take Cash desk account and Bank account. None of them moves stock.

Point-of-sale receipt of money may be reversed in part (`AllowsPartialReversal` in the pack), so one bill's line of a payment for several bills can be refunded. Cancelling a cinema booking does this: see [events](../../point-of-sale/events.md#cancel-a-booking).

## Actions

### Pay or receive money

Steps:
1. Press **New** and pick the type.
2. Pick the cash desk and the client, supplier or staff member.
3. Add a line of the fitting kind with the amount. On Payment and Receipt, the Invoice number column covers an open invoice.
4. Save and post.

Result: the cost centre's and the partner's balances and the accounts move. See [payments and open invoices](../payments-and-open-invoices.md).

Refused when:
- `OpenItemError`: see [payments](../payments-and-open-invoices.md).
- `NotConfiguredError`: the document type does not take this payment method.
- Otherwise see [saving](../drafts.md#save) and [posting](../posting.md#post).

API: `POST documents`, `POST documents/{id}/post`.

## Note

The same types are listed under [Bank](bank.md). A document is filed where it was started, and the place picked in From or To says which money moved. Nothing checks that a document started under Cash desk names a cash desk.

Payment methods per type are set on the type's Payment methods tab. The base pack links three pairs (`tbl_inter_payment_method_document_type`); a type naming none offers them all.

## Open points

- Point-of-sale cash documents come from the shift screens. Whether a person writes them here depends on who is given them.

## Related

- [Payments and open invoices](../payments-and-open-invoices.md)
- [Reversing](../reversing.md)
- [Bank](bank.md)
