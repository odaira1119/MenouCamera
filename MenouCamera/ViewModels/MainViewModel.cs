using System.Collections.ObjectModel;
using System.IO;
using MenouCamera.Commands;
using MenouCamera.Models.Core;
using MenouCamera.Models.Services.Abstractions;
using OpenCvSharp;
using MenouCamera.Utils;

namespace MenouCamera.ViewModels;

/// <summary>
/// 撮像 → プレビュー → 履歴 → 保存の ViewModel
/// </summary>
public sealed class MainViewModel : ViewModelBase
{
    /// <summary>カメラ撮像</summary>
    private readonly ICameraService _camera;

    /// <summary>サムネイル生成</summary>
    private readonly IThumbnailService _thumbnail;

    /// <summary>画像保存</summary>
    private readonly IImageStorageService _storage;

    /// <summary>ファイル/フォルダ選択ダイアログ。</summary>
    private readonly IFileDialogService _dialog;


    /// <summary>サムネイル履歴（新しい順）</summary>
    public ObservableCollection<ImageRecordViewModel> History { get; } = new();

    private ImageRecordViewModel? _selected;

    /// <summary>現在選択中の 1 件（プレビュー表示対象）。</summary>
    public ImageRecordViewModel? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                // 選択変化で保存可否が変わる
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private bool _isBusy;

    /// <summary>ビジー状態。実行中はボタンを無効化する。</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                CaptureCommand.RaiseCanExecuteChanged();
                SaveCommand.RaiseCanExecuteChanged();
            }
        }
    }


    /// <summary>撮像ボタン用の非同期コマンド</summary>
    public AsyncRelayCommand CaptureCommand { get; }

    /// <summary>保存ボタン用の非同期コマンド</summary>
    public AsyncRelayCommand SaveCommand { get; }


    /// <summary>使用するカメラのデバイス ID（OpenCV では数値インデックスが一般的）</summary>
    public string DeviceId { get; set; } = "0";

    /// <summary>撮像幅（px）。</summary>
    public int CaptureWidth { get; set; } = 1280;

    /// <summary>撮像高さ（px）。</summary>
    public int CaptureHeight { get; set; } = 720;

    /// <summary>フレームレート（fps）。</summary>
    public double CaptureFps { get; set; } = 30.0;

    /// <summary>ピクセルフォーマット</summary>
    public string CapturePixelFormat { get; set; } = "Bgr32";

    /// <summary>
    /// 依存サービスを注入して初期化する。
    /// </summary>
    public MainViewModel(
        ICameraService camera,
        IThumbnailService thumbnail,
        IImageStorageService storage,
        IFileDialogService dialog)
    {
        _camera = camera;
        _thumbnail = thumbnail;
        _storage = storage;
        _dialog = dialog;

        CaptureCommand = new AsyncRelayCommand(CaptureAsync, () => !IsBusy);
        SaveCommand = new AsyncRelayCommand(SaveAsync, () => Selected is not null && !IsBusy);
    }

    /// <summary>
    /// 撮像の実行
    /// - カメラを開いて 1 フレーム取得
    /// - サムネイルを生成
    /// - 履歴の先頭に追加し、選択してプレビューに反映
    /// </summary>
    private async Task CaptureAsync()
    {
        try
        {
            IsBusy = true;

            await _camera.OpenAsync(DeviceId);

            var settings = new CaptureSettings(
                deviceId: DeviceId,
                width: CaptureWidth,
                height: CaptureHeight,
                frameRate: CaptureFps,
                pixelFormat: CapturePixelFormat);

            // 1 フレーム取得
            var frame = await _camera.CaptureAsync(settings);

            // サムネイル（PNG バイト）作成
            var thumb = _thumbnail.CreateThumbnail(frame.Bytes, maxWidth: 240, maxHeight: 240);

            var record = new ImageRecord(
                name: null,
                capturedAt: DateTime.Now,
                imageBytes: frame.Bytes,
                thumbnailBytes: thumb,
                width: frame.Width,
                height: frame.Height,
                format: frame.Format);

            var vm = new ImageRecordViewModel(record);
            History.Insert(0, vm);
            Selected = vm;
        }
        catch (Exception)
        {
            // TODO: ユーザー通知（メッセージダイアログやステータス表示など）
        }
        finally
        {
            try { await _camera.CloseAsync(); } catch { /* ignore */ }
            IsBusy = false;
        }
    }

    /// <summary>
    /// 保存の実行
    /// - 保存ダイアログでファイル名と形式を選択
    /// - 形式が異なる場合は OpenCV で再エンコードしてから保存
    /// </summary>
    private async Task SaveAsync()
    {
        if (Selected is null) return;

        try
        {
            IsBusy = true;

            var suggested = Selected.Model.DefaultFileName;
            var fullPath = _dialog.PickSaveFile(suggested);
            if (string.IsNullOrWhiteSpace(fullPath)) return;

            var dir = Path.GetDirectoryName(fullPath)!;
            var name = Path.GetFileName(fullPath);
            var targetFmt = ExtToFormat(Path.GetExtension(fullPath));

            var srcFmt = Selected.Model.Format;
            var bytes = Selected.Model.ImageBytes;

            if (srcFmt != targetFmt)
            {
                using var mat = Cv2.ImDecode(bytes, ImreadModes.Unchanged);
                if (mat.Empty())
                    throw new InvalidOperationException("画像のデコードに失敗しました。");

                Cv2.ImEncode("." + targetFmt.ToExtension(), mat, out var buf);
                bytes = buf.ToArray();
            }

            _ = await _storage.SaveAsync(
                imageBytes: bytes,
                format: targetFmt,
                directory: dir,
                fileName: name);
        }
        catch (Exception)
        {
            // TODO: エラーメッセージ表示
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 拡張子（.png 等）から内部形式名（png/jpg/bmp/tif）へ変換する。
    /// </summary>
    private static ImageFormat ExtToFormat(string? ext)
    {
        var e = (ext ?? "").Trim().TrimStart('.').ToLowerInvariant();
        return ImageFormatExtensions.ParseOrDefault(e);
    }
}
