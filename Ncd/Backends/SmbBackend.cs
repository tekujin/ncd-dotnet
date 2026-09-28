// SMB/CIFS（Samba）バックエンド実装。
// UNC 形式 \\server\share\subdir で共有上のファイル操作を行うクラス。
using System.Net;
using SMBLibrary;
using SMBLibrary.Client;

namespace Ncd.Backends;

/// <summary>
/// SMB/CIFS バックエンド（Samba）。パス形式: \\server\share\subdir
/// 共有への接続・一覧・コピー・削除・編集などのファイル操作を提供する。
/// </summary>
public sealed class SmbBackend : IFileBackend, IDisposable
{
    /// <summary>SMB2 プロトコル用クライアント。</summary>
    private readonly SMB2Client _client = new();
    /// <summary>接続中の共有（ツリー）へのファイルストア。未接続または破棄後は null。</summary>
    private ISMBFileStore? _store;
    /// <summary>接続先サーバー名（ホスト名または IP）。</summary>
    private readonly string _server;
    /// <summary>接続先共有名。</summary>
    private readonly string _share;
    /// <summary>ログインに使用したユーザー名。</summary>
    private readonly string _user;
    /// <summary>Dispose 済みかどうかのフラグ。</summary>
    private bool _disposed;

    /// <summary>バックエンド種別識別子（常に "SMB"）。</summary>
    public string Kind => "SMB";
    /// <summary>UI 表示用の現在ルート（\\server\share + 相対パス）。</summary>
    public string DisplayRoot => $"\\\\{_server}\\{_share}{CurrentRelative}";
    /// <summary>現在パス（DisplayRoot と同じ）。</summary>
    public string CurrentPath => DisplayRoot;

    /// <summary>共有内の相対パス。バックスラッシュ区切り、先頭スラッシュなし。空文字は共有ルート。</summary>
    public string CurrentRelative { get; private set; }

    /// <summary>
    /// サーバー・共有へ接続し、ログインしてツリー接続を確立するコンストラクタ。
    /// </summary>
    /// <param name="server">サーバー名</param>
    /// <param name="share">共有名</param>
    /// <param name="domain">ドメイン（空可）</param>
    /// <param name="user">ユーザー名</param>
    /// <param name="password">パスワード</param>
    /// <param name="startPath">開始時の相対パス（省略時は共有ルート）</param>
    public SmbBackend(string server, string share, string domain, string user, string password, string? startPath = null)
    {
        // サーバー名・共有名の前後空白と区切り文字を除去して保持
        _server = server.Trim().Trim('\\', '/');
        _share = share.Trim().Trim('\\', '/');
        _user = user;
        // 開始相対パスを正規化して設定
        CurrentRelative = NormalizeRel(startPath ?? "");

        // DirectTCP で SMB サーバーへ接続。失敗時は例外
        if (!_client.Connect(_server, SMBTransportType.DirectTCPTransport))
            throw new IOException($"SMB 서버에 연결할 수 없습니다: {_server}");

        // ドメイン／ユーザー／パスワードでログイン
        var status = _client.Login(domain ?? string.Empty, user, password);
        if (status != NTStatus.STATUS_SUCCESS)
            throw new IOException($"SMB 로그인 실패: {status}");

        // 指定共有へツリー接続
        _store = _client.TreeConnect(_share, out status);
        if (status != NTStatus.STATUS_SUCCESS || _store == null)
            throw new IOException($"공유 연결 실패 ({_share}): {status}");
    }

    /// <summary>
    /// ダイアログ入力値から SmbBackend を生成するファクトリ。
    /// </summary>
    public static SmbBackend ConnectFromDialog(string server, string share, string user, string password, string domain = "")
        => new(server, share, domain, user, password);

    /// <summary>
    /// UNC または smb:// URL を解析し、サーバー・共有・相対パスに分割する。
    /// </summary>
    /// <returns>解析成功なら true。形式不正なら false。</returns>
    public static bool TryParseUnc(string input, out string server, out string share, out string rel)
    {
        // 出力を空で初期化
        server = share = rel = "";
        var s = input.Trim();
        // smb://host/share/... 形式なら UNC 風に変換
        if (s.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
        {
            s = s[6..].Replace('/', '\\');
            s = "\\\\" + s;
        }
        // スラッシュをバックスラッシュに統一
        s = s.Replace('/', '\\');
        while (s.StartsWith("\\\\"))
        {
            // 先頭が \\ であることを確認して抜ける
            // ok
            break;
        }
        // UNC（\\ 始まり）でなければ失敗
        if (!s.StartsWith("\\\\"))
            return false;
        // 先頭の \ を除き、空要素を除いて分割
        var parts = s.TrimStart('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        // サーバーと共有の最低 2 要素が必要
        if (parts.Length < 2) return false;
        server = parts[0];
        share = parts[1];
        // 3 要素目以降を相対パスとして結合
        rel = parts.Length > 2 ? string.Join("\\", parts.Skip(2)) : "";
        return true;
    }

    /// <summary>
    /// 相対パスを正規化する（/ を \ に置換し、前後の \ を除去）。
    /// </summary>
    private static string NormalizeRel(string path)
    {
        var p = path.Replace('/', '\\').Trim('\\');
        return p;
    }

    /// <summary>
    /// 現在の相対パスに名前を結合する。ルートなら名前のみ返す。
    /// </summary>
    private string CombineRel(string name)
    {
        if (string.IsNullOrEmpty(CurrentRelative)) return name;
        return CurrentRelative + "\\" + name;
    }

    /// <summary>
    /// 現在ディレクトリのエントリ一覧を取得する。親がある場合は ".." を先頭に含める。
    /// </summary>
    public IReadOnlyList<DirEntry> List()
    {
        // 破棄済みなら例外
        if (_store == null) throw new ObjectDisposedException(nameof(SmbBackend));
        var list = new List<DirEntry>();
        // 共有ルート以外なら親へ戻る ".." エントリを追加
        if (!string.IsNullOrEmpty(CurrentRelative))
        {
            var parent = ParentRel(CurrentRelative);
            list.Add(new DirEntry
            {
                Name = "..",
                FullPath = string.IsNullOrEmpty(parent) ? $"\\\\{_server}\\{_share}" : $"\\\\{_server}\\{_share}\\{parent}",
                IsDirectory = true
            });
        }

        object handle;
        FileStatus fileStatus;
        // 現在相対パスのディレクトリを開く
        var status = _store.CreateFile(
            out handle,
            out fileStatus,
            CurrentRelative,
            AccessMask.GENERIC_READ,
            SMBLibrary.FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);

        if (status != NTStatus.STATUS_SUCCESS)
            throw new IOException($"SMB 디렉터리 열기 실패: {status}");

        try
        {
            List<QueryDirectoryFileInformation>? infos;
            // SMB2 専用のディレクトリ列挙を試行
            status = ((SMB2FileStore)_store).QueryDirectory(out infos, handle, "*", FileInformationClass.FileDirectoryInformation);
            // Some versions use ISMBFileStore.QueryDirectory differently
            // 失敗時（続きなし以外）は汎用ヘルパーで再取得
            if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_NO_MORE_FILES)
            {
                // fallback via helper
                infos = QueryAll(handle);
            }

            if (infos != null)
            {
                // 各ファイル情報を DirEntry に変換して追加
                foreach (var info in infos)
                {
                    if (info is FileDirectoryInformation fdi)
                    {
                        var name = fdi.FileName;
                        // "." / ".." はスキップ
                        if (name is "." or "..") continue;
                        var isDir = (fdi.FileAttributes & SMBLibrary.FileAttributes.Directory) != 0;
                        // UNC 形式のフルパスを組み立て
                        var full = string.IsNullOrEmpty(CurrentRelative)
                            ? $"\\\\{_server}\\{_share}\\{name}"
                            : $"\\\\{_server}\\{_share}\\{CurrentRelative}\\{name}";
                        list.Add(new DirEntry
                        {
                            Name = name,
                            FullPath = full,
                            IsDirectory = isDir,
                            Size = isDir ? 0 : (long)fdi.EndOfFile,
                            Modified = fdi.LastWriteTime
                        });
                    }
                }
            }
        }
        finally
        {
            // ディレクトリハンドルを必ず閉じる
            _store.CloseFile(handle);
        }

        // ".." → ディレクトリ → 名前順でソートして返す
        return list
            .OrderByDescending(e => e.Name == "..")
            .ThenByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// ISMBFileStore.QueryDirectory を繰り返し呼び出し、全エントリを収集するフォールバック。
    /// </summary>
    private List<QueryDirectoryFileInformation> QueryAll(object handle)
    {
        var result = new List<QueryDirectoryFileInformation>();
        if (_store == null) return result;
        NTStatus status;
        do
        {
            // チャンク単位で列挙結果を取得して結合
            List<QueryDirectoryFileInformation> chunk;
            status = _store.QueryDirectory(out chunk, handle, "*", FileInformationClass.FileDirectoryInformation);
            if (chunk != null) result.AddRange(chunk);
        } while (status == NTStatus.STATUS_SUCCESS);
        return result;
    }

    /// <summary>
    /// 相対パスの親ディレクトリ相対パスを返す。親がなければ空文字。
    /// </summary>
    private static string ParentRel(string rel)
    {
        var i = rel.LastIndexOf('\\');
        return i < 0 ? "" : rel[..i];
    }

    /// <summary>
    /// 指定ディレクトリエントリへカレントを移動する。".." なら親へ。
    /// </summary>
    public void Enter(DirEntry entry)
    {
        // ディレクトリ以外は無視
        if (!entry.IsDirectory) return;
        if (entry.Name == "..")
        {
            GoParent();
            return;
        }
        // 子ディレクトリ名を相対パスに結合
        CurrentRelative = CombineRel(entry.Name);
    }

    /// <summary>
    /// 親ディレクトリへ移動する。既に共有ルートなら false。
    /// </summary>
    public bool GoParent()
    {
        if (string.IsNullOrEmpty(CurrentRelative)) return false;
        CurrentRelative = ParentRel(CurrentRelative);
        return true;
    }

    /// <summary>
    /// UNC 絶対パスへジャンプする。別サーバー／共有への切替は不可。
    /// </summary>
    public void GoTo(string absoluteOrUncPath)
    {
        // UNC 解析に失敗したら引数例外
        if (!TryParseUnc(absoluteOrUncPath, out var server, out var share, out var rel))
            throw new ArgumentException("SMB 경로는 \\\\server\\share\\path 형식이어야 합니다.");
        // 接続中と異なるサーバー／共有なら操作不可
        if (!string.Equals(server, _server, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(share, _share, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("다른 서버/공유로 바꾸려면 F5로 다시 연결하세요.");
        CurrentRelative = NormalizeRel(rel);
    }

    /// <summary>
    /// 他バックエンドのエントリを現在ディレクトリへコピーする（ディレクトリは再帰）。
    /// </summary>
    public void CopyFrom(IFileBackend source, DirEntry entry)
    {
        // "." / ".." はコピー対象外
        if (entry.Name is "." or "..")
            throw new InvalidOperationException("이 항목은 복사할 수 없습니다.");

        var destRel = CombineRel(entry.Name);

        if (entry.IsDirectory)
        {
            // 先に宛先ディレクトリを確保
            EnsureDirectory(destRel);
            var saved = CurrentRelative;
            // カレントを宛先に移して子を再帰コピー
            CurrentRelative = destRel;
            try
            {
                foreach (var child in FileOps.ListChildren(source, entry))
                {
                    if (child.Name is "." or "..") continue;
                    CopyFrom(source, child);
                }
            }
            finally
            {
                // カレントを元に戻す
                CurrentRelative = saved;
            }
            return;
        }

        // ファイル本体のバイト列をソース種別に応じて取得
        byte[] data;
        if (source is LocalBackend)
        {
            // ローカルなら直接読み込み
            data = File.ReadAllBytes(entry.FullPath);
        }
        else if (source is SmbBackend other)
        {
            // 別 SMB インスタンスならその ReadAllBytes を使用
            data = other.ReadAllBytes(entry);
        }
        else
        {
            // その他は一時ファイル経由で読み取り
            var tmp = source.BeginEdit(entry);
            try { data = File.ReadAllBytes(tmp); }
            finally { source.FinishEdit(entry, tmp, false); }
        }
        // 宛先相対パスへ書き込み
        WriteAllBytes(destRel, data);
    }

    /// <summary>
    /// 指定ディレクトリエントリ直下の子一覧を返す。
    /// </summary>
    public IReadOnlyList<DirEntry> ListChildrenOf(DirEntry dir)
    {
        var rel = RelFromEntry(dir);
        return ListAt(rel);
    }

    /// <summary>
    /// DirEntry の FullPath（UNC）または Name から、この共有内の相対パスを求める。
    /// </summary>
    private string RelFromEntry(DirEntry entry)
    {
        // UNC がこのサーバー／共有なら相対部分を使用、否则は Name を結合
        if (TryParseUnc(entry.FullPath, out var server, out var share, out var rel) &&
            string.Equals(server, _server, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(share, _share, StringComparison.OrdinalIgnoreCase))
            return NormalizeRel(rel);
        return CombineRel(entry.Name);
    }

    /// <summary>
    /// 指定相対パスのディレクトリ内容を一覧する（内部用。".." は含めない）。
    /// </summary>
    private IReadOnlyList<DirEntry> ListAt(string rel)
    {
        if (_store == null) throw new ObjectDisposedException(nameof(SmbBackend));
        var list = new List<DirEntry>();

        object handle;
        FileStatus fileStatus;
        // 対象ディレクトリを開く
        var status = _store.CreateFile(
            out handle, out fileStatus, rel,
            AccessMask.GENERIC_READ,
            SMBLibrary.FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);
        if (status != NTStatus.STATUS_SUCCESS)
            throw new IOException($"SMB 디렉터리 열기 실패: {status}");

        try
        {
            // 全エントリを取得して DirEntry 化
            var infos = QueryAll(handle);
            foreach (var info in infos)
            {
                if (info is not FileDirectoryInformation fdi) continue;
                var name = fdi.FileName;
                if (name is "." or "..") continue;
                var isDir = (fdi.FileAttributes & SMBLibrary.FileAttributes.Directory) != 0;
                var childRel = string.IsNullOrEmpty(rel) ? name : rel + "\\" + name;
                var full = $"\\\\{_server}\\{_share}\\{childRel}";
                list.Add(new DirEntry
                {
                    Name = name,
                    FullPath = full,
                    IsDirectory = isDir,
                    Size = isDir ? 0 : (long)fdi.EndOfFile,
                    Modified = fdi.LastWriteTime
                });
            }
        }
        finally
        {
            _store.CloseFile(handle);
        }

        // ディレクトリ優先・名前昇順でソート
        return list
            .OrderByDescending(e => e.IsDirectory)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 相対パスのディレクトリが存在しなければ作成する（OPEN_IF）。
    /// </summary>
    private void EnsureDirectory(string rel)
    {
        if (_store == null) throw new ObjectDisposedException(nameof(SmbBackend));
        // 空（共有ルート）は何もしない
        if (string.IsNullOrEmpty(rel)) return;

        object handle;
        FileStatus fs;
        // なければ作成、あれば開く
        var status = _store.CreateFile(
            out handle, out fs, rel,
            AccessMask.GENERIC_READ | AccessMask.GENERIC_WRITE,
            SMBLibrary.FileAttributes.Directory,
            ShareAccess.Read | ShareAccess.Write,
            CreateDisposition.FILE_OPEN_IF,
            CreateOptions.FILE_DIRECTORY_FILE,
            null);
        if (status != NTStatus.STATUS_SUCCESS)
            throw new IOException($"SMB 디렉터리 생성 실패: {status}");
        _store.CloseFile(handle);
    }

    /// <summary>
    /// エントリを削除する。ディレクトリは子を先に再帰削除する。
    /// </summary>
    public void Delete(DirEntry entry)
    {
        if (entry.Name is "." or "..")
            throw new InvalidOperationException("이 항목은 삭제할 수 없습니다.");
        if (_store == null) throw new ObjectDisposedException(nameof(SmbBackend));

        var rel = RelFromEntry(entry);
        // ディレクトリなら中身を先に削除
        if (entry.IsDirectory)
        {
            foreach (var child in ListAt(rel))
                Delete(child);
        }

        DeletePath(rel, entry.IsDirectory);
    }

    /// <summary>
    /// 相対パスのファイルまたはディレクトリを SMB 上で削除する。
    /// DELETE_ON_CLOSE を試し、失敗時は FileDispositionInformation で再試行する。
    /// </summary>
    private void DeletePath(string rel, bool isDirectory)
    {
        if (_store == null) throw new ObjectDisposedException(nameof(SmbBackend));
        // ディレクトリ／ファイルに応じた CreateOptions
        var options = isDirectory
            ? CreateOptions.FILE_DIRECTORY_FILE
            : CreateOptions.FILE_NON_DIRECTORY_FILE;

        object handle;
        FileStatus fs;
        // 閉じた際に削除されるよう開く
        var status = _store.CreateFile(
            out handle, out fs, rel,
            AccessMask.DELETE | AccessMask.GENERIC_WRITE,
            0,
            ShareAccess.None,
            CreateDisposition.FILE_OPEN,
            options | CreateOptions.FILE_DELETE_ON_CLOSE,
            null);
        if (status != NTStatus.STATUS_SUCCESS)
        {
            // 代替: 通常オープン後に DeletePending を設定
            status = _store.CreateFile(out handle, out fs, rel,
                AccessMask.DELETE, 0, ShareAccess.Read | ShareAccess.Write | ShareAccess.Delete,
                CreateDisposition.FILE_OPEN, options, null);
            if (status != NTStatus.STATUS_SUCCESS)
                throw new IOException($"SMB 삭제 실패: {status}");
            var info = new FileDispositionInformation { DeletePending = true };
            status = _store.SetFileInformation(handle, info);
            _store.CloseFile(handle);
            if (status != NTStatus.STATUS_SUCCESS)
                throw new IOException($"SMB 삭제 실패: {status}");
            return;
        }
        // DELETE_ON_CLOSE 成功時はクローズで削除完了
        _store.CloseFile(handle);
    }

    /// <summary>
    /// 編集用にリモートファイルを一時ローカルファイルへ展開し、そのパスを返す。
    /// </summary>
    public string BeginEdit(DirEntry entry)
    {
        if (entry.IsDirectory)
            throw new InvalidOperationException("디렉터리는 편집할 수 없습니다.");
        // 内容を読み取り、一意な一時ファイルへ書き出す
        var data = ReadAllBytes(entry);
        var tmp = Path.Combine(Path.GetTempPath(), "ncd-smb-" + Guid.NewGuid().ToString("N") + "-" + entry.Name);
        File.WriteAllBytes(tmp, data);
        return tmp;
    }

    /// <summary>
    /// 編集終了処理。modified なら一時ファイル内容をリモートへ書き戻し、一時ファイルを削除する。
    /// </summary>
    public void FinishEdit(DirEntry entry, string localTempPath, bool modified)
    {
        try
        {
            // 変更があり一時ファイルが残っていればアップロード
            if (modified && File.Exists(localTempPath))
                WriteAllBytes(RelFromEntry(entry), File.ReadAllBytes(localTempPath));
        }
        finally
        {
            // 一時ファイルは失敗しても無視して削除試行
            try { File.Delete(localTempPath); } catch { /* ignore */ }
        }
    }

    /// <summary>
    /// DirEntry から相対パスを求め、ファイル全体を読み取る（内部公開）。
    /// </summary>
    internal byte[] ReadAllBytes(DirEntry entry)
        => ReadAllBytes(RelFromEntry(entry));

    /// <summary>
    /// 指定相対パスのファイルを 64KB チャンクで読み取り、バイト配列で返す。
    /// </summary>
    private byte[] ReadAllBytes(string rel)
    {
        if (_store == null) throw new ObjectDisposedException(nameof(SmbBackend));
        object handle;
        FileStatus fs;
        // 読み取り専用でファイルを開く
        var status = _store.CreateFile(out handle, out fs, rel,
            AccessMask.GENERIC_READ | AccessMask.SYNCHRONIZE,
            SMBLibrary.FileAttributes.Normal,
            ShareAccess.Read,
            CreateDisposition.FILE_OPEN,
            CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
            null);
        if (status != NTStatus.STATUS_SUCCESS)
            throw new IOException($"SMB 파일 열기 실패: {status}");
        try
        {
            using var ms = new MemoryStream();
            long offset = 0;
            while (true)
            {
                // 64KB ずつ読み進める
                byte[]? chunk;
                status = _store.ReadFile(out chunk, handle, offset, 1024 * 64);
                if (status != NTStatus.STATUS_SUCCESS && status != NTStatus.STATUS_END_OF_FILE)
                    throw new IOException($"SMB 읽기 실패: {status}");
                // 空なら終端
                if (chunk == null || chunk.Length == 0) break;
                ms.Write(chunk, 0, chunk.Length);
                offset += chunk.Length;
                // 要求未満なら最終チャンク
                if (chunk.Length < 64 * 1024) break;
            }
            return ms.ToArray();
        }
        finally
        {
            _store.CloseFile(handle);
        }
    }

    /// <summary>
    /// 指定相対パスへバイト配列を上書き書き込みする（64KB チャンク）。
    /// </summary>
    private void WriteAllBytes(string rel, byte[] data)
    {
        if (_store == null) throw new ObjectDisposedException(nameof(SmbBackend));
        object handle;
        FileStatus fs;
        // 存在すれば上書き、なければ作成
        var status = _store.CreateFile(out handle, out fs, rel,
            AccessMask.GENERIC_WRITE | AccessMask.SYNCHRONIZE,
            SMBLibrary.FileAttributes.Normal,
            ShareAccess.None,
            CreateDisposition.FILE_OVERWRITE_IF,
            CreateOptions.FILE_NON_DIRECTORY_FILE | CreateOptions.FILE_SYNCHRONOUS_IO_ALERT,
            null);
        if (status != NTStatus.STATUS_SUCCESS)
            throw new IOException($"SMB 파일 쓰기 실패: {status}");
        try
        {
            int offset = 0;
            while (offset < data.Length)
            {
                // 最大 64KB のスライスを書き込む
                int len = Math.Min(64 * 1024, data.Length - offset);
                var slice = new byte[len];
                Buffer.BlockCopy(data, offset, slice, 0, len);
                int written;
                status = _store.WriteFile(out written, handle, offset, slice);
                if (status != NTStatus.STATUS_SUCCESS)
                    throw new IOException($"SMB 쓰기 실패: {status}");
                offset += written;
            }
        }
        finally
        {
            _store.CloseFile(handle);
        }
    }

    /// <summary>
    /// ツリー切断・ログオフ・接続切断を行いリソースを解放する。
    /// </summary>
    public void Dispose()
    {
        // 二重 Dispose 防止
        if (_disposed) return;
        _disposed = true;
        // 各解放処理の失敗は無視
        try { _store?.Disconnect(); } catch { /* ignore */ }
        try { _client.Logoff(); } catch { /* ignore */ }
        try { _client.Disconnect(); } catch { /* ignore */ }
    }
}
