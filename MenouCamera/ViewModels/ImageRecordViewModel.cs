using System.Globalization;
using System.Windows.Media;
using MenouCamera.Models.Core;
using MenouCamera.ViewModels.Utils;

namespace MenouCamera.ViewModels;

/// <summary>
/// <see cref="ImageRecord"/> を UI で直接使える形にしたラッパー ViewModel
/// </summary>
public sealed class ImageRecordViewModel
{
    /// <summary>
    /// 撮像レコード
    /// </summary>
    public ImageRecord Model { get; }

    /// <summary>
    /// 一覧表示用のサムネイル画像
    /// </summary>
    public ImageSource Thumbnail { get; }

    /// <summary>
    /// プレビュー表示用のフル画像
    /// </summary>
    public ImageSource FullImage { get; }

    /// <summary>
    /// UI で表示する論理名
    /// </summary>
    public string Title => Model.Name;

    /// <summary>
    /// UI 表示用に整形した撮像時刻文字列
    /// </summary>
    public string CapturedAtText => Model.CapturedAt.ToString("yyyy/MM/dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>
    /// コンストラクタ。画像バイト列を WPF の <see cref="ImageSource"/> に変換して保持する。
    /// </summary>
    /// <param name="model">ラップ対象の <see cref="ImageRecord"/></param>
    public ImageRecordViewModel(ImageRecord model)
    {
        Model = model ?? throw new System.ArgumentNullException(nameof(model));

        Thumbnail = ImageSourceFactory.FromBytes(model.ThumbnailBytes);
        FullImage = ImageSourceFactory.FromBytes(model.ImageBytes);
    }
}
