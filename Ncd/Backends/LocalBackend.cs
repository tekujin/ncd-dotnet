// LocalBackend.cs — ローカルファイルシステム向け IFileBackend 実装
namespace Ncd.Backends;

/// <summary>
/// 通常のディスク上のディレクトリを操作するバックエンド。
/// </summary>
public sealed class LocalBackend : IFileBackend
{
    /// <summary>種類ラベル（ヘッダに [Local] と出る）。</summary>
    public string Kind => "Local";

    /// <summary>表示用パス＝現在の絶対パス。</summary>
    public string DisplayRoot => CurrentPath;

    /// <summary>このバックエンドが「今いる」ディレクトリ。</summary>
    public string CurrentPath { get; private set; }

    /// <summary>開始パスを絶対パスに正規化して保持。</summary>
    public LocalBackend(string path)
    {
        CurrentPath = Path.GetFullPath(path); // 相対でも絶対に変換
    }

    /// <summary>現在ディレクトリの一覧を構築（.. → フォルダ → ファイル）。</summary>
    public IReadOnlyList<DirEntry> List()
    {
        var list = new List<DirEntry>(); // 返却用リスト
        // 親が存在するなら先頭に ".." を入れる
        if (Directory.GetParent(CurrentPath) != null)
        {
            list.Add(new DirEntry
            {
                Name = "..", // 親へ戻るエントリ
                FullPath = Directory.GetParent(CurrentPath)!.FullName,
                IsDirectory = true
            });
        }

        try
        {
            // サブディレクトリを名前順で追加
            foreach (var dir in Directory.EnumerateDirectories(CurrentPath)
                         .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
            {
                var fi = new DirectoryInfo(dir);
                list.Add(new DirEntry
                {
                    Name = fi.Name,
                    FullPath = fi.FullName,
                    IsDirectory = true,
                    Modified = fi.LastWriteTime
                });
            }

            // ファイルを名前順で追加
            foreach (var file in Directory.EnumerateFiles(CurrentPath)
                         .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
            {
                var fi = new FileInfo(file);
                list.Add(new DirEntry
                {
                    Name = fi.Name,
                    FullPath = fi.FullName,
                    IsDirectory = false,
                    Size = fi.Length,
                    Modified = fi.LastWriteTime
                });
            }
        }
        catch (Exception ex)
        {
            // 権限エラー等を IOException に包む
            throw new IOException($"목록을 읽을 수 없습니다: {ex.Message}", ex);
        }

        return list;
    }

    /// <summary>ディレクトリへ入る。ファイルなら何もしない。</summary>
    public void Enter(DirEntry entry)
    {
        if (!entry.IsDirectory) return; // ファイルは進入不可
        if (entry.Name == "..")
        {
            GoParent(); // 親へ
            return;
        }
        CurrentPath = entry.FullPath; // 子ディレクトリへ移動
    }

    /// <summary>親ディレクトリへ。ルートなら false。</summary>
    public bool GoParent()
    {
        var parent = Directory.GetParent(CurrentPath);
        if (parent == null) return false; // これ以上上れない
        CurrentPath = parent.FullName;
        return true;
    }

    /// <summary>絶対パスへジャンプ。存在しなければ例外。</summary>
    public void GoTo(string absoluteOrUncPath)
    {
        var full = Path.GetFullPath(absoluteOrUncPath.Trim()); // 正規化
        if (!Directory.Exists(full))
            throw new DirectoryNotFoundException($"디렉터리가 없습니다: {full}");
        CurrentPath = full;
    }

    /// <summary>
    /// source の entry を CurrentPath 配下へコピー。
    /// ディレクトリなら再帰（CurrentPath を一時的に子へ移して CopyFrom を繰り返す）。
    /// </summary>
    public void CopyFrom(IFileBackend source, DirEntry entry)
    {
        if (entry.Name is "." or "..")
            throw new InvalidOperationException("이 항목은 복사할 수 없습니다.");

        // コピー先のフルパス（同名）
        var destPath = Path.Combine(CurrentPath, entry.Name);

        // ファイルなら単純コピーして終了
        if (!entry.IsDirectory)
        {
            CopyFileFrom(source, entry, destPath);
            return;
        }

        // ディレクトリ：先にフォルダを作り、中身を再帰コピー
        Directory.CreateDirectory(destPath);
        var saved = CurrentPath;   // 元のカレントを退避
        CurrentPath = destPath;    // コピー先ディレクトリをカレントに
        try
        {
            foreach (var child in FileOps.ListChildren(source, entry))
            {
                if (child.Name is "." or "..") continue; // スキップ
                CopyFrom(source, child); // 再帰
            }
        }
        finally
        {
            CurrentPath = saved; // 必ず元に戻す
        }
    }

    /// <summary>1ファイルを destPath へ書き込む（ローカル直コピー or 一時ファイル経由）。</summary>
    private static void CopyFileFrom(IFileBackend source, DirEntry entry, string destPath)
    {
        if (source is LocalBackend)
        {
            File.Copy(entry.FullPath, destPath, overwrite: true); // 上書き許可
            return;
        }

        // SMB など：BeginEdit でローカル一時ファイルを得てからコピー
        var tmp = source.BeginEdit(entry);
        try
        {
            File.Copy(tmp, destPath, overwrite: true);
        }
        finally
        {
            // 変更なしとして一時ファイルを後始末
            source.FinishEdit(entry, tmp, modified: false);
        }
    }

    /// <summary>ファイルまたはディレクトリ（再帰）を削除。</summary>
    public void Delete(DirEntry entry)
    {
        if (entry.Name is "." or "..")
            throw new InvalidOperationException("이 항목은 삭제할 수 없습니다.");
        if (entry.IsDirectory)
            Directory.Delete(entry.FullPath, recursive: true); // 中身ごと削除
        else
            File.Delete(entry.FullPath);
    }

    /// <summary>vi 用。ローカルはそのパスをそのまま返す。</summary>
    public string BeginEdit(DirEntry entry)
    {
        if (entry.IsDirectory)
            throw new InvalidOperationException("디렉터리는 편집할 수 없습니다.");
        return entry.FullPath; // その場編集
    }

    /// <summary>ローカル編集は追加処理不要（空実装）。</summary>
    public void FinishEdit(DirEntry entry, string localTempPath, bool modified)
    {
        // no-op
    }
}
