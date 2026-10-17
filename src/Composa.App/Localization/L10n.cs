using System.Collections.Frozen;
using System.Globalization;
using System.Resources;

namespace Composa.App;

/// <summary>Resources are selected once at startup; preferences take effect on the next launch.</summary>
public static class L10n
{
    private static readonly ResourceManager Resources = new("Composa.App.Localization.Strings", typeof(L10n).Assembly);
    // The English catalog also adapts legacy display names without changing command IDs or model data.
    private static readonly FrozenDictionary<string, string> EnglishKeys = Resources.GetResourceSet(CultureInfo.InvariantCulture, true, true)!
        .Cast<System.Collections.DictionaryEntry>().ToFrozenDictionary(entry => (string)entry.Value!, entry => (string)entry.Key, StringComparer.Ordinal);

    public static CultureInfo Culture { get; private set; } = CultureInfo.GetCultureInfo("en");

    public static CultureInfo ResolveCulture(string? language, CultureInfo systemCulture) =>
        CultureInfo.GetCultureInfo(language switch
        {
            "en" => "en",
            "zh-CN" => "zh-CN",
            "ja" => "ja",
            _ => systemCulture.Name.Equals("zh-CN", StringComparison.OrdinalIgnoreCase)
                || systemCulture.Name.Equals("zh-SG", StringComparison.OrdinalIgnoreCase)
                || systemCulture.Name.Equals("zh-Hans", StringComparison.OrdinalIgnoreCase)
                || systemCulture.Name.StartsWith("zh-Hans-", StringComparison.OrdinalIgnoreCase) ? "zh-CN"
                : systemCulture.TwoLetterISOLanguageName == "ja" ? "ja" : "en"
        });

    public static void Initialize(string? language) => Culture = ResolveCulture(language, CultureInfo.InstalledUICulture);

    public static string Get(string key) => Resources.GetString(key, Culture) ?? key;

    public static string Format(string key, params object[] arguments) => string.Format(Culture, Get(key), arguments);

    /// <summary>Translate a known English display name, leaving unknown text and user data unchanged.</summary>
    public static string Text(string english)
    {
        if (EnglishKeys.TryGetValue(english, out var key)) return Get(key);
        if (english.EndsWith('…') && EnglishKeys.TryGetValue(english[..^1], out key)) return Get(key) + "…";
        return english;
    }
}
