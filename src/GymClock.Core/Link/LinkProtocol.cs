using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GymClock.Link
{
    /// <summary>
    /// One message between the main PC and a second screen (docs/plans/linked-displays.md, section 3).
    /// Only the fields a message type uses are set; the rest are left out of the JSON.
    /// </summary>
    public sealed class LinkMessage
    {
        public const string Announce = "ANNOUNCE";
        public const string Hello = "HELLO";
        public const string Program = "PROGRAM";
        public const string State = "STATE";
        public const string Ping = "PING";
        public const string Pong = "PONG";
        public const string Bye = "BYE";

        public int V { get; set; } = LinkProtocol.Version;
        public string Type { get; set; }
        public long Seq { get; set; }

        // ANNOUNCE / HELLO
        public string Name { get; set; }
        public int? Port { get; set; }
        /// <summary>Proof the announcer knows the link code, so a second screen picks the right main PC.</summary>
        public string Tag { get; set; }

        // PROGRAM
        public string Settings { get; set; }
        public string Script { get; set; }

        // STATE
        public int? Phase { get; set; }
        public int? Index { get; set; }
        public long? PhaseElapsedMs { get; set; }
        public long? SessionElapsedMs { get; set; }
        public bool? Paused { get; set; }
        public long? LeaderTimeMs { get; set; }

        // PING / PONG
        public long? T0 { get; set; }
    }

    /// <summary>Ports, the JSON-lines wire format and the link-code signature.</summary>
    public static class LinkProtocol
    {
        public const int Version = 1;
        public const int DiscoveryPort = 47215;
        public const int DataPort = 47216;

        /// <summary>Sent unsigned when a second screen's HELLO fails the code check.</summary>
        public const string DeniedLine = "DENIED";

        private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <summary>The signing key for a link code. Both PCs derive the same key from the same code.</summary>
        public static byte[] KeyFromCode(string code)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes("GymClockLink/v1/" + (code ?? string.Empty).Trim()));
            }
        }

        public static string NewCode()
        {
            return RandomNumberGenerator.GetInt32(1000, 10000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>One line on the wire: the signature, a space, then the JSON.</summary>
        public static string Encode(LinkMessage message, byte[] key)
        {
            string json = JsonSerializer.Serialize(message, Json);
            return Sign(json, key) + " " + json;
        }

        /// <summary>Reads a line, accepting it only if it was signed with the same link code.</summary>
        public static bool TryDecode(string line, byte[] key, out LinkMessage message)
        {
            message = null;
            if (string.IsNullOrEmpty(line)) return false;
            int space = line.IndexOf(' ');
            if (space <= 0 || space == line.Length - 1) return false;

            string signature = line.Substring(0, space);
            string json = line.Substring(space + 1);
            byte[] expected = Encoding.ASCII.GetBytes(Sign(json, key));
            byte[] actual = Encoding.ASCII.GetBytes(signature);
            if (!CryptographicOperations.FixedTimeEquals(expected, actual)) return false;

            try
            {
                message = JsonSerializer.Deserialize<LinkMessage>(json, Json);
            }
            catch (JsonException)
            {
                return false;
            }
            return message != null && message.Type != null && message.V == Version;
        }

        /// <summary>The tag a main PC puts in its announcements; only a PC with the same code can produce it.</summary>
        public static string AnnounceTag(byte[] key, string name)
        {
            return Sign("announce/" + (name ?? string.Empty), key).Substring(0, 16);
        }

        /// <summary>Announcements are broadcast unsigned (the tag is the proof), as plain JSON.</summary>
        public static string EncodeAnnounce(string name, byte[] key, int port = DataPort)
        {
            return JsonSerializer.Serialize(new LinkMessage
            {
                Type = LinkMessage.Announce, Name = name, Port = port, Tag = AnnounceTag(key, name)
            }, Json);
        }

        public static bool TryDecodeAnnounce(byte[] bytes, byte[] key, out LinkMessage message)
        {
            message = null;
            try
            {
                message = JsonSerializer.Deserialize<LinkMessage>(bytes, Json);
            }
            catch (JsonException)
            {
                return false;
            }
            return message != null && message.Type == LinkMessage.Announce && message.V == Version
                && message.Tag == AnnounceTag(key, message.Name);
        }

        private static string Sign(string text, byte[] key)
        {
            using (HMACSHA256 hmac = new HMACSHA256(key))
            {
                return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(text)));
            }
        }

        // ------------------------------------------------------------------ network helpers

        /// <summary>This PC's IPv4 addresses on networks that are up (what a teacher types on the second screen).</summary>
        public static List<UnicastIPAddressInformation> LocalAddresses()
        {
            List<UnicastIPAddressInformation> result = new List<UnicastIPAddressInformation>();
            try
            {
                foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    foreach (UnicastIPAddressInformation a in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (a.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal)) continue; // adapter with no network
                        result.Add(a);
                    }
                }
            }
            catch { }
            return result;
        }

        /// <summary>The broadcast address of one network (some routers pass this but not 255.255.255.255).</summary>
        public static IPAddress DirectedBroadcast(UnicastIPAddressInformation address)
        {
            try
            {
                byte[] ip = address.Address.GetAddressBytes();
                byte[] mask = address.IPv4Mask.GetAddressBytes();
                byte[] broadcast = new byte[4];
                for (int i = 0; i < 4; i++) broadcast[i] = (byte)(ip[i] | ~mask[i]);
                return new IPAddress(broadcast);
            }
            catch
            {
                return null;
            }
        }
    }
}
