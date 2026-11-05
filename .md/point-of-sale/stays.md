# Stays (Hotel)

Rooms by day. A stay holds a room and stays open until check-out, which bills the nights slept.

**Where:** Point of sale › Open the point of sale, at a Hotel. **Who:** the screen `pos`.

## Before you start

Add Floors under the hotel and Rooms under them: see [settings.md](settings.md). Set *A night is sold as* on the hotel's Point of sale tab: check-out needs this product.

## Actions

### Read the rooms

A grid of rooms by 14 days, from today. A free night is empty. A held night shows the first two letters of the guest. A checked-out stay shows in grey. The arrows move seven days earlier or later. Rooms are the places with nothing under them.

**API:** `GET /pos/stays?pointOfSale=&from=&to=`

### Book a stay

**Steps**
1. Tap a free night in a room.
2. Enter the number of nights, and optionally the guest and price per night.
3. Press Book the stay.

**Result:** the room is held from that day to the day it is left; it is free again that night. No document, no bill, no stock and no balance. The price is without VAT; empty takes the product's sale price.

**Refused when**
- "The room is taken on those days.": the database refuses overlapping days for a room, so two tills cannot sell the same night.
- "A stay ends on a later day than it starts."
- "The room is not one of this point of sale's."

**API:** `POST /pos/stays?pointOfSale=` with `roomId`, `fromDate`, `toDate`, `guest`, `price`.

### Check in

**Steps**
1. Tap a held night.
2. Press Check in. The button shows only while the stay is not checked in and not checked out.

**Result:** the stay records today as its arrival day (column `checked_in_on` on the reservation). The bar shows "Checked in {day}". Nothing else changes: no document, no bill, no stock and no balance. Checking in again keeps the first day.

**Refused when**
- "The stay starts on {day}: check in from that day.": today is before the stay's first day.
- "The stay is already checked out."
- "The stay does not exist."

**API:** `POST /pos/stays/{id}/check-in`. Answers with the stay.

### See a stay

**Steps:** tap a held night.

**Result:** the bar shows the guest and dates, and "Checked in {day}" once the guest is checked in. A stay not checked out offers Cancel the stay, Check in (until the guest is checked in), Extend and Check out.

### Extend a stay

**Steps**
1. Tap a held night of a stay not yet checked out.
2. Pick a later end day in the date box (*Extend to*).
3. Press Extend.

**Result:** the stay's end moves to that day, the day the room is left. The room is held on the added nights. No document, no bill, no stock and no balance; check-out bills the nights.

**Refused when**
- "Extending moves the stay's end to a later day.": the day picked is the stay's end or earlier.
- "The room is taken on those days.": another stay holds the room on an added day.
- "The stay is already checked out."
- "The stay does not exist."

**API:** `POST /pos/stays/{id}/extend?toDate=`. Answers with the stay.

### Check out

**Steps**
1. Tap a held night, then Check out.
2. Finish on the [pay screen](paying.md).

**Result:** the stay ends today if today falls after its first day and before its planned end. Checked out on its first day, it ends the next day: one night. Checked out on or after its planned end, it keeps its planned end. One bill is posted at the room for those nights, at the stay's price, to the walk-in client under the guest's name. The bill stays in [Unpaid](paying.md) if you leave.

**Refused when**
- "The stay has not started: cancel it instead.": today is before the stay's first day.
- "The stay is already checked out."
- "The stay does not exist."
- "set what a night is sold as in the point of sale's settings."
- "the point of sale has no bill type or walk-in client."
- "the product fits none of the bill's lines."
- "Open a shift first: this point of sale has none open."

**API:** `POST /pos/stays/{id}/check-out`. Answers with the bill id.

### Cancel a stay

**Steps:** tap a held night, then Cancel the stay.

**Result:** the stay is switched off and the room is free. Nothing is reversed, because nothing was billed.

**Refused when:** "The stay is checked out: reverse its bill."

**API:** `DELETE /pos/stays/{id}`

### Open a stay again

**Steps:** reverse the stay's bill (see [reversing](../documents/reversing.md)).

**Result:** the stay is open again. It shows with no bill and can be checked out again, which makes a new bill, or cancelled.

## Related

- [paying.md](paying.md)
- [settings.md](settings.md)
- [shifts.md](shifts.md)
- [Reversing](../documents/reversing.md)
