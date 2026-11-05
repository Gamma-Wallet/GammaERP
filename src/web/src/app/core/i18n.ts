// The screens' language. The English text is the key, so a word without a translation shows in English. A company works in two languages, its
// own and English (the founder, 2026-11-02). The words load before the app does, so the texts written at the top of a file translate too; a new
// language therefore reloads the page.
const KEY = 'gamma-erp.language';
const ENGLISH = 'en';

let current = ENGLISH;
let company = ENGLISH;
let words: Record<string, string> = {};

/** The language the screens are in. */
export function language(): string {
  return current;
}

/** The language of the company the user works in. */
export function companyLanguage(): string {
  return company;
}

export function setCompanyLanguage(code: string): void {
  company = code;
}

/** The text in the screens' language, its {0}, {1}… filled with the values. */
export function t(english: string, ...values: readonly unknown[]): string {
  const text = words[english] ?? english;
  return values.length ? text.replace(/\{(\d+)\}/g, (_, index: string) => String(values[Number(index)] ?? '')) : text;
}

/** The two languages of a company: its own and English; one when it is English. */
export function languageChoices(code: string): string[] {
  return [...new Set([code, ENGLISH])];
}

/** A language's name in that language, as people look for it; not every browser knows them all. */
const OWN_NAMES: Record<string, string | undefined> = {
  bg: 'Български', cs: 'Čeština', da: 'Dansk', de: 'Deutsch', el: 'Ελληνικά', en: 'English', es: 'Español', et: 'Eesti', fi: 'Suomi', fr: 'Français', hr: 'Hrvatski',
  hu: 'Magyar', it: 'Italiano', lt: 'Lietuvių', lv: 'Latviešu', mk: 'Македонски', nl: 'Nederlands', pl: 'Polski', pt: 'Português', ro: 'Română', sk: 'Slovenčina',
  sl: 'Slovenščina', sq: 'Shqip', sv: 'Svenska',
};

export function languageName(code: string): string {
  return OWN_NAMES[code] ?? code;
}

function remembered(): string {
  try {
    return localStorage.getItem(KEY) ?? ENGLISH;
  } catch {
    return ENGLISH;
  }
}

/** Reads the remembered language's words: called before the app starts. Without them the screens stay in English. */
export async function loadLanguage(): Promise<void> {
  current = remembered();
  if (current === ENGLISH) {
    return;
  }
  try {
    const answer = await fetch(`/i18n/${current}.json`);
    words = answer.ok ? ((await answer.json()) as Record<string, string>) : {};
  } catch {
    words = {};
  }
}

/** Remembers a language and starts again in it, on the same screen where it can. */
export function switchLanguage(code: string): void {
  try {
    localStorage.setItem(KEY, code);
  } catch {
    return;
  }
  // The sign-in's return address holds a code that works once: from there, go on to the home screen.
  if (location.pathname === '/signed-in') {
    location.replace('/');
  } else {
    location.reload();
  }
}
