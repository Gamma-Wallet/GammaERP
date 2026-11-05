import { t } from './i18n';

// The screens' words, in English only for now (the founder, 2026-10-30); other languages come later from the same keys.
function translated<T extends object>(words: T): T {
  return Object.fromEntries(Object.entries(words).map(([key, value]) => [key, typeof value === 'string' ? t(value) : typeof value === 'function' ? value : translated(value as object)])) as T;
}

export const text = translated({
  signIn: {
    signingIn: 'Signing you in…',
    signedOut: 'You are signed out.',
    signInAgain: 'Sign in',
    failed: 'The sign-in did not complete. Please try again.',
  },
  choose: {
    title: 'Where do you work today?',
    subtitle: 'Choose the cost centre you work at. You can change it later from the side menu.',
    none: 'You are not linked to any cost centre yet. Ask an administrator to add you to one.',
  },
  shell: {
    files: 'Files',
    help: 'Help',
    language: 'Language',
    account: 'Account',
    signOut: 'Sign out',
    changePlace: 'Change cost centre',
    administrator: 'Administrator',
    emptyMenu: 'Nothing has been given to you yet. Ask an administrator for access.',
  },
  home: {
    title: 'Welcome',
    subtitle: 'Start from the side menu, or jump straight to a section.',
  },
  screen: {
    notBuilt: 'This screen is being built.',
    comesIn: 'It arrives with',
  },
  ui: {
    choose: 'Choose…',
    search: 'Search',
    noMatch: 'Nothing matches.',
    noRows: 'Nothing to show.',
    loading: 'Loading…',
    selectAll: 'Select all',
    selectRow: 'Select row',
    expand: 'Expand',
    collapse: 'Collapse',
    previousPage: 'Previous page',
    nextPage: 'Next page',
    pageOf: (page: number, pages: number) => t('Page {0} of {1}', page, pages),
    rowsOf: (from: number, to: number, total: number) => t('{0}–{1} of {2}', from, to, total),
    close: 'Close',
    cancel: 'Cancel',
    confirm: 'Confirm',
    clear: 'Clear',
    all: 'All',
    add: 'Add',
    remove: 'Remove',
    required: 'Required',
    requiredMissing: 'Fill this in.',
    notNumber: 'Enter a number, e.g. 1500.50.',
    latitude: 'Latitude',
    longitude: 'Longitude',
    chooseFile: 'Choose a file',
    asTable: 'Show as table',
    asChart: 'Show as chart',
    comparedTo: 'vs',
    status: { good: 'Good', warning: 'Warning', serious: 'Serious', critical: 'Critical', info: 'Note', neutral: '' },
  },
});
