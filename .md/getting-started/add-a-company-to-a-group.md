# Add a company to a group

An administrator working at a group adds further companies from the entry page. A group also keeps its own books.

**Where:** the entry page (Account menu › Change cost centre) › New company. **Who:** an administrator. The company is made at the group, so the user must work at the group. The screen sends the group as the cost centre of this one request.

## Actions

### Add a company

Steps:

1. On the entry page press **New company**. It shows only to administrators.
2. **Country:** choose it.
3. **Company:** type the Company name, and a Short code if you want one. Empty makes it from the name.
4. **Activity:** choose level by level. Tick "Load only what this activity uses" to take the activity's profile. Untick it for the country's full configuration.
5. **Starting configuration:** choose Start from: The country's starter pack, or Copy of another company. The list offers the companies of your cost centres, never the group itself. Then tick the parts: Chart of accounts, Financial documents, Posting groups, Depreciation. Ticking a part brings what it needs. Unticking takes along what needs it.
6. **Review:** check it and press **Create the company**.

Result, in one transaction:

- A new company under the group, in the category Companies, with its Country and Activity.
- Its configuration, from the pack or copied. A copy takes new ids and only active rows. A row pointing at something not copied is left out.
- The group and the new company become each other's partners, when both have partner categories.
- The screen says how many configuration rows were made and goes back to the entry page.

Cash desks, banks and other places are added afterwards in [Cost centres](../company/cost-centres.md).

Refused when:

- Your organisation is a single company: "Your organisation is a single company, so there is no group to add a company to." A separate business is set up on the server.
- You are not an administrator working at the group: `NotAllowedError: only an administrator working at the corporate adds companies.`
- `SetupError: the group already has a company <code>.`
- `SetupError: the company to copy from is not a company of this group.`
- `SetupError: there is no starter pack for <country>...`, when starting from a pack.
- A step lacks something: "Choose the country.", "Name the company.", "Choose at least one part to load." The Next button stays off.

API:

- `GET /setup/countries`, `GET /setup/activities`, `GET /setup/groups`
- `POST /companies`, with header `X-Gamma-Node` set to the group. Body: `name`, `country`, `code`, `activity`, `fromCatalogue`, `groups`, `copyFrom`. The API also takes `cashDesks` and `banks`, which the screen does not send.

### Set up the group's books

A group behaves as a company of its own. A group made today gets its books when it is made. This card is for a group that has none.

Steps:

1. An administrator working at the group opens the home page.
2. The card **Set up the group's books** shows when the group has no document types.
3. Choose a Country and press the button.

Result: the country's pack, owned by the group, and the group's companies linked as partners. This is done once. What passes between the group and its companies goes by documents.

Refused when:

- `SetupError: a single company keeps its books already.`
- `SetupError: the group keeps its books already.`
- `NotAllowedError: only an administrator working at the group sets up its books.`

API: `GET /group-books` gives `needed`. `POST /group-books` with `country` and optional `groups`.

### Link the group's partners again

Makes the group and each company each other's partner where they are not yet. Safe to repeat.

Steps:

1. On the entry page press **New company**.
2. Under the wizard, on the card **The group's partners**, press **Link the group's partners again**.

Result: the screen says "{n} partner links made.", or "The group and its companies are already partners of each other." when nothing was missing. The request is made at the group.

Refused when: `SetupError: a single company has no group.`, or the user is not an administrator working at the group.

API: `POST /group-books/partners`

## Related

- [Create a company](create-a-company.md)
- [Choose where you work](choose-where-you-work.md)
- [Purchases from the group](../company/group-purchases.md)
