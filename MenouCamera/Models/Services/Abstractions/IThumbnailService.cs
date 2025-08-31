namespace MenouCamera.Models.Services.Abstractions;

/// <summary>
/// フルサイズ画像のバイト列からサムネイル生成インターフェース
/// </summary>
public interface IThumbnailService
{
    /// <summary>サムネイルを生成する</summary>
    /// <param name="fullImage">入力画像のバイト列</param>
    /// <param name="maxWidth">サムネイルの最大幅</param>
    /// <param name="maxHeight">サムネイルの最大高さ</param>
    /// <returns>エンコード済みサムネイル画像のバイト列</returns>
    byte[] CreateThumbnail(byte[] fullImage, int maxWidth, int maxHeight);
}
