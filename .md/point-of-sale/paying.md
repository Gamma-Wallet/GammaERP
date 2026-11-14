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

### Pay by bank (QR code)

For a payment method with **Shows a bank QR** ticked (the packs' "Bank transfer (QR)"), linked at this point of sale to a bank account (Currencies and payments › Payment methods by cost centre, **Cash bank**). The founder, 2026-11-14.

Steps:
1. On the pay screen choose **Bank transfer (QR)**. In place of the keypad the screen shows the payment QR code, the holder, the IBAN, the amount and the reference: the codes of the bills ticked.
2. The customer scans it with their banking app and pays (in the euro area banks send it as an instant transfer).
3. When the bank account is connected to its bank: press **Check bank**. The screen lists the bank's money-in lines of that amount from the last hour not yet held, those that mention the reference first, pending ones marked. Press **Paid** on the right line. When nothing shows: "Not in the bank yet. Check again in a moment."
4. When it is not connected: check the bank app by eye and press **Received**.

Result:
- **Paid:** the receipt is posted into the bank, as for any payment of the method, and that bank line is kept on the bank's statement "Till payments <day>", already matched to the receipt, in the same press (the founder, 2026-11-14, Q2). A later Fetch from bank skips it, since its bank id is held. Finance can Undo the match on Bank statements.
- **Received:** the receipt is posted into the bank; the bank line is matched later on Bank statements (Q3).

The QR code follows EPC069-12, version 002: BCD, 002, 1 (UTF-8), SCT, BIC, holder (at most 70), IBAN, EUR and the amount, the reference as text (at most 140). Error correction M. Drawn on the server as SVG.

Refused when:
- `NotConfiguredError: link this payment method to a bank account at this point of sale (... Cash bank).`
- `NotConfiguredError: the bank account <name> has no IBAN. Type it on Bank statements, Bank accounts.`
- "The bank account has no holder: type it on Bank statements, Bank accounts." (the company's name is used when the holder is empty)
- "A payment QR code is in euro only; the company's currency is …" (the founder, 2026-11-14, Q8)
- Check bank: every refusal of a read of the bank, see [Bank statements](../documents/bank-statements.md#fetch-from-the-bank).
- Paid: "One bank payment pays the open bills of one client."; "The bank line's amount is not the amount to pay."; "This bank line already pays something else."

API: `GET /pos/bank-qr?pointOfSale=&paymentMethod=&amount=&reference=` (no content when the method shows no QR), `POST /pos/bank-check` with `pointOfSaleId`, `paymentMethodId`, `amount`, `reference`, `POST /pos/bills/pay-by-bank` with `pay` (as `/pos/bills/pay`) and `bankLine` (a line from Check bank).

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
