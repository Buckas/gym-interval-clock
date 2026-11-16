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

        /// <summary>For titles: "3.1" for 3.1.0, "3.1.2" for a patch release, "4.0 Preview 1" for 4.0.0-preview.1.</summary>
        public static string Display
        {
            get { return Format(Full); }
        }

        /// <summary>True for a test build (a version with a suffix, e.g. 4.0.0-preview.1).</summary>
        public static bool IsPreview
        {
            get { return Full.IndexOf('-') > 0; }
        }

        public static string Format(string version)
        {
            string text = version ?? string.Empty;
            string suffix = string.Empty;
            int dash = text.IndexOf('-');
            if (dash > 0)
            {
                // "preview.1" -> " Preview 1"
                string label = text.Substring(dash + 1).Replace('.', ' ').Trim();
                if (label.Length > 0) suffix = " " + char.ToUpperInvariant(label[0]) + label.Substring(1);
                text = text.Substring(0, dash);
            }

            if (!Version.TryParse(text, out Version v)) return version ?? string.Empty;
            return (v.Build > 0 ? v.Major + "." + v.Minor + "." + v.Build : v.Major + "." + v.Minor) + suffix;
        }
    }
}
