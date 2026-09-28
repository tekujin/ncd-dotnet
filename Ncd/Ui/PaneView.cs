// PaneView.cs — 左右どちらか一方のペイン状態（一覧・カーソル・マーク）
namespace Ncd.Ui;

/// <summary>
/// 片方の画面パネルが持つデータと選択状態。
/// </summary>
public sealed class PaneView
{
    /// <summary>このペインが参照するストレージ（Local / SMB）。</summary>
    public required Backends.IFileBackend Backend { get; set; }

    /// <summary>現在表示中のエントリ一覧。</summary>
    public IReadOnlyList<Backends.DirEntry> Entries { get; private set; } = Array.Empty<Backends.DirEntry>();

    /// <summary>カーソル行インデックス（0 始まり）。</summary>
    public int SelectedIndex { get; set; }

    /// <summary>一覧の先頭に見えている行のオフセット（スクロール）。</summary>
    public int ScrollOffset { get; set; }

    /// <summary>List 失敗時のエラーメッセージ（描画時に表示）。</summary>
    public string? Error { get; set; }

    /// <summary>Space で付けたマーク対象（FullPath をキーに保持）。</summary>
    private readonly HashSet<string> _marked = new(StringComparer.Ordinal);

    /// <summary>Backend.List を呼び、表示内容とマークを更新する。</summary>
    public void Refresh()
    {
        try
        {
            Entries = Backend.List(); // 最新一覧を取得
            Error = null;             // 成功したらエラー解除
            // もう存在しないパスのマークを削除
            var alive = new HashSet<string>(Entries.Select(e => e.FullPath), StringComparer.Ordinal);
            _marked.RemoveWhere(p => !alive.Contains(p));
            // カーソルが範囲外なら末尾へ補正
            if (SelectedIndex >= Entries.Count) SelectedIndex = Math.Max(0, Entries.Count - 1);
            if (SelectedIndex < 0) SelectedIndex = 0;
        }
        catch (Exception ex)
        {
            // 一覧取得失敗時は空にしてエラーを保持
            Entries = Array.Empty<Backends.DirEntry>();
            Error = ex.Message;
            SelectedIndex = 0;
            ScrollOffset = 0;
            _marked.Clear(); // マークもクリア
        }
    }

    /// <summary>カーソル上のエントリ。範囲外なら null。</summary>
    public Backends.DirEntry? Selected =>
        Entries.Count > 0 && SelectedIndex >= 0 && SelectedIndex < Entries.Count
            ? Entries[SelectedIndex]
            : null;

    /// <summary>指定エントリがマーク済みか。</summary>
    public bool IsMarked(Backends.DirEntry e) => _marked.Contains(e.FullPath);

    /// <summary>カーソル行のマークをトグル（. と .. は無視）。</summary>
    public void ToggleMarkCurrent()
    {
        var e = Selected;
        if (e == null || e.Name is "." or "..") return; // 親参照はマーク不可
        // Add が false なら既に存在 → 解除
        if (!_marked.Add(e.FullPath))
            _marked.Remove(e.FullPath);
    }

    /// <summary>すべてのマークを解除。</summary>
    public void ClearMarks() => _marked.Clear();

    /// <summary>現在のマーク数。</summary>
    public int MarkedCount => _marked.Count;

    /// <summary>
    /// 操作対象リストを返す。
    /// マークがあればそれら、なければカーソル1件（.. 以外）。
    /// </summary>
    public List<Backends.DirEntry> GetTargets()
    {
        var result = new List<Backends.DirEntry>();
        if (_marked.Count > 0)
        {
            // 表示順を保ったままマーク済みだけ抽出
            foreach (var e in Entries)
            {
                if (e.Name is "." or "..") continue;
                if (_marked.Contains(e.FullPath))
                    result.Add(e);
            }
            return result;
        }

        // マーク無し → カーソル項目を1件だけ
        var cur = Selected;
        if (cur != null && cur.Name is not ("." or ".."))
            result.Add(cur);
        return result;
    }

    /// <summary>カーソルを delta 行動かして見える範囲にスクロール。</summary>
    public void Move(int delta, int pageSize)
    {
        if (Entries.Count == 0) return; // 空なら何もしない
        SelectedIndex = Math.Clamp(SelectedIndex + delta, 0, Entries.Count - 1);
        EnsureVisible(pageSize);
    }

    /// <summary>カーソルが画面外なら ScrollOffset を調整。</summary>
    public void EnsureVisible(int pageSize)
    {
        if (SelectedIndex < ScrollOffset)
            ScrollOffset = SelectedIndex; // 上にはみ出した
        if (SelectedIndex >= ScrollOffset + pageSize)
            ScrollOffset = SelectedIndex - pageSize + 1; // 下にはみ出した
        if (ScrollOffset < 0) ScrollOffset = 0;
    }
}
