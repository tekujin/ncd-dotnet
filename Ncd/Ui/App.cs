// App.cs — Norton Commander 風デュアルペイン・ファイルマネージャの
// メイン UI コントローラ。左右ペインの描画・キー入力処理・コピー/削除/
// SMB 接続・vi 編集などの操作を統括する。
using System.Diagnostics;
using System.Text;
using Ncd.Backends;
using Ncd.I18n;

namespace Ncd.Ui;

/// <summary>
/// デュアルペイン UI のメインアプリケーションクラス。
/// コンソール上で左右ペインを描画し、ファンクションキー等の入力を処理する。
/// </summary>
public sealed class App
{
    /// <summary>左ペインのビュー状態。</summary>
    private readonly PaneView _left;
    /// <summary>右ペインのビュー状態。</summary>
    private readonly PaneView _right;
    /// <summary>フォーカス中のペイン（0=左, 1=右）。</summary>
    private int _focus; // 0=left, 1=right
    /// <summary>画面下部に表示するステータスメッセージ。</summary>
    private string _status;
    /// <summary>メインループ継続フラグ。false で終了。</summary>
    private bool _running = true;

    /// <summary>
    /// 指定パスを開始ディレクトリとして左右ペインを初期化する。
    /// </summary>
    /// <param name="startPath">左右ペインの初期ローカルパス。</param>
    public App(string startPath)
    {
        // 多言語リソースを初期化
        Loc.Init();
        // 起動時ステータスを「準備完了」に設定
        _status = Loc.Ready;
        // 左右それぞれに同一パスのローカルバックエンドを作成
        var leftBackend = new LocalBackend(startPath);
        var rightBackend = new LocalBackend(startPath);
        // ペインビューにバックエンドを割り当て
        _left = new PaneView { Backend = leftBackend };
        _right = new PaneView { Backend = rightBackend };
        // 初期ディレクトリ一覧を読み込む
        _left.Refresh();
        _right.Refresh();
    }

    /// <summary>
    /// メインイベントループを開始する。終了時にカーソルと画面を復元する。
    /// </summary>
    public void Run()
    {
        // UTF-8 出力とカーソル非表示を設定
        Console.OutputEncoding = Encoding.UTF8;
        Console.CursorVisible = false;
        try
        {
            // 描画 → キー入力 → 処理 を繰り返す
            while (_running)
            {
                Draw();
                var key = Console.ReadKey(true);
                HandleKey(key);
            }
        }
        finally
        {
            // 終了時にコンソール状態を元に戻す
            Console.CursorVisible = true;
            Console.Clear();
            Console.ResetColor();
        }
    }

    /// <summary>現在フォーカス中のペインを返す。</summary>
    private PaneView Active => _focus == 0 ? _left : _right;

    /// <summary>
    /// キー入力を解釈し、対応する操作を実行する。
    /// </summary>
    /// <param name="key">読み取ったキー情報。</param>
    private void HandleKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.F1:
                // ヘルプ画面を表示
                ShowHelp();
                return;
            case ConsoleKey.F2:
                // パス直接移動ダイアログ
                GoToPath();
                return;
            case ConsoleKey.F3:
                // 左右フォーカス切替
                _focus = 1 - _focus;
                _status = _focus == 0 ? Loc.LeftActive : Loc.RightActive;
                return;
            case ConsoleKey.F4:
                // 左→右へコピー
                CopyLeftToRight();
                return;
            case ConsoleKey.F5:
                // SMB/Samba 接続
                ConnectSamba();
                return;
            case ConsoleKey.F6:
                // 選択/マーク項目の削除
                DeleteSelected();
                return;
            case ConsoleKey.F7:
                // vi でファイル編集
                EditWithVi();
                return;
            case ConsoleKey.F8:
            case ConsoleKey.F9:
            case ConsoleKey.F11:
                // 未割当ファンクションキー
                _status = Loc.UnsetStatus(key.Key.ToString());
                return;
            case ConsoleKey.F10:
            case ConsoleKey.F12:
                // 終了
                _running = false;
                return;
            case ConsoleKey.Escape when key.Modifiers == ConsoleModifiers.None:
                // Esc で終了確認
                if (Confirm(Loc.ConfirmQuit))
                    _running = false;
                return;
            case ConsoleKey.Spacebar:
                // マーク切替
                ToggleMark();
                return;
            case ConsoleKey.Enter:
                // ディレクトリ入場 / ファイルヒント
                EnterSelected();
                return;
            case ConsoleKey.Backspace:
                // 親ディレクトリへ移動
                if (Active.Backend.GoParent())
                {
                    Active.SelectedIndex = 0;
                    Active.Refresh();
                    _status = Loc.ParentDir;
                }
                return;
            case ConsoleKey.UpArrow:
                // カーソルを上へ
                Active.Move(-1, ListHeight());
                return;
            case ConsoleKey.DownArrow:
                // カーソルを下へ
                Active.Move(1, ListHeight());
                return;
            case ConsoleKey.PageUp:
                // 1 ページ上へ
                Active.Move(-ListHeight(), ListHeight());
                return;
            case ConsoleKey.PageDown:
                // 1 ページ下へ
                Active.Move(ListHeight(), ListHeight());
                return;
            case ConsoleKey.Home:
                // 先頭へ
                Active.SelectedIndex = 0;
                Active.EnsureVisible(ListHeight());
                return;
            case ConsoleKey.End:
                // 末尾へ
                Active.SelectedIndex = Math.Max(0, Active.Entries.Count - 1);
                Active.EnsureVisible(ListHeight());
                return;
            case ConsoleKey.Tab:
                // Tab でもフォーカス切替
                _focus = 1 - _focus;
                _status = _focus == 0 ? Loc.LeftActive : Loc.RightActive;
                return;
        }
    }

    /// <summary>
    /// 端末の幅・高さを取得する。取得失敗時は環境変数または既定値にフォールバック。
    /// </summary>
    private static void GetTermSize(out int w, out int h)
    {
        try
        {
            // コンソールウィンドウサイズを取得
            w = Console.WindowWidth;
            h = Console.WindowHeight;
        }
        catch
        {
            // 取得失敗時は 0 にして後段で補正
            w = 0;
            h = 0;
        }
        // 幅が狭すぎる場合は COLUMNS または 80 列
        if (w < 40)
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("COLUMNS"), out var cw) && cw >= 40)
                w = cw;
            else
                w = 80;
        }
        // 高さが低すぎる場合は LINES または 24 行
        if (h < 10)
        {
            if (int.TryParse(Environment.GetEnvironmentVariable("LINES"), out var ch) && ch >= 10)
                h = ch;
            else
                h = 24;
        }
    }

    /// <summary>
    /// ファイル一覧に使える行数（端末高さからヘッダ等を差し引いた値）を返す。
    /// </summary>
    private int ListHeight()
    {
        GetTermSize(out _, out var h);
        // 最低 3 行を確保（タイトル・ヘッダ・Fn・ステータス分を引く）
        return Math.Max(3, h - 5);
    }

    /// <summary>
    /// 現在カーソル位置のエントリのマークを切り替え、1 行下へ移動する。
    /// </summary>
    private void ToggleMark()
    {
        var e = Active.Selected;
        // 「.」「..」や未選択は対象外
        if (e == null || e.Name is "." or "..")
            return;
        Active.ToggleMarkCurrent();
        var on = Active.IsMarked(e);
        _status = Loc.MarkedToggle(e.Name, on);
        // classic NC: move down after mark
        Active.Move(1, ListHeight());
    }

    /// <summary>
    /// マーク/選択された項目群の概要文字列を生成する。
    /// </summary>
    private static string Summarize(IReadOnlyList<Backends.DirEntry> items)
    {
        // ファイル数・ディレクトリ数を集計
        var files = items.Count(i => !i.IsDirectory);
        var dirs = items.Count(i => i.IsDirectory);
        // 単一項目なら表示名のみ
        if (items.Count == 1)
            return items[0].DisplayName;
        // 複数なら要約 + 先頭 5 件の名前一覧
        return Loc.MarkedSummary(files, dirs) + "\n" +
               string.Join(", ", items.Take(5).Select(i => i.Name)) +
               (items.Count > 5 ? "…" : "");
    }

    /// <summary>
    /// Enter キー処理。ディレクトリなら入場、ファイルならヒントを表示。
    /// </summary>
    private void EnterSelected()
    {
        var e = Active.Selected;
        if (e == null) return;
        if (e.IsDirectory)
        {
            // ディレクトリへ入り、一覧を先頭から再表示
            Active.Backend.Enter(e);
            Active.SelectedIndex = 0;
            Active.ScrollOffset = 0;
            Active.Refresh();
            _status = Active.Backend.DisplayRoot;
        }
        else
        {
            // ファイル選択時は操作ヒントをステータスに表示
            _status = Loc.FileHint(e.Name);
        }
    }

    /// <summary>
    /// F2: パス入力ダイアログでカレントディレクトリを変更する。
    /// SMB ペインでは UNC のみ、ローカルでは UNC を拒否する。
    /// </summary>
    private void GoToPath()
    {
        var paneName = _focus == 0 ? Loc.Left : Loc.Right;
        // 現在パスを初期値として入力を促す
        var input = Prompt(Loc.PromptGoto(paneName), Active.Backend.DisplayRoot);
        if (input == null) return;
        try
        {
            if (Active.Backend is SmbBackend smb)
            {
                // SMB ペイン: UNC 形式のみ許可
                if (SmbBackend.TryParseUnc(input, out _, out _, out _))
                    smb.GoTo(input);
                else
                    throw new InvalidOperationException(Loc.ErrSmbUncOnly);
            }
            else
            {
                // ローカルペイン: UNC は拒否（右ペインで SMB 接続を促す）
                if (SmbBackend.TryParseUnc(input, out _, out _, out _))
                {
                    _status = Loc.HintSmbOnRight;
                    throw new InvalidOperationException(Loc.ErrLocalF2Only);
                }
                Active.Backend.GoTo(input);
            }
            // 移動成功後にカーソル・スクロールをリセット
            Active.SelectedIndex = 0;
            Active.ScrollOffset = 0;
            Active.Refresh();
            _status = Loc.Moved(Active.Backend.DisplayRoot);
        }
        catch (Exception ex)
        {
            _status = Loc.ErrorPrefix(ex.Message);
            Alert(ex.Message);
        }
    }

    /// <summary>
    /// F4: 左ペインの選択/マーク項目を右ペインへコピーする。
    /// </summary>
    private void CopyLeftToRight()
    {
        // Marked (or cursor) items on LEFT → RIGHT
        var targets = _left.GetTargets();
        if (targets.Count == 0)
        {
            _status = Loc.SelectFileOnLeft;
            return;
        }

        // 確認ダイアログ
        var summary = Summarize(targets);
        if (!Confirm(Loc.ConfirmCopy(summary, _right.Backend.DisplayRoot)))
        {
            _status = Loc.CopyCancelled;
            return;
        }

        try
        {
            // 各エントリを右バックエンドへコピー
            foreach (var entry in targets)
                _right.Backend.CopyFrom(_left.Backend, entry);
            _left.ClearMarks();
            _right.Refresh();
            _left.Refresh();
            _status = Loc.CopyDone(summary.Split('\n')[0]);
        }
        catch (Exception ex)
        {
            _status = Loc.CopyFail(ex.Message);
            Alert(ex.Message);
            _right.Refresh();
        }
    }

    /// <summary>
    /// F5: Samba/SMB 共有へ接続し、右ペインのバックエンドを差し替える。
    /// </summary>
    private void ConnectSamba()
    {
        // 接続パラメータを順に入力
        var server = Prompt(Loc.PromptSmbServer, "");
        if (server == null) return;
        var share = Prompt(Loc.PromptShare, "");
        if (share == null) return;
        var user = Prompt(Loc.PromptUser, Environment.UserName);
        if (user == null) return;
        var pass = PromptSecret(Loc.PromptPassword);
        if (pass == null) return;
        var domain = Prompt(Loc.PromptDomain, "");
        if (domain == null) domain = "";

        try
        {
            // 既存右バックエンドが IDisposable なら破棄
            if (_right.Backend is IDisposable d)
                d.Dispose();
            // SMB バックエンドに差し替え
            _right.Backend = new SmbBackend(server, share, domain ?? "", user, pass);
            _right.SelectedIndex = 0;
            _right.ScrollOffset = 0;
            _right.Refresh();
            // 接続後は右ペインにフォーカス
            _focus = 1;
            _status = Loc.SmbConnected(_right.Backend.DisplayRoot);
        }
        catch (Exception ex)
        {
            _status = Loc.SmbFail(ex.Message);
            Alert(Loc.SmbConnectFail(ex.Message));
            // 失敗時、右がローカルでなければローカルに戻す
            if (_right.Backend is not LocalBackend)
            {
                _right.Backend = new LocalBackend(Directory.GetCurrentDirectory());
                _right.Refresh();
            }
        }
    }

    /// <summary>
    /// F6: アクティブペインの選択/マーク項目を削除する。
    /// </summary>
    private void DeleteSelected()
    {
        var pane = Active;
        var side = _focus == 0 ? Loc.Left : Loc.Right;
        var targets = pane.GetTargets();
        if (targets.Count == 0)
        {
            _status = Loc.NothingToDelete;
            return;
        }

        // 削除確認
        var summary = Summarize(targets);
        if (!Confirm(Loc.ConfirmDelete(side, summary)))
        {
            _status = Loc.DeleteCancelled;
            return;
        }

        try
        {
            foreach (var entry in targets)
                pane.Backend.Delete(entry);
            pane.ClearMarks();
            pane.Refresh();
            _status = Loc.DeleteDone(side, summary.Split('\n')[0]);
        }
        catch (Exception ex)
        {
            _status = Loc.DeleteFail(ex.Message);
            Alert(ex.Message);
            pane.Refresh();
        }
    }

    /// <summary>
    /// F7: 選択ファイルを vi（または vim/nvim）で編集し、変更をバックエンドへ反映する。
    /// </summary>
    private void EditWithVi()
    {
        var entry = Active.Selected;
        // ディレクトリや未選択は編集不可
        if (entry == null || entry.IsDirectory)
        {
            _status = Loc.SelectFileToEdit;
            return;
        }

        // 編集用ローカル一時パスを取得
        string localPath;
        try
        {
            localPath = Active.Backend.BeginEdit(entry);
        }
        catch (Exception ex)
        {
            Alert(ex.Message);
            return;
        }

        // 編集前の更新時刻を記録
        var before = File.GetLastWriteTimeUtc(localPath);
        try
        {
            // 画面をクリアし vi を起動
            Console.Clear();
            Console.CursorVisible = true;
            var vi = FindVi();
            var psi = new ProcessStartInfo
            {
                FileName = vi,
                ArgumentList = { localPath },
                UseShellExecute = false
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit();
        }
        finally
        {
            Console.CursorVisible = false;
        }

        // 更新時刻比較で変更有無を判定
        var after = File.Exists(localPath) ? File.GetLastWriteTimeUtc(localPath) : before;
        var modified = after > before;
        try
        {
            Active.Backend.FinishEdit(entry, localPath, modified);
            Active.Refresh();
            _status = modified ? Loc.Saved(entry.Name) : Loc.EditClosed(entry.Name);
        }
        catch (Exception ex)
        {
            _status = Loc.EditApplyFail(ex.Message);
            Alert(ex.Message);
        }
    }

    /// <summary>
    /// 一般的なパスから vi / vim / nvim を探し、見つからなければ "vi" を返す。
    /// </summary>
    private static string FindVi()
    {
        foreach (var c in new[] { "/usr/bin/vi", "/bin/vi", "/usr/bin/vim", "/usr/bin/nvim" })
            if (File.Exists(c)) return c;
        return "vi";
    }

    /// <summary>
    /// F1: ヘルプテキストを組み立ててアラート表示する。
    /// </summary>
    private void ShowHelp()
    {
        var sb = new StringBuilder();
        sb.AppendLine(Loc.HelpTitle);
        sb.AppendLine();
        // ファンクションキー一覧
        foreach (var (k, l) in Loc.FnBar)
            sb.AppendLine($"  {k,-4}  {l}");
        sb.AppendLine();
        // その他の操作説明
        sb.AppendLine(Loc.HelpEnter);
        sb.AppendLine(Loc.HelpBack);
        sb.AppendLine(Loc.HelpSpace);
        sb.AppendLine(Loc.HelpTab);
        sb.AppendLine(Loc.HelpF4);
        sb.AppendLine(Loc.HelpF5);
        sb.AppendLine(Loc.HelpF6);
        sb.AppendLine(Loc.HelpF12);
        sb.AppendLine(Loc.HelpLang);
        Alert(sb.ToString());
    }

    /// <summary>
    /// 画面全体を再描画する（タイトル・左右ヘッダ/一覧・Fn バー・ステータス）。
    /// </summary>
    private void Draw()
    {
        GetTermSize(out var w, out var h);

        // カーソルを左上へ（失敗は無視）
        try { Console.SetCursorPosition(0, 0); } catch { /* ignore */ }
        var listH = ListHeight();
        // 左右ペインの幅を計算
        var mid = w / 2;
        var leftW = mid;
        var rightW = w - mid;

        // タイトルバー
        WriteLinePadded(PaintTitle(), w, ConsoleColor.Black, ConsoleColor.Cyan);

        // 左右ペインヘッダ
        DrawPaneHeader(0, 1, leftW, _left, _focus == 0);
        DrawPaneHeader(mid, 1, rightW, _right, _focus == 1);

        // 左右ファイル一覧
        DrawPaneList(0, 2, leftW, listH, _left, _focus == 0);
        DrawPaneList(mid, 2, rightW, listH, _right, _focus == 1);

        // ファンクションキーバー
        var fnY = 2 + listH;
        DrawFnBar(fnY, w);

        // ステータス行
        var stY = fnY + 1;
        if (stY < h)
        {
            Console.SetCursorPosition(0, stY);
            WriteLinePadded(" " + Trim(_status, w - 2), w, ConsoleColor.Gray, ConsoleColor.Black);
        }

        // 残りの行を空白でクリア
        for (var y = stY + 1; y < h; y++)
        {
            Console.SetCursorPosition(0, y);
            Console.Write(new string(' ', w));
        }
    }

    /// <summary>タイトルバー文字列（アクティブ側の表示付き）を返す。</summary>
    private string PaintTitle()
        => Loc.TitleBar(_focus == 0 ? Loc.ActiveLeft : Loc.ActiveRight);

    /// <summary>
    /// ペインヘッダ（バックエンド種別とカレントパス）を描画する。
    /// </summary>
    private void DrawPaneHeader(int x, int y, int width, PaneView pane, bool active)
    {
        var tag = pane.Backend.Kind;
        var path = Trim($"[{tag}] {pane.Backend.DisplayRoot}", width - 2);
        Console.SetCursorPosition(x, y);
        // アクティブなら黄背景、非アクティブならダークブルー
        var fg = active ? ConsoleColor.Black : ConsoleColor.White;
        var bg = active ? ConsoleColor.Yellow : ConsoleColor.DarkBlue;
        WritePadded(" " + path, width, fg, bg);
    }

    /// <summary>
    /// ペインのファイル一覧を 1 行ずつ描画する（選択・マーク・DIR の色分け）。
    /// </summary>
    private void DrawPaneList(int x, int y, int width, int height, PaneView pane, bool active)
    {
        // 選択行が可視範囲に入るようスクロール調整
        pane.EnsureVisible(height);
        for (var i = 0; i < height; i++)
        {
            var idx = pane.ScrollOffset + i;
            Console.SetCursorPosition(x, y + i);
            // 先頭行にエラーメッセージを赤表示
            if (pane.Error != null && i == 0)
            {
                WritePadded(" ! " + Trim(pane.Error, width - 4), width, ConsoleColor.Red, ConsoleColor.Black);
                continue;
            }
            // エントリが無い行は空白
            if (idx >= pane.Entries.Count)
            {
                WritePadded("", width, ConsoleColor.Gray, ConsoleColor.Black);
                continue;
            }
            var e = pane.Entries[idx];
            // サイズ欄（DIR or 数値）とマーク記号を組み立て
            var size = e.IsDirectory ? "<DIR>" : FormatSize(e.Size);
            var mark = pane.IsMarked(e) ? "*" : " ";
            var line = $"{mark}{e.DisplayName}";
            var right = size.PadLeft(8);
            var room = width - right.Length - 1;
            if (room < 4) room = 4;
            line = Trim(line, room).PadRight(room) + right + " ";
            line = Trim(line, width);

            // 選択・マーク・ディレクトリに応じた色
            var selected = idx == pane.SelectedIndex;
            ConsoleColor fg, bg;
            if (selected && active)
            {
                fg = ConsoleColor.Black;
                bg = ConsoleColor.Cyan;
            }
            else if (selected)
            {
                fg = ConsoleColor.White;
                bg = ConsoleColor.DarkCyan;
            }
            else if (pane.IsMarked(e))
            {
                fg = ConsoleColor.Yellow;
                bg = ConsoleColor.Black;
            }
            else if (e.IsDirectory)
            {
                fg = ConsoleColor.Cyan;
                bg = ConsoleColor.Black;
            }
            else
            {
                fg = ConsoleColor.Gray;
                bg = ConsoleColor.Black;
            }
            WritePadded(line, width, fg, bg);
        }
    }

    /// <summary>
    /// 画面下部のファンクションキーバーを描画する。
    /// </summary>
    private void DrawFnBar(int y, int w)
    {
        Console.SetCursorPosition(0, y);
        var sb = new StringBuilder();
        foreach (var (k, label) in Loc.FnBar)
        {
            sb.Append(k);
            sb.Append(':');
            sb.Append(label);
            sb.Append(' ');
        }
        WriteLinePadded(Trim(sb.ToString(), w), w, ConsoleColor.Black, ConsoleColor.Gray);
    }

    /// <summary>
    /// バイト数を人間可読な単位（K/M/G）に整形する。
    /// </summary>
    private static string FormatSize(long n)
    {
        if (n < 1024) return n.ToString();
        if (n < 1024 * 1024) return $"{n / 1024.0:0.#}K";
        if (n < 1024L * 1024 * 1024) return $"{n / (1024.0 * 1024):0.#}M";
        return $"{n / (1024.0 * 1024 * 1024):0.#}G";
    }

    /// <summary>
    /// 文字列を最大長に切り詰め、超過時は末尾に省略記号を付ける。
    /// </summary>
    private static string Trim(string s, int max)
    {
        if (max <= 0) return "";
        if (s.Length <= max) return s;
        if (max <= 1) return s[..max];
        return s[..(max - 1)] + "…";
    }

    /// <summary>
    /// 指定色でテキストを幅いっぱいにパディングして書き込む。
    /// </summary>
    private static void WritePadded(string text, int width, ConsoleColor fg, ConsoleColor bg)
    {
        // 現在の色を退避
        var oldFg = Console.ForegroundColor;
        var oldBg = Console.BackgroundColor;
        Console.ForegroundColor = fg;
        Console.BackgroundColor = bg;
        // 幅超過分を切り詰め、右パディングして出力
        if (text.Length > width) text = text[..width];
        Console.Write(text.PadRight(width));
        // 色を復元
        Console.ForegroundColor = oldFg;
        Console.BackgroundColor = oldBg;
    }

    /// <summary>
    /// WritePadded のラッパー（行単位描画用）。
    /// </summary>
    private static void WriteLinePadded(string text, int width, ConsoleColor fg, ConsoleColor bg)
    {
        WritePadded(text, width, fg, bg);
    }

    /// <summary>通常入力プロンプトを表示する。</summary>
    private string? Prompt(string message, string defaultValue)
        => DialogInput(message, defaultValue, secret: false);

    /// <summary>パスワード用（マスク表示）プロンプトを表示する。</summary>
    private string? PromptSecret(string message)
        => DialogInput(message, "", secret: true);

    /// <summary>
    /// 中央ダイアログで文字列入力を受け付ける。Esc でキャンセル（null）、Enter で確定。
    /// </summary>
    private string? DialogInput(string message, string defaultValue, bool secret)
    {
        GetTermSize(out var w, out var h);
        // ダイアログの位置・サイズを計算
        var boxW = Math.Min(w - 4, 70);
        var boxH = 7;
        var x = Math.Max(0, (w - boxW) / 2);
        var y = Math.Max(0, (h - boxH) / 2);
        DrawBox(x, y, boxW, boxH, message);

        var input = defaultValue;
        while (true)
        {
            // シークレット時は * でマスク
            var field = secret ? new string('*', input.Length) : input;
            Console.SetCursorPosition(x + 2, y + 3);
            WritePadded("> " + Trim(field, boxW - 6), boxW - 4, ConsoleColor.White, ConsoleColor.DarkBlue);
            Console.SetCursorPosition(x + 2, y + 5);
            WritePadded(Loc.EnterOkEscCancel, boxW - 4, ConsoleColor.Gray, ConsoleColor.DarkBlue);

            var key = Console.ReadKey(true);
            if (key.Key == ConsoleKey.Escape) return null;
            if (key.Key == ConsoleKey.Enter) return input;
            if (key.Key == ConsoleKey.Backspace)
            {
                if (input.Length > 0) input = input[..^1];
                continue;
            }
            // 制御文字以外を追記
            if (!char.IsControl(key.KeyChar))
                input += key.KeyChar;
        }
    }

    /// <summary>
    /// Yes/No 確認ダイアログ。Y/Enter で true、N/Esc で false。
    /// </summary>
    private bool Confirm(string message)
    {
        GetTermSize(out var w, out var h);
        var lines = message.Split('\n');
        // メッセージ幅に合わせたボックスサイズ
        var boxW = Math.Min(w - 4, Math.Max(40, lines.Max(l => l.Length) + 6));
        var boxH = lines.Length + 5;
        var x = Math.Max(0, (w - boxW) / 2);
        var y = Math.Max(0, (h - boxH) / 2);
        DrawBox(x, y, boxW, boxH, Loc.ConfirmTitle);
        // メッセージ各行を描画
        for (var i = 0; i < lines.Length; i++)
        {
            Console.SetCursorPosition(x + 2, y + 2 + i);
            WritePadded(Trim(lines[i], boxW - 4), boxW - 4, ConsoleColor.White, ConsoleColor.DarkBlue);
        }
        Console.SetCursorPosition(x + 2, y + boxH - 2);
        WritePadded(Loc.YesNo, boxW - 4, ConsoleColor.Yellow, ConsoleColor.DarkBlue);

        while (true)
        {
            var key = Console.ReadKey(true);
            if (key.Key is ConsoleKey.Y or ConsoleKey.Enter) return true;
            if (key.Key is ConsoleKey.N or ConsoleKey.Escape) return false;
        }
    }

    /// <summary>
    /// メッセージをアラートボックスで表示し、任意キー待ちする。
    /// </summary>
    private void Alert(string message)
    {
        GetTermSize(out var w, out var h);
        var lines = message.Replace("\r", "").Split('\n');
        var boxW = Math.Min(w - 4, Math.Max(40, lines.Max(l => Math.Min(l.Length, w - 10)) + 6));
        var boxH = Math.Min(h - 2, lines.Length + 4);
        var x = Math.Max(0, (w - boxW) / 2);
        var y = Math.Max(0, (h - boxH) / 2);
        DrawBox(x, y, boxW, boxH, Loc.AlertTitle);
        // 表示可能行数だけメッセージを描画
        for (var i = 0; i < Math.Min(lines.Length, boxH - 4); i++)
        {
            Console.SetCursorPosition(x + 2, y + 2 + i);
            WritePadded(Trim(lines[i], boxW - 4), boxW - 4, ConsoleColor.White, ConsoleColor.DarkRed);
        }
        Console.SetCursorPosition(x + 2, y + boxH - 2);
        WritePadded(Loc.PressAnyKey, boxW - 4, ConsoleColor.Yellow, ConsoleColor.DarkRed);
        Console.ReadKey(true);
    }

    /// <summary>
    /// 指定位置にタイトル付きの塗りつぶしボックスを描画する。
    /// </summary>
    private static void DrawBox(int x, int y, int boxW, int boxH, string title)
    {
        // 背景を塗りつぶす
        for (var row = 0; row < boxH; row++)
        {
            Console.SetCursorPosition(x, y + row);
            WritePadded(new string(' ', boxW), boxW, ConsoleColor.White, ConsoleColor.DarkBlue);
        }
        // タイトル行
        Console.SetCursorPosition(x, y);
        WritePadded("─" + Trim(title, boxW - 2).PadRight(boxW - 1), boxW, ConsoleColor.Black, ConsoleColor.Cyan);
    }
}
