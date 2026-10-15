# AetherGlot

A [Dalamud](https://github.com/goatcorp/Dalamud) plugin for **Final Fantasy XIV** that translates in-game chat in real time using Large Language Model (LLM) APIs.

---

## Features

| Feature | Details |
|---|---|
| **Inbound translation** | Translates chat from other players into your client language and shows the result as an Echo message. |
| **Same-language skip** | Messages already in your client language are silently skipped by default (configurable). |
| **Skip own messages** | Your own chat is not translated by default (configurable). |
| **Outbound translation** | Translates your message before sending (optional, configurable prompt). |
| **Provider choice** | OpenAI, Google Gemini, Anthropic Claude, or any OpenAI-compatible endpoint (Ollama, LM Studio, etc.). |
| **Channel filter** | Choose exactly which chat channels (Say, Shout, Party, FC, Novice Network, LS 1–8, CWLS 1–8) to translate. |
| **MMO-style prompts** | Pre-built casual / formal outbound prompts; fully editable by the user. |
| **Multi-language UI** | Supports EN / JA / DE / FR via [CheapLoc](https://github.com/steverichey/CheapLoc). |

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
| Enable translation | Check *Enable inbound translation* (and/or outbound) |
| Choose channels | Tick the chat channels you want translated |
| Skip same-language | Off by default — uncheck *Translate even if message is in client language* to see all messages regardless |
| Skip own messages | On by default — uncheck *Skip translation for own messages* to translate your own chat too |

Echo messages appear in-game as:
```
[AetherGlot] PlayerName [EN]: Translated text here
```
Parts that could not be translated are kept in the original language and marked like `‹zorp strat›`.
When a response is discarded (invalid / refusal), nothing is echoed: the original message is already in the chat log.

### Outbound send command

```
/agsend <text>
/agsend <prefix> <text>
```

Opens a preview window with the translation. Confirm to send to the chosen channel.

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

**Settings** have six tabs: General, Connection (with the staged connection test), AI & translation,
Channels (which channels are translated), Dictionary, and Display.

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

Locale files live in `AetherGlot/loc/` as `<lang>.json`.  
The English file (`en.json`) is the source; community translations are welcomed via pull request.  
A Crowdin → GitHub Actions pipeline for automatic PR generation is planned.

---

## Architecture

```
AetherGlot (plugin, Dalamud)               AetherGlot.Core (no Dalamud dependency, unit-tested)
 Plugin.cs                                  Connection/  ConnectionSettings(+Builder), LlmHttpSender,
  ├─ ChatTranslationService ───────────────►             AccessResponseClassifier, RequestDecorator,
  ├─ Windows/ConfigWindow, SendPreviewWindow             ConnectionDiagnostics
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
