using System.Windows.Input;

namespace ImageMerger.Helpers;

/// <summary>
/// Cài đặt <see cref="ICommand"/> đơn giản: bọc một <see cref="Action"/> để bind vào <c>Button.Command</c>.
///
/// ICommand gồm ba thành viên:
/// - <c>Execute</c>: việc cần làm khi bấm.
/// - <c>CanExecute</c>: trả false thì Button/MenuFlyoutItem tự mờ đi.
/// - <c>CanExecuteChanged</c>: sự kiện báo control hỏi lại CanExecute. Ta gọi
///   <see cref="NotifyCanExecuteChanged"/> mỗi khi điều kiện có thể đã đổi.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (CanExecute(parameter)) _execute();
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}

/// <summary>
/// Phiên bản async của <see cref="RelayCommand"/>. Trong lúc task đang chạy, <c>CanExecute</c> trả false,
/// nên người dùng không bấm lặp được (ví dụ mở hai File Picker cùng lúc).
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_isRunning && (_canExecute?.Invoke() ?? true);

    /// <summary>
    /// ICommand.Execute trả về void nên bắt buộc là <c>async void</c>. Code muốn chờ command chạy xong
    /// thì gọi <see cref="ExecuteAsync"/>.
    /// </summary>
    public async void Execute(object? parameter) => await ExecuteAsync();

    public async Task ExecuteAsync()
    {
        if (!CanExecute(null)) return;

        _isRunning = true;
        NotifyCanExecuteChanged();
        try
        {
            await _execute();
        }
        finally
        {
            _isRunning = false;
            NotifyCanExecuteChanged();
        }
    }

    public void NotifyCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
