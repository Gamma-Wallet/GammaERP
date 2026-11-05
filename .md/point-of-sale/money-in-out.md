# Money in and out

Cash put into or taken out of the cash desk outside a bill, such as ice bought from the shop next door.

**Where:** Point of sale › Open the point of sale › Money in / out. **Who:** the screen `pos`, and the money document types given to you or your role.

## Actions

### Record money in or out

**Steps**
1. Choose Money in or Money out.
2. Choose the person from the list. The list is who the document type allows on its side, for example staff or clients. The walk-in client is proposed when listed; a single name is chosen for you.
3. Optionally type a reason. It becomes the document's description.
4. Type the amount on the keypad.
5. Press Record money in or Record money out.

**Result:** a document of the money-in or money-out type is saved and posted, with the person on the side the type names and one line on the cash desk for the amount at no VAT. The screen stays open for the next one. In the [shift](shifts.md) it counts as Money in or Money out, not as a bill payment.

**Refused when**
- The Record button is disabled until a person and an amount above zero are given.
- "No document type is set for money {in or out}.": the settings hold no type.
- "There is no one to choose.": the type's side has no elements.
- "{type} has no cash-desk line.": the type has no cost-centre line.
- "this document type has not been given to you or to one of your roles."
- "Open a shift first: the point of sale of this cash desk has none open.": a point of sale that requires a shift uses this cash desk, and no shift is open on it. The server checks it on posting, also from the Documents screens. See [shifts.md](shifts.md).
- Posting refusals, such as a closed period.

**API:** `POST /documents`, `POST /documents/{id}/post`

## Related

- [settings.md](settings.md)
- [shifts.md](shifts.md)
- [paying.md](paying.md)
