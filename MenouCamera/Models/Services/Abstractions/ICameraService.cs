using MenouCamera.Models.Core;

namespace MenouCamera.Models.Services.Abstractions;

/// <summary>
/// USB カメラの撮像インターフェース
/// </summary>
public interface ICameraService
{
    /// <summary>
    /// 指定したデバイス ID のカメラをオープンし、撮像できる状態に初期化する
    /// </summary>
    /// <param name="deviceId">デバイス識別子</param>
    /// <param name="cancelToken">キャンセルトークン</param>
    /// <returns>非同期操作</returns>
    /// <exception cref="System.InvalidOperationException">デバイスを開けない場合</exception>
    Task OpenAsync(string deviceId, CancellationToken cancelToken = default);

    /// <summary>
    /// 指定の撮像設定で 1 フレームを取得し、エンコード済み画像（PNG/JPEG 等）として返す。
    /// </summary>
    /// <param name="settings">撮像設定</param>
    /// <param name="cancelToken">キャンセルトークン</param>
    /// <returns>取得したフレームを表す <see cref="ImageData"/>。</returns>
    /// <exception cref="System.InvalidOperationException">未オープン、もしくは取得失敗の場合</exception>
    Task<ImageData> CaptureAsync(CaptureSettings settings, CancellationToken cancelToken = default);

    /// <summary>
    /// デバイスをクローズし、ネイティブリソースを解放する
    /// </summary>
    /// <returns>非同期操作</returns>
    Task CloseAsync();
}
