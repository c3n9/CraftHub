using CraftHub.Services;

namespace CraftHub.Helpers;

public static class Localizer
{
    public static string Get(string key) => LanguageService.Instance.Get(key);
    public static string Get(string key, params object[] args) => LanguageService.Instance.Get(key, args);
}