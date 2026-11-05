# Shifts and cash-up

A shift is opened with the cash in the drawer and closed with the cash counted, always by hand. A point of sale has one open shift at most, and so does a cash desk. A shift belongs to the point of sale, not to a person.

**Where:** Point of sale › Open the point of sale › Shift, and Point of sale › Shifts for the list. **Who:** the screen `pos` for open, summary and close. The screen `pos-shifts` for the list.

## Requires shift

The column `RequiresShift` in `tbl_point_of_sale_setting` decides. It is read own row, then kind, then default; empty at every level means a shift is required. The base pack sets it false for a repair shop only. Set it for one point of sale on its own tab (Company › Cost centres › the point of sale › tab Point of sale), field "Bills need an open shift": Yes, No, or empty to take its kind's or the company's. Set a kind's or the company's in Point of sale › Point-of-sale settings.

When a shift is required and none is open:
- The whole point of sale screen shows only *Open a shift*.
- The server refuses these from anywhere, including the Documents screens:
  - posting a bill: "Open a shift first: this point of sale has none open." The check reads the point of sale on the bill's lines.
  - saving an order of the point of sale's order type: the same message. An order is not posted (it is billed at the till), so the check is on saving.
  - posting money in or out of a cash desk used by such a point of sale: "Open a shift first: the point of sale of this cash desk has none open." It passes when a shift is open on that cash desk.

When it is not required, the Shift view still works and a shift can be opened.

## Which documents a shift holds

The time defines the shift (the founder, 2026-11-04): its documents are those of its point of sale's types made between the shift's opening and its closing, by their `CreatedOn`. There is no link from a document to the shift.

At a till, the shift is the till's own: the user signs in at the till and opens it there. Its bills are those that record the till; its money is what went to its cash desk. "Open a shift first" on a bill made at a till looks at the till's shift.

## Actions

### Open a shift

**Steps**
1. Press Shift, or use the *Open a shift* card.
2. Enter *Cash in the drawer*.
3. Press Open the shift.

**Result:** a row in `tbl_pos_shift` with the point of sale (the till, when the user signed in at one), the cash desk from its settings, who opened it, the time and the opening cash. Nothing is posted.

**Refused when**
- "This point of sale already has an open shift."
- "Another point of sale has a shift open on this cash desk: close it first, or give each point of sale its own cash desk." Expected cash is read from the cash desk, so two shifts open on one desk would count each other's money.
- "The opening cash can't be below zero."
- "This point of sale has no cash desk."
- "Shifts belong to a company: choose one of its nodes."

**API:** `POST /pos/shift/open` with `pointOfSaleId`, `openingCash`. `GET /pos/shift/current?pointOfSale=` reads the open one.

### See the summary

**Steps:** press Shift while one is open.

**Result:** who opened it, the opening cash, and so far:
- **Bills:** number of posted bills at this point of sale since the shift opened.
- **Sales:** their total with VAT. A reversal subtracts.
- **One line per payment method:** money documents made from bills, with a line on the cash desk. A reversal counts the other way.
- **Money in and Money out:** money documents on the cash desk not made from a bill.

Expected cash is not shown while the shift is open.

**API:** `GET /pos/shift/{id}`

### Close a shift

**Steps**
1. Press Shift.
2. Count the drawer and enter *Counted cash*, with a note if needed.
3. Press Close the shift.

**Result:** the count goes in first, and only then the screen shows Expected cash, Counted cash and Difference.

```
Expected cash = opening cash + payments by cash methods + money in - money out
Difference    = counted cash - expected cash
```

Which methods count as cash is the `IsCash` column of `tbl_payment_method`; a payment with no method counts as cash. In the base pack Cash does; Credit card and Unpaid do not.

**A closed shift is final and posts nothing.** A difference makes no document. Bills still unpaid stay in the [unpaid list](paying.md).

**Refused when**
- "This shift is closed."
- "The counted cash can't be below zero."
- "The shift does not exist."

**API:** `POST /pos/shift/{id}/close` with `countedCash`, `note`.

### See the shifts list

**Where:** Point of sale › Shifts.

**Steps**
1. Open the screen. It lists the newest 500 shifts of your companies, with point of sale, who opened and closed, times, opening, expected and counted cash, and difference.
2. Click a row to see its summary and note.

**API:** `GET /pos-shifts`, `GET /pos-shifts/{id}`

## Related

- [settings.md](settings.md)
- [paying.md](paying.md)
- [money-in-out.md](money-in-out.md)
