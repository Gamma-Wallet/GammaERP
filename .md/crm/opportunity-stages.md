# Opportunity stages

The steps of the company's pipeline. Each stage has a chance of winning and an outcome. This is configuration: the board and the forecast only read these rows.

**Where:** CRM › Opportunity stages. **Who:** the menu item in your menu. Anyone signed in can read the rows; only an administrator adds, changes or switches them off.

Configuration table: `tbl_opportunity_stage`.

| Base pack stage | Chance | Sort order | Outcome |
|-----------------|--------|------------|---------|
| Qualify | 10 | 10 | 0 |
| Propose | 40 | 20 | 0 |
| Negotiate | 70 | 30 | 0 |
| Won | 100 | 40 | 1 |
| Lost | 0 | 50 | 2 |

## Actions

### Add a stage

Steps:
1. Click Add.
2. Fill:
   - Name. One text per language.
   - Chance. The chance of winning, in percent.
   - Sort order. The column's place on the board.
   - Outcome. 0 open, 1 won, 2 lost. It is typed as a number.
3. Click Save.

Result: a new column on the Pipeline, and the stage is offered on the opportunity form. At outcome 2 it asks for the reason it was lost.

Refused when:
- Chance below 0 or above 100, or Outcome not 0, 1 or 2: "A stage's chance is 0 to 100 %, and its outcome 0 (open), 1 (won) or 2 (lost)."
- You are not an administrator: "only an administrator changes the configuration."
- You work above a company: "Configuration belongs to a company: choose one of its nodes."

API: `POST /config/opportunity-stages`. Read with `GET /config/opportunity-stages`.

### Change a stage

Steps: click the row, change it, click Save.

Result: the board and the forecast use the new values at once. A new chance changes the weighted forecast of every open opportunity at that stage.

Refused when: as for adding.

API: `POST /config/opportunity-stages` with its `id`.

### Switch a stage off

Steps: click the row, click Switch off…, confirm.

Result: the column goes. Nothing is deleted.

Refused when: an active opportunity is at this stage: "Opportunities are in this stage: move them to another stage first." Move them on the [Pipeline](pipeline.md) or in the register first.

API: `DELETE /config/opportunity-stages/{id}`

## Related

- [Pipeline](pipeline.md)
- [Forecast](forecast.md)
- [Opportunities](opportunities.md)
