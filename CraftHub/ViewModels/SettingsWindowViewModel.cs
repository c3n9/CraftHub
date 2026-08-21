using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CraftHub.Models.Enums;
using CraftHub.Services;

namespace CraftHub.ViewModels;

/// <summary>A language the interface can be shown in: the code the service stores, and the name
/// shown in the list — always in that language itself, so it is readable to whoever needs it.</summary>
public sealed record LanguageChoice(string Code, string DisplayName);

/// <summary>Which pane the sidebar is showing. A plain enum rather than a list of child view
/// models — every pane here is a handful of toggles, and giving each one its own type would be
/// more scaffolding than settings.</summary>
public enum SettingsSection
{
    Appearance,
    Notifications,
    Editor,
    About
}

/// <summary>
/// Backs the settings window. Everything applies the moment it is changed — there is no OK/Cancel,
/// because every one of these is instantly reversible and a preference dialog that makes you
/// confirm is a preference dialog you avoid opening.
///
/// About's actions are supplied as delegates rather than resolved here, since the release and
/// GitHub flows already live on <see cref="MainWindowViewModel"/>.
///
/// Note what is deliberately NOT here: the tour and the formula reference keep their own buttons
/// in the header (help buried two clicks inside a preferences dialog is help nobody finds), and
/// the comparison options stay in the comparison view. Those are adjusted while looking at a
/// result — moving them here would mean closing the thing you were tuning them against.
/// </summary>
public sealed partial class SettingsWindowViewModel : ObservableObject
{
    private readonly ThemeService _themeService;
    private readonly NotificationService _notificationService;

    [ObservableProperty] private SettingsSection _selectedSection = SettingsSection.Appearance;

    public string CurrentVersion { get; }

    public Func<Task>? ShowReleasesRequested { get; init; }
    public Action? OpenGitHubRequested { get; init; }

    public SettingsWindowViewModel(ThemeService themeService, NotificationService notificationService, string currentVersion)
    {
        _themeService = themeService;
        _notificationService = notificationService;
        CurrentVersion = currentVersion;

        _isDarkTheme = themeService.CurrentTheme == ThemeType.Dark;
        _followSystemTheme = themeService.CurrentTheme == ThemeType.Default;
        _selectedLanguage = Languages.FirstOrDefault(l => l.Code == LanguageService.Instance.CurrentLang)
                            ?? Languages[0];
        _showNotificationPopups = notificationService.ShowPopups;
        _showDiffOnSave = Properties.Settings.Default.ShowDiffOnSave;
    }

    // -----------------------------------------------------------------------
    //  Appearance
    // -----------------------------------------------------------------------

    [ObservableProperty] private bool _isDarkTheme;
    [ObservableProperty] private bool _followSystemTheme;

    /// <summary>One entry per available language. A list rather than a switch because a switch can
    /// only ever mean "one of two" — adding a third language would mean replacing the control and
    /// everything bound to it.</summary>
    public IReadOnlyList<LanguageChoice> Languages { get; } = new[]
    {
        new LanguageChoice("EN", "English"),
        new LanguageChoice("RU", "Русский")
    };

    [ObservableProperty] private LanguageChoice _selectedLanguage;

    partial void OnIsDarkThemeChanged(bool value)
    {
        if (FollowSystemTheme) return; // the system switch owns the theme while it's on
        _themeService.SwitchTheme(value ? ThemeType.Dark : ThemeType.Light);
    }

    partial void OnFollowSystemThemeChanged(bool value)
    {
        if (value)
        {
            _themeService.SwitchTheme(ThemeType.Default);
            return;
        }
        // Turning it off has to land on something concrete, so keep whatever is on screen.
        _themeService.SwitchTheme(IsDarkTheme ? ThemeType.Dark : ThemeType.Light);
    }

    partial void OnSelectedLanguageChanged(LanguageChoice value)
    {
        // LanguageService only knows how to flip between the two it has; with more than two this
        // is where a SetLanguage(code) would go.
        if (LanguageService.Instance.CurrentLang != value.Code) LanguageService.Instance.Toggle();
    }

    // -----------------------------------------------------------------------
    //  Notifications / editor
    // -----------------------------------------------------------------------

    [ObservableProperty] private bool _showNotificationPopups;
    [ObservableProperty] private bool _showDiffOnSave;

    partial void OnShowNotificationPopupsChanged(bool value)
    {
        _notificationService.ShowPopups = value;
        Properties.Settings.Default.ShowNotificationPopups = value;
        Properties.Settings.Default.Save();
    }

    partial void OnShowDiffOnSaveChanged(bool value)
    {
        Properties.Settings.Default.ShowDiffOnSave = value;
        Properties.Settings.Default.Save();
    }

    // -----------------------------------------------------------------------
    //  Navigation + About
    // -----------------------------------------------------------------------

    /// <summary>Takes the section name as text, not as <see cref="SettingsSection"/>. XAML passes
    /// CommandParameter as a string, and a RelayCommand typed to the enum throws out of CanExecute
    /// the moment the binding attaches — which crashed the window on open rather than failing at
    /// the click. Parsing here keeps the markup declarative and the failure impossible.</summary>
    [RelayCommand]
    private void SelectSection(string? section)
    {
        if (Enum.TryParse<SettingsSection>(section, out var parsed)) SelectedSection = parsed;
    }

    [RelayCommand]
    private async Task ShowReleases()
    {
        if (ShowReleasesRequested is { } show) await show();
    }

    [RelayCommand]
    private void OpenGitHub() => OpenGitHubRequested?.Invoke();
}
