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
- An API key for your chosen provider

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
| Configure provider | Select provider, choose/type model, enter API key |
| Enable translation | Check *Enable inbound translation* (and/or outbound) |
| Choose channels | Tick the chat channels you want translated |
| Skip same-language | Off by default — uncheck *Translate even if message is in client language* to see all messages regardless |
| Skip own messages | On by default — uncheck *Skip translation for own messages* to translate your own chat too |

Echo messages appear in-game as:
```
[AetherGlot] PlayerName [EN]: Translated text here
```

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

## Supported Providers

| Provider | Default model |
|---|---|
| OpenAI | `gpt-4o-mini` |
| Google Gemini | `gemini-2.0-flash` |
| Anthropic Claude | `claude-3-5-haiku-latest` |
| Custom (Ollama etc.) | User-specified |

All model names and endpoints are **read from configuration** — nothing is hardcoded.

---

## Security

- API keys are stored in Dalamud's standard `Configuration.json` (per-user, local machine only).
- `Configuration.json` is excluded from version control via `.gitignore`.
- Never commit your API key to a public repository.

---

## Localization (i18n)

Locale files live in `AetherGlot/loc/` as `AetherGlot_<lang>.json`.  
The English file (`en.json`) is the source; community translations are welcomed via pull request.  
A Crowdin → GitHub Actions pipeline for automatic PR generation is planned.

---

## Architecture

```
Plugin.cs
 └─ ChatTranslationService
      └─ ITranslatorProvider (interface)
           ├─ OpenAIProvider
           ├─ GeminiProvider
           ├─ ClaudeProvider
           └─ CustomEndpointProvider
```

To add a new LLM provider: implement `ITranslatorProvider`, add a case in `ProviderFactory`, and add default model suggestions.

---

## License

MIT
