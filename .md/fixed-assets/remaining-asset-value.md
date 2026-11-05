# Remaining asset value

What an asset is still worth in one depreciation book: its values less their depreciation.

**Where:** Fixed assets › Remaining asset value. **Who:** the Remaining asset value item in your menu. Administrators pass.

## Actions

### Show the remaining value

Steps:
1. Choose **Book**, **Asset (product)** and **Unit**. With one book only, it is chosen for you.
2. Press **Show**.

Result: nothing changes. The screen shows the remaining value. It is 0 for a product and unit with no value in that book. Values of a sold asset are not counted; a reversed sale counts them again.

API: `GET /depreciation/remaining?product=&unit=&book=`, and `GET /depreciation/books` for the list of books.

## Related

- [registering-an-asset.md](registering-an-asset.md)
- [selling-an-asset.md](selling-an-asset.md)
