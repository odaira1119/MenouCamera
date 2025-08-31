namespace MenouCamera.Utils;

public enum ImageFormat
{
    Png,
    Jpg,
    Bmp,
    Tif
}

public static class ImageFormatExtensions
{
    /// <summary>
    /// 拡張子文字列（ドットなし）に変換
    /// </summary>
    public static string ToExtension(this ImageFormat format) => format switch
    {
        ImageFormat.Png => "png",
        ImageFormat.Jpg => "jpg",
        ImageFormat.Bmp => "bmp",
        ImageFormat.Tif => "tif",
        _ => "png"
    };

    /// <summary>
    /// 文字列を安全に ImageFormat へパース（未知値は既定 Png）
    /// </summary>
    public static ImageFormat ParseOrDefault(string? s) => (s ?? "").Trim().ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => ImageFormat.Jpg,
        "png" => ImageFormat.Png,
        "bmp" => ImageFormat.Bmp,
        "tif" or "tiff" => ImageFormat.Tif,
        _ => ImageFormat.Png
    };
}
