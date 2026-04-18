using System;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;

namespace CraftHub.Services;

public sealed class LanguageService
{
    public static readonly LanguageService Instance = new();

    private string _currentLang = "EN";
    public string CurrentLang => _currentLang;

    private ResourceInclude? _activeDictionary;

    private LanguageService()
    {
        var saved = Properties.Settings.Default.CurrentLanguage;
        _currentLang = saved == "RU" ? "RU" : "EN";
    }

    /// <summary>
    /// Must be called once after Application.Current is ready (e.g. in App.OnFrameworkInitializationCompleted).
    /// </summary>
    public void Initialize()
    {
        Apply(_currentLang);
    }

    public void Toggle()
    {
        _currentLang = _currentLang == "EN" ? "RU" : "EN";
        Apply(_currentLang);
        Properties.Settings.Default.CurrentLanguage = _currentLang;
        Properties.Settings.Default.Save();
    }

    /// <summary>
    /// Reads a localized string from the current Application ResourceDictionary.
    /// Use this in ViewModels where {DynamicResource} is not available.
    /// </summary>
    public string Get(string key)
    {
        if (Application.Current?.TryGetResource(key, null, out var value) == true && value is string str)
            return str;
        return $"[{key}]";
    }

    /// <summary>
    /// Convenience overload for formatted strings: Get("KeyWithArgs", arg0, arg1, ...)
    /// </summary>
    public string Get(string key, params object[] args)
    {
        var template = Get(key);
        try { return string.Format(template, args); }
        catch { return template; }
    }

    private void Apply(string lang)
    {
        var app = Application.Current;
        if (app == null) return;

        if (_activeDictionary != null)
            app.Resources.MergedDictionaries.Remove(_activeDictionary);

        var uri = new Uri($"avares://CraftHub/Resources/Localizations/{lang}.axaml");
        _activeDictionary = new ResourceInclude(uri) { Source = uri };
        app.Resources.MergedDictionaries.Add(_activeDictionary);
    }
}
