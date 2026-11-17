using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace GymClock.Link
{
    /// <summary>A snapshot of the link for the setup window to show: what this PC is, and how the link is doing.</summary>
    public sealed class LinkStatus
    {
        /// <summary>"off", "lead" or "follow".</summary>
        public string Role = "off";
        public string Code = string.Empty;

        // ---- main PC
        public bool LeaderRunning;
        /// <summary>Why the link could not start on the main PC, or empty.</summary>
        public string LeaderError = string.Empty;
        /// <summary>The linked second screens, e.g. "GYM-PC2 (network)".</summary>
        public List<string> Screens = new List<string>();
        public bool BluetoothListening;

        // ---- second screen
        public FollowerStatus FollowerState = FollowerStatus.Searching;
        /// <summary>What it is doing right now ("Looking for the main PC on the network...").</summary>
        public string Activity = string.Empty;
        public string LeaderName = string.Empty;
        /// <summary>"network" or "Bluetooth" while linked.</summary>
        public string Via = string.Empty;
        public bool ClockMatched;
        public LinkQuality Quality = new LinkQuality();
        /// <summary>How many times every route has been tried without finding the main PC.</summary>
        public int FailedRounds;
        /// <summary>True while a session is counting down on this second screen.</summary>
        public bool SessionRunning;
        /// <summary>Largest recent difference from the main PC's countdown (ms), while a session runs.</summary>
        public double DriftPeakMs;
    }

    /// <summary>One network this PC is connected to.</summary>
    public sealed class LinkNetwork
    {
        public string Name { get; set; }
        public string Address { get; set; }
        /// <summary>True for a Bluetooth Personal Area Network connection.</summary>
        public bool IsBluetooth { get; set; }

        public override string ToString()
        {
            return Name + " (" + Address + ")";
        }
    }

    /// <summary>What this PC has to link with: its networks and its Bluetooth. Used for the setup window's checks.</summary>
    public static class LinkEnvironment
    {
        /// <summary>The networks this PC is connected to right now (IPv4, with an address).</summary>
        public static List<LinkNetwork> Networks()
        {
            List<LinkNetwork> result = new List<LinkNetwork>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation a in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        string address = a.Address.ToString();
                        if (address.StartsWith("169.254.", StringComparison.Ordinal)) continue; // adapter with no network
                        result.Add(new LinkNetwork
                        {
                            Name = nic.Name,
                            Address = address,
                            IsBluetooth = (nic.Description ?? string.Empty).IndexOf("Bluetooth", StringComparison.OrdinalIgnoreCase) >= 0
                        });
                    }
                }
            }
            catch { }
            return result;
        }

        public static bool HasBluetooth()
        {
            return BluetoothLink.HasRadio();
        }

        /// <summary>The computers paired with this PC by Bluetooth (not phones, headphones and so on).</summary>
        public static List<BluetoothDevice> PairedComputers()
        {
            if (!BluetoothLink.HasRadio()) return new List<BluetoothDevice>();
            return BluetoothLink.PairedDevices().Where(d => d.IsComputer).ToList();
        }

        /// <summary>"Wi-Fi (192.168.1.20)" / "Not connected to a network".</summary>
        public static string DescribeNetworks(IReadOnlyList<LinkNetwork> networks)
        {
            if (networks == null || networks.Count == 0) return "Not connected to a network";
            return string.Join(",  ", networks.Select(n => n.ToString()));
        }
    }
}
