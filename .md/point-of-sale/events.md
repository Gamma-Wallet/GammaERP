# Events (Cinema / theatre)

Shows in auditoriums, each seat sold once, booked on one bill and paid at once.

**Where:** Point of sale › Open the point of sale, at a Cinema / theatre. Shows are set in Point of sale › Shows. **Who:** the screen `pos`. Only an administrator changes shows.

## Actions

### Set up the auditorium

Add an Auditorium under the point of sale, Rows under it and Seats under each row. See [settings.md](settings.md).

### Add a show

**Steps**
1. Open Point of sale › Shows and add a row.
2. Fill in the name, the auditorium, the product a ticket is sold as, the day, and optionally the start time and price.
3. Save.

**Result:** a row in `tbl_pos_show`. The price is without VAT; empty takes the product's sale price.

**Refused when:** you are not an administrator.

**API:** `POST /config/pos-shows`

### See the shows

The screen lists shows from today on, soonest first, with the day, time, auditorium and free seats.

**API:** `GET /pos/shows?pointOfSale=&from=`

### Book seats

**Steps**
1. Tap a show. The seat plan shows rows from the Screen / stage down.
2. Tap free seats to choose them; tap again to unchoose.
3. Optionally type the guest's name.
4. Press Book and pay.
5. Finish on the [pay screen](paying.md).

**Result:** all the seats or none: a reservation per seat and one posted bill with the seats as the quantity of the show's product, to the walk-in client. A typed guest name shows as the bill's client name and on the reservation. The bill is a normal bill: stock moves as the product's line type says, and it stays in [Unpaid](paying.md) if you leave.

**Refused when**
- "Choose a seat first."
- "The show does not exist."
- "A seat is not one of this show's."
- "A seat has just been taken: choose another.": another till sold it. The database refuses a second reservation of a seat at a show. The screen reloads the seats.
- "the point of sale has no bill type or walk-in client."
- "the product fits none of the bill's lines."
- "Open a shift first: this point of sale has none open."

**API:** `POST /pos/shows/book?pointOfSale=` with `showId`, `seatIds`, `guest`. Answers with the bill id.

### See who holds a seat

**Steps:** tap a taken seat.

**Result:** the bar shows *Taken* and the guest name if there is one, with Keep and Cancel the booking.

**API:** `GET /pos/shows/{id}/taken`

### Cancel a booking

**Steps**
1. Tap a taken seat.
2. Press Cancel the booking.

**Result:** all or nothing: the bill is reversed whole and **all the seats on that bill** are free again. If the bill was paid, each posted payment's line for this bill is reversed too, with the payment type's reversal type (in the pack: Point-of-sale refund, which reverses Point-of-sale receipt of money). The money goes back out of the cash desk. The pack lets Point-of-sale receipt of money be reversed in part, so when one payment paid several bills, only this bill's line is refunded.

**Refused when**
- "No seats are held on this bill."
- "no reversal type reverses this document type; set one first." The pack sets a reversal type for the bill and for Point-of-sale receipt of money.

**API:** `POST /pos/shows/cancel/{bill}`

## Related

- [paying.md](paying.md)
- [settings.md](settings.md)
- [shifts.md](shifts.md)
