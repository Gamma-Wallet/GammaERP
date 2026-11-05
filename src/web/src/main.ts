import { loadLanguage } from './app/core/i18n';

// The words first: the app's texts are read as its files load.
await loadLanguage();
await import('./start');
