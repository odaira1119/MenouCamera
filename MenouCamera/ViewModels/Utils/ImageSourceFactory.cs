using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MenouCamera.ViewModels.Utils;

/// <summary>
/// <c>byte[]</c> から WPF の <see cref="ImageSource"/> を生成するユーティリティ
/// </summary>
public static class ImageSourceFactory
{
    /// <summary>
    /// エンコード済み画像のバイト列（PNG/JPEG/BMP 等）から <see cref="ImageSource"/> を生成する。
    /// </summary>
    /// <param name="bytes">画像データのバイト列（必須）。</param>
    /// <returns>生成された <see cref="ImageSource"/>（フリーズ済み）。</returns>
    public static ImageSource FromBytes(byte[] bytes)
    {
        if (bytes is null || bytes.Length == 0)
            throw new ArgumentException("bytes must not be null or empty.", nameof(bytes));

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();

            bitmap.Freeze();

            return bitmap;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("バイト配列からImageSourceをデコードできませんでした。", ex);
        }
    }
}
