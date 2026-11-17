using System.Collections.Generic;
using System.Reflection;

namespace GymClock.Wpf
{
    /// <summary>
    /// The icon glyphs the app uses, from the Segoe Fluent Icons font (Windows 11), with the same
    /// code points in Segoe MDL2 Assets (Windows 10) as the fallback. Show one with a TextBlock
    /// whose FontFamily is {DynamicResource IconFontFamily}, for example
    /// &lt;TextBlock Text="{x:Static local:Icons.Play}" FontFamily="{DynamicResource IconFontFamily}" /&gt;.
    /// </summary>
    public static class Icons
    {
        public const string Play = "";
        public const string Pause = "";
        public const string Skip = "";
        public const string Back = "";
        public const string Reset = "";

        public const string Session = "";
        public const string Programs = "";
        public const string Builder = "";
        public const string Assistant = "";
        public const string Exercises = "";
        public const string Link = "";
        public const string Settings = "";
        public const string About = "";

        public const string SoundOn = "";
        public const string SoundOff = "";
        public const string FullScreen = "";
        public const string ExitFullScreen = "";
        public const string More = "";
        public const string Close = "";
        public const string Tick = "";
        public const string DragHandle = "";
        public const string Add = "";
        public const string Delete = "";
        public const string ChevronUp = "";
        public const string ChevronDown = "";

        /// <summary>Every icon above with its name, for the style gallery.</summary>
        public static IReadOnlyList<KeyValuePair<string, string>> All
        {
            get
            {
                List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
                foreach (FieldInfo f in typeof(Icons).GetFields(BindingFlags.Public | BindingFlags.Static))
                {
                    if (f.IsLiteral && f.FieldType == typeof(string))
                        list.Add(new KeyValuePair<string, string>(f.Name, (string)f.GetRawConstantValue()));
                }
                return list;
            }
        }
    }
}
