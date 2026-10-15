# AetherGlot

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for **Final Fantasy XIV** that translates in-game chat in real time using Large Language Model (LLM) APIs.

---

## Features

| Feature | Details |
|---|---|
| **Inbound translation** | Chat from other players is translated into your client language and shown as bubbles next to the party list, in the translation log and/or in the game chat (per channel). Messages already in your language are recognised by the model and not shown twice. |
| **Outbound translation** | A send preview translates as you type, with tone (short / normal / polite), back-translation, alternatives and a 500-byte counter, then sends through the game's chat box. |
| **Dictionaries** | Game data (job, action, status, duty and place names, every auto-translate phrase), a community dictionary for slang and mechanics, and your own dictionary with context rules for words with several meanings. |
| **Auto-translate aware** | Auto-translate phrases are kept as placeholders and filled back in with the game's own text. |
| **Inspector** | See exactly what was sent to the model and what came back (secrets never shown). |
| **Connections** | Direct (OpenAI, Anthropic Claude, Google Gemini), LiteLLM (virtual keys, Cloudflare Access service tokens), or any OpenAI-compatible endpoint (Ollama, LM Studio, vLLM, …). Separate models for receiving and sending. |
| **Secrets** | Keys are stored in Windows Credential Manager, never in the settings file. |
| **Skip own messages** | Your own chat is not translated by default (configurable). |
| **Multi-language UI** | English, Japanese, German and French ([CheapLoc](https://github.com/goatcorp/CheapLoc)); follows Dalamud's language setting. |

---

## Requirements

- FFXIV with [XIVLauncher](https://github.com/goatcorp/FFXIVQuickLauncher) and Dalamud **API 15**
- An API key for your chosen provider, or a LiteLLM proxy (optionally behind Cloudflare Access)

---

## Installation

### Via custom plugin repository (recommended)

1. In Dalamud Settings → *Experimental* tab, enable **Developer Mode**.
2. Under **Custom Plugin Repositories**, add the following URL:
   ```
   https://github.com/spencomeister/AetherGlot/releases/latest/download/repo.json
   ```
3. Click the reload button, then install *AetherGlot* from *All Plugins*.

This URL is **permanent** — it always points to the latest release.

### Manual (dev build)

1. Build the plugin (see [Building](#building)).
2. In Dalamud Developer Settings → *Dev Plugin Locations*, add the DLL path:
   ```
   AetherGlot\bin\Release\AetherGlot.dll
   ```
3. Install *AetherGlot* from the *Dev Plugins* tab.

---

## Usage

| Action | How |
|---|---|
| Open settings | `/aetherglot` or Dalamud's ⚙ → *AetherGlot Settings* |
| Open the translation log | `/aetherglot log` or Dalamud's main UI button |
| Configure connection | Choose Direct / LiteLLM / OpenAI-compatible, enter the URL, key and models |
| Test the connection | `/aetherglot test` |
| Open the dictionary | `/aetherglot dict [text]` or *Open dictionary* in the Dictionary tab |
| Browse auto-translate phrases | `/aetherglot at` |
| Inspect the latest translation | `/aetherglot inspect`, or *Inspect* on a log entry |
| Enable translation | General tab: *Translate received messages*, *Enable /agsend* |
| Choose channels | Channels tab: tick the chat channels you want translated |
| Choose where translations appear | Display tab: presets or the channel × destination table |
| Skip own messages | On by default — uncheck *Skip my own messages* to translate your own chat too |

In the game chat a translation appears below the original with a prefix (default `[AG] ↳`), or in
Echo. Parts that could not be translated are kept in the original language and marked like
`‹zorp strat›`. When a response is discarded (invalid / refusal), nothing is printed: the original
message is already in the chat log.

### Outbound send command

```
/agsend <text>
/agsend <prefix> <text>
```

Opens the send preview (also opened by *Reply* in the translation log):

- Type in your language; the translation follows ~0.5 s after you stop typing.
- Pick the channel and the tone (**Short / Normal / Polite**).
- The translation is editable and shows its size (`N / 500` bytes, the chat limit; longer text is cut).
- Term chips show the fixed names (actions, jobs, … use the game data's official name) and the
  terms the model used; the back-translation and two alternatives (click one to use it) help you
  check the meaning.
- **Enter** sends the translation (Ctrl+Enter = new line); *Send original* sends what you typed;
  *Cancel* closes. *Inspect* opens the inspector for this translation.

Messages are sent through the game's chat box on the game thread. Text is reduced to one line
without control characters and cut at 500 bytes, and the game's own sanitiser removes anything
that is not plain text, so a message cannot carry links or run another command. Only the base
channel commands that exist in every client language are used (`/p`, `/fc`, `/beginner`, `/cwl1`, …).

| Prefix | Channel |
|---|---|
| `s` | Say |
| `sh` | Shout |
| `y` | Yell |
| `p` | Party |
| `a` | Alliance |
| `fc` | Free Company |
| `nn` | Novice Network |
| `l1`–`l8` | Linkshell 1–8 |
| `cw1`–`cw8` | Cross-world Linkshell 1–8 |

If no prefix is given, the *Default Send Channel* from the settings is used.

---

## Building

```bash
dotnet build AetherGlot/AetherGlot.csproj -c Release
```

The compiled plugin (`AetherGlot.dll`) and locale files are output to `AetherGlot/bin/Release/`.

---

## Connection

| Type | Details |
|---|---|
| **Direct** | OpenAI, Google Gemini or Anthropic Claude. Endpoints are editable in the settings. |
| **LiteLLM proxy** | OpenAI-compatible `/v1/chat/completions` and `/v1/models`, virtual key sent as `Authorization: Bearer`, user tag sent in the `user` field. |
| **OpenAI-compatible** | Ollama, LM Studio, vLLM, etc. |

- **Cloudflare Access (service token)**: for LiteLLM / OpenAI-compatible, `CF-Access-Client-Id` and `CF-Access-Client-Secret` are added to every request (chat, model list, connection test). Extra headers can be configured as name/value pairs. A login redirect (302) or an Access 403 page is reported as "rejected by Cloudflare Access" instead of being parsed as JSON.
- **Models**: inbound (fast) and outbound (high quality) models are set separately. Nothing is hardcoded; with LiteLLM the list can be fetched from `/v1/models`.
- **JSON output**: `response_format` is sent by default. If a model rejects it, AetherGlot falls back to prompt-only JSON instructions and remembers that per model.

### Connection test

`/aetherglot test` (or the button in the settings) checks, in order:

1. Cloudflare Access is passed
2. LiteLLM authentication
3. Model list (and whether the configured models are in it)
4. JSON response from the inbound and outbound models

| Failure | What to check |
|---|---|
| 302 / 403 from Access | The Access policy must use **Service Auth**, and the service token must be correct. |
| 401 | The LiteLLM virtual key is invalid or expired. |
| Model not found | The virtual key must be allowed to use that model. |

`/aetherglot models` prints the model list.

### Translation pipeline

The model must answer with a fixed JSON schema (`status`, `lang`, `translation`, `terms`, `unresolved`, `senses`).
Parts it cannot translate go into `unresolved` and are shown in the original language; it is told never to refuse or apologise.
A response is discarded and the original message is shown when the JSON is invalid twice (one retry), when it does not match the schema,
when the "translation" is a refusal/apology or far longer than the source, or when placeholders were dropped.
Identical messages are served from a size-limited cache.

---

## Translation log and display

**Translation log window** (`/aetherglot log`): one line per message (time, channel, sender,
translation). Click a line to see the original, term chips (e.g. `bio → トイレ（離席） 92%` with
alternatives), unresolved words with an *Add to dictionary* button, and *Reply*.
Auto-translate phrases are green with 【】; unresolved words keep the original text with a dashed
underline. Auto-scroll pauses while you scroll up and an "N new" button jumps back. Filters:
party only / all / to me; messages that mention your name, job or role are highlighted. The
footer shows the connection state, inbound model, dictionary version and size, and the average
response time.

**Inspector** (*Inspect* on a log entry, *Inspect latest* in the footer, or `/aetherglot inspect`)
shows one translation in four panels: ① the text sent (auto-translate phrases as `{{atN}}`, the
auto-translate map and IDs, the glossary pairs), ② the context hints (combat, location, party
jobs, recent lines), ③ validation (JSON schema, refusal, length, placeholders, unresolved count,
and why the original was shown), ④ every model attempt with model, tokens, time, JSON mode and
the raw reply, plus the prompts. The endpoint and request header **names** are listed; header
values are always shown as `••••`. *Translate again* repeats the same request without the cache
and updates the log entry; unresolved words can be registered from there.

**Settings** have six tabs: General, Connection (with the staged connection test), AI & translation,
Channels (which channels are translated), Dictionary, and Display.

**Bubbles** (party / alliance): the translation appears next to the speaker's row of the party
list and fades out (default 6 s, longer for long text, at most 2 per member). Click a bubble to pin
it. A small list of the last 3 translations sits at the bottom right; toggle it with
**Ctrl+Shift+T** (configurable). When the party list is hidden no bubbles are drawn; the log still
receives everything. Speakers are matched to party list rows by name, including the abbreviated
name display styles (`K. Rhyne`, `Kaela R.`, `K. R.`). Alliance members outside your own party
are not in the party list, so they get no bubble.

**Game chat**: the translation is printed below the original in the same channel (or in Echo),
with a prefix (default `[AG] ↳`). *Replace the original* rewrites the original line instead, but
only when the translation is already cached (the line has to be changed before it is shown);
otherwise the translation is added as a new line.

**Display tab**: presets (Recommended / Window only / Game chat only / Bubbles only) and a table of
channel × destination (bubble, translation log, game chat) with an *in combat* column per channel
(all / to me only / stop). Editing the table switches the preset to *Custom*.
Recommended: party → bubble + log; FC and Tell → log + game chat; others → log.
*Test display* adds sample messages to the log without calling the model.

---

## Dictionaries

Terms are looked up locally before the model is called; only the pairs that occur in the
message are added to the prompt (never the whole dictionary).

| Source | Content | Updated |
|---|---|---|
| **My dictionary** | Your own entries (`mydict.json` in the plugin config folder). | When you edit it |
| **Game data** | Job, action, status, duty and place names, and every auto-translate phrase (Completion sheet and the sheets its groups point to), in Japanese and English. | Every start (follows patches automatically) |
| **Community dictionary** | Slang, raid mechanics, abbreviations ([`community-glossary/`](community-glossary/README.md)). | Downloaded at start; cached; a copy ships with the plugin |

Priority: **My dictionary > game data > community dictionary**. A community entry with several
meanings (e.g. `bio`) overrides the single game-data term so its context rules can apply.

- **Spelling variants**: case, full-width characters, hyphens, spaces and doubled words are
  normalised, so `opo`, `Opo-opo`, `opo opo` and `opoopo` are the same term. Matching is by whole
  words (English) or longest match (Japanese).
- **Several meanings**: each meaning can list conditions (in combat or not, words that appear with
  it, jobs in the party). If they decide the meaning locally, only that meaning is sent; otherwise
  all meanings are sent and the model chooses.
- **Auto-translate phrases** arrive as IDs, not text. They are replaced by `{{at0}}`, `{{at1}}` …
  before the message is sent to the model, and filled back in with the game's own text in your
  client language. A message made only of auto-translate phrases is not sent at all.
- **Outbound**: action, job, status, duty and place names in your message are fixed to their
  official in-game name in the target language.
- **Unresolved words** are collected (`pending.json`) as candidates for your dictionary.
- The community dictionary is fetched with the Cloudflare Access headers **only** when it is
  served from the same host as your LiteLLM / OpenAI-compatible endpoint, so the service token is
  never sent to third parties such as GitHub.

**Dictionary window** (`/aetherglot dict`):

- Left: sources with counts (all / auto-translate / game names / community / mine), *Pending
  approval* (unresolved words with *Register* / *Ignore*), last update times and *Update now*.
- Center: search over English, Japanese and abbreviations (spelling variants are ignored), a kind
  filter and *+ Add*. Columns: English / abbreviations, Japanese, kind, context rules, source.
- Right: for My dictionary entries, the sense editor — kind, spellings (comma separated), and per
  meaning: English, Japanese, priority, combat condition (either / in combat / out of combat),
  words that appear with it, party jobs, note; plus what to do when unsure (show every meaning /
  use the top priority). Delete needs Shift+click. Other entries are read-only and can be copied to
  My dictionary to override them.

**Auto-translate viewer** (`/aetherglot at`, read-only): how a phrase travels (payload ID →
`{{atN}}` in the text sent to the model → phrase filled back in, using the latest received
message), the categories of the game data with counts, and every phrase in Japanese and English
with what the plugin uses it for (jobs: fixed when received and a replacement candidate when
sending; actions: also matched when typed; places: also a context hint; others: shown as is).
*Only seen in chat* lists the phrases received this session.

Commands: `/aetherglot reload` reloads all dictionaries (and downloads the community dictionary);
`/aetherglot lookup <text>` shows which terms and meanings would be used for a message.

---

## Security

- API keys, the LiteLLM virtual key, the Cloudflare Access Client ID / Secret and extra header values are stored in **Windows Credential Manager** (targets `AetherGlot:*`), never in `Configuration.json`.
- Plaintext keys from older versions are moved to Credential Manager on first start and then removed from `Configuration.json`.
- Secrets are masked in logs, error messages and the connection test output.

---

## Integration tests

The integration tests call real APIs and are skipped unless their environment variables are set.

- **Local**: `./Run-IntegrationTests.ps1 -Provider LiteLLM` injects the values from 1Password (`integration.op-env`, references only).
- **CI**: run the *Integration Tests* workflow manually. It reads the same variable names from the GitHub environment `integration`
  (secrets: `AETHERGLOT_LITELLM_KEY`, `AETHERGLOT_CF_CLIENT_ID`, `AETHERGLOT_CF_CLIENT_SECRET`;
  variables: `AETHERGLOT_LITELLM_BASE_URL`, `AETHERGLOT_LITELLM_INBOUND_MODEL`, `AETHERGLOT_LITELLM_OUTBOUND_MODEL`, `AETHERGLOT_LITELLM_USER_TAG`).
  If the Access policy restricts source IPs, GitHub-hosted runners will be rejected.

Unit tests run on every push / pull request (`.github/workflows/tests.yml`).

---

## Localization (i18n)

The UI is available in **English, Japanese, German and French** and follows Dalamud's language
setting (changing it updates the texts and window titles immediately). Other languages fall back
to English.

- Locale files: `AetherGlot/loc/{en,ja,de,fr}.json` (CheapLoc format). `en.json` holds the same
  English text as the fallbacks in the code.
- Channel and game terms use the official in-game names of each language (e.g. *Neulinge*,
  *Réseau des novices*, *Auto-Übersetzung*, *Traduction automatique*, 定型文).
- `LocFilesTests` fails when a key is missing in any language, when `{0}`-style placeholders
  differ, or when `en.json` no longer matches the English fallback in the code. Add new keys to all
  four files in the same change.
- Corrections and new languages are welcome as pull requests.

### Settings migration

Settings files from older versions are upgraded automatically on first start:

- **v1 → v2:** plaintext API keys move to Windows Credential Manager and are removed from the file;
  the single provider / model becomes the new connection settings (same model for receiving and
  sending).
- **v2 → v3:** the old default outbound prompt (which forced a very casual style) is cleared,
  because the tone is now chosen in the send preview; a prompt you edited is kept as extra style
  instructions. The unused *always translate* option is removed.

---

## Architecture

```
AetherGlot (plugin, Dalamud)               AetherGlot.Core (no Dalamud dependency, unit-tested)
 Plugin.cs                                  Connection/  ConnectionSettings(+Builder), LlmHttpSender,
  ├─ ChatTranslationService ───────────────►             AccessResponseClassifier, RequestDecorator,
  ├─ Windows/ConfigWindow, TranslationLog,              ConnectionDiagnostics, RequestPreview
  │   SendPreview, Inspector, Glossary,
  │   AutoTranslate
  ├─ Helpers/DalamudChatSender
  └─ Security/CredentialManagerSecretStore   Providers/   ITranslatorProvider
                                                          ├─ OpenAICompatibleProvider ─ LiteLlmProvider
                                                          ├─ ClaudeProvider
                                                          └─ GeminiProvider
                                             Translation/ TranslationPipeline, PromptBuilder,
                                                          ResponseParser, ResponseValidator,
                                                          RefusalDetector, Rehydrator, TranslationCache
                                             Security/    ISecretStore, SecretRedactor, LegacySecretsMigrator
```

To add a new LLM backend: implement `ITranslatorProvider` in Core (honour `LlmRequest.JsonMode`) and add a case in `TranslatorProviderFactory`.

---

## License

MIT
