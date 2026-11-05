# Production point settings

What makes the display work: a cost centre of the right type, a role, and three settings on document types.

**Where:** Company › Cost centres; System settings › Document types; Menu access. **Who:** an administrator for document types.

## Cost-centre type

A production point is a cost centre whose type has `IsProductionPoint` (`tbl_cost_center_type`). The base pack has one: Production point (`PRODUCTION_POINT`), directly under the company. Another type becomes one by ticking "Production points: they have a production point display" in its type editor: Company › Cost centres › Set up, saved with the type (`POST /cost-centers/types`). See [Cost centres](../../company/cost-centres.md#mark-a-type-as-a-bank-or-a-production-point).

### Add a production point

**Steps**
1. Open Company › Cost centres and add a row of type Production point, such as Kitchen or Bar.
2. Save.

**Result:** it is listed on the display. A production document takes the ingredients from this cost centre's stock.

**API:** `POST /cost-centers`

## The Production point role

The role `PRODUCTION_POINT` (`tbl_user_category`) comes with the base pack. The pack gives it the menu item Point of sale › Production point display (`tbl_inter_menu_user`), the document type Production (`tbl_inter_document_type_user`) and Write on the document category Production (`tbl_elements_access`). An administrator can change these.

### Give someone the role

**Steps**
1. Open the user in Users and rights › Users and give them the role.
2. On the **Cost centres** tab link them to the production point.

**Result:** the display shows in their menu and Produced works. If the order's production document type is not the pack's Production, give that type to the role in System settings › Document types, on its users tab; otherwise Produced on a product with a recipe is refused with "this document type has not been given to you or to one of your roles." An item with no recipe needs no document, so it still works.

## Document type settings

**Where:** System settings › Document types › the type › General tab. **Who:** an administrator; others see the boxes disabled.

| Column (`tbl_document_type`) | Screen label | Meaning | Base pack |
|------------------------------|--------------|---------|-----------|
| `sends_to_production` | Its lines go to the production point display | Orders of this type reach the display | Point-of-sale order |
| `production_document_type_id` | Production document made | The production document a recipe makes. Shown once the box above is ticked | Production, on Point-of-sale order |
| `sells_produced_product` | A line from a produced order takes the product itself | A bill line made from a produced order line takes the product, not its ingredients again | Point-of-sale receipt |

### Change a setting

**Steps**
1. Open the document type's General tab.
2. Tick or untick, and choose the production document.
3. Press Save.

**Result:** the type's row changes. Without `sells_produced_product`, a composite product made on the display would give up its ingredients a second time when billed.

**Refused when:** you are not an administrator.

**API:** `GET /documents/types/{id}/settings`, `PUT /documents/types/{id}/settings`

## Other pieces it needs

- **An order type:** "Order sent first, bill at payment" (`OrderTypeId`) in the [point-of-sale settings](../settings.md), or on the point of sale's own tab. A restaurant with no order type posts its bill at Send, and nothing reaches the display.
- **A document flow** from the order type to the bill type, in System settings › Document flows. The pack has it.
- **Recipes:** Stock and production › Recipes.

## Related

- [display.md](display.md)
- [routes.md](routes.md)
- [../settings.md](../settings.md)
