using System.IO;
using MenouCamera.Models.Services.Abstractions;
using MenouCamera.Utils;

namespace MenouCamera.Models.Services.Implementations;

/// <summary>
/// 画像データ保存実装
/// </summary>
public sealed class FileSystemImageStorageService : IImageStorageService
{
    /// <summary>
    /// 指定されたディレクトリとファイル名で、画像バイト列を書き出す。
    /// </summary>
    /// <param name="imageBytes">保存対象の画像データ</param>
    /// <param name="format">保存形式</param>
    /// <param name="directory">保存先ディレクトリ</param>
    /// <param name="fileName">保存ファイル名</param>
    /// <param name="cancelToken">キャンセルトークン</param>
    /// <returns>保存先ファイルの絶対パス</returns>
    public async Task<string> SaveAsync(
        byte[] imageBytes,
        ImageFormat format,
        string directory,
        string fileName,
        CancellationToken cancelToken = default)
    {
        if (imageBytes is null || imageBytes.Length == 0)
            throw new System.ArgumentException("画像データがnullまたは空です。", nameof(imageBytes));
        if (string.IsNullOrWhiteSpace(directory))
            throw new System.ArgumentException("保存先ディレクトリががnullまたは空です。", nameof(directory));
        if (string.IsNullOrWhiteSpace(fileName))
            throw new System.ArgumentException("保存ファイル名ががnullまたは空です。", nameof(fileName));

        cancelToken.ThrowIfCancellationRequested();

        System.IO.Directory.CreateDirectory(directory);

        var ext = format.ToExtension();
        var safeName = SanitizeFileName(Path.ChangeExtension(fileName, ext));

        var fullPath = Path.Combine(directory, safeName);

        await File.WriteAllBytesAsync(fullPath, imageBytes, cancelToken).ConfigureAwait(false);

        return fullPath;
    }

    /// <summary>
    /// ファイル名に使えない文字を安全な文字に置換し、末尾のドット等を除去する。
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        foreach (var ch in invalid)
            name = name.Replace(ch, '_');
        return name.Trim().Trim('.');
    }
}
