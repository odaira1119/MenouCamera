using System.Globalization;

using MenouCamera.Utils;
using MenouCamera.Models.Core;
using MenouCamera.Models.Services.Abstractions;

using OpenCvSharp;

namespace MenouCamera.Models.Services.Implementations;

/// <summary>
/// OpenCvSharp による USB カメラ実装
/// </summary>
public sealed class OpenCvCameraService : ICameraService, IDisposable
{
    /// <summary>
    /// OpenCV の <see cref="VideoCapture"/>
    /// </summary>
    private VideoCapture? _capture;

    /// <summary>
    /// デバイスがオープン済みかどうか
    /// </summary>
    private bool _isOpened;

    /// <summary>
    /// デバイスをオープンし、撮像可能な状態に初期化する
    /// </summary>
    /// <param name="deviceId">カメラ識別子</param>
    /// <param name="cancelToken">キャンセルトークン</param>
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
    /// 指定した撮像設定で 1 フレームを取得し、PNG エンコード済みの <see cref="ImageData"/> を返す。
    /// 初回の古いバッファを避けるため、数フレームの「捨て読み」（ウォームアップ）を行う。
    /// </summary>
    /// <param name="settings">撮像設定。</param>
    /// <param name="cancelToken">キャンセルトークン</param>
    /// <returns>取得したフレームを表す <see cref="ImageData"/>。</returns>
    public async Task<ImageData> CaptureAsync(CaptureSettings settings, CancellationToken cancelToken = default)
    {
        if (!_isOpened || _capture is null)
            throw new InvalidOperationException("Camera is not opened.");

        return await Task.Run(() =>
        {
            cancelToken.ThrowIfCancellationRequested();

            _capture.Set(VideoCaptureProperties.FrameWidth, settings.Width);
            _capture.Set(VideoCaptureProperties.FrameHeight, settings.Height);
            _capture.Set(VideoCaptureProperties.Fps, settings.FrameRate);

            using var tmp = new Mat();

            for (int i = 0; i < 3; i++)
            {
                if (cancelToken.IsCancellationRequested) cancelToken.ThrowIfCancellationRequested();
                _capture.Read(tmp);
            }

            using var frame = new Mat();
            if (!_capture.Read(frame) || frame.Empty())
                throw new InvalidOperationException("Failed to capture a frame.");

            var targetFormat = ImageFormat.Png;
            var ext = "." + targetFormat.ToExtension();

            Cv2.ImEncode(ext, frame, out var buf);
            var bytes = buf.ToArray();

            return new ImageData(
                bytes: bytes,
                width: frame.Width,
                height: frame.Height,
                pixelFormat: "Bgr24",
                format: targetFormat
            );
        }, cancelToken).ConfigureAwait(false);
    }

    /// <summary>
    /// デバイスをクローズし、ネイティブ リソースを解放する。
    /// </summary>
    public async Task CloseAsync()
    {
        await Task.Run(() =>
        {
            _isOpened = false;
            _capture?.Release();
            _capture?.Dispose();
            _capture = null;
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// 破棄処理。<see cref="CloseAsync"/> 相当の解放を同期的に行う。
    /// </summary>
    public void Dispose()
    {
        try
        {
            _capture?.Release();
        }
        finally
        {
            _capture?.Dispose();
            _capture = null;
        }
        GC.SuppressFinalize(this);
    }
}
