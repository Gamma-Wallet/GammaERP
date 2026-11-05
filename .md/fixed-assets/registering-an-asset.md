# Registering an asset

Puts a product into a depreciation book with its value, start date and life. The product must already exist under Products and services, and its type must be a fixed asset type: the **Fixed asset** box ticked on the type (see [product types](../products/product-types-and-categories.md)).

**Where:** Fixed assets › Depreciation, **Assets** tab. **Who:** the Depreciation item in your menu. Administrators pass.

## Actions

### Choose a book

Steps:
1. Choose **Book** at the top. With one book only, it is chosen for you.

Result: the screen shows the book's method and whether its journals are posted.

API: `GET /depreciation/books`.

### See the book's assets

Steps:
1. Open the **Assets** tab.
2. Click an asset to see its depreciation, oldest first. Click a row there to open its journal.

Result: nothing changes. Each asset shows code, name, method, start and end, value, depreciated, book value and state (In use, Fully depreciated, Disposed). A sold asset shows as Disposed: see [selling-an-asset.md](selling-an-asset.md).

API: `GET /depreciation/books/{book}/assets`, `GET /depreciation/assets/{id}/history`.

### Mark an asset disposed, or back in use

For an asset taken out of use without a sale, such as one thrown away.

Steps:
1. Open the **Assets** tab and click the asset.
2. Press **Mark disposed**. On a disposed asset the button is **Back in use**.

Result: `tbl_asset_value_unique.disposed` is set, or cleared, for the asset's product and unit in this book. A disposed asset shows as Disposed and is left out of the book's depreciation runs. Back in use brings it into the runs again. Nothing is posted.

A sold asset shows no such button: its sale closed it. See [selling-an-asset.md](selling-an-asset.md).

Refused when:
- "This asset is sold: its sale closed it."
- "The asset does not exist."
- "The asset has no book settings to mark."
- "Assets belong to a company."

API: `POST /depreciation/assets/{assetValueId}/disposed?disposed=true` or `?disposed=false`. Needs the Depreciation screen in your menu.

### Register an asset

Steps:
1. Open the **Assets** tab and go to **Register an asset**.
2. Choose **Asset (product)** and **Unit**. The list holds only products of fixed asset types.
3. Enter **Initial value** and **Start date**.
4. Choose **Depreciation cost centre**, or leave it empty. See below.
5. Enter **Months**, or leave it empty to take the life the book's depreciation scenario proposes for the asset's category.
6. Press **Register**.

Result: a new row in `tbl_asset_value`. On the first registration of this product and unit in this book, also a row in `tbl_asset_value_unique` with the end date (start date plus months) and the percentages the scenario proposes. A further registration of the same product and unit in the book adds another value and updates that row: the end date becomes the later of the two, and the percentages become the newest registration's. Nothing is posted and no stock moves.

The method is the book's. The screen does not offer the method or the percentages; the API takes `methodId`, `linearPercentage`, `decliningPercentage` and `documentId`.

### Depreciation cost centre

The cost centre the asset's depreciation goes to, wherever the asset is.

- Empty: the place that holds the asset in stock when the run is calculated.
- Chosen: this cost centre always. Choose one for assets kept at clients, because no place of the company holds them.

It is saved on `tbl_asset_value.default_cost_center_id`.

Refused when:
- "The product's type is not a fixed asset type."
- "An asset needs a value and a number of months to depreciate over (none is proposed for its category: land is not depreciated)."
- "The book does not exist."
- "Assets belong to a company."

The product list of this screen and of the **Remaining asset value** screen comes from `GET /depreciation/asset-products`.

API: `POST /depreciation/assets` with `productId`, `unitId`, `bookId`, `initialValue`, `startDate`, and optionally `months`, `defaultCostCenterId`.

## Other ways in

Migration's opening assets step (`OPENING_ASSETS`) registers assets with the depreciation they already had, so runs carry on from the opening day. See the Migration documentation.

## Related

- [depreciation-run.md](depreciation-run.md)
- [depreciation-scenarios.md](depreciation-scenarios.md)
- [assets-at-clients.md](assets-at-clients.md)
