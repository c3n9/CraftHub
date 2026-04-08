using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using CraftHub.Domain.Models;
using CraftHub.Models;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CraftHub.Views;

public partial class ProgressDialogView : Window, INotifyPropertyChanged
{
    private string _titleText = "Updating CraftHub";
    private string _messageText = "Preparing update...";
    private int _progressValue = 0;
    private bool _isIndeterminate = true;
    private string _statusText = "Starting...";
    private bool _isCancelEnabled = true;
    private bool _isCanceled = false;
    private bool _isFinished = false;
    private CancellationTokenSource? _cts;

    public ProgressDialogView()
    {
        InitializeComponent();
        DataContext = this;

        CancelButton.Click += (_, _) => Cancel();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public string TitleText
    {
        get => _titleText;
        set
        {
            _titleText = value;
            OnPropertyChanged();
        }
    }

    public string MessageText
    {
        get => _messageText;
        set
        {
            _messageText = value;
            OnPropertyChanged();
        }
    }

    public int ProgressValue
    {
        get => _progressValue;
        set
        {
            _progressValue = value;
            OnPropertyChanged();
        }
    }

    public bool IsIndeterminate
    {
        get => _isIndeterminate;
        set
        {
            _isIndeterminate = value;
            OnPropertyChanged();
        }
    }

    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public bool IsCancelEnabled
    {
        get => _isCancelEnabled;
        set
        {
            _isCancelEnabled = value;
            OnPropertyChanged();
        }
    }

    public bool IsCanceled => _isCanceled;

    private void Cancel()
    {
        if (_isFinished)
        {
            Close();
            return;
        }

        _isCanceled = true;
        _cts?.Cancel();

        StatusText = "Cancelling...";
        IsCancelEnabled = true;
    }

    public async Task<T?> RunWithProgress<T>(Func<IProgress<UpdateProgress>, CancellationToken, Task<T>> task)
    {
        _cts = new CancellationTokenSource();
        T? result = default;

        var progress = new Progress<UpdateProgress>(p =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (p.PercentComplete >= 0)
                {
                    IsIndeterminate = false;
                    ProgressValue = p.PercentComplete;
                }

                if (!string.IsNullOrEmpty(p.Status))
                    StatusText = p.Status;

                if (!string.IsNullOrEmpty(p.Message))
                    MessageText = p.Message;

                if (p.IsIndeterminate)
                    IsIndeterminate = true;
            });
        });

        try
        {
            result = await task(progress, _cts.Token);

            Dispatcher.UIThread.Post(() =>
            {
                _isFinished = true;

                IsIndeterminate = false;
                ProgressValue = 100;
                StatusText = "Complete!";
                MessageText = "Operation completed successfully!";
                IsCancelEnabled = true;
                CancelButton.Content = "Close";
                CancelButton.IsEnabled = true;
            });

            return result;
        }
        catch (OperationCanceledException)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _isFinished = true;

                StatusText = "Cancelled";
                MessageText = "Operation was cancelled";
                IsCancelEnabled = true;
                CancelButton.Content = "Close";
            });
            throw;
        }
        catch (Exception ex)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _isFinished = true;

                IsIndeterminate = false;
                StatusText = $"Error: {ex.Message}";
                MessageText = "Operation failed";
                IsCancelEnabled = true;
                CancelButton.Content = "Close";
            });
            throw;
        }
    }

    public async Task RunWithProgress(Func<IProgress<UpdateProgress>, CancellationToken, Task> task)
    {
        await RunWithProgress(async (progress, token) =>
        {
            await task(progress, token);
            return true;
        });
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        CancelButton.Focus();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        _cts?.Cancel();
    }
}
