using MenouCamera.Models.Services.Abstractions;
using OpenCvSharp;

namespace MenouCamera.Models.Services.Implementations;

/// <summary>
/// OpenCvSharp を利用してサムネイルを生成するサービス実装。<br/>
/// - 入力は PNG/JPEG/BMP 等のエンコード済み画像バイト列。<br/>
/// - 出力は PNG エンコード済みの縮小画像バイト列。<br/>
/// - 拡大は行わず、指定サイズに内接する最大サイズで縮小する。
/// </summary>
public sealed class OpenCvThumbnailService : IThumbnailService
{
    /// <summary>
    /// サムネイルを生成する。
    /// </summary>
    /// <param name="fullImage">
    /// 入力画像のエンコード済みバイト列。<br/>
    /// <c>null</c> または空配列は許可されない。
    /// </param>
    /// <param name="maxWidth">サムネイルの最大幅（px, &gt; 0）。</param>
    /// <param name="maxHeight">サムネイルの最大高さ（px, &gt; 0）。</param>
    /// <returns>
    /// PNG エンコード済みのサムネイル画像バイト列。<br/>
    /// 入力画像が <paramref name="maxWidth"/>×<paramref name="maxHeight"/> 以下の場合は縮小せずそのまま返す。
    /// </returns>
    /// <exception cref="ArgumentException"><paramref name="fullImage"/> が <c>null</c> または空の場合。</exception>
    /// <exception cref="InvalidOperationException">画像デコードに失敗した場合。</exception>
    public byte[] CreateThumbnail(byte[] fullImage, int maxWidth, int maxHeight)
    {
        if (fullImage is null || fullImage.Length == 0)
            throw new ArgumentException("fullImage must not be null or empty.", nameof(fullImage));

        using var src = Cv2.ImDecode(fullImage, ImreadModes.Unchanged);
        if (src.Empty())
            throw new InvalidOperationException("Failed to decode image.");

        // 縦横比を維持したスケール計算（拡大はしない）
        var scale = Math.Min((double)maxWidth / src.Width, (double)maxHeight / src.Height);
        if (scale > 1.0) scale = 1.0;

        var destSize = new OpenCvSharp.Size(
            width: Math.Max(1, (int)Math.Round(src.Width * scale)),
            height: Math.Max(1, (int)Math.Round(src.Height * scale)));

        using var dst = new Mat();
        Cv2.Resize(src, dst, destSize, 0, 0, InterpolationFlags.Lanczos4);

        Cv2.ImEncode(".png", dst, out var buf);
        return buf.ToArray();
    }
}
