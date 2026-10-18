using System.Globalization;
using System.Resources;
using System.Windows.Markup;

namespace FitMerge.Services;

/// <summary>Looks up UI strings in Resources/Strings(.de).resx for the current UI culture.</summary>
public static class Loc
{
    private static readonly ResourceManager Resources = new("FitMerge.Resources.Strings", typeof(Loc).Assembly);

    public static string Get(string key, params object[] args) =>
        string.Format(Resources.GetString(key) ?? key, args);

    /// <summary>Applies a language such as "en" or "de" to all threads; throws for unknown cultures.</summary>
    public static void SetLanguage(string name)
    {
        var culture = CultureInfo.GetCultureInfo(name);
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentUICulture = culture;
    }
}

/// <summary>XAML access to <see cref="Loc"/>: <c>Text="{svc:Loc PreviewHeader}"</c>.</summary>
public sealed class LocExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(key);
}
