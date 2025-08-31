using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MenouCamera.ViewModels;

/// <summary>
/// <see cref="INotifyPropertyChanged"/> の最小基底実装
/// - プロパティ変更通知の共通化（<see cref="OnPropertyChanged(string)"/>）
/// - 値の変更時のみ通知するヘルパ（<see cref="SetProperty{T}(ref T, T, string?, Action?)"/>）
/// </summary>
public abstract class ViewModelBase : INotifyPropertyChanged
{
    /// <summary>
    /// プロパティ変更時に発火するイベント
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// プロパティ変更通知を発行する
    /// </summary>
    /// <param name="propertyName">変更されたプロパティ名（省略可）。</param>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>
    /// 値が変化したときのみフィールドを更新し、変更通知を発行するヘルパ。
    /// 変更がない場合は何もしない。
    /// </summary>
    /// <typeparam name="T">プロパティの型。</typeparam>
    /// <param name="storage">実体フィールド（ref）。</param>
    /// <param name="value">新しい値。</param>
    /// <param name="propertyName">プロパティ名</param>
    /// <param name="onChanged">値更新後に実行する追加アクション</param>
    /// <returns>値を更新して通知した場合は <see langword="true"/>。変更がなければ <see langword="false"/>。</returns>
    protected bool SetProperty<T>(
        ref T storage,
        T value,
        [CallerMemberName] string? propertyName = null,
        Action? onChanged = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
            return false;

        storage = value;
        onChanged?.Invoke();
        OnPropertyChanged(propertyName);
        return true;
    }
}
