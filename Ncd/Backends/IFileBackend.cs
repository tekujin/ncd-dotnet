// IFileBackend.cs — ストレージ抽象（ローカル／SMB 共通インターフェース）
namespace Ncd.Backends;

/// <summary>
/// ディレクトリ一覧の1行分（ファイルまたはフォルダ）。
/// </summary>
public sealed class DirEntry
{
    /// <summary>表示・操作用の名前（ベース名）。</summary>
    public required string Name { get; init; }

    /// <summary>ローカル絶対パス、または UNC（\\server\share\...）。</summary>
    public required string FullPath { get; init; }

    /// <summary>true=ディレクトリ / false=ファイル。</summary>
    public required bool IsDirectory { get; init; }

    /// <summary>ファイルサイズ（バイト）。ディレクトリは通常 0。</summary>
    public long Size { get; init; }

    /// <summary>最終更新日時（取得できない場合は null）。</summary>
    public DateTime? Modified { get; init; }

    /// <summary>UI 表示名。ディレクトリは [Name] 形式。</summary>
    public string DisplayName => IsDirectory ? $"[{Name}]" : Name;
}

/// <summary>
/// ファイルシステムの読み書きを抽象化したバックエンド。
/// LocalBackend / SmbBackend が実装する。
/// </summary>
public interface IFileBackend
{
    /// <summary>種類ラベル（"Local" または "SMB"）。ヘッダ表示用。</summary>
    string Kind { get; }

    /// <summary>ペイン見出しに出す現在位置の表示文字列。</summary>
    string DisplayRoot { get; }

    /// <summary>現在のパス（ローカル絶対、または UNC 表示）。</summary>
    string CurrentPath { get; }

    /// <summary>現在ディレクトリの一覧（先頭に ".." を含む場合あり）。</summary>
    IReadOnlyList<DirEntry> List();

    /// <summary>ディレクトリへ進入（".." なら親へ）。</summary>
    void Enter(DirEntry entry);

    /// <summary>親ディレクトリへ移動。移動できなければ false。</summary>
    bool GoParent();

    /// <summary>絶対パス／UNC へ直接移動。</summary>
    void GoTo(string absoluteOrUncPath);

    /// <summary>
    /// source 上の entry を「このバックエンドの現在ディレクトリ」へコピー。
    /// ディレクトリの場合は再帰コピー。
    /// </summary>
    void CopyFrom(IFileBackend source, DirEntry entry);

    /// <summary>entry を削除（ディレクトリは再帰）。</summary>
    void Delete(DirEntry entry);

    /// <summary>
    /// vi 編集用にローカルパスを用意する。
    /// ローカルはそのまま、SMB は一時ファイルへダウンロード。
    /// </summary>
    string BeginEdit(DirEntry entry);

    /// <summary>
    /// 編集終了後の後処理。modified=true なら SMB 等へ再アップロード。
    /// </summary>
    void FinishEdit(DirEntry entry, string localTempPath, bool modified);
}
