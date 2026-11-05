# The general client

A partner that always exists by default, for sales to a customer nobody identifies. The point of sale never identifies a customer: it uses this partner.

**Where:** Clients and suppliers › category Clients, code GENERAL. Named on Point of sale › Point-of-sale settings, column Walk in client. **Who:** to see it, the read right on Clients. To change which client is used, an administrator.

The base pack makes the partner General client: type Unknown, category Clients. Each point-of-sale setting row points at it.

## Actions

### Choose the general client of a point of sale

Steps:
1. Open Point of sale › Point-of-sale settings.
2. Click the row.
3. Choose Walk in client.
4. Click Save.

Result: that point of sale's bills go to this client.

Refused when: you are not an administrator, or the link points at a row the company does not see.

API: `POST /config/point-of-sale-settings`, column `walkInClientId`.

### See what the general client has bought

Steps: open Reports › Financial › Client and supplier ledger and choose the partner.

Result: its posted documents and running balance.

API: `GET /partners/{id}/ledger?from={day}&to={day}`

## What else uses it

- Bills made by the point of sale's seat and stay bookings go to this client, under the guest's name. With no bill type or client set, they are refused: NotConfiguredError, "the point of sale has no bill type or walk-in client."
- A new client typed on a repair copies the kind and categories of the general client. With none set, it is refused: NotConfiguredError, "the company has no general client to model a client on."

The General client can't be switched off while a point of sale's settings sell to it: "This client is the general client of a point of sale's settings: choose another client there first." Renaming it is fine: the settings point to the row, not the name.

## Related

- [Point-of-sale settings](../point-of-sale/settings.md)
- [Partners](partners.md)
- [Balances](balances.md)
