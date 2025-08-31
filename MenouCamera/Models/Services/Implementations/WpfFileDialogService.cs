using System.IO;
using MenouCamera.Models.Services.Abstractions;
using Microsoft.Win32;
using Ookii.Dialogs.Wpf;
using MenouCamera.Utils;

namespace MenouCamera.Models.Services.Implementations;

/// <summary>
/// WPF の標準ダイアログを用いた実装
/// </summary>
public sealed class WpfFileDialogService : IFileDialogService
{
    /// <summary>
    /// フォルダ選択ダイアログを表示し、選択されたディレクトリの絶対パスを返す
    /// </summary>
    /// <returns>選択されたフォルダの絶対パス。</returns>
    public string? PickDirectory()
    {
        var dialog = new VistaFolderBrowserDialog
        {
            Description = "保存先フォルダを選択してください",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        return dialog.ShowDialog() == true ? dialog.SelectedPath : null;
    }

    /// <summary>
    /// 「名前を付けて保存」ダイアログを表示し、ユーザーにファイル名と保存形式（拡張子）を選ばせる
    /// </summary>
    /// <param name="suggested">
    /// 既定のファイル名
    /// </param>
    /// <returns>選択されたファイルの絶対パス</returns>
    public string? PickSaveFile(string suggested)
    {
        var ext = Path.GetExtension(suggested);
        var defaultFormat = string.IsNullOrWhiteSpace(ext)
            ? ImageFormat.Png
            : ImageFormatExtensions.ParseOrDefault(ext.TrimStart('.'));

        var dialog = new SaveFileDialog
        {
            FileName = suggested,
            AddExtension = true,
            OverwritePrompt = true,
            CheckPathExists = true,
            RestoreDirectory = true,
            DefaultExt = "." + defaultFormat.ToExtension(),
            Filter = BuildFilterString()
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }


    private static string BuildFilterString()
    {
        return string.Join("|", new[]
        {
            BuildFilterItem(ImageFormat.Png , "PNG" , new[] { "png" }),
            BuildFilterItem(ImageFormat.Jpg , "JPEG", new[] { "jpg", "jpeg" }),
            BuildFilterItem(ImageFormat.Bmp , "BMP" , new[] { "bmp" }),
            BuildFilterItem(ImageFormat.Tif , "TIFF", new[] { "tif", "tiff" }),
        });
    }

    private static string BuildFilterItem(ImageFormat fmt, string label, IEnumerable<string> exts)
    {
        var patterns = string.Join(";", exts.Select(e => $"*.{e}"));
        return $"{label} ({patterns})|{patterns}";
    }
}
