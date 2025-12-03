using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MenouCamera.Models.Core;
using MenouCamera.Models.Services.Abstractions;
using MenouCamera.Utils;
using OpenCvSharp;

namespace MenouCamera.Models.Services.Implementations;

/// <summary>
/// OpenCvSharp による USB カメラ実装（静止画 + ライブビュー）
/// </summary>
public sealed class OpenCvCameraService : ICameraService, IDisposable
{
    private VideoCapture? _capture;
    private bool _isOpened;

    // --- ライブビュー用 ---
    private Task? _liveTask;
    private CancellationTokenSource? _liveCts;
    private volatile bool _isLive;
    private readonly object _latestLock = new();
    private Mat? _latest;              // サービス所有。呼び出し側はCopyToで取得する
    private long _seq;
    private readonly Stopwatch _fpsSw = new();
    private int _fpsFrames;
    private double _latestFps;

    /// <summary>ライブ中に1秒ごとに通知されるFPS</summary>
    public double LatestFps => _latestFps;

    /// <summary>フレーム到着イベント（CloneしたMatを渡す。受け取り側がDispose）</summary>
    public event Action<Mat, DateTime, long>? FrameArrived;

    /// <summary>FPS更新イベント（1秒ごと）</summary>
    public event Action<double>? FpsUpdated;

    public async Task OpenAsync(string deviceId, CancellationToken cancelToken = default)
    {
        await Task.Run(() =>
        {
            cancelToken.ThrowIfCancellationRequested();

            int index = 0;
            if (!string.IsNullOrWhiteSpace(deviceId) &&
                int.TryParse(deviceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                index = parsed;
            }

            _capture?.Release();
            _capture?.Dispose();

            _capture = new VideoCapture(index, VideoCaptureAPIs.ANY);
            if (!_capture.IsOpened())
            {
                _capture?.Dispose();
                _capture = null;
                throw new InvalidOperationException($"Camera open failed. device index={index}");
            }

            _isOpened = true;
        }, cancelToken).ConfigureAwait(false);
    }

    /// <summary>
    /// PNGで1枚取得（回転/反転対応＋計測ログ）
    /// </summary>
    public async Task<ImageData> CaptureAsync(CaptureSettings settings, CancellationToken cancelToken = default)
    {
        if (!_isOpened || _capture is null)
            throw new InvalidOperationException("Camera is not opened.");

        return await Task.Run(() =>
        {
            cancelToken.ThrowIfCancellationRequested();

            // カメラ設定
            _capture.Set(VideoCaptureProperties.FrameWidth, settings.Width);
            _capture.Set(VideoCaptureProperties.FrameHeight, settings.Height);
            _capture.Set(VideoCaptureProperties.Fps, settings.FrameRate);

            using var tmp = new Mat();

            // ウォームアップ（古いバッファ回避）
            for (int i = 0; i < 3; i++)
            {
                if (cancelToken.IsCancellationRequested) cancelToken.ThrowIfCancellationRequested();
                _capture.Read(tmp);
            }

            // 計測開始（単発キャプチャの実効FPS=1/取得時間）
            var sw = Stopwatch.StartNew();

            using var frame = new Mat();
            if (!_capture.Read(frame) || frame.Empty())
                throw new InvalidOperationException("Failed to capture a frame.");

            using var rot = new Mat();
            Mat view = frame;

            // 回転（90/180/270のみ高速パス）
            if (settings.RotateAngle is 90 or 180 or 270)
            {
                RotateRightAngle(frame, settings.RotateAngle, rot);
                view = rot;
            }

            // 反転（必要時のみ、1回に集約）
            if (settings.FlipHorizontal || settings.FlipVertical)
            {
                var flipCode =
                    (settings.FlipHorizontal && settings.FlipVertical) ? FlipMode.XY :
                    settings.FlipHorizontal ? FlipMode.Y :
                    FlipMode.X;
                Cv2.Flip(view, rot, flipCode);
                view = rot;
            }

            sw.Stop();
            var seconds = Math.Max(sw.Elapsed.TotalSeconds, 1e-9);
            var effectiveFps = 1.0 / seconds;
            Debug.WriteLine($"[Capture] {settings.Width}x{settings.Height}@{settings.FrameRate:F2} rotated={settings.RotateAngle} flip(H:{settings.FlipHorizontal},V:{settings.FlipVertical}) -> {effectiveFps:F2} fps (elapsed {seconds * 1000:F1} ms)");

            // エンコード
            var targetFormat = ImageFormat.Png;
            var ext = "." + targetFormat.ToExtension();

            Cv2.ImEncode(ext, view, out var buf);
            var bytes = buf.ToArray();

            return new ImageData(
                bytes: bytes,
                width: view.Width,
                height: view.Height,
                pixelFormat: "Bgr24",
                format: targetFormat
            );
        }, cancelToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 計測用：現在の設定で1枚の生Matを取得（回転/反転対応・呼び出し側でDispose）
    /// </summary>
    public Mat CaptureOneMat(
        int width,
        int height,
        double fps,
        int rotateAngle = 0,
        bool flipHorizontal = false,
        bool flipVertical = false,
        CancellationToken cancelToken = default)
    {
        if (!_isOpened || _capture is null)
            throw new InvalidOperationException("Camera is not opened.");

        _capture.Set(VideoCaptureProperties.FrameWidth, width);
        _capture.Set(VideoCaptureProperties.FrameHeight, height);
        _capture.Set(VideoCaptureProperties.Fps, fps);

        using var warm = new Mat();
        var deadline = Stopwatch.StartNew();
        int warmFrames = 0;
        while (deadline.Elapsed.TotalSeconds < 2.0 && warmFrames < 30)
        {
            if (cancelToken.IsCancellationRequested) cancelToken.ThrowIfCancellationRequested();
            _capture.Read(warm);
            warmFrames++;
        }

        var sw = Stopwatch.StartNew();

        var frame = new Mat();
        if (!_capture.Read(frame) || frame.Empty())
            throw new InvalidOperationException("Failed to capture a frame.");

        Mat src = frame;
        Mat? toDispose = null;

        // 回転（90/180/270）
        if (rotateAngle is 90 or 180 or 270)
        {
            var dst = new Mat();
            RotateRightAngle(src, rotateAngle, dst);
            toDispose = src != frame ? src : toDispose;
            src = dst;
        }

        // 反転
        if (flipHorizontal || flipVertical)
        {
            var flipCode =
                (flipHorizontal && flipVertical) ? FlipMode.XY :
                flipHorizontal ? FlipMode.Y :
                FlipMode.X;
            var dst = new Mat();
            Cv2.Flip(src, dst, flipCode);
            toDispose = src != frame ? src : toDispose;
            src = dst;
        }

        sw.Stop();
        var seconds = Math.Max(sw.Elapsed.TotalSeconds, 1e-9);
        var effectiveFps = 1.0 / seconds;
        Debug.WriteLine($"[CaptureOneMat] {width}x{height}@{fps:F2} rotated={rotateAngle} flip(H:{flipHorizontal},V:{flipVertical}) -> {effectiveFps:F2} fps (elapsed {seconds * 1000:F1} ms)");

        // 注意：加工が入った場合は src は新規Mat。呼び出し側所有にするため Clone() して返す。
        try
        {
            if (ReferenceEquals(src, frame))
                return frame;                  // 未加工: そのまま返す（呼び出し側がDispose）
            else
                return src.Clone();            // 加工済み: 独立クローンを返し、内部一時は破棄
        }
        finally
        {
            toDispose?.Dispose();
            if (!ReferenceEquals(src, frame)) src.Dispose(); // Clone済みなので作業バッファは破棄
        }
    }


    // =========================
    // ライブビュー API
    // =========================

    public sealed record LiveViewSettings(
        int Width,
        int Height,
        double TargetFps,
        int RotateAngle = 0,   // 0,90,180,270
        bool FlipHorizontal = false,
        bool FlipVertical = false,
        string? PixelFormatFourCC = null,  // "MJPG","YUY2","NV12","H264" など
        bool ConvertRgb = true             // true=OpenCVでBGR化（既定）
        );

    /// <summary>ライブビュー中か</summary>
    public bool IsLive => _isLive;

    private FpsLogger? _fpsLogger;

    /// <summary>
    /// ライブビュー開始：内部スレッドでReadし続け、最新フレームを保持＋イベント通知
    /// </summary>
    public Task StartLiveAsync(LiveViewSettings settings, CancellationToken cancelToken = default)
    {
        if (!_isOpened || _capture is null)
            throw new InvalidOperationException("Camera is not opened.");
        if (_isLive) return Task.CompletedTask;

        // 既存のループがあれば停止
        StopLive();

        _liveCts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
        var ct = _liveCts.Token;

        // 設定
        _capture.Set(VideoCaptureProperties.FrameWidth, settings.Width);
        _capture.Set(VideoCaptureProperties.FrameHeight, settings.Height);
        _capture.Set(VideoCaptureProperties.Fps, settings.TargetFps);

        // ★ FourCC 指定があれば適用
        if (!string.IsNullOrWhiteSpace(settings.PixelFormatFourCC))
        {
            var fourcc = FourCC.FromString(settings.PixelFormatFourCC.Trim().ToUpperInvariant());
            _capture.Set(VideoCaptureProperties.FourCC, fourcc);
        }
        // ★ ConvertRgb（BGR化）の指定
        _capture.Set(VideoCaptureProperties.ConvertRgb, settings.ConvertRgb ? 1 : 0);

        // 実際に適用された FourCC をログ出力
        try
        {
            var applied = (int)_capture.Get(VideoCaptureProperties.FourCC);
            Debug.WriteLine($"[Live] FourCC={FourCCToString(applied)}, ConvertRgb={settings.ConvertRgb}");
        }
        catch { /* 一部バックエンドでは取得できない */ }

        // ウォームアップ軽く
        using (var warm = new Mat())
        {
            for (int i = 0; i < 3; i++) _capture.Read(warm);
        }

        _isLive = true;
        _seq = 0;
        _fpsFrames = 0;
        _latestFps = 0;
        _fpsSw.Restart();

        //_fpsLogger = new FpsLogger(/* 必要なら "C:\\logs\\live_fps.csv" など */);

        _liveTask = Task.Run(() =>
        {
            using var work = new Mat();
            using var rot = new Mat();      // 回転/反転用の作業バッファ

            while (!ct.IsCancellationRequested)
            {
                if (!_capture.Read(work) || work.Empty())
                    continue;

                Mat view = work;

                // 回転（90/180/270のみ最速のRotateを使用）
                if (settings.RotateAngle is 90 or 180 or 270)
                {
                    RotateRightAngle(work, settings.RotateAngle, rot);
                    view = rot;
                }

                // 反転（必要時のみ・1回で済ませる）
                if (settings.FlipHorizontal || settings.FlipVertical)
                {
                    var flipCode =
                        (settings.FlipHorizontal && settings.FlipVertical) ? FlipMode.XY :
                        settings.FlipHorizontal ? FlipMode.Y :
                        FlipMode.X;

                    Cv2.Flip(view, rot, flipCode);
                    view = rot;
                }

                var ts = DateTime.Now;
                var seq = Interlocked.Increment(ref _seq);

                // 最新フレームを保持（コピーで上書き、アロケーション抑制）
                lock (_latestLock)
                {
                    _latest ??= new Mat();
                    view.CopyTo(_latest);
                }

                // 必要ならイベントでPush（Cloneして渡す＝受け取り側がDispose）
                if (FrameArrived is not null)
                {
                    var clone = view.Clone();
                    try { FrameArrived?.Invoke(clone, ts, seq); }
                    catch { clone.Dispose(); throw; }
                }

                // FPSカウント（1秒窓）
                _fpsFrames++;
                var sec = _fpsSw.Elapsed.TotalSeconds;
                if (sec >= 1.0)
                {
                    _latestFps = _fpsFrames / sec;
                    _fpsFrames = 0;
                    _fpsSw.Restart();
                    FpsUpdated?.Invoke(_latestFps);
                    _fpsLogger?.OnTick(_latestFps);
                }

                // 軽いスロットリング（必要なら）
                Thread.Yield();
            }
        }, ct);

        return Task.CompletedTask;
    }

    /// <summary>ライブビュー停止</summary>
    public void StopLive()
    {
        if (!_isLive) return;

        try
        {
            _liveCts?.Cancel();
            _liveTask?.Wait(200);
        }
        catch { /* キャンセル想定 */ }
        finally
        {
            _isLive = false;
            _liveTask = null;
            _liveCts?.Dispose();
            _liveCts = null;
            _fpsSw.Reset();

            // 終了時にCSV保存（パス設定していれば）
            _fpsLogger?.Dispose();
            _fpsLogger = null;
        }
    }

    /// <summary>
    /// 最新フレームを呼び出し側のMatへコピー（ゼロコピーはライフサイクルが難しいため安全優先）
    /// </summary>
    /// <returns>取得できたらtrue（未取得/未初期化時はfalse）</returns>
    public bool TryGetLatestFrame(Mat dst)
    {
        lock (_latestLock)
        {
            if (_latest is null || _latest.Empty())
                return false;
            _latest.CopyTo(dst);
            return true;
        }
    }

    /// <summary>
    /// 90/180/270度専用の回転（dstは呼び出し側が使い回し）
    /// </summary>
    public static void RotateRightAngle(Mat src, int angle, Mat dst)
    {
        RotateFlags flag;
        switch (angle)
        {
            case 90: flag = RotateFlags.Rotate90Clockwise; break;
            case 180: flag = RotateFlags.Rotate180; break;
            case 270: flag = RotateFlags.Rotate90Counterclockwise; break;
            default: throw new ArgumentOutOfRangeException(nameof(angle), "90/180/270のみ対応");
        }
        Cv2.Rotate(src, dst, flag);
    }

    public async Task CloseAsync()
    {
        await Task.Run(() =>
        {
            StopLive();
            _isOpened = false;
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;

            lock (_latestLock)
            {
                _latest?.Dispose();
                _latest = null;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// ライブビュー開始直後 5〜35 秒のFPSを収集し、CSVに保存してファイルパスを返す。
    /// 収集対象は 1秒ごとに発火する <see cref="FpsUpdated"/> の値。
    /// </summary>
    public string MeasureLiveFpsWindowAndSaveCsv(
        LiveViewSettings settings,
        string outputFolder,               // 例: @"D:\MyApp\計測"
        CancellationToken ct = default)
    {
        if (!_isOpened || _capture is null)
            throw new InvalidOperationException("Camera is not opened.");

        // ウィンドウ設定：開始直後5秒はウォームアップ、以降30秒間を計測＝合計35秒運転
        const double warmupSeconds = 5.0;
        const double measureSeconds = 30.0;
        var totalSeconds = warmupSeconds + measureSeconds;

        // 出力先準備
        System.IO.Directory.CreateDirectory(outputFolder);
        string FileSafe(bool b) => b ? "1" : "0";
        var pf = string.IsNullOrWhiteSpace(settings.PixelFormatFourCC) ? "AUTO" : settings.PixelFormatFourCC.Trim().ToUpperInvariant();
        var stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        // ★ ファイル名にピクセルフォーマット/ConvertRgbを含める
        var fileName =
            $"livefps_{settings.Width}x{settings.Height}_pf{pf}_conv{(settings.ConvertRgb ? 1 : 0)}_rot{settings.RotateAngle}_fh{FileSafe(settings.FlipHorizontal)}_fv{FileSafe(settings.FlipVertical)}_tfps{settings.TargetFps:0}_{stamp}.csv";
        var fullPath = System.IO.Path.Combine(outputFolder, fileName);

        // サンプル蓄積
        var samples = new System.Collections.Generic.List<(DateTime ts, double fps)>();
        var sw = Stopwatch.StartNew();

        void OnFps(double fps)
        {
            // 1秒ごとに呼ばれる想定。5〜35秒の間だけ採用。
            var t = sw.Elapsed.TotalSeconds;
            if (t >= warmupSeconds && t < totalSeconds)
            {
                samples.Add((DateTime.Now, fps));
            }
        }

        try
        {
            // 計測開始
            FpsUpdated += OnFps;
            StartLiveAsync(settings, ct);

            // 合計35秒回す（5秒捨て+30秒計測）
            while (sw.Elapsed.TotalSeconds < totalSeconds)
            {
                ct.ThrowIfCancellationRequested();
                Thread.Sleep(50);
            }
        }
        finally
        {
            // クリーンアップ
            try { StopLive(); } catch { /* ignore */ }
            FpsUpdated -= OnFps;
            sw.Stop();
        }

        // CSV書き出し（列はExcel貼り付け想定）
        using (var w = new System.IO.StreamWriter(fullPath, append: false))
        {
            w.WriteLine("Timestamp,FPS,Width,Height,PixelFormat,ConvertRgb,RotateAngle,FlipH,FlipV,TargetFps");
            foreach (var s in samples)
            {
                w.WriteLine($"{s.ts:yyyy-MM-dd HH:mm:ss.fff},{s.fps:F3},{settings.Width},{settings.Height},{pf},{(settings.ConvertRgb ? 1 : 0)},{settings.RotateAngle},{settings.FlipHorizontal},{settings.FlipVertical},{settings.TargetFps:0.##}");
            }
        }

        // 簡易ログ
        if (samples.Count > 0)
        {
            var arr = samples.Select(x => x.fps).OrderBy(x => x).ToArray();
            double P(double q) => arr[(int)Math.Clamp(Math.Round((arr.Length - 1) * q), 0, arr.Length - 1)];
            var avg = samples.Average(x => x.fps);
            Debug.WriteLine($"[MEASURE] pf={pf}, conv={(settings.ConvertRgb ? 1 : 0)}, rot={settings.RotateAngle}, tfps={settings.TargetFps:0} -> count={samples.Count}, avg={avg:F2}, p50={P(0.5):F2}, p90={P(0.9):F2}, p99={P(0.99):F2}");
        }

        return fullPath;
    }

    private static string FourCCToString(int v)
    {
        char c1 = (char)(v & 0xFF);
        char c2 = (char)((v >> 8) & 0xFF);
        char c3 = (char)((v >> 16) & 0xFF);
        char c4 = (char)((v >> 24) & 0xFF);
        return new string(new[] { c1, c2, c3, c4 });
    }

    public void Dispose()
    {
        try
        {
            StopLive();
            _capture?.Release();
        }
        finally
        {
            _capture?.Dispose();
            _capture = null;
            lock (_latestLock)
            {
                _latest?.Dispose();
                _latest = null;
            }
        }
        GC.SuppressFinalize(this);
    }
}
