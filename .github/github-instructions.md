# AetherGlot 開発ガイド（AI アシスタント・コントリビューター向け）

## プロジェクト概要

**AetherGlot** は、FFXIV のチャットを LLM で翻訳する Dalamud プラグインです。

- **受信:** 他言語 → クライアント言語。表示先は吹き出し（パーティリスト横）、翻訳ログ、ゲームチャット。
- **送信:** クライアント言語 → 相手の言語。送信プレビューでトーン（短く/普通/丁寧）を選び、逆翻訳と言い換え案で確認してから送る。
- **辞書:** ゲームデータ（Lumina）、コミュニティ辞書（URL から取得してキャッシュ）、マイ辞書を併用する。優先順位はマイ辞書 > ゲームデータ > コミュニティ。
- **接続:** Direct（OpenAI / Claude / Gemini）、LiteLLM（Cloudflare Access 対応）、OpenAI 互換。

---

## 技術スタック

- **言語:** C# 14
- **ターゲット:**
  - プラグイン: `net10.0-windows`
  - `AetherGlot.Core`: `net10.0`。Dalamud に依存しないので、単体テストができる。
- **SDK:** `Dalamud.NET.Sdk` 15（Dalamud API 15）
- **UI:** `Dalamud.Bindings.ImGui`
- **ゲームデータ:** Lumina（`Lumina.Excel.Sheets`）、FFXIVClientStructs
- **多言語化:** CheapLoc（`AetherGlot/loc/{en,ja,de,fr}.json`）
- **テスト:** xUnit。統合テストは GitHub Actions の environment `integration` で手動実行する。

---

## 守ること

### セキュリティ
- API キー、Virtual key、Cloudflare Access の Client ID / Secret は **Windows 資格情報マネージャー**（`ISecretStore`）に保存する。`Configuration.json` には絶対に書かない。
- 秘密情報をログ、例外メッセージ、UI に出さない。検査画面のヘッダーは名前だけを表示し、値は `••••` にする（`RequestPreview`）。
- 秘密情報をリポジトリにコミットしない。CI は GitHub の environment secrets を使う。

### 非同期処理
- チャットイベントの中では、メッセージのコピーとゲーム状態の取得だけを行う。API 呼び出しはバックグラウンドで行う。
- ゲームの API（チャット送信、`Print` など）は `IFramework.RunOnFrameworkThread` で呼ぶ。
- `HttpClient` はプラグインの寿命の間、1 つを使い回す。

### 推測しない
- ゲームのシート構造や ClientStructs のシグネチャは、Dalamud / Lumina の XML ドキュメント、メタデータ、データマイニング CSV で確認してから使う。
- 例：チャット送信のコマンドは `TextCommand.Command` / `ShortCommand` のうち、全言語にあるものだけを使う（ビギナーチャンネルは `/beginner`）。

### ハードコードしない
- モデル名と URL は設定から読む。
- 定型文の「使い方」はグループ番号ではなく、参照先のシート名から判定する。

### ローカライズ
- ユーザーに見える文字列は `Loc.Localize("Key", "English fallback")` を通す。
- キーを追加したら 4 言語すべての JSON に入れる。`LocFilesTests` が、キーの過不足・`{0}` などのプレースホルダーの不一致・英語フォールバックとの差異を検出する。

### README
- 機能や操作が変わったら README.md も更新する。

---

## 構成

```
AetherGlot (プラグイン)                     AetherGlot.Core (Dalamud 非依存)
 Plugin.cs                                   Connection/   接続設定、HTTP、診断、RequestPreview
  ├─ ChatTranslationService                  Providers/    ITranslatorProvider と各実装
  ├─ Windows/ 設定、翻訳ログ、送信プレビュー、 Translation/  パイプライン、プロンプト、検証、キャッシュ、ChatText
  │           検査、辞書、定型文               Glossary/     辞書の索引、意味の解決、コミュニティ辞書、GlossaryBrowser
  ├─ UI/ 吹き出し、テーマ、RichText            Display/      表示先の振り分け、BubbleBoard
  ├─ Glossary/ ゲームデータ読み込み            Log/          TranslationLog
  ├─ Helpers/DalamudChatSender                Security/     ISecretStore、SecretRedactor、移行
  └─ Security/CredentialManagerSecretStore
```

### 翻訳の流れ（受信）
1. 定型文（AutoTranslatePayload）を `{{atN}}` に置き換える。
2. 本文に出てくる辞書の組だけを引き、文脈ヒントと一緒にプロンプトへ入れる。
3. モデルに送る。JSON の形式が決まっていて、`response_format` に非対応のモデルには自動でプロンプト指示に切り替える。
4. 検証する（スキーマ、拒否文、長さ、プレースホルダー）。失敗したら 1 回だけ再試行し、だめなら原文を表示する。
5. 定型文を戻して表示する。

### 新しいプロバイダーの追加
`ITranslatorProvider` を Core に実装し（`LlmRequest.JsonMode` に対応させる）、`TranslatorProviderFactory` に分岐を足す。

---

## 参考資料

- [Dalamud](https://dalamud.dev/)
- [FFXIVClientStructs](https://github.com/aers/FFXIVClientStructs)
- [ffxiv-datamining](https://github.com/xivapi/ffxiv-datamining)（シート構造の確認用）
- [CheapLoc](https://github.com/goatcorp/CheapLoc)
