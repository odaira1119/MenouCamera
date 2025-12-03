using MenouCamera.Commands;
using MenouCamera.Models.Core;
using MenouCamera.Models.Services.Abstractions;
using MenouCamera.Models.Services.Implementations;
using MenouCamera.Utils;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks; // 非同期
using System.Windows.Media.Imaging;
using System.Windows.Threading;

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
                BenchmarkRotateCommand.RaiseCanExecuteChanged();
                StartLiveCommand.RaiseCanExecuteChanged();
                StopLiveCommand.RaiseCanExecuteChanged();
                MeasureLiveFpsWindowCommand.RaiseCanExecuteChanged();
                MeasureLiveFpsBatchCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>撮像ボタン用の非同期コマンド</summary>
    public AsyncRelayCommand CaptureCommand { get; }

    /// <summary>保存ボタン用の非同期コマンド</summary>
    public AsyncRelayCommand SaveCommand { get; }

    /// <summary>回転純コストベンチ実行コマンド</summary>
    public AsyncRelayCommand BenchmarkRotateCommand { get; }

    /// <summary>ライブ開始/停止コマンド</summary>
    public AsyncRelayCommand StartLiveCommand { get; }
    public AsyncRelayCommand StopLiveCommand { get; }

    /// <summary>ライブFPS計測コマンド（単発／一括）</summary>
    public AsyncRelayCommand MeasureLiveFpsWindowCommand { get; }
    public AsyncRelayCommand MeasureLiveFpsBatchCommand { get; }

    /// <summary>使用するカメラのデバイス ID（OpenCV では数値インデックスが一般的）</summary>
    public string DeviceId { get; set; } = "0";

    /// <summary>撮像幅（px）。</summary>
    public int CaptureWidth { get; set; } = 640;   // ★ 640 固定

    /// <summary>撮像高さ（px）。</summary>
    public int CaptureHeight { get; set; } = 480;  // ★ 480 固定

    /// <summary>フレームレート（fps）。</summary>
    public double CaptureFps { get; set; } = 30.0;

    /// <summary>ピクセルフォーマット（静止画保存時のヒント）</summary>
    public string CapturePixelFormat { get; set; } = "Bgr32";

    /// <summary>ベンチ設定：ウォームアップ回数</summary>
    public int BenchWarmup { get; set; } = 30;

    /// <summary>ベンチ設定：反復回数（推奨1000）</summary>
    public int BenchIterations { get; set; } = 1000;

    /// <summary>ベンチ出力CSVパス</summary>
    public string BenchCsvPath { get; set; } = "rotate_pure.csv";

    // --- ライブビューの可変設定（UIバインド前提） ---
    public int LiveRotateAngle { get; set; } = 0;      // 0/90/180/270
    public bool LiveFlipHorizontal { get; set; } = false;
    public bool LiveFlipVertical { get; set; } = false;
    public string? LivePixelFormatFourCC { get; set; } = null;   // "MJPG","YUY2","NV12" 等。null/空でデフォルト
    public bool LiveConvertRgb { get; set; } = true;   // true=OpenCVでBGR化

    // 一括計測の候補セット
    public double[] MeasureTargetFpsList { get; set; } = new[] { 20.0, 25.0, 30.0 };
    public int[] MeasureAngles { get; set; } = new[] { 0, 90, 180, 270 };
    public string[] MeasurePixelFormats { get; set; } = new[] { "MJPG", "YUY2", "NV12" };

    // 計測CSV保存先
    public string MeasureOutputFolder { get; set; } = @"D:\MyApp\計測";

    // --- ライブビュー用 ---
    private WriteableBitmap? _liveBitmap;
    public WriteableBitmap? LiveBitmap
    {
        get => _liveBitmap;
        private set
        {
            if (SetProperty(ref _liveBitmap, value))
                OnPropertyChanged(nameof(IsNotLive));
        }
    }

    private double _liveFps;
    public double LiveFps
    {
        get => _liveFps;
        private set => SetProperty(ref _liveFps, value);
    }

    private bool _isLive;
    public bool IsLive
    {
        get => _isLive;
        private set
        {
            if (SetProperty(ref _isLive, value))
            {
                OnPropertyChanged(nameof(IsNotLive));
                StartLiveCommand.RaiseCanExecuteChanged();
                StopLiveCommand.RaiseCanExecuteChanged();
                CaptureCommand.RaiseCanExecuteChanged();
            }
        }
    }
    public bool IsNotLive => !IsLive;

    private readonly object _wbLock = new();

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
        BenchmarkRotateCommand = new AsyncRelayCommand(BenchmarkRotateAsync, () => !IsBusy);

        StartLiveCommand = new AsyncRelayCommand(StartLiveAsync, () => !IsBusy && !IsLive);
        StopLiveCommand = new AsyncRelayCommand(StopLiveAsync, () => !IsBusy && IsLive);

        MeasureLiveFpsWindowCommand = new AsyncRelayCommand(MeasureLiveFpsWindowAsync, () => !IsBusy);
        MeasureLiveFpsBatchCommand = new AsyncRelayCommand(MeasureLiveFpsBatchAsync, () => !IsBusy);
    }

    /// <summary>
    /// 撮像の実行（静止画1フレーム）
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
            // TODO: ユーザー通知
        }
        finally
        {
            try { await _camera.CloseAsync(); } catch { /* ignore */ }
            IsBusy = false;
        }
    }

    /// <summary>
    /// 保存の実行
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
    /// 回転純コストベンチ（90/180/270）
    /// </summary>
    private async Task BenchmarkRotateAsync()
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

            // 1フレーム取得して Mat に復元（計測対象は Rotate のみ）
            var shot = await _camera.CaptureAsync(settings);
            using var src = Cv2.ImDecode(shot.Bytes, ImreadModes.Color);
            if (src.Empty())
                throw new InvalidOperationException("ベンチ用フレームのデコードに失敗しました。");

            // CSV ヘッダ（なければ作成）
            if (!File.Exists(BenchCsvPath))
                File.WriteAllText(BenchCsvPath, "angle,iter,rotate_ms\n");

            // 3角度を測定
            RunAngleBench(src, 90, BenchWarmup, BenchIterations, BenchCsvPath);
            RunAngleBench(src, 180, BenchWarmup, BenchIterations, BenchCsvPath);
            RunAngleBench(src, 270, BenchWarmup, BenchIterations, BenchCsvPath);

            Console.WriteLine($"CSV: {Path.GetFullPath(BenchCsvPath)}");
            Debug.WriteLine($"CSV: {Path.GetFullPath(BenchCsvPath)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[BenchmarkRotate] Error: {ex.Message}");
            Debug.WriteLine($"[BenchmarkRotate] Error: {ex}");
        }
        finally
        {
            try { await _camera.CloseAsync(); } catch { /* ignore */ }
            IsBusy = false;
        }
    }

    private static void RunAngleBench(Mat src, int angle, int warmup, int iterations, string csvPath)
    {
        using var dst = new Mat();
        var sw = new Stopwatch();

        // ウォームアップ
        for (int i = 0; i < warmup; i++)
            RotateRightAngle(src, angle, dst);

        // 計測
        var samples = new double[iterations];
        for (int i = 0; i < iterations; i++)
        {
            sw.Restart();
            RotateRightAngle(src, angle, dst);
            sw.Stop();
            samples[i] = sw.Elapsed.TotalMilliseconds;

            // 明細追記（Excelで開けるCSV）
            File.AppendAllText(csvPath, $"{angle},{i + 1},{samples[i]:F6}\n");
        }

        Array.Sort(samples);
        double avg = samples.Average();
        double p95 = samples[(int)Math.Floor(0.95 * (samples.Length - 1))];
        double max = samples[^1];
        double fps = avg > 0 ? 1000.0 / avg : double.PositiveInfinity;

        // サマリ行
        File.AppendAllText(csvPath,
            $"summary,{angle},{iterations},avg_ms,{avg:F6},p95_ms,{p95:F6},max_ms,{max:F6},est_fps,{fps:F2}\n");

        // コンソール/デバッグ出力
        var line = $"{angle}deg N={iterations} avg={avg:F3}ms p95={p95:F3}ms max={max:F3}ms -> ~{fps:F1} FPS";
        Console.WriteLine(line);
        Debug.WriteLine(line);
    }

    /// <summary>
    /// 90/180/270°専用 Rotate（補間なし・高速）
    /// </summary>
    private static void RotateRightAngle(Mat src, int angle, Mat dst)
    {
        RotateFlags flag = angle switch
        {
            90 => RotateFlags.Rotate90Clockwise,
            180 => RotateFlags.Rotate180,
            270 => RotateFlags.Rotate90Counterclockwise,
            _ => throw new ArgumentOutOfRangeException(nameof(angle), "90/180/270 のみ")
        };
        Cv2.Rotate(src, dst, flag);
    }

    /// <summary>
    /// 拡張子（.png 等）から内部形式名（png/jpg/bmp/tif）へ変換する。
    /// </summary>
    private static ImageFormat ExtToFormat(string? ext)
    {
        var e = (ext ?? "").Trim().TrimStart('.').ToLowerInvariant();
        return ImageFormatExtensions.ParseOrDefault(e);
    }

    // ---- ライブ開始/停止（OpenCvCameraService のライブAPIを利用） ----

    private OpenCvCameraService? AsConcreteCamera() => _camera as OpenCvCameraService;

    private async Task StartLiveAsync()
    {
        try
        {
            IsBusy = true;

            var cam = AsConcreteCamera() ?? throw new InvalidOperationException(
                "ライブビューは OpenCvCameraService 実装でサポートされています。");

            await cam.OpenAsync(DeviceId);

            // ライブ設定（UIプロパティをそのまま適用）
            var lv = new OpenCvCameraService.LiveViewSettings(
                Width: CaptureWidth,
                Height: CaptureHeight,
                TargetFps: CaptureFps,
                RotateAngle: LiveRotateAngle,
                FlipHorizontal: LiveFlipHorizontal,
                FlipVertical: LiveFlipVertical,
                PixelFormatFourCC: LivePixelFormatFourCC,
                ConvertRgb: LiveConvertRgb
            );

            // イベント購読（多重購読防止）
            cam.FrameArrived -= OnFrameArrived;
            cam.FpsUpdated -= OnFpsUpdated;
            cam.FrameArrived += OnFrameArrived;
            cam.FpsUpdated += OnFpsUpdated;

            await cam.StartLiveAsync(lv);
            IsLive = true;

            StartLiveCommand.RaiseCanExecuteChanged();
            StopLiveCommand.RaiseCanExecuteChanged();
            CaptureCommand.RaiseCanExecuteChanged();
            SaveCommand.RaiseCanExecuteChanged();
            BenchmarkRotateCommand.RaiseCanExecuteChanged();
        }
        catch
        {
            try { await _camera.CloseAsync(); } catch { /* ignore */ }
            IsLive = false;
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task StopLiveAsync()
    {
        try
        {
            IsBusy = true;

            var cam = AsConcreteCamera();
            cam?.StopLive();
            if (cam is not null)
            {
                cam.FrameArrived -= OnFrameArrived;
                cam.FpsUpdated -= OnFpsUpdated;
            }

            await _camera.CloseAsync();
        }
        finally
        {
            IsLive = false;
            LiveFps = 0;
            LiveBitmap = null;

            StartLiveCommand.RaiseCanExecuteChanged();
            StopLiveCommand.RaiseCanExecuteChanged();
            CaptureCommand.RaiseCanExecuteChanged();
            SaveCommand.RaiseCanExecuteChanged();
            BenchmarkRotateCommand.RaiseCanExecuteChanged();

            IsBusy = false;
        }
    }

    // ---- ライブイベント受領 ----

    private void OnFpsUpdated(double fps)
    {
        // UIスレッドで反映
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            LiveFps = fps;
        }, DispatcherPriority.Render);
    }

    private void OnFrameArrived(Mat mat, DateTime ts, long seq)
    {
        try
        {
            // Mat(BGR/GRAY/… ) → WPF向け BGRA32 へ
            using var bgra = new Mat();
            switch (mat.Channels())
            {
                case 4: mat.CopyTo(bgra); break;
                case 3: Cv2.CvtColor(mat, bgra, ColorConversionCodes.BGR2BGRA); break;
                default: Cv2.CvtColor(mat, bgra, ColorConversionCodes.GRAY2BGRA); break;
            }

            int width = bgra.Cols;
            int height = bgra.Rows;
            int stride = width * 4; // BGRA32

            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                lock (_wbLock)
                {
                    if (LiveBitmap == null || LiveBitmap.PixelWidth != width || LiveBitmap.PixelHeight != height)
                    {
                        LiveBitmap = new WriteableBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null);
                    }

                    // unsafe不要：WritePixels(IntPtr, size, stride)
                    LiveBitmap.Lock();
                    try
                    {
                        var rect = new System.Windows.Int32Rect(0, 0, width, height);
                        LiveBitmap.WritePixels(rect, bgra.Data, height * stride, stride);
                    }
                    finally
                    {
                        LiveBitmap.Unlock();
                    }
                }
            }, DispatcherPriority.Render);
        }
        finally
        {
            // FrameArrivedで渡されたCloneは受け取り側でDispose
            mat.Dispose();
        }
    }

    // ---- FPS計測（単発：現在のUI設定で5〜35秒） ----
    private async Task MeasureLiveFpsWindowAsync()
    {
        try
        {
            IsBusy = true;

            var cam = AsConcreteCamera() ?? throw new InvalidOperationException(
                "ライブビュー計測は OpenCvCameraService 実装でサポートされています。");

            await cam.OpenAsync(DeviceId);

            var lv = new OpenCvCameraService.LiveViewSettings(
                Width: CaptureWidth,
                Height: CaptureHeight,
                TargetFps: CaptureFps,
                RotateAngle: LiveRotateAngle,
                FlipHorizontal: LiveFlipHorizontal,
                FlipVertical: LiveFlipVertical,
                PixelFormatFourCC: LivePixelFormatFourCC,
                ConvertRgb: LiveConvertRgb
            );

            var saved = cam.MeasureLiveFpsWindowAndSaveCsv(lv, MeasureOutputFolder);
            Debug.WriteLine($"Saved: {saved}");

            // ★ 追加：保存CSVから統計を読み、設定FPSと実測FPSを並べてログ出力
            var stat = SummarizeFpsCsv(saved);
            Debug.WriteLine($"[MEASURE-ONE] tfps={CaptureFps:0.##}, avg={stat.Avg:F2}, p50={stat.P50:F2}, p90={stat.P90:F2}, p99={stat.P99:F2}, min={stat.Min:F2}, max={stat.Max:F2}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MeasureLiveFpsWindow] Error: {ex}");
            // TODO: ユーザー通知
        }
        finally
        {
            try { await _camera.CloseAsync(); } catch { /* ignore */ }
            IsBusy = false;
        }
    }

    // ---- FPS計測（一括：PixelFormat × Angle × TargetFps を総当たり＋サマリCSV） ----
    private async Task MeasureLiveFpsBatchAsync()
    {
        try
        {
            IsBusy = true;

            var cam = AsConcreteCamera() ?? throw new InvalidOperationException(
                "ライブビュー計測は OpenCvCameraService 実装でサポートされています。");

            await cam.OpenAsync(DeviceId);

            Directory.CreateDirectory(MeasureOutputFolder);
            var batchStamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var summaryPath = Path.Combine(MeasureOutputFolder, $"summary_{batchStamp}.csv");
            File.WriteAllText(summaryPath,
                "PixelFormat,Angle,TargetFps,Width,Height,Samples,Avg,P50,P90,P99,Min,Max,SrcCsv\n");

            foreach (var pf in MeasurePixelFormats)
            {
                foreach (var angle in MeasureAngles)
                {
                    foreach (var tfps in MeasureTargetFpsList)
                    {
                        var lv = new OpenCvCameraService.LiveViewSettings(
                            Width: CaptureWidth,
                            Height: CaptureHeight,
                            TargetFps: tfps,
                            RotateAngle: angle,
                            FlipHorizontal: LiveFlipHorizontal,
                            FlipVertical: LiveFlipVertical,
                            PixelFormatFourCC: pf,
                            ConvertRgb: LiveConvertRgb
                        );

                        string savedCsv;
                        try
                        {
                            savedCsv = cam.MeasureLiveFpsWindowAndSaveCsv(lv, MeasureOutputFolder);
                            Debug.WriteLine($"Saved: {savedCsv}");
                        }
                        catch (Exception ex)
                        {
                            // 非対応FourCCやFPS設定失敗はスキップ
                            Debug.WriteLine($"[MeasureLiveFpsBatch] Skip pf={pf}, angle={angle}, fps={tfps} : {ex.Message}");
                            continue;
                        }

                        try
                        {
                            var stat = SummarizeFpsCsv(savedCsv);
                            File.AppendAllText(summaryPath,
                                $"{pf},{angle},{tfps:0.##},{CaptureWidth},{CaptureHeight},{stat.Count},{stat.Avg:F3},{stat.P50:F3},{stat.P90:F3},{stat.P99:F3},{stat.Min:F3},{stat.Max:F3},{savedCsv}\n");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[MeasureLiveFpsBatch] Summarize error for {savedCsv}: {ex.Message}");
                        }
                    }
                }
            }

            Debug.WriteLine($"Summary: {summaryPath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MeasureLiveFpsBatch] Error: {ex}");
            // TODO: ユーザー通知
        }
        finally
        {
            try { await _camera.CloseAsync(); } catch { /* ignore */ }
            IsBusy = false;
        }
    }

    // ---- 単一CSVの統計（Avg/P50/P90/P99/Min/Max） ----
    private static (int Count, double Avg, double P50, double P90, double P99, double Min, double Max)
        SummarizeFpsCsv(string path)
    {
        // ヘッダ: Timestamp,FPS,Width,Height,RotateAngle,FlipH,FlipV,TargetFps
        var values = File.ReadLines(path)
            .Skip(1) // ヘッダ
            .Select(line =>
            {
                var cols = line.Split(',');
                return cols.Length >= 2 && double.TryParse(cols[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                    ? (double?)v : null;
            })
            .Where(v => v.HasValue)
            .Select(v => v!.Value)
            .ToList();

        if (values.Count == 0) return (0, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN);

        values.Sort();
        double Avg(IEnumerable<double> xs) => xs.Average();
        double Px(IList<double> xs, double q)
        {
            var idx = (int)Math.Clamp(Math.Round((xs.Count - 1) * q), 0, xs.Count - 1);
            return xs[idx];
        }

        return (
            Count: values.Count,
            Avg: Avg(values),
            P50: Px(values, 0.5),
            P90: Px(values, 0.9),
            P99: Px(values, 0.99),
            Min: values.First(),
            Max: values.Last()
        );
    }
}
