# Depreciation methods

A method is a formula. The starter pack has Straight-line, `(VF/DT)*DK`, and Remaining value, `((VF*PD*((1-PD)^YK))/365)*DK`.

**Where:** Fixed assets › Depreciation methods (`/config/depreciation-methods`, table `tbl_depreciation_method`). **Who:** an administrator changes; see [configuration-lists.md](../accounting/configuration-lists.md).

## Columns

Name, Description, Formula.

## What a formula may use

Numbers, `+ - * / ^`, brackets, and these variables:

| Variable | Meaning |
|----------|---------|
| VF | The initial value. |
| DT | The asset's total days, start to end. |
| DK | The days of this run's period. |
| YK | The full years since its start. |
| PD | The declining percentage, as a fraction. |
| PL | The linear percentage, as a fraction. |

Anything else is refused, so a formula can never run code.

## Actions

### Add or change a method

Steps:
1. Press **Add**, or click a method.
2. Enter the formula and press **Save**.

Result: runs from now on use it. Past runs do not change.

Refused when: "The formula can't be worked out; it may use VF, DT, DK, YK, PD, PL." The formula is tried on sample values when saved.

API: `POST /config/depreciation-methods`.

### Switch a method off

Steps: open it, press **Switch off…** and confirm.

API: `DELETE /config/depreciation-methods/{id}`.

## Related

- [depreciation-run.md](depreciation-run.md)
- [books.md](books.md)
