using System.Reflection;

namespace StarshipTitanicAp;

/// <summary>
/// Central place for the app's display name and version, so the title bar
/// (and anywhere else that needs it later) stays in sync with the single
/// &lt;Version&gt; in StarshipTitanicAp.csproj instead of a hand-maintained
/// string duplicated elsewhere.
/// </summary>
public static class AppInfo
{
    public const string DisplayName = "Starship Titanic AP";

    /// <summary>
    /// e.g. "0.1.0" - read at runtime from the assembly version, which the
    /// SDK derives from the csproj's &lt;Version&gt;. Trims the trailing
    /// ".0" revision the SDK pads on, so this shows exactly what's in the
    /// csproj rather than a padded four-part number.
    /// </summary>
    public static string Version
    {
        get
        {
            System.Version? v = Assembly.GetExecutingAssembly().GetName().Version;
            if (v is null)
                return "0.0.0";

            return v.Revision == 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : v.ToString();
        }
    }

    public static string TitleBarText => $"{DisplayName} v{Version}";
}
