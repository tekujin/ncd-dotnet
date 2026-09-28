// Program.cs — アプリケーションのエントリポイント（起動処理）
using Ncd.I18n; // 多言語リソース Loc を使う
using Ncd.Ui;   // デュアルペイン UI（App）を使う

// ロケール／NCD_LANG から表示言語（ja/ko/en）を決定する
Loc.Init();

// 引数があればそれを開始ディレクトリ、なければカレントディレクトリ
var start = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
// 指定パスが存在しなければカレントにフォールバック
if (!Directory.Exists(start))
    start = Directory.GetCurrentDirectory();

try
{
    // パイプ等で標準入力がリダイレクトされ、かつ強制フラグが無い場合は対話不可
    if (Console.IsInputRedirected && Environment.GetEnvironmentVariable("NCD_FORCE") != "1")
    {
        // 対話端末で起動するよう促して終了コード 1
        Console.Error.WriteLine(Loc.NeedTty);
        return 1;
    }

    // UI 本体を生成し、キー入力ループを開始する
    new App(start).Run();
}
catch (Exception ex)
{
    // 異常終了時はカーソルを戻してからエラーを表示
    Console.CursorVisible = true;
    Console.Error.WriteLine(Loc.FatalPrefix + ex.Message);
    return 1; // 失敗
}

// 正常終了
return 0;
