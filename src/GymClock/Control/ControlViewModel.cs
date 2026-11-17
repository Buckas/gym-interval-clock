using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace GymClock.Wpf
{
    /// <summary>One page of the Control window: its id (used by ControlHost.Open), title and icon.</summary>
    public sealed class ControlPage
    {
        private readonly Func<object> _create;
        private object _view;

        public ControlPage(string id, string title, string glyph, Func<object> create, bool freshEachTime = false)
        {
            Id = id;
            Title = title;
            Glyph = glyph;
            _create = create;
            FreshEachTime = freshEachTime;
        }

        /// <summary>True: the page is made again each time it is opened (it reads settings that may have changed).</summary>
        public bool FreshEachTime { get; }

        /// <summary>Forgets the page, so the next <see cref="View"/> makes a new one.</summary>
        public void Forget()
        {
            _view = null;
        }

        public string Id { get; }
        public string Title { get; }
        public string Glyph { get; }

        /// <summary>The page itself, made the first time it is shown and kept after that.</summary>
        public object View
        {
            get { return _view ?? (_view = _create()); }
        }
    }

    /// <summary>
    /// The Control window: which page is showing, and whether the window is a full window with
    /// the navigation rail or a side panel over the clock (one-screen setups, mockup 3).
    /// </summary>
    public sealed class ControlViewModel : ObservableObject
    {
        public const string SessionPage = "session";
        public const string ProgramsPage = "programs";
        public const string BuilderPage = "builder";
        public const string AssistantPage = "assistant";
        public const string ExercisesPage = "exercises";
        public const string LinkPage = "link";
        public const string SettingsPage = "settings";
        public const string AboutPage = "about";
        public const string GalleryPage = "gallery";

        private readonly IDictionary<string, Func<object>> _built;
        private readonly List<ControlPage> _pages;
        private readonly ControlPage _gallery;
        private ControlPage _current;
        private bool _isPanel;

        /// <param name="pages">The pages that are built, by id; every other page shows a placeholder.</param>
        public ControlViewModel(IDictionary<string, Func<object>> pages = null)
        {
            _built = pages ?? new Dictionary<string, Func<object>>();

            // Until each page is built (phases 4 to 6) it says where that job is done today.
            _pages = new List<ControlPage>
            {
                Placeholder(SessionPage, "Session", Icons.Session, "Phase 4",
                    "Run the session from the clock: Space starts and pauses, the arrow keys skip and go back, R resets."),
                Placeholder(ProgramsPage, "Programs", Icons.Programs, "Phase 5",
                    "Press S on the clock to open the program editor, where programs are loaded and saved."),
                Placeholder(BuilderPage, "Builder", Icons.Builder, "Phase 5",
                    "Press S on the clock to open the program editor."),
                Placeholder(AssistantPage, "Assistant", Icons.Assistant, "Phase 6",
                    "Press A on the clock to open the program assistant."),
                Placeholder(ExercisesPage, "Exercises", Icons.Exercises, "Phase 6",
                    "Open the exercise library from the program editor (press S on the clock)."),
                Placeholder(LinkPage, "Link displays", Icons.Link, "Phase 4",
                    "Press L on the clock to set up linked displays."),
                Placeholder(SettingsPage, "Settings", Icons.Settings, "Phase 4",
                    "Press S on the clock to open the settings."),
                Placeholder(AboutPage, "About", Icons.About, "Phase 4",
                    "Press F3 on the clock to see the version and licence."),
            };
            _gallery = new ControlPage(GalleryPage, "Style gallery", Icons.More, () => new GalleryPageView());
            _current = _pages[0];

            ShowPage = new RelayCommand(p => Navigate(p as string));
            ShowGallery = new RelayCommand(_ => Navigate(GalleryPage));
            FullWindow = new RelayCommand(_ => Raise(FullWindowRequested));
            Close = new RelayCommand(_ => Raise(CloseRequested));
        }

        /// <summary>The pages in the navigation rail, in order.</summary>
        public IReadOnlyList<ControlPage> Pages
        {
            get { return _pages; }
        }

        /// <summary>The page showing. The style gallery is not in <see cref="Pages"/>, so the rail shows no selection for it.</summary>
        public ControlPage CurrentPage
        {
            get { return _current; }
            set
            {
                if (value == null) return;     // the rail clears its selection when the gallery shows
                if (value == _current) return;
                if (value.FreshEachTime) value.Forget();
                _current = value;
                Raise();
                Raise(nameof(CurrentView));
            }
        }

        public object CurrentView
        {
            get { return _current.View; }
        }

        /// <summary>True when the window is the side panel docked over the clock (one screen).</summary>
        public bool IsPanel
        {
            get { return _isPanel; }
            set { Set(ref _isPanel, value); }
        }

        public ICommand ShowPage { get; }
        public ICommand ShowGallery { get; }
        public ICommand FullWindow { get; }
        public ICommand Close { get; }

        public event Action FullWindowRequested;
        public event Action CloseRequested;

        /// <summary>Shows a page by id. Returns false (and changes nothing) for an unknown id.</summary>
        public bool Navigate(string id)
        {
            if (string.Equals(id, GalleryPage, StringComparison.OrdinalIgnoreCase))
            {
                CurrentPage = _gallery;
                return true;
            }
            foreach (ControlPage page in _pages)
            {
                if (string.Equals(page.Id, id, StringComparison.OrdinalIgnoreCase))
                {
                    CurrentPage = page;
                    return true;
                }
            }
            return false;
        }

        /// <summary>The page with this id: the real one if it has been built, otherwise a placeholder.</summary>
        private ControlPage Placeholder(string id, string title, string glyph, string phase, string today)
        {
            Func<object> real;
            if (_built.TryGetValue(id, out real)) return new ControlPage(id, title, glyph, real, FreshPages.Contains(id));
            return new ControlPage(id, title, glyph, () => new PlaceholderPageView(title, phase, today));
        }

        /// <summary>Pages that read this PC's settings or the licence when they open, so they are made again each time.</summary>
        private static readonly HashSet<string> FreshPages = new HashSet<string> { LinkPage, AboutPage, SettingsPage };

        private static void Raise(Action handler)
        {
            if (handler != null) handler();
        }
    }
}
