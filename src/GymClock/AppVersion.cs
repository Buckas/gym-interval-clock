using System;
using System.Reflection;
using System.Windows.Forms;

namespace GymClock
{
    /// <summary>
    /// The app's version as set in GymClock.csproj (&lt;Version&gt;), for window titles and the
    /// About box - so nothing needs editing by hand when the version is bumped.
    /// </summary>
    public static class AppVersion
    {
        /// <summary>
        /// "3.1.0", read from this assembly's informational version (= &lt;Version&gt; in the
        /// project file). A git hash after a '+' (e.g. "3.1.0+abcdef1234...") is dropped.
        /// </summary>
        public static string Full
        {
            get
            {
                Assembly assembly = typeof(AppVersion).Assembly;
                string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                    ?? assembly.GetName().Version?.ToString(3)
                    ?? Application.ProductVersion
                    ?? string.Empty;
                int plus = version.IndexOf('+');
                return plus >= 0 ? version.Substring(0, plus) : version;
            }
        }

        /// <summary>For titles: "3.1" for 3.1.0, "3.1.2" for a patch release.</summary>
        public static string Display
        {
            get { return Format(Full); }
        }

        public static string Format(string version)
        {
            if (!Version.TryParse(version, out Version v)) return version ?? string.Empty;
            return v.Build > 0 ? v.Major + "." + v.Minor + "." + v.Build : v.Major + "." + v.Minor;
        }
    }
}
