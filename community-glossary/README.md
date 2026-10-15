# AetherGlot community glossary

`glossary.json` is the community dictionary AetherGlot uses for slang, raid mechanics and
abbreviations that are not in the game data. The plugin ships a copy of this file and, when
"update the community dictionary at startup" is on, downloads the latest version from the URL in
the settings (by default this file on GitHub).

Priority: **My dictionary > game data > community dictionary**. The exception is an entry with
several senses (e.g. `bio`): it overrides the plain game-data term so the context rules can
choose between the meanings.

## Format (schema 1)

```jsonc
{
  "schema": 1,
  "version": "2026.10.15",          // bump on every change
  "entries": [
    // One meaning
    { "id": "stack", "kind": "mechanic", "en": "stack", "ja": "頭割り", "aliases": ["stack up"] },

    // Several meanings, chosen by context
    { "id": "bio", "kind": "slang", "en": "bio", "onAmbiguous": "showAll",
      "senses": [
        { "id": "action", "ja": "バイオ（技）", "en": "Bio", "priority": 1,
          "when": { "inCombat": true, "words": ["dot", "reapply"], "partyJobs": ["SCH"] } },
        { "id": "afk", "ja": "トイレ（離席）", "en": "bathroom break", "priority": 2,
          "when": { "inCombat": false, "words": ["brb", "afk"] } } ] }
  ]
}
```

| Field | Meaning |
|---|---|
| `id` | Unique, stable id. |
| `kind` | `status`, `action`, `job`, `mechanic`, `slang`, `abbreviation`, `place`, `content`, `other`. |
| `en` / `ja` | Canonical text. Use the official in-game name when one exists. |
| `aliases` / `aliasesJa` | Other spellings. Case, hyphens, spaces and doubled words (`opo-opo`) are normalised automatically. |
| `senses[].when` | `inCombat` (true/false), `words` that tend to appear together, `partyJobs` (job abbreviations). |
| `onAmbiguous` | `showAll` (default: show every meaning) or `preferFirst`. |

Matching is done on whole words, so short aliases such as `tb` do not match inside other words.
