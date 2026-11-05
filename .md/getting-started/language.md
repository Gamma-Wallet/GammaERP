# Language

Each company has its own language plus English. English is the default.

**Where:** the language list in the top bar. **Who:** any signed-in user. The list shows only when the company has a language other than English.

## Actions

### Switch language

Steps:

1. In the top bar open the language list.
2. Choose the company's language or English.

Result:

- The choice is kept on your user and in this browser, and the page starts again in it.
- Screen words load from `/i18n/<language>.json`. A word with no translation stays in English.
- Every API call carries `?language=`, so names from the database and refusal messages come in that language.
- When you change to a company that does not have your language, the screens go to English.

Refused when: `<code> is not a language.` A language is two lower-case letters.

API: `PUT /account/language` with `language`.

### Where a language is not your choice

| Text | Language |
|---|---|
| A name you type | Both languages. Each name field has one row for English and one for the company's language. One left empty takes the other's text when saved. |
| The chart of accounts | The country's own language. |
| A printed document | The company's language, whatever the screens are in. |
| The sign-in pages | The language remembered in this browser, passed to the sign-in request. |

## Related

- [Choose where you work](choose-where-you-work.md)
- [Architecture: languages](../architecture.md#languages)
