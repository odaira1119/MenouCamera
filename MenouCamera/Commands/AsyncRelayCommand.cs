using System.Windows.Input;

namespace MenouCamera.Commands;

/// <summary>
/// 非同期処理向けの <see cref="ICommand"/> 実装。<br/>
/// - 多重実行を抑止（実行中は <see cref="CanExecute(object)"/> が <c>false</c>）<br/>
/// - 例外はここで捕捉し、必要に応じてハンドラへ通知（UI スレッドの落下防止）<br/>
/// - ViewModel 側のメソッドを薄く橋渡しするだけ（ビジネスロジックは VM 側に集約）
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    /// <summary>実行本体（非同期）。</summary>
    private readonly Func<object?, Task> _executeAsync;

    /// <summary>実行可否の判定デリゲート。</summary>
    private readonly Func<bool>? _canExecute;

    /// <summary>例外通知用のハンドラ。</summary>
    private readonly Action<Exception>? _onException;

    /// <summary>実行中フラグ（多重実行抑止）。</summary>
    private bool _isExecuting;

    /// <summary>
    /// パラメータなしの非同期デリゲートを受け取るコンストラクタ。
    /// </summary>
    /// <param name="executeAsync">非同期で実行する処理（VM メソッドを渡す）。</param>
    /// <param name="canExecute">実行可否（省略可）。実行中は自動的に <c>false</c> になる。</param>
    /// <param name="onException">例外ハンドラ（省略可）。未指定なら例外は握りつぶす。</param>
    public AsyncRelayCommand(
        Func<Task> executeAsync,
        Func<bool>? canExecute = null,
        Action<Exception>? onException = null)
        : this(_ => executeAsync(), canExecute, onException)
    { }

    /// <summary>
    /// コマンドパラメータ付きの非同期デリゲートを受け取るコンストラクタ。
    /// </summary>
    /// <param name="executeAsync">コマンドパラメータ付きの非同期処理。</param>
    /// <param name="canExecute">実行可否（省略可）。実行中は自動的に <c>false</c> になる。</param>
    /// <param name="onException">例外ハンドラ（省略可）。未指定なら例外は握りつぶす。</param>
    /// <exception cref="ArgumentNullException"><paramref name="executeAsync"/> が <c>null</c>。</exception>
    public AsyncRelayCommand(
        Func<object?, Task> executeAsync,
        Func<bool>? canExecute = null,
        Action<Exception>? onException = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
        _onException = onException;
    }

    /// <summary>
    /// コマンドが現在実行可能かどうかを示す。
    /// </summary>
    /// <param name="parameter">コマンドパラメータ（未使用のことが多い）。</param>
    public bool CanExecute(object? parameter)
        => !_isExecuting && (_canExecute?.Invoke() ?? true);

    /// <summary>
    /// コマンドの実行
    /// </summary>
    /// <param name="parameter">コマンドパラメータ。</param>
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        try
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            await _executeAsync(parameter);
        }
        catch (Exception ex)
        {
            _onException?.Invoke(ex);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    /// <summary>
    /// XAML の <see cref="CommandManager"/> と連動して <see cref="CanExecute(object)"/> 再評価を促すイベント。
    /// </summary>
    public event EventHandler? CanExecuteChanged
    {
        add { CommandManager.RequerySuggested += value; }
        remove { CommandManager.RequerySuggested -= value; }
    }

    /// <summary>
    /// 明示的に <see cref="CanExecute(object)"/> の再評価を通知（VM から呼ぶ）。
    /// </summary>
    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
