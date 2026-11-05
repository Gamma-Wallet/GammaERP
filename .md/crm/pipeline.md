# Pipeline

A board with one column per stage. A person moves each opportunity by hand.

**Where:** CRM › Pipeline. **Who:** the Pipeline screen in your menu.

## Actions

### Read the board

Steps: open the screen.

Result: a column for each active stage, in sort order, with its chance in percent. The board shows only the opportunities the Opportunities register shows you: its category rights and its Access tab apply. Administrators see all. Each card shows the opportunity's name, client, amount and currency, and its expected close when it has one. Below them: how many documents are linked to it, and what was invoiced, in the opportunity's currency at each document's day. An invoice here is a posted document whose type holds an open item, the invoice run's rule; a posted reversal of one counts against it. The link opens the opportunity in its register, where its Documents tab lists them. Cards are ordered by expected close.

Refused when: the screen is not in your menu: "this screen is not in your menu."

API: `GET /opportunities/board`

### Move an opportunity

Steps:
1. On the card, open "Move to" and choose a stage.

Result: the opportunity is in that stage and the board reloads. The move is in the row's history. Moving it out of a lost stage clears its reason. Nothing else changes: no document is made and the partner stays as it is.

Refused when:
- There is no such stage: "There is no such stage."
- There is no such opportunity: "There is no such opportunity."

API: `PUT /opportunities/{id}/stage` with `stageId`.

### Mark it lost

Steps:
1. On the card, choose a lost stage in "Move to". A box "Reason it was lost" appears.
2. Type the reason and leave the box.

Result: the opportunity moves and keeps the reason. An empty box sends nothing.

Refused when: no reason reaches the service: "A lost opportunity needs its reason."

API: `PUT /opportunities/{id}/stage` with `stageId` and `lostReason`.

### Mark it won

Steps: choose a won stage in "Move to".

Result: it counts in full under Won in the forecast. Making the offer, order or invoice is a separate step by hand.

API: `PUT /opportunities/{id}/stage`

## Related

- [Opportunities](opportunities.md)
- [Opportunity stages](opportunity-stages.md)
- [Forecast](forecast.md)
