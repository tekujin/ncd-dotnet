// FileOps.cs — バックエンド横断の「子エントリ列挙」ヘルパー
using Ncd.Backends; // DirEntry / LocalBackend / SmbBackend

namespace Ncd;

/// <summary>
/// ディレクトリ再帰コピー時に、ソース側の子ファイル／フォルダ一覧を取得する。
/// </summary>
public static class FileOps
{
    /// <summary>
    /// source 上の dir（ディレクトリ）直下のエントリ一覧を返す。
    /// </summary>
    public static IReadOnlyList<DirEntry> ListChildren(IFileBackend source, DirEntry dir)
    {
        // ディレクトリでなければ空
        if (!dir.IsDirectory)
            return Array.Empty<DirEntry>();

        // ---- ローカル FS の場合 ----
        if (source is LocalBackend)
        {
            var list = new List<DirEntry>(); // 結果バッファ
            // パスが消えている場合は空リスト
            if (!Directory.Exists(dir.FullPath))
                return list;

            // サブディレクトリを名前順（大文字小文字無視）で列挙
            foreach (var d in Directory.EnumerateDirectories(dir.FullPath)
                         .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
            {
                var fi = new DirectoryInfo(d); // メタデータ取得
                list.Add(new DirEntry
                {
                    Name = fi.Name,           // ディレクトリ名
                    FullPath = fi.FullName,   // 絶対パス
                    IsDirectory = true,       // ディレクトリである
                    Modified = fi.LastWriteTime // 更新日時
                });
            }

            // ファイルを名前順で列挙
            foreach (var f in Directory.EnumerateFiles(dir.FullPath)
                         .OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase))
            {
                var fi = new FileInfo(f); // ファイル情報
                list.Add(new DirEntry
                {
                    Name = fi.Name,         // ファイル名
                    FullPath = fi.FullName, // 絶対パス
                    IsDirectory = false,    // ファイル
                    Size = fi.Length,       // バイトサイズ
                    Modified = fi.LastWriteTime
                });
            }

            return list; // ローカル側の子一覧
        }

        // ---- SMB の場合 ----
        if (source is SmbBackend smb)
            return smb.ListChildrenOf(dir); // SMB 実装に委譲

        // 未知のバックエンド
        throw new NotSupportedException("Unknown backend");
    }
}
