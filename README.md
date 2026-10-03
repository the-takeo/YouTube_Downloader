# YouTube_Downloader

YouTube の動画・チャンネル・再生リストを一括ダウンロードする Windows 向けツールです。
次の 2 通りの使い方ができます。

- **コマンドライン (CLI)**: チャンネル ID を指定して、そのチャンネルの全動画をダウンロード
- **Chrome 拡張機能**: YouTube のページを開いたままポップアップからダウンロード（動画 / 再生リスト / チャンネルに対応）

Chrome 拡張機能は、同じ実行ファイル (`YouTube_Downloader.exe`) をネイティブメッセージングホストとして呼び出し、実際のダウンロードを行います。

## 必要なもの

- Windows (x64)
- [.NET 9 SDK](https://dotnet.microsoft.com/download)
- YouTube Data API v3 の API キー（[Google Cloud Console](https://console.cloud.google.com/) で「YouTube Data API v3」を有効にして発行）
- Google Chrome（拡張機能を使う場合）

## ビルド

1. リポジトリ直下に `ApiKey.cs` を作成し、API キーを記入します（`.gitignore` 済みなのでコミットされません）。

   ```csharp
   namespace YouTube_Downloader
   {
       public static class ApiKey
       {
           public const string apiKey = "AIza...";
       }
   }
   ```

   > このファイルはビルドに必須です。Chrome 拡張機能だけを使う場合も作成してください（拡張機能側は、ポップアップで設定した API キーを使用します）。

2. ビルドします。

   ```bash
   dotnet build
   ```

   `bin\Debug\net9.0\win-x64\YouTube_Downloader.exe` が生成されます。

## 使い方 1: コマンドライン

```bash
YouTube_Downloader.exe <チャンネルID> <保存先フォルダ>
```

例:

```bash
YouTube_Downloader.exe UCxxxxxxxxxxxxxxxxxxxxxx C:\Users\me\Videos
```

- チャンネルにアップロードされた全動画（50 件を超える場合もすべて）を、保存先フォルダにダウンロードします。
- チャンネル ID は `UC` で始まる文字列です。調べ方は[こちら](https://reposub.jp/blogs/tips/youtube_channel_id)を参照してください。
- 引数が 2 つでない場合はネイティブメッセージングモード（Chrome 拡張機能用）で起動するため、CLI として使うときは必ず 2 つ指定してください。

## 使い方 2: Chrome 拡張機能

### セットアップ（初回のみ）

1. 上記の手順で `YouTube_Downloader.exe` をビルドしておきます。
2. Chrome で `chrome://extensions` を開き、右上の **デベロッパーモード** をオンにします。
3. **パッケージ化されていない拡張機能を読み込む** をクリックし、このリポジトリの `chrome-extension` フォルダを選択します。
4. 読み込まれた「YouTube Downloader」の **ID**（32 文字の英字）をコピーします。
5. PowerShell でネイティブメッセージングホストを登録します。

   ```powershell
   .\chrome-extension\setup\register-host.ps1 -ExtensionId <拡張機能のID>
   ```

   - EXE は `bin\Debug\net9.0\win-x64\YouTube_Downloader.exe` から自動検出されます。別の場所に置いた場合は `-ExePath` で指定してください。

     ```powershell
     .\chrome-extension\setup\register-host.ps1 -ExtensionId <拡張機能のID> -ExePath C:\path\to\YouTube_Downloader.exe
     ```

   - スクリプトの実行がブロックされる場合は、`powershell -ExecutionPolicy Bypass -File .\chrome-extension\setup\register-host.ps1 -ExtensionId <ID>` のように実行してください。
   - スクリプトは `%LOCALAPPDATA%\YouTubeDownloader\native-host-manifest.json` を作成し、`HKCU\SOFTWARE\Google\Chrome\NativeMessagingHosts\com.youtube_downloader` に登録します（管理者権限は不要です）。

6. Chrome を再起動します。
7. 拡張機能のポップアップを開き、下部の **設定（APIキー）** に YouTube Data API v3 キーを入力して **保存** します。

### ダウンロードのしかた

1. YouTube で次のいずれかのページを開きます。

   | ページ | URL の例 | ダウンロード対象 |
   | --- | --- | --- |
   | 動画 | `https://www.youtube.com/watch?v=...` | その動画 1 本 |
   | 再生リスト | `https://www.youtube.com/playlist?list=...` | 再生リスト内の動画 |
   | チャンネル | `https://www.youtube.com/@ハンドル` / `/channel/UC...` / `/c/名前` | チャンネルのアップロード動画 |

2. ツールバーの拡張機能アイコンをクリックしてポップアップを開きます。
3. 必要に応じて **ダウンロード数** を入力します（空欄ならすべて。新しい順に指定件数を取得）。動画ページでは表示されません。
4. **保存先フォルダ** にフォルダのフルパス（例: `C:\Users\me\Downloads`）を入力します。入力内容は次回以降も保持されます。
5. **ダウンロード開始** をクリックします。進捗バーと現在ダウンロード中の動画名が表示されます。

- ダウンロードはバックグラウンドで続行されるため、**ポップアップを閉じても中断されません**。再度開くと進捗が表示されます。
- **キャンセル** ボタンで中止できます（ダウンロード中の動画が終わった時点で停止します）。
- 完了すると成功件数と失敗件数が表示されます。

> API キーが未設定でも、動画ページからの単体ダウンロードは可能です。再生リスト・チャンネルのダウンロードや、`@ハンドル` / `/c/` 形式の URL の解決には API キーが必要です。

## トラブルシューティング

| 症状 | 対処 |
| --- | --- |
| 「ネイティブアプリへの接続に失敗しました」「Specified native messaging host not found」 | `register-host.ps1` を実行したか、指定した拡張機能 ID が正しいかを確認し、Chrome を再起動してください。 |
| 「Access to the specified native messaging host is forbidden」 | 拡張機能を読み込み直すなどで ID が変わっています。新しい ID で `register-host.ps1` を再実行してください。 |
| 「Native host has exited」 | EXE を移動・削除していないか確認してください。移動した場合は `-ExePath` を付けて再登録します。 |
| 「YouTubeのチャンネルまたは再生リストのページで開いてください」 | 対応していないページです。上の表の URL 形式のページで開いてください。 |
| 一部の動画が失敗する | 年齢制限・メンバー限定・ライブ配信など、取得できない動画はスキップされ失敗件数に数えられます。 |

## ライセンス

[LICENSE](LICENSE) を参照してください。
