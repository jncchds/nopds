import i18n from 'i18next'
import { initReactI18next } from 'react-i18next'
import en from '../locales/en.json'
import uk from '../locales/uk.json'
import pl from '../locales/pl.json'
import de from '../locales/de.json'

export const LANGUAGES = [
  { code: 'uk', name: 'Українська' },
  { code: 'en', name: 'English' },
  { code: 'pl', name: 'Polski' },
  { code: 'de', name: 'Deutsch' },
] as const

const STORAGE_KEY = 'nopds.lang'

/** Maps a BCP-47 tag to a supported UI language: ru/be → uk, unknown → null. */
export function matchLanguage(tag?: string | null): string | null {
  if (!tag) return null
  const primary = tag.toLowerCase().split(/[-_]/)[0]
  if (primary === 'en' || primary === 'uk' || primary === 'pl' || primary === 'de') return primary
  if (primary === 'ru' || primary === 'be' || primary === 'ua') return 'uk'
  return null
}

/** System language by default: first match in navigator.languages, else English. */
export function detectLanguage(preferred?: string | null): string {
  const stored = safeGet()
  const candidates = [preferred, stored, ...(navigator.languages ?? []), navigator.language]
  for (const c of candidates) {
    const m = matchLanguage(c)
    if (m) return m
  }
  return 'en'
}

function safeGet() {
  try {
    return localStorage.getItem(STORAGE_KEY)
  } catch {
    return null
  }
}

export function rememberLanguage(lang: string | null) {
  try {
    if (lang) localStorage.setItem(STORAGE_KEY, lang)
    else localStorage.removeItem(STORAGE_KEY)
  } catch {
    /* storage unavailable */
  }
}

const initial = detectLanguage()
document.documentElement.lang = initial

i18n.use(initReactI18next).init({
  resources: { en: { translation: en }, uk: { translation: uk }, pl: { translation: pl }, de: { translation: de } },
  lng: initial,
  fallbackLng: 'en',
  interpolation: { escapeValue: false },
  returnNull: false,
})

i18n.on('languageChanged', (lng) => {
  document.documentElement.lang = lng
})

export default i18n
