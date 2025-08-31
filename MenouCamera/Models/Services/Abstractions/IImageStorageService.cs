using MenouCamera.Utils;

namespace MenouCamera.Models.Services.Abstractions;

/// <summary>
/// 画像データ保存インターフェース
/// </summary>
public interface IImageStorageService
{
    /// <summary>
    /// 画像のバイト列を指定フォーマットで保存し、保存先の絶対パスを返す
    /// </summary>
    /// <param name="imageBytes">保存対象の画像バイト列</param>
    /// <param name="format">保存形式</param>
    /// <param name="directory">保存先ディレクトリ（存在しない場合は作成しても良い）</param>
    /// <param name="fileName">保存ファイル名</param>
    /// <param name="cancelToken">キャンセルトークン</param>
    /// <returns>保存したファイルの絶対パス</returns>
    Task<string> SaveAsync(
        byte[] imageBytes,
        ImageFormat format,
        string directory,
        string fileName,
        CancellationToken cancelToken = default);
}
