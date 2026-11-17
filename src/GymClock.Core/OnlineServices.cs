using System;
using System.Globalization;
using System.Net.Http;
using System.Threading.Tasks;

namespace GymClock
{
    /// <summary>
    /// Fetches licence keys and version information from a static website.
    ///
    /// Deliberately minimal: outbound HTTPS GETs to two fixed paths, no request
    /// body, no personal information, nothing posted anywhere. The only value
    /// leaving the machine is the machine ID, which is already a one-way hash.
    ///
    /// Every call fails silently. A blocked proxy, no wifi in the gym, or the site
    /// being down must never stop the clock from working - a licence is always
    /// verified locally against the signature, never against the network.
    /// </summary>
    public static class OnlineServices
    {
        /// <summary>
        /// Root of the licence site. Change this to your GitHub Pages address.
        /// No trailing slash.
        /// </summary>
        public const string BaseUrl = "https://buckas.github.io/gymclock-licences";

        /// <summary>How long to wait before giving up. Short on purpose.</summary>
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

        /// <summary>Do not re-check more often than this.</summary>
        private static readonly TimeSpan MinimumInterval = TimeSpan.FromHours(12);

        private static readonly HttpClient Client = CreateClient();

        private static HttpClient CreateClient()
        {
            HttpClient client = new HttpClient();
            client.Timeout = Timeout;
            client.DefaultRequestHeaders.Add("User-Agent",
                "GymClock/" + typeof(OnlineServices).Assembly.GetName().Version);
            return client;
        }

        /// <summary>
        /// True when it is worth looking for a licence: we have none, ours has
        /// expired or is close to it, and we have not just checked.
        /// </summary>
        public static bool ShouldCheck(LicenceStatus status, bool force)
        {
            if (force) return true;

            bool wanted =
                status.State == LicenceState.TrialEnding ||
                status.State == LicenceState.TrialExpired ||
                status.State == LicenceState.LicenceExpired ||
                status.State == LicenceState.LicenceGrace ||
                (status.State == LicenceState.Licensed && status.DaysRemaining <= 45);

            if (!wanted) return false;

            DateTime last = Licensing.LastOnlineCheck();
            return last == DateTime.MinValue || DateTime.Now - last > MinimumInterval;
        }

        /// <summary>
        /// Looks for a licence issued to this machine and installs it if the
        /// signature checks out. Returns true only if something changed.
        /// </summary>
        public static async Task<bool> TryFetchLicenceAsync()
        {
            Licensing.RecordOnlineCheck();

            string url = BaseUrl + "/keys/" + Licensing.MachineId() + ".txt";
            string body = await GetStringOrNullAsync(url).ConfigureAwait(false);
            if (string.IsNullOrEmpty(body)) return false;

            string error;
            return Licensing.Install(body.Trim(), out error);
        }

        /// <summary>
        /// Reads version.txt, which holds the latest version on the first line and
        /// an optional download URL on the second.
        /// </summary>
        public static async Task<UpdateInfo> TryFetchVersionAsync()
        {
            string body = await GetStringOrNullAsync(BaseUrl + "/version.txt").ConfigureAwait(false);
            if (string.IsNullOrEmpty(body)) return null;

            string[] lines = body.Replace("\r", string.Empty).Split('\n');
            if (lines.Length == 0) return null;

            Version latest;
            if (!Version.TryParse(lines[0].Trim().TrimStart('v', 'V'), out latest)) return null;

            UpdateInfo info = new UpdateInfo();
            info.Latest = latest;
            info.DownloadUrl = lines.Length > 1 ? lines[1].Trim() : BaseUrl;
            return info;
        }

        /// <summary>
        /// Reads the current contact address from the site, so a change of address
        /// reaches copies that were built long before it existed.
        /// </summary>
        public static async Task<bool> TryFetchContactAsync()
        {
            string body = await GetStringOrNullAsync(BaseUrl + "/contact.txt").ConfigureAwait(false);
            if (string.IsNullOrEmpty(body)) return false;

            return Licensing.TrySetContactOverride(body);
        }

        private static async Task<string> GetStringOrNullAsync(string url)
        {
            try
            {
                using (HttpResponseMessage response = await Client.GetAsync(url).ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) return null;   // 404 = nothing waiting for us
                    return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                }
            }
            catch
            {
                return null;   // offline, blocked, DNS failure - all equally fine
            }
        }
    }

    public class UpdateInfo
    {
        public Version Latest;
        public string DownloadUrl;

        public bool IsNewerThanThis
        {
            get
            {
                try
                {
                    Version current = typeof(UpdateInfo).Assembly.GetName().Version;
                    return Latest != null && current != null && Latest > current;
                }
                catch
                {
                    return false;
                }
            }
        }

        public string Description
        {
            get
            {
                return Latest == null
                    ? string.Empty
                    : string.Format(CultureInfo.CurrentCulture, "Version {0} is available", Latest);
            }
        }
    }
}
