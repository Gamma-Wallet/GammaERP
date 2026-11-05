# Bank documents

Money in and out of a bank account. The types are the same as the cash desk's, with a bank as the cost centre.

**Where:** Documents › Bank. **Who:** see [who may do what](../life-cycle.md#who-may-do-what).

## Types in the base pack

| Type | Code | From | To | Covers invoices | Reversed by |
|------|------|------|----|-----------------|-------------|
| Payment | `PAYMENT` | Cost centre | Client or supplier | Yes | Reversal of payment |
| Receipt | `RECEIPT` | Client or supplier | Cost centre | Yes | Reversal of receipt |
| Prepayment | `PREPAYMENT` | Cost centre | Client or supplier | No | Reversal of prepayment |
| Advance | `ADVANCE` | Client or supplier | Cost centre | No | Reversal of advance |
| Employee advance | `EMPLOYEE_ADVANCE` | Cost centre | Staff | No | Reversal of employee advance |

Kinds of line are as in [Cash desk](cash-desk.md).

## Actions

### Pay or receive through the bank

Steps:
1. Press **New** and pick the type, or use **Write a new document** on a bank statement line: the bank and date are then filled in.
2. Pick the bank in the cost centre side, and the client, supplier or staff member.
3. Add a line with the amount. On Payment and Receipt, type the Invoice number to cover an open invoice.
4. Save and post.

Result: the bank's and the partner's balances and the accounts move. Once posted, the document can be matched to a bank statement line. One written from a statement line is matched to it when posted in the editor; see [bank statements](../bank-statements.md#write-a-new-document-for-a-line).

Refused when: see [Cash desk](cash-desk.md#pay-or-receive-money).

API: `POST documents`, `POST documents/{id}/post`.

## Related

- [Bank statements](../bank-statements.md)
- [Payments and open invoices](../payments-and-open-invoices.md)
- [Cash desk](cash-desk.md)
