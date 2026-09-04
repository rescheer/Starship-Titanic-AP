using System.Reflection;

namespace StarshipTitanicAp;

/// <summary>
/// Holds the app's display name and version.
/// </summary>
public static class AppInfo
{
    public const string DisplayName = "Starship Titanic AP Client";

    /// <summary>
    /// Returns the app version string.
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
