# GitHub Copilot Instructions for AetherGlot

## プロジェクト概要

**AetherGlot** は、FFXIV（Final Fantasy XIV）のゲーム内チャットをAI（LLM）APIを用いてリアルタイムに翻訳・ローカライズする Dalamud プラグインです。

- FFXIV固有の用語・Lore用語を保持しながら正確に翻訳する
- 受信チャット：他言語 → クライアント言語（ゲーム内Echoに表示）
- 送信チャット：クライアント言語 → 相手の言語（カスタムプロンプト対応でMMOスラング化も可能）
- 複数のAIプロバイダー（OpenAI、Gemini、Claude等）に対応
- 多言語UI対応（CheapLoc + Crowdin）

---

## 技術スタック

- **言語**: C# 12
- **ターゲットフレームワーク**: .NET 8.0
- **SDK**: `Dalamud.NET.Sdk`（NuGet経由、Dalamud API 11 / v13+準拠）
- **UI**: `Dalamud.Bindings.ImGui`（旧 `ImGuiNET` からの移行済み）
- **非同期通信**: `System.Net.Http.HttpClient`（async/await）
- **多言語化**: `CheapLoc`（Crowdin連携を前提とした JSON ベースのロケールファイル）

---

## コーディング規約

### 基本方針
- コードコメントおよびログメッセージは**英語**で記述する
- UIラベル等のユーザー向け文字列は必ず `CheapLoc` を通じてローカライズ対応する（ハードコード禁止）
- APIキーや認証情報はリポジトリにコミットしない。Dalamud の `Configuration.json` に保存する

### 非同期処理
- チャットイベントハンドラ（`ChatGui.ChatMessage`）内でAPIリクエストを直接行わない
- 必ず `Task.Run` や `async/await` を使用してバックグラウンドで処理し、メインスレッドをブロックしない

### ImGui（Dalamud.Bindings.ImGui）
- `Dalamud.Bindings.ImGui` を使用し、旧 `ImGuiNET` は使用しない
- パフォーマンス向上のため、文字列リテラルは可能な限り UTF-8 リテラル（`"Save"u8` 等）を使用する

### モデル・プロバイダー名
- モデル名（例: `gpt-4o`）やエンドポイントをコード内にハードコードしない
- 設定ファイルまたはAPI経由で動的に取得・更新できるように設計する

### README.md の常時更新
- READMME.md は修正するたびに更新する

---

## アーキテクチャ

### プロバイダー抽象化レイヤー

複数のAIプロバイダーをサポートするため、`ITranslatorProvider` インターフェースを実装する。
新しいプロバイダー追加時は、このインターフェースを実装したクラスを追加するだけでよい構造を維持する。

```csharp
public interface ITranslatorProvider
{
    string ProviderName { get; }
    Task<string> TranslateAsync(
        string text,
        string systemPrompt,
        string userPrompt,
        string apiKey,
        string? customEndpoint = null);
}
```

### チャットフロー

```
[FFXIV Chat Log]
    └─> ChatGui.ChatMessage イベント
        └─> 言語判定 (ClientState.ClientLanguage)
            └─> ITranslatorProvider (抽象化レイヤー)
                ├─ OpenAIProvider
                ├─ GeminiProvider
                ├─ ClaudeProvider
                └─ CustomEndpointProvider (Ollama 等)
                    └─> LLM API Call (システムプロンプト + 辞書注入)
                        └─> Echo出力:
                            [AetherGlot] <Player Name> [OriginLang]: <TranslatedText>
```

---

## 翻訳プロンプト設計

### Echoフォーマット

```
[AetherGlot] <Player Name> [OriginLang]: <TranslatedText>
```

- `OriginLang`: 元の言語コード（例: `[EN]`, `[FR]`, `[DE]`）。AIに判定させる
- 翻訳先言語は `ClientState.ClientLanguage` から動的に取得してシステムプロンプトに埋め込む

### システムプロンプト（受信・Inbound）

- パーティチャット、アライアンスチャット、CWLSチャットなどのどのチャットを翻訳の対象にするかを確認するための設定ウインドウが必要
```
Translate the input text to {CurrentClientLanguage}.
Keep all FFXIV-specific terms, character names, and lore words intact.
Identify the original language and return it as a language code (e.g., EN, FR, DE, JA).
```

### カスタムプロンプト（送信・Outbound）

設定画面でユーザーが自由に編集可能なテキストエリアとして提供する。

**デフォルトプリセット（Casual MMO）:**

```
You are a player in an MMORPG. Translate the user's input into natural English.
- Keep it very casual. Avoid formal grammar or structures.
- Actively use gaming slangs: 'i' instead of 'I', 'btw', 'ty', 'gg', 'brb', 'thx', 'omw', etc.
- Avoid capitalizing words unless necessary. Do not end sentences with periods.
```

**フォーマルプリセット（Formal）:**

```
Translate the user's input into proper, polite English suitable for formal contexts or roleplay.
Use complete sentences and correct grammar.
```

---

## 多言語対応（i18n）

### ロケールファイル構成

```
AetherGlot/
└─ loc/
   ├─ en.json
   ├─ ja.json
   ├─ de.json
   └─ fr.json
```

### CheapLoc 初期化

```csharp
var locDir = Path.Combine(pluginInterface.AssemblyLocation.DirectoryName!, "loc");
Loc.Setup(locDir, "AetherGlot_");
```

### Crowdin 連携方針

- GitHub リポジトリと Crowdin を GitHub Actions で自動同期する
- `loc/en.json` をソースファイルとして Crowdin にアップロードし、翻訳完了後に自動でプルリクエストを作成する構成を目指す

---

## 実装時の注意事項

1. **再帰ループ防止**: AIが翻訳して出力した自分のEchoメッセージ（`[AetherGlot]` プレフィックス）を、再度翻訳対象として処理しないようにフィルタリングを実装する
2. **APIキーのセキュリティ**: APIキーは `Configuration.json` に保存し、`.gitignore` で除外する。誤コミット防止の仕組みを設ける
3. **プロバイダーの拡張性**: モデル名・エンドポイントはハードコードせず、設定から動的に読み込む設計を徹底する
4. **Dalamud API バージョン**: 最新の Dalamud SDK（Dalamud API 11 / v13+）に準拠し、deprecated な API は使用しない

---

## 参考リソース

- [Dalamud Documentation](https://v6.dalamud.dev/)
- [Dalamud Plugin Project Template](https://github.com/goatcorp/DalamudPluginProjectTemplate)
- [CheapLoc](https://github.com/steverichey/CheapLoc)
- [Crowdin](https://crowdin.com/)

---

外側のコードブロックを取り除いたことで、各セクションのコードスニペットが正しく独立したコードブロックとして機能するようになりました。このまま `.github/copilot-instructions.md` としてファイルに貼り付けてご利用いただけます。