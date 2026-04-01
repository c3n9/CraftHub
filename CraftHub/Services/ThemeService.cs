using System;
using System.IO;
using System.Text.Json;
using Avalonia;
using Avalonia.Styling;
using CraftHub.Models.Enums;

namespace CraftHub.Services
{
    public class ThemeService
    {
        private static readonly string AppDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CraftHub");
        private static readonly string SettingsFile = Path.Combine(AppDataFolder, "theme_settings.json");

        public ThemeType CurrentTheme { get; private set; }
        public ThemeType GetSystemTheme() => CurrentTheme;

        public ThemeService()
        {
            InitializeTheme();
        }

        private void InitializeTheme()
        {
            var savedTheme = LoadThemeSetting();

            CurrentTheme = savedTheme switch
            {
                "Light" => ThemeType.Light,
                "Dark" => ThemeType.Dark,
                _ => ThemeType.Dark
            };

            ApplyTheme(CurrentTheme);
        }

        public void SwitchTheme(ThemeType theme)
        {
            if (CurrentTheme == theme) return;

            CurrentTheme = theme;
            ApplyTheme(theme);
            SaveThemeSetting(theme.ToString());
        }

        private void ApplyTheme(ThemeType theme)
        {
            if (Application.Current == null) return;

            var app = Application.Current;
            switch (theme)
            {
                case ThemeType.Dark:
                    app.RequestedThemeVariant = ThemeVariant.Dark;
                    break;

                case ThemeType.Light:
                    app.RequestedThemeVariant = ThemeVariant.Light;
                    break;

                case ThemeType.Default:
                default:
                    app.RequestedThemeVariant = ThemeVariant.Default;
                    break;
            }
        }

        private string LoadThemeSetting()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    var json = File.ReadAllText(SettingsFile);
                    var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("CurrentTheme", out var themeElement))
                    {
                        return themeElement.GetString() ?? "Default";
                    }
                }
            }
            catch
            {
                // Ignore parsing errors and fallback to Default
            }

            return "Default";
        }

        private void SaveThemeSetting(string theme)
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }

                var json = JsonSerializer.Serialize(new { CurrentTheme = theme });
                File.WriteAllText(SettingsFile, json);
            }
            catch
            {
                // Ignore saving errors
            }
        }
    }
}
