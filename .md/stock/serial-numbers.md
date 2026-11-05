# Serial numbers

Each unit of a tracked product has its own number. The register shows who holds each unit now and every move it made. Units stay under their product; stock and cost do not change by numbering.

**Where:** Stock and production › Serial numbers, with the tabs Find and Assign to stock. Numbers on lines are in the document editor. **Who:** the Serial numbers screen in your menu for moves, stock and assigning. Reading the list needs no screen: the document dialog uses it.

Tables: `tbl_product_serial` (a number, once per product in a company, and its holder) and `tbl_transaction_serial` (the numbers on a line).

## Actions

### Switch serial tracking on

Steps:
1. Open System settings › Types and categories › Products and services and click the type.
2. Tick "Tracks serial numbers: each unit has its own number on every line".
3. Click Save.

Result: every stock line of a product of this type needs one number per unit at posting.

API: `POST /products/types` with `tracksSerial`.

### Give numbers on a document line

Steps:
1. Pick the product and give the quantity. A link "Serial numbers: 0 of N" appears, red while the count differs.
2. Click it.
3. When they come new (the from side is a partner, or the type has no from side), type them: one per line, a part number after a comma, semicolon or tab.
4. Otherwise tick them from what the from side holds. You can search by serial or part number.
5. Click Done, then post the document.

Result: on posting, each number moves to the to side. A new number is added to the register. A number with no to side left has no holder, and the register shows "Left the company".

A draft can be saved without numbers; the check runs at posting.

Refused when, at posting:
- The count differs from the quantity: "… tracks serial numbers: N are needed, M are given. Give them on an invoice's line; a point of sale can't take them."
- A number is given twice on the document: "serial number … is given twice."
- A new number comes from anything but a partner: "serial number … of … is not here."
- A number that exists comes in with no from side: "serial number … of … already exists."
- The from side does not hold it: "serial number … of … is held elsewhere."

API: `serials` on the line of `POST /documents`, then `POST /documents/{id}/post`.

### Reverse a line with numbers

Steps: reverse the document. For part of a line, name the numbers that come back.

Result: those numbers go back to the side they came from.

Refused when: the numbers named differ from the quantity reversed: "line … reverses …: name that many of its serial numbers."

API: `POST /documents/{id}/reverse` with `serials`.

### Find a number

Steps:
1. Open the Find tab.
2. Type a serial or part number in Search.

Result: at most 500 rows, by number: serial number, part number, product, held by.

Refused when: you work above a company: "Serial numbers belong to a company: choose one of its nodes."

API: `GET /serials?product={id}&holder={id}&search={text}`

### See where a number has been

Steps:
1. Click a row on the Find tab.
2. Read its moves. Click a move to open its document.

Result: every posted move, oldest first: date, document, type, from, to. A number given to stock already held has no move yet.

API: `GET /serials/{id}/moves`

### Give numbers to stock already held

Steps:
1. Open the Assign to stock tab. It lists tracked products at each cost centre: Quantity, With a number, Without.
2. Click a row.
3. Type the numbers, one per line, a part number after a comma.
4. Click Assign. The button is off when more numbers are typed than are left.

Result: the numbers enter the register, held by that cost centre. The stock and its cost stay as they are. No document is made.

Refused when:
- The product does not track serial numbers: "This product does not track serial numbers: set it on its type first."
- No number, or one given twice: "Give each serial number once."
- More than fit: "The place holds …, of which … have a serial number: … more do not fit."
- The number exists for this product: "Serial number … already exists."

API: `GET /serials/stock`, `POST /serials/assign` with `costCenterId`, `productId`, `unitId`, `serials`.

## Not possible

- Point of sale: lines cannot take numbers, so a tracked product is refused at posting there.
- Production and stock conversion: those screens give no numbers, so a tracked product is refused at posting.
- A composite product is not itself tracked, even when its type tracks serial numbers.

## Related

- [Document lines](../documents/lines.md)
- [Reversing a document](../documents/reversing.md)
- [Product types and categories](../products/product-types-and-categories.md)
- [Lots](lots.md)
- [Orders to produce](production.md)
