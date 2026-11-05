import { languageChoices, languageName, t } from './i18n';

describe('languages', () => {
  it('shows the English text when a word has no translation', () => {
    expect(t('Save')).toBe('Save');
  });

  it('fills a sentence with its values', () => {
    expect(t('{0} posted by {1}.', 'F-1', 'Ana')).toBe('F-1 posted by Ana.');
    expect(t('{0} posted.', null)).toBe(' posted.');
  });

  it('offers a company its own language and English, and one choice when that is English', () => {
    expect(languageChoices('sq')).toEqual(['sq', 'en']);
    expect(languageChoices('en')).toEqual(['en']);
  });

  it('names a language in that language', () => {
    expect(languageName('sq')).toBe('Shqip');
    expect(languageName('en')).toBe('English');
  });
});
