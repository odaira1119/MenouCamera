using System;

namespace MenouCamera.Models.Core;

/// <summary>
/// 撮像条件
/// </summary>
public sealed class CaptureSettings : IEquatable<CaptureSettings>
{
    /// <summary>フレームレート比較時の許容誤差</summary>
    private const double FrameRateEpsilon = 1e-9;

    /// <summary>許容する回転角（0/90/180/270）</summary>
    private static readonly int[] AllowedAngles = new[] { 0, 90, 180, 270 };

    /// <summary>使用するカメラのデバイス ID</summary>
    public string DeviceId { get; }

    /// <summary>撮像解像度の幅</summary>
    public int Width { get; }

    /// <summary>撮像解像度の高さ</summary>
    public int Height { get; }

    /// <summary>フレームレート（fps）</summary>
    public double FrameRate { get; }

    /// <summary>ピクセルフォーマット</summary>
    public string PixelFormat { get; }

    /// <summary>回転角（0/90/180/270）</summary>
    public int RotateAngle { get; }

    /// <summary>水平反転</summary>
    public bool FlipHorizontal { get; }

    /// <summary>垂直反転</summary>
    public bool FlipVertical { get; }

    /// <summary>
    /// 代表的な初期値（1280x720, 30fps, "Bgr32", 回転なし/反転なし）で設定を生成。
    /// </summary>
    public static CaptureSettings Default(string deviceId) =>
        new(deviceId, width: 1280, height: 720, frameRate: 30.0, pixelFormat: "Bgr32",
            rotateAngle: 0, flipHorizontal: false, flipVertical: false);

    /// <summary>
    /// コンストラクタ。すべての項目を明示し、インスタンスは不変となる。
    /// </summary>
    /// <param name="deviceId">使用するカメラのデバイス ID（必須）。</param>
    /// <param name="width">撮像幅（px, &gt; 0）。</param>
    /// <param name="height">撮像高さ（px, &gt; 0）。</param>
    /// <param name="frameRate">フレームレート（fps, &gt; 0）。</param>
    /// <param name="pixelFormat">ピクセルフォーマットのヒント（必須）。</param>
    /// <param name="rotateAngle">回転角（0/90/180/270）。</param>
    /// <param name="flipHorizontal">水平反転するか。</param>
    /// <param name="flipVertical">垂直反転するか。</param>
    /// <exception cref="ArgumentException"><paramref name="deviceId"/> または <paramref name="pixelFormat"/> が空または空白。</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> または <paramref name="height"/> または <paramref name="frameRate"/> が 0 以下、または <paramref name="rotateAngle"/> が許容外。</exception>
    public CaptureSettings(
        string deviceId,
        int width,
        int height,
        double frameRate,
        string pixelFormat,
        int rotateAngle = 0,
        bool flipHorizontal = false,
        bool flipVertical = false)
    {
        if (string.IsNullOrWhiteSpace(deviceId))
            throw new ArgumentException("DeviceId must not be null or empty.", nameof(deviceId));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Width must be > 0.");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Height must be > 0.");
        if (frameRate <= 0) throw new ArgumentOutOfRangeException(nameof(frameRate), "FrameRate must be > 0.");
        if (string.IsNullOrWhiteSpace(pixelFormat))
            throw new ArgumentException("PixelFormat must not be null or empty.", nameof(pixelFormat));
        if (Array.IndexOf(AllowedAngles, rotateAngle) < 0)
            throw new ArgumentOutOfRangeException(nameof(rotateAngle), "RotateAngle must be 0, 90, 180, or 270.");

        DeviceId = deviceId.Trim();
        Width = width;
        Height = height;
        FrameRate = frameRate;
        PixelFormat = pixelFormat.Trim();
        RotateAngle = rotateAngle;
        FlipHorizontal = flipHorizontal;
        FlipVertical = flipVertical;
    }

    /// <summary>
    /// 一部の値だけ差し替えた新しい設定を作る（不変オブジェクトのための With パターン）。
    /// </summary>
    public CaptureSettings With(
        string? deviceId = null,
        int? width = null,
        int? height = null,
        double? frameRate = null,
        string? pixelFormat = null,
        int? rotateAngle = null,
        bool? flipHorizontal = null,
        bool? flipVertical = null)
    {
        var nextAngle = rotateAngle ?? RotateAngle;
        if (Array.IndexOf(AllowedAngles, nextAngle) < 0)
            throw new ArgumentOutOfRangeException(nameof(rotateAngle), "RotateAngle must be 0, 90, 180, or 270.");

        return new CaptureSettings(
            deviceId: deviceId ?? DeviceId,
            width: width ?? Width,
            height: height ?? Height,
            frameRate: frameRate ?? FrameRate,
            pixelFormat: pixelFormat ?? PixelFormat,
            rotateAngle: nextAngle,
            flipHorizontal: flipHorizontal ?? FlipHorizontal,
            flipVertical: flipVertical ?? FlipVertical
        );
    }

    /// <summary>値等価：全プロパティが一致すれば等価（FrameRate は微小誤差を許容）。</summary>
    public bool Equals(CaptureSettings? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return string.Equals(DeviceId, other.DeviceId, StringComparison.Ordinal)
            && Width == other.Width
            && Height == other.Height
            && Math.Abs(FrameRate - other.FrameRate) < FrameRateEpsilon
            && string.Equals(PixelFormat, other.PixelFormat, StringComparison.Ordinal)
            && RotateAngle == other.RotateAngle
            && FlipHorizontal == other.FlipHorizontal
            && FlipVertical == other.FlipVertical;
    }

    public override bool Equals(object? obj) => Equals(obj as CaptureSettings);

    public override int GetHashCode()
        => HashCode.Combine(
            DeviceId,
            Width,
            Height,
            Math.Round(FrameRate / FrameRateEpsilon),
            PixelFormat,
            RotateAngle,
            FlipHorizontal,
            FlipVertical
        );

    public override string ToString()
        => $"{DeviceId} {Width}x{Height} @{FrameRate:0.##}fps {PixelFormat} rot={RotateAngle} flip(H:{FlipHorizontal},V:{FlipVertical})";
}
