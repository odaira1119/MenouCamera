using MenouCamera.Utils;

namespace MenouCamera.Models.Core;

/// <summary>
/// カメラから取得した 1 フレームを表す中立 DTO
/// </summary>
public sealed class ImageData
{
    /// <summary>
    /// 画像バイト列（PNG/JPEG/BMP 等）
    /// </summary>
    public byte[] Bytes { get; }

    /// <summary>
    /// 画像の幅（ピクセル）
    /// </summary>
    public int Width { get; }

    /// <summary>
    /// 画像の高さ（ピクセル）
    /// </summary>
    public int Height { get; }

    /// <summary>
    /// ピクセルフォーマット（例: "Bgr32", "Bgr24", "Mono8" など）
    /// </summary>
    public string PixelFormat { get; }

    /// <summary>
    /// 画像フォーマット
    /// </summary>
    public ImageFormat Format { get; }

    /// <summary>
    /// コンストラクタ
    /// </summary>
    /// <param name="bytes">画像</param>
    /// <param name="width">画像の幅</param>
    /// <param name="height">画像の高さ</param>
    /// <param name="pixelFormat">ピクセルフォーマット</param>
    /// <param name="format">画像形式</param>
    public ImageData(byte[] bytes, int width, int height, string pixelFormat, ImageFormat format)
    {
        if (bytes is null || bytes.Length == 0)
            throw new ArgumentException("画像がnullまたは空です。", nameof(bytes));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        if (string.IsNullOrWhiteSpace(pixelFormat))
            throw new ArgumentException("ピクセルフォーマットがnullまたは空です。", nameof(pixelFormat));

        Bytes = new byte[bytes.Length];
        Buffer.BlockCopy(bytes, 0, Bytes, 0, bytes.Length);

        Width = width;
        Height = height;
        PixelFormat = pixelFormat.Trim();
        Format = format;
    }
}
