using MenouCamera.Utils;
using System.IO;

namespace MenouCamera.Models.Core;

/// <summary>
/// 撮像レコード
/// </summary>
public sealed class ImageRecord
{
    /// <summary>
    /// 履歴の一意識別子
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// 画像の論理名
    /// ユーザー未指定時は"capture_yyyyMMdd_HHmmss_fff" が自動設定される
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// 撮像タイムスタンプ（ローカル時刻）
    /// </summary>
    public DateTime CapturedAt { get; }

    /// <summary>
    /// フルサイズ画像
    /// </summary>
    public byte[] ImageBytes { get; }

    /// <summary>
    /// 一覧表示用サムネイル
    /// </summary>
    public byte[] ThumbnailBytes { get; }

    /// <summary>
    /// 画像幅（ピクセル）
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// 画像高さ（ピクセル）
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// 画像形式
    /// </summary>
    public ImageFormat Format { get; }

    /// <summary>
    /// 既定のファイル名（例: capture_20250828_134512_123.png）
    /// </summary>
    public string DefaultFileName => $"{Name}.{Format.ToExtension()}";

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="name">論理名</param>
    /// <param name="capturedAt">撮像時刻</param>
    /// <param name="imageBytes">フル画像のエンコード済みバイト列</param>
    /// <param name="thumbnailBytes">サムネイルのエンコード済みバイト列</param>
    /// <param name="width">幅(px)</param>
    /// <param name="height">高さ(px)</param>
    /// <param name="format">フォーマット("png" / "jpg" / "bmp" / "tif" )</param>
    public ImageRecord(
        string? name,
        DateTime capturedAt,
        byte[] imageBytes,
        byte[] thumbnailBytes,
        int width,
        int height,
        ImageFormat format)
    {
        if (imageBytes is null || imageBytes.Length == 0)
            throw new ArgumentException("フルサイズ画像がnullまたは空です。", nameof(imageBytes));
        if (thumbnailBytes is null || thumbnailBytes.Length == 0)
            throw new ArgumentException("サムネイル画像がnullまたは空です。", nameof(thumbnailBytes));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        CapturedAt = capturedAt;
        Width = width;
        Height = height;
        Format = format;

        ImageBytes = new byte[imageBytes.Length];
        Buffer.BlockCopy(imageBytes, 0, ImageBytes, 0, imageBytes.Length);

        ThumbnailBytes = new byte[thumbnailBytes.Length];
        Buffer.BlockCopy(thumbnailBytes, 0, ThumbnailBytes, 0, thumbnailBytes.Length);

        var baseName = string.IsNullOrWhiteSpace(name)
            ? $"capture_{CapturedAt:yyyyMMdd_HHmmss_fff}"
            : name!.Trim();

        Name = SanitizeForFileName(baseName);
        if (string.IsNullOrWhiteSpace(Name))
            Name = $"capture_{CapturedAt:yyyyMMdd_HHmmss_fff}";
    }

    /// <summary>
    /// ファイル名として不適切な文字を安全な文字に置換し、末尾のドット等を除去する。
    /// </summary>
    private static string SanitizeForFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return cleaned.Trim().Trim('.');
    }
}
