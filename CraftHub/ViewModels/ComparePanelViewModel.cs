using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Core;
using CraftHub.Helpers;
using CraftHub.Models;

namespace CraftHub.ViewModels;

/// <summary>
/// One input pane of the JSON comparer: its text, live validity, and the actions that fill it
/// (open a file, paste, format) or reposition it among its siblings.
/// </summary>
public sealed partial class ComparePanelViewModel : ObservableObject
{
    /// <summary>Long enough that validation doesn't run on every keystroke, short enough that the
    /// badge still feels immediate.</summary>
    private const int ValidationDebounceMs = 300;

    private readonly IDialogService _dialogService;
    private readonly IFileDialogService _fileDialogService;
    private CancellationTokenSource? _validationCts;

    [ObservableProperty] private string _label;
    [ObservableProperty] private string _text = string.Empty;
    [ObservableProperty] private bool _isValid;
    [ObservableProperty] private bool _isEmpty = true;
    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty] private bool _canClose;
    [ObservableProperty] private bool _canMoveLeft;
    [ObservableProperty] private bool _canMoveRight;

    /// <summary>True for the leftmost pane. Every other pane is compared against this one, and that
    /// wasn't discoverable from the layout alone — hence the badge in the header.</summary>
    [ObservableProperty] private bool _isBaseline;

    /// <summary>Covers reading/reformatting a large file — the debounced validation is not worth
    /// flagging, it's short and runs while the user keeps typing.</summary>
    [ObservableProperty] private bool _isBusy;

    /// <summary>Wired by the owning window — the panel asks to be removed or moved rather than
    /// reaching into its parent collection itself.</summary>
    public Action<ComparePanelViewModel>? CloseRequested { get; set; }
    public Action<ComparePanelViewModel, int>? MoveRequested { get; set; }
    public Action? ValidityChanged { get; set; }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public ComparePanelViewModel(string label, IDialogService dialogService, IFileDialogService fileDialogService)
    {
        _label = label;
        _dialogService = dialogService;
        _fileDialogService = fileDialogService;
    }

    partial void OnTextChanged(string value) => ScheduleValidation();

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private void ScheduleValidation()
    {
        _validationCts?.Cancel();
        var cts = new CancellationTokenSource();
        _validationCts = cts;
        _ = ValidateAsync(cts.Token);
    }

    private async Task ValidateAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(ValidationDebounceMs, token);
        }
        catch (TaskCanceledException)
        {
            return; // superseded by a newer keystroke
        }

        var text = Text;

        if (string.IsNullOrWhiteSpace(text))
        {
            IsEmpty = true;
            IsValid = false;
            ErrorMessage = null;
            ValidityChanged?.Invoke();
            return;
        }

        var (ok, error) = await Task.Run(() =>
        {
            var parsed = JsonDiffHelper.TryParseJson(text, out var doc, out var err);
            doc?.Dispose();
            return (parsed, err);
        }, token);

        if (token.IsCancellationRequested) return;

        IsEmpty = false;
        IsValid = ok;
        ErrorMessage = ok ? null : error;
        ValidityChanged?.Invoke();
    }

    /// <summary>Loads a dropped or picked file. Called from the view's drop handler too.</summary>
    public async Task LoadFromFileAsync(string path)
    {
        IsBusy = true;
        try
        {
            var text = await File.ReadAllTextAsync(path);
            Text = await Task.Run(() => JsonDiffHelper.PrepareForEditor(text));
            Label = Path.GetFileNameWithoutExtension(path);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var filters = new List<FileFilter> { new("JSON and TXT files", new[] { "*.json", "*.txt" }) };
        var path = await _fileDialogService.OpenFileAsync(Localizer.Get("ComparerLoadFile"), filters);
        if (path == null) return;

        await LoadFromFileAsync(path);
    }

    [RelayCommand]
    private async Task PasteAsync()
    {
        var text = await _dialogService.GetFromClipboardAsync();
        if (string.IsNullOrEmpty(text)) return;

        IsBusy = true;
        try
        {
            Text = await Task.Run(() => JsonDiffHelper.PrepareForEditor(text));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Pretty-prints in place; leaves invalid text alone so the user doesn't lose it.</summary>
    [RelayCommand]
    private async Task FormatAsync()
    {
        if (string.IsNullOrWhiteSpace(Text)) return;

        IsBusy = true;
        try
        {
            var current = Text;
            var formatted = await Task.Run(() => JsonDiffHelper.CanonicalizeForDiff(current));
            if (formatted != current) Text = formatted;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke(this);

    [RelayCommand]
    private void MoveLeft() => MoveRequested?.Invoke(this, -1);

    [RelayCommand]
    private void MoveRight() => MoveRequested?.Invoke(this, +1);
}
