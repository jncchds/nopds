// Verifies that all locale files define the same keys and that every t('key') used in src exists.
import { readFileSync, readdirSync, statSync } from 'node:fs'
import { join } from 'node:path'

const LOCALES = ['en', 'uk', 'pl', 'de']
const flatten = (o, p = '') =>
  Object.entries(o).flatMap(([k, v]) => (v && typeof v === 'object' ? flatten(v, `${p}${k}.`) : [`${p}${k}`]))
// i18next plural keys (key_one, key_few, …) count as the base key.
const base = (k) => k.replace(/_(zero|one|two|few|many|other)$/, '')

const sets = Object.fromEntries(LOCALES.map((l) => [l, new Set(flatten(JSON.parse(readFileSync(`src/locales/${l}.json`, 'utf8'))).map(base))]))

const files = []
const walk = (d) => readdirSync(d).forEach((f) => (statSync(join(d, f)).isDirectory() ? walk(join(d, f)) : /\.tsx?$/.test(f) && files.push(join(d, f))))
walk('src')
const used = new Set()
for (const f of files) {
  const src = readFileSync(f, 'utf8')
  for (const m of src.matchAll(/\bt\(\s*['"`]([a-zA-Z0-9_.]+)['"`]/g)) used.add(m[1])
  for (const m of src.matchAll(/\bt\(\s*`([a-zA-Z0-9_.]+)\.\$\{/g)) used.add(`${m[1]}.*`)
}

let failed = false
for (const key of used) {
  if (key.endsWith('.*')) {
    const prefix = key.slice(0, -1)
    if (![...sets.en].some((k) => k.startsWith(prefix))) { console.error(`missing dynamic group ${key}`); failed = true }
    continue
  }
  for (const l of LOCALES) if (!sets[l].has(key)) { console.error(`${l}: missing "${key}"`); failed = true }
}
for (const l of LOCALES.slice(1)) {
  for (const k of sets.en) if (!sets[l].has(k)) { console.error(`${l}: missing "${k}" (present in en)`); failed = true }
  for (const k of sets[l]) if (!sets.en.has(k)) { console.error(`${l}: extra "${k}"`); failed = true }
}
if (failed) process.exit(1)
console.log(`locales OK: ${sets.en.size} keys × ${LOCALES.length} languages, ${used.size} used`)
