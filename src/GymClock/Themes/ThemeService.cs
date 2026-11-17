using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace GymClock.Wpf
{
    /// <summary>
    /// Picks and applies the theme for the new windows: Dark or Light from the Windows setting
    /// (or the teacher's choice in the theme setting), and High contrast whenever Windows is in a
    /// contrast theme. Changes apply live.
    ///
    /// How the resources are layered in Application.Resources.MergedDictionaries:
    ///   [0] WPF's built-in Fluent theme (set through Application.ThemeMode; WPF keeps it first)
    ///   [1] Themes/Controls.xaml, which merges Themes/Tokens.xaml (never changes)
    ///   [2] the palette: Themes/Tokens.Dark/Light/HighContrast.xaml (swapped on a theme change)
    /// Later dictionaries win, so the palette also recolours the built-in controls through the
    /// Fluent key aliases added below.
    /// </summary>
    public static class ThemeService
    {
        public const string Dark = "Dark";
        public const string Light = "Light";
        public const string HighContrast = "HighContrast";

        private static Application _app;
        private static ResourceDictionary _palette;
        private static string _choice = "system";

        /// <summary>The theme showing now: Dark, Light or HighContrast.</summary>
        public static string Current { get; private set; }

        /// <summary>The setting value in force: system, light or dark.</summary>
        public static string Choice
        {
            get { return _choice; }
        }

        public static event Action Changed;

        internal static void Initialise(Application app)
        {
            _app = app;
            string mode = Resolve(_choice, WindowsUsesLightTheme(), SystemParameters.HighContrast);
            app.ThemeMode = ThemeModeFor(mode);          // the Fluent dictionary must exist before ours
            app.Resources.MergedDictionaries.Add(Load("Themes/Controls.xaml"));
            ApplyPalette(mode);

            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        }

        /// <summary>Applies a theme setting value (system, light or dark). Anything else means system.</summary>
        public static void Apply(string choice)
        {
            _choice = TimerSettings.ReadTheme(choice);
            if (_app == null) return;
            string mode = Resolve(_choice, WindowsUsesLightTheme(), SystemParameters.HighContrast);
            if (mode == Current) return;
            _app.ThemeMode = ThemeModeFor(mode);
            ApplyPalette(mode);
        }

        /// <summary>The theme to show for a setting value and the state of Windows.</summary>
        public static string Resolve(string choice, bool windowsUsesLightTheme, bool highContrast)
        {
            if (highContrast) return HighContrast;
            switch (TimerSettings.ReadTheme(choice))
            {
                case "light": return Light;
                case "dark": return Dark;
                default: return windowsUsesLightTheme ? Light : Dark;
            }
        }

        /// <summary>Loads one of the app's XAML files, such as "Themes/Tokens.Dark.xaml".</summary>
        public static ResourceDictionary Load(string path)
        {
            return new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/GymClock;component/" + path, UriKind.Absolute)
            };
        }

        private static ThemeMode ThemeModeFor(string mode)
        {
            if (mode == Light) return ThemeMode.Light;
            if (mode == Dark) return ThemeMode.Dark;
            return ThemeMode.System;     // high contrast: Fluent follows the Windows contrast colours itself
        }

        private static void ApplyPalette(string mode)
        {
            ResourceDictionary palette = Load("Themes/Tokens." + mode + ".xaml");
            if (mode != HighContrast) AddFluentAliases(palette);
            AddImplicitStyles(palette);

            if (_palette != null) _app.Resources.MergedDictionaries.Remove(_palette);
            _app.Resources.MergedDictionaries.Add(palette);
            _palette = palette;
            Current = mode;

            Action handler = Changed;
            if (handler != null) handler();
        }

        // The built-in Fluent controls (TextBox, ComboBox, CheckBox, Slider, ScrollBar, ToolTip)
        // keep their shape but take our colours: each Fluent brush key on the left is pointed at
        // our token on the right. A key Fluent does not use is simply ignored.
        private static readonly string[,] FluentAliases =
        {
            { "AccentFillColorDefaultBrush", "Accent" },
            { "AccentFillColorSecondaryBrush", "Accent" },
            { "AccentFillColorTertiaryBrush", "Accent" },
            { "AccentButtonBackground", "Accent" },
            { "AccentButtonBackgroundPointerOver", "Accent" },
            { "AccentButtonBackgroundPressed", "Accent" },
            { "AccentButtonForeground", "Accent.On" },
            { "AccentButtonForegroundPointerOver", "Accent.On" },
            { "TextOnAccentFillColorPrimaryBrush", "Accent.On" },
            { "CheckBoxCheckBackgroundFillChecked", "Accent" },
            { "CheckBoxCheckBackgroundFillCheckedPointerOver", "Accent" },
            { "CheckBoxCheckBackgroundFillCheckedPressed", "Accent" },
            { "CheckBoxCheckBackgroundStrokeChecked", "Accent" },
            { "CheckBoxCheckBackgroundStrokeCheckedPointerOver", "Accent" },
            { "CheckBoxCheckBackgroundStrokeCheckedPressed", "Accent" },
            { "CheckBoxCheckGlyphForegroundChecked", "Accent.On" },
            { "CheckBoxCheckGlyphForegroundCheckedPointerOver", "Accent.On" },
            { "CheckBoxCheckGlyphForegroundCheckedPressed", "Accent.On" },
            { "SliderThumbBackground", "Accent" },
            { "SliderThumbBackgroundPointerOver", "Accent" },
            { "SliderThumbBackgroundPressed", "Accent" },
            { "SliderTrackValueFill", "Accent" },
            { "SliderTrackValueFillPointerOver", "Accent" },
            { "SliderTrackValueFillPressed", "Accent" },
            { "ComboBoxBorderBrushFocused", "Accent" },
            { "ComboBoxItemPillFillBrush", "Accent" },
            { "TextControlBackground", "Surface.Input" },
            { "TextControlBackgroundPointerOver", "Surface.Input" },
            { "TextControlBackgroundFocused", "Surface.Input" },
            { "ComboBoxBackground", "Surface.Input" },
            { "ComboBoxBackgroundPointerOver", "Surface.Input" },
            { "TextControlBorderBrush", "Border.Control" },
            { "TextControlBorderBrushPointerOver", "Border.Control" },
            { "TextControlBorderBrushFocused", "Accent" },
            { "ComboBoxBorderBrush", "Border.Control" },
            { "ComboBoxBorderBrushPointerOver", "Border.Control" },
            { "ComboBoxDropDownBackground", "Surface.Raised" },
            { "ComboBoxDropDownBorderBrush", "Border.Control" },
            { "ToolTipBackground", "Surface.Raised" },
            { "ToolTipBackgroundBrush", "Surface.Raised" },
            { "ToolTipBorderBrush", "Border.Control" },
            { "ToolTipForeground", "Text.Primary" },
            { "ToolTipForegroundBrush", "Text.Primary" },
            { "ComboBoxForeground", "Text.Primary" },
            { "ComboBoxDropDownForeground", "Text.Primary" },
            { "TextControlForeground", "Text.Primary" },
            { "TextFillColorPrimaryBrush", "Text.Primary" },
            { "TextFillColorSecondaryBrush", "Text.Secondary" },
            { "ScrollBarThumbFill", "Text.Muted" },
            { "ListBoxBackground", "Surface.Input" },
            { "ListBoxItemForeground", "Text.Primary" },
            { "ListBoxItemSelectedBackgroundThemeBrush", "Surface.Selected" },
            { "ListBoxItemSelectedBackgroundPointerOverThemeBrush", "Surface.Selected" },
            { "ListBoxItemSelectedBackgroundPressedThemeBrush", "Surface.Selected" },
            { "ListBoxItemBackgroundSelectedPressedThemeBrush", "Surface.Selected" },
            { "ListBoxItemSelectedForegroundThemeBrush", "Text.Primary" },
            { "ListViewItemForeground", "Text.Primary" },
            { "ListViewItemPillFillBrush", "Accent" },
        };

        private static void AddFluentAliases(ResourceDictionary palette)
        {
            for (int i = 0; i < FluentAliases.GetLength(0); i++)
            {
                palette[FluentAliases[i, 0]] = palette[FluentAliases[i, 1]];
            }
        }

        /// <summary>The keys every palette file must define (build guide 3.2). Used by the tests.</summary>
        public static readonly IReadOnlyList<string> PaletteKeys = new[]
        {
            "Surface.App", "Surface.Rail", "Surface.Card", "Surface.Raised", "Surface.Selected", "Surface.Input",
            "Border.Card", "Border.Control", "Border.Dashed",
            "Text.Primary", "Text.Secondary", "Text.Muted",
            "Accent", "Accent.On", "Accent.Soft", "Accent.SoftText",
            "Success", "Success.Soft", "Success.SoftText",
            "Warning.Soft", "Warning.SoftText",
            "Danger", "Danger.Soft", "Danger.SoftText",
            "Bubble.User",
        };

        // Fluent's own TextBox, ComboBox and CheckBox are a little small for a gym laptop and a
        // touch screen: the same styles, 40 high with 14-point text (build guide 3.3). They are
        // based on whichever Fluent theme is loaded now, so they are rebuilt with the palette.
        private static void AddImplicitStyles(ResourceDictionary palette)
        {
            ResourceDictionary fluent = FluentDictionary();
            AddSized(palette, fluent, typeof(TextBox));
            AddSized(palette, fluent, typeof(PasswordBox));
            AddSized(palette, fluent, typeof(ComboBox));
            AddSized(palette, fluent, typeof(CheckBox));
            AddSized(palette, fluent, typeof(RadioButton));
        }

        private static void AddSized(ResourceDictionary palette, ResourceDictionary fluent, Type type)
        {
            Style basedOn = fluent != null ? fluent[type] as Style : null;
            Style style = new Style(type, basedOn);
            style.Setters.Add(new Setter(Control.FontSizeProperty, 14.0));
            style.Setters.Add(new Setter(FrameworkElement.MinHeightProperty, 40.0));
            style.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            palette[type] = style;
        }

        private static ResourceDictionary FluentDictionary()
        {
            foreach (ResourceDictionary d in _app.Resources.MergedDictionaries)
            {
                if (d.Source != null && d.Source.ToString().IndexOf("PresentationFramework.Fluent", StringComparison.OrdinalIgnoreCase) >= 0)
                    return d;
            }
            return null;
        }

        private static bool WindowsUsesLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key != null ? key.GetValue("AppsUseLightTheme") : null;
                    if (value is int i) return i != 0;
                }
            }
            catch { }
            return true;     // the Windows default
        }

        private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category == UserPreferenceCategory.General || e.Category == UserPreferenceCategory.Color
                || e.Category == UserPreferenceCategory.VisualStyle || e.Category == UserPreferenceCategory.Accessibility)
            {
                Reapply();
            }
        }

        private static void OnSystemParameterChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == "HighContrast") Reapply();
        }

        private static void Reapply()
        {
            if (_app == null) return;
            _app.Dispatcher.BeginInvoke(new Action(() => Apply(_choice)));
        }
    }
}
