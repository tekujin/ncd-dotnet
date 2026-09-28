// Loc.cs — UI 文言の国際化（日本語 / 韓国語 / 英語）
using System.Globalization; // CurrentUICulture 参照用

namespace Ncd.I18n;

/// <summary>アプリがサポートする表示言語。</summary>
public enum AppLang
{
    English,  // 英語
    Korean,   // 韓国語
    Japanese  // 日本語
}

/// <summary>
/// ロケール連動の文字列カタログ。
/// 判定順: NCD_LANG → LC_ALL → LC_MESSAGES → LANG → CurrentUICulture → 英語。
/// </summary>
public static class Loc
{
    /// <summary>現在選択中の言語。</summary>
    public static AppLang Lang { get; private set; } = AppLang.English;

    /// <summary>起動時に一度呼び、Lang を確定する。</summary>
    public static void Init()
    {
        Lang = Detect(); // 環境から検出
    }

    /// <summary>環境変数とカルチャから AppLang を決める。</summary>
    public static AppLang Detect()
    {
        // 明示オーバーライド（例: NCD_LANG=ja）
        var overrideLang = Environment.GetEnvironmentVariable("NCD_LANG");
        if (!string.IsNullOrWhiteSpace(overrideLang))
            return FromCode(overrideLang);

        // POSIX ロケール変数を優先順に見る
        foreach (var key in new[] { "LC_ALL", "LC_MESSAGES", "LANG" })
        {
            var v = Environment.GetEnvironmentVariable(key);
            // C / C.UTF-8 は「言語なし」扱いでスキップ
            if (!string.IsNullOrWhiteSpace(v) && v != "C" && !v.StartsWith("C.", StringComparison.Ordinal))
                return FromCode(v);
        }

        try
        {
            // .NET の UI カルチャ（例: ja-JP）
            return FromCode(CultureInfo.CurrentUICulture.Name);
        }
        catch
        {
            return AppLang.English; // 失敗時は英語
        }
    }

    /// <summary>言語コード文字列（ko_KR / ja-JP / en 等）を AppLang に変換。</summary>
    public static AppLang FromCode(string code)
    {
        var c = code.Trim().Replace('_', '-').ToLowerInvariant(); // 正規化
        if (c.StartsWith("ko")) return AppLang.Korean;   // 韓国語
        if (c.StartsWith("ja")) return AppLang.Japanese; // 日本語
        if (c.StartsWith("en")) return AppLang.English;  // 英語
        // その他（zh 等）は英語フォールバック
        return AppLang.English;
    }

    /// <summary>タイトル表示用の短い言語コード。</summary>
    public static string Code => Lang switch
    {
        AppLang.Korean => "ko",
        AppLang.Japanese => "ja",
        _ => "en"
    };

    /// <summary>現在言語に応じて en/ko/ja のどれかを返す。</summary>
    private static string T(string en, string ko, string ja) => Lang switch
    {
        AppLang.Korean => ko,
        AppLang.Japanese => ja,
        _ => en
    };

    // ========== ファンクションキーバー用ラベル ==========
    public static string FnHelp => T("Help", "도움말", "ヘルプ");           // F1
    public static string FnGoto => T("GoTo", "경로이동", "パス移動");         // F2
    public static string FnSwitch => T("Switch", "창전환", "切替");           // F3
    public static string FnCopy => T("Copy→R", "복사→右", "コピー→右");      // F4
    public static string FnSmb => T("SMB", "SMB연결", "SMB接続");             // F5
    public static string FnDelete => T("Delete", "삭제", "削除");             // F6
    public static string FnEdit => T("vi Edit", "vi편집", "vi編集");          // F7
    public static string FnUnset => T("N/A", "미설정", "未設定");             // 未割当キー
    public static string FnQuit => T("Quit", "종료", "終了");                 // F10/F12

    /// <summary>画面下部に並べる F1〜F12 の (キー, ラベル) 配列。</summary>
    public static (string Key, string Label)[] FnBar =>
    [
        ("F1", FnHelp),
        ("F2", FnGoto),
        ("F3", FnSwitch),
        ("F4", FnCopy),
        ("F5", FnSmb),
        ("F6", FnDelete),
        ("F7", FnEdit),
        ("F8", FnUnset),
        ("F9", FnUnset),
        ("F10", FnQuit),
        ("F11", FnUnset),
        ("F12", FnQuit),
    ];

    // ========== 一般 UI 文言 ==========
    public static string Ready => T("Ready", "준비", "準備"); // 初期ステータス
    public static string Left => T("Left", "왼쪽", "左");     // 左ペイン呼称
    public static string Right => T("Right", "오른쪽", "右"); // 右ペイン呼称
    public static string LeftActive => T("Left pane active", "왼쪽 창 활성", "左ペイン有効");
    public static string RightActive => T("Right pane active", "오른쪽 창 활성", "右ペイン有効");
    // 未設定キーを押したときのステータス
    public static string UnsetStatus(string key) => T($"{key}: unset", $"{key}: 미설정", $"{key}: 未設定");
    public static string ConfirmQuit => T("Quit NCD?", "종료하시겠습니까?", "終了しますか？");
    public static string ParentDir => T("Parent directory", "상위 디렉터리", "親ディレクトリ");
    // ファイル行で Enter したときのヒント
    public static string FileHint(string name) =>
        T($"File: {name} (F7=edit, F4=copy, F6=delete)",
          $"파일: {name} (F7=편집, F4=복사, F6=삭제)",
          $"ファイル: {name} (F7=編集, F4=コピー, F6=削除)");

    // F2 パス入力プロンプト
    public static string PromptGoto(string pane) =>
        T($"{pane} path (absolute or \\\\server\\share\\path):",
          $"{pane} 이동 경로 (절대경로 또는 \\\\server\\share\\path):",
          $"{pane} 移動先 (絶対パスまたは \\\\server\\share\\path):");

    public static string ErrSmbUncOnly =>
        T("SMB pane accepts UNC paths only, or reconnect with F5.",
          "SMB 창에서는 UNC 경로만 사용하거나, F5로 다시 연결하세요.",
          "SMBペインはUNCパスのみ、またはF5で再接続してください。");

    public static string ErrLocalF2Only =>
        T("F2 on local pane supports local absolute paths only. Use F5 for SMB.",
          "로컬 창 F2는 로컬 절대경로만 지원합니다. SMB는 F5.",
          "ローカルのF2は絶対パスのみ。SMBはF5を使用。");

    public static string HintSmbOnRight =>
        T("To switch a local pane to SMB, use F5 on the right pane.",
          "로컬 창에서 SMB로 바꾸려면 오른쪽에서 F5를 사용하세요.",
          "ローカルからSMBへは右ペインでF5を使用。");

    public static string Moved(string path) => T($"Go: {path}", $"이동: {path}", $"移動: {path}");
    public static string ErrorPrefix(string msg) => T("Error: ", "오류: ", "エラー: ") + msg;

    // ========== コピー（F4）関連 ==========
    public static string SelectFileOnLeft =>
        T("Select file(s) on the left pane (Space to mark).",
          "왼쪽에서 파일/폴더를 선택하세요 (Space로 선택).",
          "左でファイル/フォルダを選択してください (Spaceで選択)。");
    public static string FilesOnlyCopy =>
        T("Only files can be copied.", "파일만 복사할 수 있습니다.", "ファイルのみコピーできます。");
    public static string ConfirmCopy(string summary, string dest) =>
        T($"Copy to the right pane?\n{summary}\n→ {dest}",
          $"오른쪽 디렉터리로 복사할까요?\n{summary}\n→ {dest}",
          $"右のディレクトリへコピーしますか？\n{summary}\n→ {dest}");
    public static string CopyDone(string summary) =>
        T($"Copied: {summary}", $"복사 완료: {summary}", $"コピー完了: {summary}");
    public static string CopyFail(string msg) => T("Copy failed: ", "복사 실패: ", "コピー失敗: ") + msg;
    public static string CopyCancelled => T("Copy cancelled", "복사 취소", "コピーキャンセル");
    // 複数選択時の要約（ファイル数・フォルダ数）
    public static string MarkedSummary(int files, int dirs) =>
        T($"{files} file(s), {dirs} folder(s)",
          $"파일 {files}개, 폴더 {dirs}개",
          $"ファイル {files}、フォルダ {dirs}");
    // Space トグル後のステータス
    public static string MarkedToggle(string name, bool on) =>
        on
            ? T($"Marked: {name}", $"선택: {name}", $"選択: {name}")
            : T($"Unmarked: {name}", $"선택 해제: {name}", $"選択解除: {name}");
    public static string HelpSpace =>
        T("  Space     Mark / unmark item",
          "  Space     파일·폴더 선택/해제",
          "  Space     ファイル・フォルダ選択/解除");
    public static string HelpF12 =>
        T("  F12       Quit", "  F12       종료", "  F12       終了");

    // ========== SMB（F5）ダイアログ ==========
    // サンプル値はプレースホルダ（実パスワードをリポジトリに書かない）
    public static string PromptSmbServer => T("SMB server (e.g. xxx.xxx.xxx.xxx):", "SMB 서버 (예: xxx.xxx.xxx.xxx):", "SMBサーバー (例: xxx.xxx.xxx.xxx):");
    public static string PromptShare => T("Share name:", "공유 이름:", "共有名:");
    public static string PromptUser => T("User:", "사용자:", "ユーザー:");
    public static string PromptPassword => T("Password (e.g. xxxxxxx):", "비밀번호 (예: xxxxxxx):", "パスワード (例: xxxxxxx):");
    public static string PromptDomain => T("Domain (Enter if none):", "도메인 (없으면 Enter):", "ドメイン (なければEnter):");
    public static string SmbConnected(string path) => T($"SMB connected: {path}", $"SMB 연결: {path}", $"SMB接続: {path}");
    public static string SmbFail(string msg) => T("SMB failed: ", "SMB 실패: ", "SMB失敗: ") + msg;
    public static string SmbConnectFail(string msg) =>
        T("SMB connection failed:\n", "SMB 연결 실패:\n", "SMB接続失敗:\n") + msg;

    // ========== 削除（F6） ==========
    public static string NothingToDelete =>
        T("Nothing to delete.", "삭제할 항목이 없습니다.", "削除する項目がありません。");
    public static string ConfirmDelete(string side, string summary) =>
        T($"Delete on the {side} pane?\n\n{summary}",
          $"{side} 화면의 항목을 삭제하시겠습니까?\n\n{summary}",
          $"{side}画面の項目を削除しますか？\n\n{summary}");
    public static string DeleteCancelled => T("Delete cancelled", "삭제 취소", "削除キャンセル");
    public static string DeleteDone(string side, string summary) =>
        T($"{side} deleted: {summary}", $"{side} 삭제 완료: {summary}", $"{side} 削除完了: {summary}");
    public static string DeleteFail(string msg) => T("Delete failed: ", "삭제 실패: ", "削除失敗: ") + msg;

    // ========== vi 編集（F7） ==========
    public static string SelectFileToEdit =>
        T("Select a file to edit.", "편집할 파일을 선택하세요.", "編集するファイルを選択してください。");
    public static string Saved(string name) => T($"Saved: {name}", $"저장됨: {name}", $"保存: {name}");
    public static string EditClosed(string name) => T($"Edit closed: {name}", $"편집 종료: {name}", $"編集終了: {name}");
    public static string EditApplyFail(string msg) => T("Failed to apply edit: ", "편집 반영 실패: ", "編集反映失敗: ") + msg;

    // ========== ヘルプ（F1）本文 ==========
    public static string HelpTitle =>
        T("NCD — Dual-pane file manager (.NET)",
          "NCD — 듀얼 패널 파일 관리자 (.NET)",
          "NCD — デュアルペイン ファイルマネージャ (.NET)");
    public static string HelpEnter => T("  Enter     Enter directory", "  Enter     디렉터리 들어가기", "  Enter     ディレクトリへ入る");
    public static string HelpBack => T("  Backspace Parent directory", "  Backspace 상위 디렉터리", "  Backspace 親ディレクトリ");
    public static string HelpTab => T("  Tab/F3    Switch panes", "  Tab/F3    왼쪽↔오른쪽", "  Tab/F3    左右切替");
    public static string HelpF4 => T("  F4        Copy marked (left → right)", "  F4        선택 항목 → 오른쪽 복사", "  F4        選択項目 → 右へコピー");
    public static string HelpF5 => T("  F5        Samba (SMB) on right", "  F5        오른쪽 창 Samba(SMB) 연결", "  F5        右ペイン Samba(SMB) 接続");
    public static string HelpF6 => T("  F6        Delete marked items", "  F6        선택 항목 삭제", "  F6        選択項目を削除");
    public static string HelpLang =>
        T($"  Language  {Code} (NCD_LANG / LC_MESSAGES / LANG)",
          $"  언어      {Code} (NCD_LANG / LC_MESSAGES / LANG)",
          $"  言語      {Code} (NCD_LANG / LC_MESSAGES / LANG)");

    // タイトルバー1行
    public static string TitleBar(string active) =>
        T($" NCD/.NET  Dual Pane  │ Active: {active}  │ Lang:{Code}  │ F12=Quit ",
          $" NCD/.NET  듀얼패널  │ 활성: {active}  │ 언어:{Code}  │ F12=종료 ",
          $" NCD/.NET  デュアル  │ 有効: {active}  │ 言語:{Code}  │ F12=終了 ");

    public static string ActiveLeft => T("LEFT", "왼쪽", "左");   // タイトル用
    public static string ActiveRight => T("RIGHT", "오른쪽", "右");

    // ダイアログ定型句
    public static string EnterOkEscCancel =>
        T("Enter=OK  Esc=Cancel", "Enter=확인  Esc=취소", "Enter=確定  Esc=取消");
    public static string ConfirmTitle => T("Confirm", "확인", "確認");
    public static string YesNo => T("Y=Yes  N=No", "Y=예  N=아니오", "Y=はい  N=いいえ");
    public static string AlertTitle => T("Alert", "알림", "通知");
    public static string PressAnyKey => T("Press any key", "아무 키나 누르세요", "キーを押してください");

    // Program.cs 用
    public static string NeedTty =>
        T("ncd: run in an interactive terminal (ssh -t recommended)",
          "ncd: 대화형 터미널에서 실행하세요. (ssh -t 권장)",
          "ncd: 対話型ターミナルで実行してください (ssh -t 推奨)");
    public static string FatalPrefix => T("ncd error: ", "ncd 오류: ", "ncd エラー: ");

    // 旧メッセージ（互換用に残置）
    public static string DirCopyNotSupported =>
        T("Directory copy is not supported yet.", "디렉터리 복사는 아직 지원하지 않습니다.", "ディレクトリコピーは未対応です。");
}
