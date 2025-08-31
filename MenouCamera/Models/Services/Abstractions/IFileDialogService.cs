namespace MenouCamera.Models.Services.Abstractions;

/// <summary>
/// ファイル/フォルダ選択ダイアログインターフェース
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// フォルダ選択ダイアログを表示し、選択されたディレクトリのパスを返す
    /// </summary>
    /// <returns>
    /// 選択されたディレクトリの絶対パス
    /// </returns>
    string? PickDirectory();

    /// <summary>
    /// 「名前を付けて保存」ダイアログを表示し、ユーザーにファイル名と保存形式（拡張子）を選ばせる
    /// </summary>
    /// <param name="suggested">
    /// 既定のファイル名
    /// </param>
    /// <returns>
    /// 選択されたファイルの完全パス（絶対パス）
    /// </returns>
    string? PickSaveFile(string suggested);
}
