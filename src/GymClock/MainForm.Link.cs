using System;
using System.Drawing;
using System.Windows.Forms;
using GymClock.Link;

namespace GymClock
{
    /// <summary>
    /// Linked displays, as far as this window is concerned: opening the Link displays setup,
    /// drawing the status line, and refusing the session keys on a second screen. The link
    /// itself is <see cref="LinkController"/> in GymClock.Core.
    /// </summary>
    public partial class MainForm
    {
        /// <summary>The settings that belong to this PC (and are saved to its settings file).</summary>
        private TimerSettings OwnSettings
        {
            get { return _session.OwnSettings; }
        }

        /// <summary>True while this PC is a second screen: the main PC has the controls.</summary>
        private bool IsFollowing
        {
            get { return _session.Link.IsFollowing; }
        }

        private bool _hasLeaderProgram
        {
            get { return _session.Link.HasLeaderProgram; }
        }

        private void StartLink()
        {
            _session.Link.Start();
        }

        private void LinkTick()
        {
            _session.Link.Tick();
        }

        /// <summary>
        /// The Link displays window (L, or the button in the settings window): the guided setup. Every
        /// choice in it takes effect at once, so the window can show the link coming up live.
        /// </summary>
        internal void ShowLinkSettings(IWin32Window owner = null)
        {
            using (LinkSetupForm dialog = new LinkSetupForm(OwnSettings, _session.Link.GetStatus, _session.Link.ApplyChange))
            {
                dialog.ShowDialog(owner ?? this);
            }
            ShowHint(6);
        }

        /// <summary>A control key or click on a second screen: say why nothing happened.</summary>
        private void FollowerControlBlocked()
        {
            ShowToast("THE MAIN PC CONTROLS THIS SCREEN  -  PRESS L FOR LINK SETTINGS", 3);
        }

        /// <summary>Second screen, Parallel programs: is this station one of the ones this screen shows?</summary>
        private bool LinkShowsStation(int stationIndex, int stationCount)
        {
            return _session.Link.ShowsStation(stationIndex, stationCount);
        }

        /// <summary>The small link status line at the bottom of the screen, or empty when the link is off.</summary>
        private string LinkBadgeText(bool detail, out Color colour)
        {
            string text = _session.Link.BadgeText(detail, out bool isWarning);
            colour = isWarning ? Color.FromArgb(235, 255, 200, 90) : Color.FromArgb(130, 255, 255, 255);
            return text;
        }

        // ---- kept only for the link tests, which still reach into this window by name.
        // Build guide task P1.8 moves those tests onto AppSession and deletes these.

        private TimerSettings _ownSettings
        {
            get { return _session.ShowingLeaderSettings ? _session.OwnSettings : null; }
        }

        private double _linkDriftPeakMs
        {
            get { return _session.Link.DriftPeakMs; }
        }

        private void StopLink(bool restoreOwnProgram)
        {
            _session.Link.Stop(restoreOwnProgram);
        }

        private LinkStatus GetLinkStatus()
        {
            return _session.Link.GetStatus();
        }
    }
}
