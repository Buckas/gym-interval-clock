using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GymClock.Wpf;
using Xunit;

namespace GymClock.Tests
{
    /// <summary>Phase 2 of version 5: the theme, the control styles and the Control window shell.</summary>
    public class ThemeAndShellTests
    {
        [Theory]
        [InlineData("Dark")]
        [InlineData("Light")]
        [InlineData("HighContrast")]
        public void EveryPalette_DefinesEveryColourToken(string theme)
        {
            WpfTestHost.Run(() =>
            {
                ResourceDictionary palette = ThemeService.Load("Themes/Tokens." + theme + ".xaml");
                foreach (string key in ThemeService.PaletteKeys)
                {
                    Assert.True(palette.Contains(key), theme + " is missing " + key);
                    Assert.IsAssignableFrom<SolidColorBrush>(palette[key]);
                }
                Assert.Equal(ThemeService.PaletteKeys.Count, palette.Count);
            });
        }

        [Fact]
        public void ControlStyles_LoadAndHoldEveryNamedStyle()
        {
            WpfTestHost.Run(() =>
            {
                string[] keys =
                {
                    "Button.Primary", "Button.Standard", "Button.Subtle", "Button.Danger", "Button.Icon",
                    "ToggleSwitch", "Card", "Card.Selected", "Tag.Success", "Tag.Accent", "Tag.Warning", "Tag.Neutral",
                    "InfoBar.Info", "InfoBar.Success", "InfoBar.Warning", "InfoBar.Error",
                    "SegmentedControl", "NavList", "NavItem", "FocusVisual.Accent",
                    "TextStyle.Title", "TextStyle.CardTitle", "TextStyle.Subtitle", "TextStyle.Body", "TextStyle.Small", "TextStyle.Caption",
                };
                foreach (string key in keys)
                {
                    Assert.True(Application.Current.TryFindResource(key) is Style, "No style " + key);
                }
                Assert.True(Application.Current.TryFindResource(typeof(NumberBox)) is Style);
            });
        }

        [Theory]
        [InlineData("system", false, false, "Dark")]
        [InlineData("system", true, false, "Light")]
        [InlineData("light", false, false, "Light")]
        [InlineData("dark", true, false, "Dark")]
        [InlineData("dark", true, true, "HighContrast")]
        [InlineData("nonsense", true, false, "Light")]
        [InlineData(null, false, false, "Dark")]
        public void Theme_FollowsTheSettingThenWindows_AndHighContrastAlwaysWins(string choice, bool windowsLight, bool contrast, string expected)
        {
            Assert.Equal(expected, ThemeService.Resolve(choice, windowsLight, contrast));
        }

        [Fact]
        public void SwitchingTheme_SwapsThePaletteLive()
        {
            WpfTestHost.Run(() =>
            {
                ThemeService.Apply("light");
                Assert.Equal(System.Windows.Media.Color.FromRgb(0xF5, 0xF6, 0xF8),
                    ((SolidColorBrush)Application.Current.FindResource("Surface.App")).Color);
                ThemeService.Apply("dark");
                Assert.Equal(System.Windows.Media.Color.FromRgb(0x12, 0x15, 0x1A),
                    ((SolidColorBrush)Application.Current.FindResource("Surface.App")).Color);
                // The built-in controls take our accent through the aliases.
                Assert.Equal(System.Windows.Media.Color.FromRgb(0x5A, 0xA2, 0xFF),
                    ((SolidColorBrush)Application.Current.FindResource("AccentFillColorDefaultBrush")).Color);
            });
        }

        [Fact]
        public void ThemeAndWindowPlace_AreThisPcsOwn_SavedButNeverSentToASecondScreen()
        {
            TimerSettings s = new TimerSettings { Theme = "light", ControlBounds = "10,20,1280,800,max" };

            string saved = s.ToText(true);
            Assert.Contains("theme=light", saved);
            Assert.Contains("controlbounds=10,20,1280,800,max", saved);

            string sent = s.ToText(false);
            Assert.DoesNotContain("theme=", sent);
            Assert.DoesNotContain("controlbounds=", sent);

            TimerSettings received = TimerSettings.FromText(saved, string.Empty);
            Assert.Equal("system", received.Theme);
            Assert.Equal(string.Empty, received.ControlBounds);

            TimerSettings copy = s.Clone();
            Assert.Equal("light", copy.Theme);
            Assert.Equal("10,20,1280,800,max", copy.ControlBounds);
        }

        [Fact]
        public void DisplaySettings_AreThisPcsOwn_SavedRead_AndCheckedOnTheWayIn()
        {
            TimerSettings s = new TimerSettings { DisplayLayout = "bignumber", SafeMargin = 5, ReduceMotion = "on", ControlBar = false };
            string saved = s.ToText(true);
            foreach (string line in new[] { "displaylayout=bignumber", "safemargin=5", "reducemotion=on", "controlbar=false" })
                Assert.Contains(line, saved);

            string sent = s.ToText(false);
            Assert.DoesNotContain("displaylayout=", sent);
            Assert.DoesNotContain("safemargin=", sent);
            Assert.DoesNotContain("reducemotion=", sent);
            Assert.DoesNotContain("controlbar=", sent);

            TimerSettings copy = s.Clone();
            Assert.Equal("bignumber", copy.DisplayLayout);
            Assert.Equal(5, copy.SafeMargin);
            Assert.Equal("on", copy.ReduceMotion);
            Assert.False(copy.ControlBar);

            Assert.Equal("standard", TimerSettings.ReadLayout("sideways"));
            Assert.Equal("stations", TimerSettings.ReadLayout(" Stations "));
            Assert.Equal("system", TimerSettings.ReadReduceMotion("maybe"));
            Assert.True(DisplayWindow.ReduceMotionOn("on"));
            Assert.False(DisplayWindow.ReduceMotionOn("off"));
        }

        [Fact]
        public void NumberBox_KeepsToDigitsInRange_AndStepsWithTheArrows()
        {
            WpfTestHost.Run(() =>
            {
                NumberBox box = new NumberBox { Minimum = 0, Maximum = 300, Step = 5 };
                box.Value = 45;
                Assert.Equal("45", box.Text);

                box.Text = "999";
                box.Commit();
                Assert.Equal(300, box.Value);
                Assert.Equal("300", box.Text);

                box.Text = "abc";
                box.Commit();
                Assert.Equal(300, box.Value);
                Assert.Equal("300", box.Text);

                box.Value = -4;
                Assert.Equal(0, box.Value);
            });
        }

        [Fact]
        public void ControlWindow_ShowsEachPageById_AndIgnoresUnknownIds()
        {
            WpfTestHost.Run(() =>
            {
                ControlViewModel model = new ControlViewModel();
                Assert.Equal(8, model.Pages.Count);
                Assert.Equal("session", model.CurrentPage.Id);

                foreach (ControlPage page in model.Pages)
                {
                    Assert.True(model.Navigate(page.Id));
                    Assert.Same(page, model.CurrentPage);
                    Assert.NotNull(model.CurrentView);
                }

                Assert.False(model.Navigate("nowhere"));
                Assert.Equal("about", model.CurrentPage.Id);

                Assert.True(model.Navigate("gallery"));
                Assert.IsType<GalleryPageView>(model.CurrentView);
                Assert.DoesNotContain(model.CurrentPage, model.Pages);
            });
        }

        [Fact]
        public void ControlWindowPlace_IsWrittenAndReadBack()
        {
            string text = ControlHost.FormatBounds(new Rectangle(-1900, 40, 1280, 800), true);
            Assert.Equal("-1900,40,1280,800,max", text);

            Rectangle r;
            bool max;
            Assert.True(ControlHost.TryParseBounds(text, out r, out max));
            Assert.Equal(new Rectangle(-1900, 40, 1280, 800), r);
            Assert.True(max);

            Assert.False(ControlHost.TryParseBounds("", out r, out max));
            Assert.False(ControlHost.TryParseBounds("1,2,0,5", out r, out max));
            Assert.False(ControlHost.TryParseBounds("a,b,c,d", out r, out max));
        }

        [Fact]
        public void SidePanel_IsTheRight40PercentOfTheScreen_AndAWindowOffScreenIsNotReused()
        {
            Assert.Equal(new Rectangle(1152, 0, 768, 1080), ControlHost.PanelBounds(new Rectangle(0, 0, 1920, 1080)));
            Assert.Equal(new Rectangle(3072, 0, 768, 1080), ControlHost.PanelBounds(new Rectangle(1920, 0, 1920, 1080)));

            Rectangle screen = new Rectangle(0, 0, 1920, 1040);
            Assert.True(ControlHost.IsMostlyOn(new Rectangle(100, 100, 1280, 800), screen));
            Assert.False(ControlHost.IsMostlyOn(new Rectangle(1880, 100, 1280, 800), screen));
            Assert.False(ControlHost.IsMostlyOn(new Rectangle(-3000, 100, 1280, 800), screen));
        }

        /// <summary>
        /// Not a check: renders the Control window with the style gallery, in dark and light, to
        /// PNG files for the Phase 2 gate. Runs only when GYMCLOCK_SCREENSHOTS names a folder.
        /// </summary>
        [Fact]
        public void Screenshots_OfTheGallery_WhenAsked()
        {
            string folder = Environment.GetEnvironmentVariable("GYMCLOCK_SCREENSHOTS");
            if (string.IsNullOrWhiteSpace(folder)) return;
            Directory.CreateDirectory(folder);

            WpfTestHost.Run(() =>
            {
                foreach (string theme in new[] { "dark", "light" })
                {
                    ThemeService.Apply(theme);
                    foreach (string page in new[] { "gallery", "session" })
                    {
                        foreach (bool panel in new[] { false, true })
                        {
                            if (panel && page == "gallery") continue;
                            Render(theme, page, panel, Path.Combine(folder, "control-" + page + (panel ? "-panel" : string.Empty) + "-" + theme + ".png"));
                        }
                    }
                }
                ThemeService.Apply("system");
            });
        }

        private static void Render(string theme, string page, bool panel, string path)
        {
            ControlViewModel model = new ControlViewModel { IsPanel = panel };
            model.Navigate(page);
            ControlWindow window = new ControlWindow(model)
            {
                Left = -20000, Top = -20000,
                Width = panel ? 640 : 1280, Height = page == "gallery" ? 1180 : 800,
                MinWidth = 0, MinHeight = 0,
                ShowActivated = false, ShowInTaskbar = false,
                WindowStyle = panel ? WindowStyle.None : WindowStyle.SingleBorderWindow,
            };
            window.Show();
            window.UpdateLayout();

            FrameworkElement content = (FrameworkElement)window.Content;
            int w = (int)Math.Ceiling(content.ActualWidth);
            int h = (int)Math.Ceiling(content.ActualHeight);
            DrawingVisual visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(window.Background, null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new VisualBrush(content), null, new Rect(0, 0, w, h));
            }
            RenderTargetBitmap bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            PngBitmapEncoder png = new PngBitmapEncoder();
            png.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream file = File.Create(path)) png.Save(file);
            window.Close();
        }
    }
}
