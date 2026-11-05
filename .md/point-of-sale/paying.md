# Paying and unpaid bills

Paying one or more posted bills into the cash desk, with the change worked out, and the list of bills still waiting.

**Where:** the pay screen opens from a table, a basket, a booking, a check-out or the Unpaid view of the point of sale. **Who:** the screen `pos`, and the money-in document type given to you or your role.

## Actions

### Pay bills

**Steps**
1. On the pay screen, keep the bills you are paying ticked. All are ticked at first.
2. Choose the payment method. The method in the settings is proposed. The choices are the methods of the money-in document type.
3. Type what was received on the keypad, or press Exact. This step is optional.
4. Press Pay with the amount.

**Result**
- One money document per client is saved and posted into the cash desk, with one line per bill for its **whole open amount**, at no VAT. The line's reference is the bill's code.
- The document is linked to the bills it pays, so they leave the unpaid list.
- A message shows what was paid and the change.
- The screen goes back to the kind's home view.

A bill cannot be paid in part here. Any method can be chosen, and the amount received is not stored.

**Refused when**
- The Pay button is disabled when no bill is ticked, or when less than due was received (*Not enough* shows). Leaving Received empty is allowed.
- "Bill {id} is not an open bill of this type.": someone else paid it first.
- "There is no open bill to pay."
- "There is no such cash desk."
- "the payment type has no line for this cash desk's type.": the money type has no line for that kind of cost centre.

**API:** `POST /pos/bills/pay` with `billTypeId`, `paymentTypeId`, `cashDeskId`, `billIds`, `paymentMethodId`. The API also takes `tableId`, `all` and `date`; the screen does not use them.

### Work out the change

**Steps:** type the amount received. *Change (money back)* updates as you type: received less due, to the cent. Exact fills in the amount due.

**Result:** shown on screen only. No document records it, and the shift counts the amount due, not the amount received.

### See unpaid bills

**Steps**
1. Press Unpaid in the top bar.
2. Turn *Only mine* off to see everyone's. It is on at first.

**Result:** a list of bills with code, table, waiter, time and open amount. Open means total less paid less reversed. The waiter is the user who wrote the bill. The list holds the posted bills of the point of sale's bill type that you can see, and only those of the point of sale on screen: bills whose lines leave from it. Two points of sale that share a bill type each see only their own.

**API:** `GET /pos/bills/unpaid?type=&pointOfSale=&user=&table=`

### Pay from the unpaid list

**Steps:** press Pay on a row, or Pay all shown when more than one shows.

**Result:** the pay screen opens with those bills.

## Related

- [tables.md](tables.md)
- [shop.md](shop.md)
- [shifts.md](shifts.md)
- [money-in-out.md](money-in-out.md)

## Open points

- Open orders that are not billed yet are not in the unpaid list. They show on the table only.
