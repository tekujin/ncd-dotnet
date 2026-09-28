<!--
Qiita 投稿用ドラフト

推奨タイトル:
  Raspberry Pi で動く Norton Commander 風デュアルペインファイラを C# / .NET 8 で作った

推奨タグ（Qiita 投稿画面で設定）:
  RaspberryPi, CSharp, dotnet, Linux, 自作ツール

公開リポジトリ:
  https://github.com/tekujin/ncd-dotnet
-->

# Raspberry Pi で動く Norton Commander 風デュアルペインファイラを C# / .NET 8 で作った

昔よく使っていた **Norton Commander / NCD** のような「左右2画面＋ファンクションキー」の操作感が、今でも Raspberry Pi の SSH 先で欲しくなることがあります。

そこで、**.NET 8 / C#** でコンソール向けデュアルペイン・ファイルマネージャ **ncd** を作りました。  
ローカル FS に加えて **Samba (SMB)** も扱え、表示言語は **日本語 / 韓国語 / 英語** をロケールに合わせて切り替えます。

- リポジトリ: [https://github.com/tekujin/ncd-dotnet](https://github.com/tekujin/ncd-dotnet)
- 実行形態: `linux-arm64` の **単一実行ファイル**（self-contained publish）

---

## できること

| 操作 | 内容 |
|------|------|
| Space | ファイル／ディレクトリを複数選択（`*` 表示） |
| F4 | 左の選択項目を右へコピー（確認あり・ディレクトリは再帰） |
| F5 | 右ペインを SMB 共有へ接続 |
| F6 | 選択項目を削除（確認あり） |
| F7 | `vi` で編集 → 終了後に元画面へ復帰 |
| F2 | 絶対パス／UNC へ移動 |
| F3 / Tab | 左右ペイン切替 |
| F12 / F10 | 終了 |

起動直後は、左右ペインとも **同じカレントディレクトリ** を表示します。

言語は次の優先順で決まります。

1. `NCD_LANG`（`ja` / `ko` / `en`）
2. `LC_ALL` → `LC_MESSAGES` → `LANG`
3. それ以外 → 英語

```bash
NCD_LANG=ja ncd
```

---

## 画面イメージ（テキスト UI）

```text
 NCD/.NET  デュアル  │ 有効: 左  │ 言語:ja  │ F12=終了
 [Local] /home/pi/work          [Local] /home/pi/work
 * [src]                 <DIR>    [src]                 <DIR>
   README.md              12K     README.md              12K
 F1:ヘルプ F2:パス移動 F3:切替 F4:コピー→右 F5:SMB接続 ...
 準備
```

下部に F1〜F12 の説明が出ます。未割当キーは「未設定」と表示します。

---

## Raspberry Pi に .NET 8 を入れる

ビルドするマシン（ここでは Pi）に **.NET 8 SDK** を入れます。公式スクリプトなら `sudo` 不要で `~/.dotnet` に入ります。

```bash
uname -m
# aarch64（64-bit）を推奨

curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --channel 8.0 --install-dir "$HOME/.dotnet"

# PATH（~/.bashrc に追記すると便利）
export DOTNET_ROOT=$HOME/.dotnet
export PATH=$DOTNET_ROOT:$PATH:$HOME/.dotnet/tools

dotnet --version
```

---

## ビルドして使う

```bash
git clone https://github.com/tekujin/ncd-dotnet.git
cd ncd-dotnet/Ncd

dotnet publish -c Release -r linux-arm64 --self-contained true \
  -p:PublishSingleFile=true -o ../publish

mkdir -p ~/bin
cp -f ../publish/ncd ~/bin/ncd
chmod 755 ~/bin/ncd

# 実行（SSH なら -t 付きで入る）
ncd
```

SSH 例（IP は自分の Pi に置き換え）:

```bash
ssh -t pi@xxx.xxx.xxx.xxx
```

self-contained で publish していれば、**実行側に SDK は不要**です。

---

## VS Code で開発する（おすすめ）

ソースは普通の C# プロジェクトなので、**Visual Studio Code** で編集できます。

おすすめ構成:

1. 開発用 PC に [VS Code](https://code.visualstudio.com/) を入れる
2. 拡張機能 **C#**（または C# Dev Kit）と **Remote - SSH** を入れる
3. Pi に SSH 接続して `ncd-dotnet/Ncd` を開く
4. 統合ターミナルで `dotnet build` / `dotnet publish`

`HostName` には `xxx.xxx.xxx.xxx`（実 IP）を指定してください。

---

## ざっくり構成

```text
ncd-dotnet/
├── README.md          … 詳細メモ（日本語）
├── README_qiita.md    … 本記事（Qiita 用）
└── Ncd/
    ├── Program.cs           … エントリ
    ├── FileOps.cs           … 再帰コピー用の子一覧
    ├── I18n/Loc.cs          … 多言語文言
    ├── Ui/App.cs            … 画面・キー処理
    ├── Ui/PaneView.cs       … 片側ペイン状態
    └── Backends/
        ├── IFileBackend.cs  … 抽象
        ├── LocalBackend.cs  … ローカル FS
        └── SmbBackend.cs    … SMB（SMBLibrary）
```

ポイントだけ書くと:

- **App.cs** … 描画ループと F キー／Space のハンドラ
- **PaneView** … カーソル・スクロール・複数マーク
- **LocalBackend / SmbBackend** … 同じ `IFileBackend` でコピー・削除・vi 用一時ファイル

ソースには **日本語の行コメント** も入れてあるので、実装を追うときも読みやすいはずです。

---

## SMB（F5）について

右ペインで F5 を押すと、サーバ／共有／ユーザ／パスワードなどを聞かれます。  
接続後は右が SMB、左がローカル、という使い方がしやすいです。

- F4: 左 → 右へコピー（ローカル → SMB も可）
- F6: アクティブ側の選択を削除
- F7: SMB 上のファイルは一時ダウンロード → vi → 変更時は再アップロード

---

## 使ってみてほしい人

- Raspberry Pi を SSH で日常的に触っている
- `mc` はあるが、もっと単純な NC 風 UI が欲しい
- C# / .NET を Linux ARM で動かしてみたい

Issue や PR はリポジトリまでどうぞ。

**GitHub:** [https://github.com/tekujin/ncd-dotnet](https://github.com/tekujin/ncd-dotnet)

---

## 参考リンク

- [.NET インストール（公式）](https://learn.microsoft.com/dotnet/core/install/linux)
- [dotnet-install スクリプト](https://learn.microsoft.com/dotnet/core/tools/dotnet-install-script)
- [VS Code](https://code.visualstudio.com/)
- [SMBLibrary](https://github.com/TalAloni/SMBLibrary)

以上です。昔の NC / NCD を懐かしみつつ、Pi 上の作業が少し楽になればうれしいです。
