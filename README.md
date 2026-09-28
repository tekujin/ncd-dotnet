# NCD (.NET) — プログラム説明メモ（日本語）

Norton Commander / 旧DOSの **NCD** を参考にした、Raspberry Pi 向け **デュアルペイン型ファイルマネージャ** です。  
C# / .NET 8 で実装し、`linux-arm64` の単一実行ファイルとして公開しています。

| 項目 | 内容 |
|------|------|
| 実行ファイル | `~/bin/ncd` |
| ソース | `~/ncd-dotnet/Ncd/` |
| ランタイム | .NET 8（`~/.dotnet`） |
| UI | コンソール（左右2画面 + ファンクションキーバー） |
| 言語 | ロケールに応じて 日本語 / 韓国語 / 英語 を自動切替 |

---

## 主な機能

- 左右ペインに同一（または別）ディレクトリを表示
- **Space** でファイル／ディレクトリを複数選択（`*` 表示）
- **F4** 左の選択項目 → 右へコピー（確認ダイアログ、ディレクトリは再帰）
- **F5** 右ペインを Samba (SMB/CIFS) 接続に切替
- **F6** 選択項目の削除（確認ダイアログ）
- **F7** `vi` で編集し、終了後に元画面へ復帰
- **F12**（および F10）で終了
- 表示言語は `NCD_LANG` / `LC_MESSAGES` / `LANG` を参照

### キー一覧

| キー | 機能 |
|------|------|
| F1 | ヘルプ |
| F2 | 絶対パス（または UNC）へ移動 |
| F3 / Tab | 左右ペイン切替 |
| F4 | 左の選択 → 右へコピー |
| F5 | 右ペイン SMB 接続 |
| F6 | 選択項目を削除 |
| F7 | vi 編集 |
| F8, F9, F11 | 未設定 |
| F10, F12 | 終了 |
| Space | 選択／解除 |
| Enter | ディレクトリへ入る |
| Backspace | 親ディレクトリ |

---

## 実行方法

```bash
ncd
# または
~/bin/ncd
```

SSH の場合は疑似端末が必要です（`xxx.xxx.xxx.xxx` は Raspberry Pi の IP に置き換えてください）。

```bash
ssh -t pi@xxx.xxx.xxx.xxx
```

言語を強制する場合:

```bash
NCD_LANG=ja ncd   # 日本語
NCD_LANG=ko ncd   # 韓国語
NCD_LANG=en ncd   # 英語
```

---

## Raspberry Pi への .NET インストール

本プロジェクトは **.NET 8 SDK** でビルドします。公式インストーラスクリプトでユーザ領域（`~/.dotnet`）に入れる方法が簡単です（`sudo` 不要）。

### 1. 前提

- Raspberry Pi OS（64-bit / `aarch64` 推奨）
- インターネット接続
- シェル: `bash`

アーキテクチャ確認:

```bash
uname -m
# aarch64 であること（armv7l の 32-bit の場合は別 RID が必要）
```

### 2. SDK のインストール（.NET 8）

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet"
```

### 3. PATH の設定（永続化）

`~/.bashrc` に追加します。

```bash
echo '' >> ~/.bashrc
echo '# .NET SDK' >> ~/.bashrc
echo 'export DOTNET_ROOT=$HOME/.dotnet' >> ~/.bashrc
echo 'export PATH=$DOTNET_ROOT:$PATH:$HOME/.dotnet/tools' >> ~/.bashrc
source ~/.bashrc
```

### 4. 動作確認

```bash
dotnet --version
# 例: 8.0.xxx
dotnet --list-sdks
```

テレメトリを止めたい場合:

```bash
export DOTNET_CLI_TELEMETRY_OPTOUT=1
# 必要なら ~/.bashrc にも追記
```

### 5. （参考）依存パッケージ

自己完結型（self-contained）で publish する場合、実行側に SDK は不要です。  
SDK でビルドする Pi 本体では、通常 Raspberry Pi OS の標準ライブラリで足ります。問題が出た場合は公式の Linux 依存一覧を参照してください。  
https://learn.microsoft.com/dotnet/core/install/linux

---

## Visual Studio Code での開発

ソースは通常の C#（.NET 8）プロジェクトなので、**Visual Studio Code（VS Code）** で編集・デバッグ・ビルドできます。  
おすすめは **開発用 PC に VS Code を入れ、Remote - SSH で Raspberry Pi 上のソースを開く** 方法です。

### A. 開発 PC への VS Code インストール

#### Windows / macOS

1. 公式サイトからインストーラを入手: https://code.visualstudio.com/
2. インストール後、VS Code を起動

#### Linux（Debian / Ubuntu 系の例）

```bash
# Microsoft のリポジトリを使う場合（公式ドキュメントに従う）
# または配布パッケージ:
sudo snap install code --classic
# もしくは .deb を公式サイトからダウンロードしてインストール
```

### B. 推奨拡張機能

VS Code の拡張機能ビュー（`Ctrl+Shift+X` / `Cmd+Shift+X`）からインストール:

| 拡張機能 | 用途 |
|----------|------|
| **C#**（Microsoft, `ms-dotnettools.csharp`）または **C# Dev Kit** | 補完・ビルド・デバッグ |
| **Remote - SSH**（Microsoft, `ms-vscode-remote.remote-ssh`） | Pi 上のフォルダを直接編集 |

### C. Remote - SSH で Raspberry Pi に接続

1. VS Code で `F1` → **Remote-SSH: Connect to Host...**
2. ホストを追加（例）:

```text
Host rpi-ncd
    HostName xxx.xxx.xxx.xxx
    User pi
```

3. 接続後、**File → Open Folder** で ` /home/pi/ncd-dotnet/Ncd ` を開く
4. ターミナル（`` Ctrl+` ``）で:

```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$DOTNET_ROOT:$PATH
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r linux-arm64 --self-contained true \
  -p:PublishSingleFile=true -o ~/ncd-dotnet/publish
```

### D. （任意）Raspberry Pi 本体に VS Code を入れる場合

デスクトップ環境付き Pi なら、公式 `.deb`（arm64）や `code` パッケージでインストールできる場合があります。  
リソースが限られるため、**編集は PC 側 Remote-SSH、実行は Pi** の構成を推奨します。

ヘッドレス運用でブラウザから編集したい場合は **code-server** 等の選択肢もありますが、本メモの範囲外とします。

### E. ローカル PC だけで編集し、Pi にコピーする場合

PC に .NET 8 SDK を入れ、ソースを編集したあと `scp` / `rsync` で Pi へ送り、Pi 上で `dotnet publish` しても構いません。  
クロス publish する場合の RID は Pi が 64-bit なら `linux-arm64` です。

```bash
# 例: 開発 PC → Pi へソース同期（IP はプレースホルダ）
rsync -az --exclude bin/ --exclude obj/ \
  ./ncd-dotnet/ pi@xxx.xxx.xxx.xxx:~/ncd-dotnet/
```

---

## ビルド／再インストール

```bash
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$DOTNET_ROOT:$PATH
cd ~/ncd-dotnet/Ncd
dotnet publish -c Release -r linux-arm64 --self-contained true \
  -p:PublishSingleFile=true -o ~/ncd-dotnet/publish
cp -f ~/ncd-dotnet/publish/ncd ~/bin/ncd
chmod 755 ~/bin/ncd
```

---


## ソース内コメントについて

各 `.cs` / `.csproj` ファイルには **日本語の行単位コメント**（`//` / `///`）を付与しています。  
実装の読み方はソース側のコメントを、全体像は本 README の「ソースファイル単位のコード注釈」を参照してください。

## ディレクトリ構成

```
~/ncd-dotnet/
├── README.md                 ← 本ファイル（日本語メモ）
├── README_qiita.md           ← Qiita 投稿用
├── publish/ncd               ← publish 成果物
└── Ncd/
    ├── Ncd.csproj            ← プロジェクト定義
    ├── Program.cs            ← エントリポイント
    ├── FileOps.cs            ← 再帰コピー用の子一覧ヘルパー
    ├── I18n/
    │   └── Loc.cs            ← 多言語文字列
    ├── Ui/
    │   ├── App.cs            ← 画面描画・キー処理本体
    │   └── PaneView.cs       ← 1ペイン分の状態（一覧・選択）
    └── Backends/
        ├── IFileBackend.cs   ← ストレージ抽象インターフェース
        ├── LocalBackend.cs   ← ローカルファイルシステム
        └── SmbBackend.cs     ← Samba / SMB クライアント
```

---

## ソースファイル単位のコード注釈

### `Ncd.csproj`

**役割:** プロジェクト設定と依存パッケージ定義。

- `TargetFramework`: `net8.0`
- `OutputType`: 実行可能（Exe）
- `AssemblyName`: `ncd`
- `PublishSingleFile` / `SelfContained`: 単一ファイル配布向け
- NuGet: **SMBLibrary**（SMB2/CIFS アクセス用）

---

### `Program.cs`

**役割:** プロセス起動の入口。

- `Loc.Init()` で表示言語を決定
- 起動引数があればそのパス、なければカレントディレクトリを開始位置にする
- 標準入力がリダイレクトされている場合はエラーを出して終了（`ssh -t` 推奨の旨を表示）
- `NCD_FORCE=1` で上記チェックを迂回可能
- `App` を生成して `Run()` を呼び、未処理例外はメッセージ出力後に終了コード 1

---

### `I18n/Loc.cs`

**役割:** UI 文言の国際化（i18n）。

- 対応言語: **英語 (en)** / **韓国語 (ko)** / **日本語 (ja)**
- 判定優先順位:
  1. 環境変数 `NCD_LANG`
  2. `LC_ALL` → `LC_MESSAGES` → `LANG`
  3. `CultureInfo.CurrentUICulture`
  4. 不明時は英語
- `T(en, ko, ja)` で3言語を切り替え
- ファンクションキーバー、確認ダイアログ、ヘルプ、ステータス文言などをすべてここに集約
- タイトルバーに現在の言語コード（`Lang:ja` など）を表示するための `Code` プロパティあり

---

### `Ui/App.cs`

**役割:** デュアルペイン UI の中核（描画ループ＋キーハンドラ）。

- 左右それぞれ `PaneView` を保持し、初期状態は同じ開始ディレクトリ
- `_focus`（0=左, 1=右）でアクティブペインを管理
- メインループ: `Draw()` → `Console.ReadKey` → `HandleKey`
- UI は `/dev/tty` 相当のコンソールに描画（タイトル、ヘッダ、一覧、Fキーバー、ステータス）
- 主なキー処理:
  - **Space** … マーク切替後、カーソルを1つ下へ
  - **F2** … パス入力ダイアログ（ローカル絶対パス／UNC）
  - **F3 / Tab** … ペイン切替
  - **F4** … 左のマーク（なければカーソル項目）を右へコピー（確認あり）
  - **F5** … SMB 接続情報を聞き、右ペインを `SmbBackend` に置換
  - **F6** … アクティブペインのマーク／カーソル項目を削除（確認あり）
  - **F7** … 画面を一旦クリアして `vi`/`vim`/`nvim` を起動し、終了後に復帰
  - **F10 / F12** … 終了
- ダイアログ系: `Prompt` / `PromptSecret` / `Confirm` / `Alert`
- 端末サイズは `Console.WindowWidth/Height`、ダメなら `COLUMNS`/`LINES`、最後は 80×24

---

### `Ui/PaneView.cs`

**役割:** 片側ペインの表示状態。

- `Backend` … 実際の一覧取得・コピー・削除を行うストレージ実装
- `Entries` … 現在ディレクトリの一覧
- `SelectedIndex` / `ScrollOffset` … カーソルとスクロール位置
- `_marked` … Space で付けた選択パスの集合（`FullPath` キー）
- `ToggleMarkCurrent()` … カレント行のマーク ON/OFF（`.` / `..` は対象外）
- `GetTargets()` …
  - マークがあればその一覧（表示順）
  - なければカーソル上の1件（`..` 以外）
- `Refresh()` … `Backend.List()` を呼び、消えたマークを掃除

---

### `Backends/IFileBackend.cs`

**役割:** ローカル / SMB を共通に扱う抽象。

- `DirEntry` … 名前・フルパス・ディレクトリフラグ・サイズ・更新日時
- `IFileBackend` メソッド:
  - `List` / `Enter` / `GoParent` / `GoTo`
  - `CopyFrom(source, entry)` … ファイル／ディレクトリ（再帰）コピー先側の実装
  - `Delete`
  - `BeginEdit` / `FinishEdit` … vi 編集用（SMB は一時ファイル経由）

---

### `Backends/LocalBackend.cs`

**役割:** ローカルディスク実装。

- `CurrentPath` をカレントとして `Directory` / `File` API を使用
- `List` は `..` → ディレクトリ → ファイルの順
- `CopyFrom`:
  - ファイル: ローカル同士は `File.Copy`、他バックエンドからは一時取得してコピー
  - ディレクトリ: 作成後、`FileOps.ListChildren` で子を再帰コピー（一時的に `CurrentPath` を子へ移動）
- `Delete` はディレクトリなら `recursive: true`
- `BeginEdit` は実ファイルパスをそのまま返し、その場編集

---

### `Backends/SmbBackend.cs`

**役割:** Samba / Windows 共有への SMB2 クライアント実装（SMBLibrary 使用）。

- 接続: サーバ・共有・ドメイン・ユーザ・パスワード
- 表示パス形式: `\\server\share\相対パス`
- `CurrentRelative` … 共有内の相対パス（空＝共有ルート）
- `TryParseUnc` … `\\server\share\...` および `smb://...` を解析
- `List` / `ListChildrenOf` / `ListAt` … ディレクトリ列挙
- `CopyFrom` … ファイル書き込み／ディレクトリ作成＋再帰
- `EnsureDirectory` … SMB 上にディレクトリを OPEN_IF で作成
- `Delete` … ディレクトリは子を先に消してから自身を削除
- `BeginEdit` … 内容を一時ファイルへ落とし、`FinishEdit` で変更時のみ再アップロード
- `Dispose` で TreeDisconnect / Logoff / Disconnect

---

### `FileOps.cs`

**役割:** バックエンド横断の「子エントリ列挙」ヘルパー。

- `ListChildren(source, dir)`:
  - `LocalBackend` → `Directory.Enumerate*`
  - `SmbBackend` → `ListChildrenOf`
- `LocalBackend` / `SmbBackend` の再帰コピーから利用され、ディレクトリツリーを辿る

---

## 処理の流れ（概要）

```
Program.cs
  └─ Loc.Init()           # 言語決定
  └─ new App(startPath)
       ├─ 左右 PaneView + LocalBackend
       └─ Run()
            loop:
              Draw()                 # 左右一覧 + Fキーバー
              HandleKey()
                Space → マーク
                F4    → 左 GetTargets → 右 CopyFrom（確認）
                F5    → SmbBackend 接続（右）
                F6    → Active GetTargets → Delete（確認）
                F7    → vi
                F12   → 終了
```

---

## 注意事項

- F4 のコピー元は常に **左ペイン** の選択（またはカーソル）です。右でマークした項目は F4 の対象になりません（削除 F6 はアクティブ側）。
- SMB 接続や大量の再帰コピーはネットワーク／権限の影響を受けます。
- 一部のバックエンド例外メッセージは実装時の文言が残っている場合があります（UI 本体は `Loc` で多言語化済み）。
- 単一ファイル実行バイナリはサイズが大きい（数十 MB）です。`libc` 等のシステムライブラリには動的リンクします。

---

## 変更履歴（メモ）

- デュアルペイン UI、ローカル＋SMB、vi 連携を実装
- ロケール連動の ja / ko / en 表示を追加
- Space 複数選択、F4/F6 の選択一括コピー・削除、F12 終了を追加

---

*この README は NCD (.NET) の設計メモです。実装の詳細は各 `.cs` ファイルを参照してください。*
