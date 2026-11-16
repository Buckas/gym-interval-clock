using System.Collections.Generic;

namespace GymClock.Link
{
    /// <summary>One page of the built-in Link displays help.</summary>
    public sealed class LinkHelpTopic
    {
        public string Id { get; set; }
        public string Title { get; set; }
        /// <summary>
        /// The page, one entry per paragraph. A line starting with "# " is a heading; "1. " etc. a
        /// numbered step; "- " a bullet. Everything else is plain text.
        /// </summary>
        public string[] Lines { get; set; }
    }

    /// <summary>
    /// The Link displays guide, kept in the app so a teacher never needs a separate document:
    /// what it does, how to set it up on a network and over Bluetooth, what to do when it won't
    /// connect, and what a second screen can do. Shown by <see cref="LinkHelpForm"/>.
    ///
    /// The wording quotes the app's own buttons and status lines exactly - the tests check that.
    /// </summary>
    public static class LinkHelp
    {
        public const string Start = "start";
        public const string Network = "network";
        public const string Bluetooth = "bluetooth";
        public const string Trouble = "trouble";
        public const string SecondScreen = "second";
        public const string InStep = "instep";

        public static readonly IReadOnlyList<LinkHelpTopic> Topics = new List<LinkHelpTopic>
        {
            new LinkHelpTopic
            {
                Id = Start, Title = "Quick start",
                Lines = new[]
                {
                    "# What Link displays does",
                    "It shows one session on two or more PCs, in step. The MAIN PC runs the session as normal. "
                    + "Every SECOND SCREEN shows the same program and countdown, each on its own screen or projector.",
                    "Start, pause, skip, back and reset on the main PC appear on every second screen straight away. "
                    + "A second screen only displays: nobody can change the session from it.",
                    "# Setting it up",
                    "1. On the main PC press L and choose \"This is the MAIN PC\". That is all: the link is on, "
                    + "and a link code (4 digits) is shown in the window and at the bottom of the clock screen.",
                    "2. On the other PC press L, choose \"This is a SECOND SCREEN\" and type the link code.",
                    "3. Wait a few seconds. The second PC finds the main PC by itself - over the network, or over "
                    + "Bluetooth if the PCs are paired - and then shows \"Linked to\" and the main PC's name.",
                    "Both PCs must be running the same version of Gym Interval Clock.",
                    "# What you need",
                    "- Both PCs on the same network (school Wi-Fi, a phone hotspot, or a cable), OR",
                    "- The two PCs paired by Bluetooth - see \"Bluetooth setup\".",
                    "No internet is needed, and nothing is sent outside the room.",
                    "# Next time",
                    "Both PCs remember their choice. Start Gym Interval Clock on each and they link again by "
                    + "themselves. To stop, press L and choose \"Turn linking off\"."
                }
            },
            new LinkHelpTopic
            {
                Id = Network, Title = "Network setup",
                Lines = new[]
                {
                    "# The usual way: the same network",
                    "1. Connect both PCs to the same network - the same Wi-Fi name, or the same phone hotspot.",
                    "2. Main PC: press L, choose \"This is the MAIN PC\".",
                    "3. Second PC: press L, choose \"This is a SECOND SCREEN\", type the link code.",
                    "The Link displays window shows each PC's network and address, so you can check they match.",
                    "# The Windows firewall question",
                    "The first time, Windows may ask whether to allow Gym Interval Clock on the network. Choose "
                    + "Allow on both PCs. If it was refused, or the PC is managed by the school, ask IT to allow "
                    + "Gym Interval Clock (UDP port 47215 and TCP port 47216).",
                    "# If the second PC cannot find the main PC",
                    "Some networks - school Wi-Fi especially - do not let PCs see each other. Try these in order:",
                    "1. Type the address. On the main PC the Link displays window shows \"This PC's address\". "
                    + "On the second PC open \"More options\" and type it in \"Main PC's address\".",
                    "2. Use a phone hotspot. Turn on the hotspot on a phone and connect both PCs to it. "
                    + "No mobile data is used by the link.",
                    "3. Use Bluetooth - see \"Bluetooth setup\".",
                    "4. Join the two PCs with a network cable.",
                    "# Which network is best?",
                    "Any network where the link test says GOOD. A phone hotspot or a small travel router in the gym "
                    + "is usually the most reliable, because nothing else is sharing it."
                }
            },
            new LinkHelpTopic
            {
                Id = Bluetooth, Title = "Bluetooth setup",
                Lines = new[]
                {
                    "Use Bluetooth where there is no network the two PCs can share. It reaches about 10 metres, "
                    + "so keep the PCs in the same room.",
                    "Bluetooth is set up once in Windows. After that Gym Interval Clock uses it by itself: "
                    + "there is nothing to switch on in the app.",
                    "# In Windows",
                    "1. Turn Bluetooth on (both PCs). Open Settings > Bluetooth & devices and switch Bluetooth on. "
                    + "Leave this Settings page open on both PCs while you pair them - a PC can only be found while it is open.",
                    "2. Pair the two PCs (once only). On the second PC choose Add device > Bluetooth, then pick the "
                    + "main PC's name. A number appears on both screens: check they match, then choose Connect on one "
                    + "PC and Yes on the other.",
                    "- If the main PC is not in the list: on the second PC, scroll down in Bluetooth & devices > "
                    + "Devices, set \"Bluetooth devices discovery\" to Advanced, and try Add device again.",
                    "3. Join the Personal Area Network (second PC). Right-click the Bluetooth icon near the clock and "
                    + "choose \"Join a Personal Area Network\". Click the main PC, then choose Connect using > Direct "
                    + "connection. Do this each time the PCs are switched on.",
                    "The two buttons below open the Windows screens for steps 1-2 and step 3.",
                    "# In Gym Interval Clock",
                    "4. Main PC: press L, choose \"This is the MAIN PC\". The window shows \"Bluetooth: ready\".",
                    "5. Second PC: press L, choose \"This is a SECOND SCREEN\", type the link code. It tries the "
                    + "network first, then Bluetooth. Connecting by Bluetooth can take several seconds.",
                    "# If Bluetooth will not connect",
                    "- Check Bluetooth is switched on at both PCs.",
                    "- Check each PC lists the other as Paired in Settings > Bluetooth & devices.",
                    "- Check the main PC's Link displays window says \"Bluetooth: ready\".",
                    "- If more than one computer is paired with the second PC, open \"More options\" there and choose "
                    + "the main PC under \"Bluetooth main PC\"."
                }
            },
            new LinkHelpTopic
            {
                Id = Trouble, Title = "Not connecting?",
                Lines = new[]
                {
                    "Find the message you see at the bottom of the clock screen, or in the Link displays window.",
                    "# \"No second screen yet\" (main PC)",
                    "The main PC is ready and waiting. Set up the second PC: press L there, choose "
                    + "\"This is a SECOND SCREEN\" and type the link code.",
                    "# \"Looking for the main PC\" / \"The main PC was not found\" (second PC)",
                    "- Is Gym Interval Clock running on the main PC, set as the MAIN PC?",
                    "- Are both PCs on the same network? The Link displays window shows each PC's network.",
                    "- Did Windows ask about the firewall on either PC? It must be allowed - see \"Network setup\".",
                    "- If the network keeps PCs apart, type the main PC's address in \"More options\", use a phone "
                    + "hotspot, or use Bluetooth.",
                    "# \"Link code not accepted\"",
                    "The code typed on the second PC is not the main PC's code. Press L on the main PC to read the "
                    + "code again, then press L on the second PC and retype it. If \"New code\" is pressed on the main "
                    + "PC, every second screen needs the new code.",
                    "# \"Reconnecting to the main PC\"",
                    "The link dropped - the main PC was closed, went to sleep, or moved out of range. The second "
                    + "screen keeps counting by itself and links again as soon as the main PC is back.",
                    "# \"Link not started\" (main PC)",
                    "Another program is using the link's network port, or Gym Interval Clock is already running "
                    + "on this PC. Close the other copy and press L again.",
                    "# It links, but the screens are out of step",
                    "Open the Link displays window on the second PC and read the link test. If it says USABLE or "
                    + "POOR, the network is slow: move the PCs closer to the Wi-Fi, or use a phone hotspot.",
                    "# Still stuck?",
                    "Press F3 and choose \"Save report...\" to make a support file, and send it with a note of the "
                    + "message you see."
                }
            },
            new LinkHelpTopic
            {
                Id = SecondScreen, Title = "On a second screen",
                Lines = new[]
                {
                    "# What it shows",
                    "A second screen shows the main PC's program, colours and countdown. Its own program and "
                    + "settings are kept safe, and come back when linking is turned off.",
                    "# Keys that work",
                    "- L - Link displays (this setup window)",
                    "- M - sound on or off for this screen",
                    "- F11 - full screen,  F6 - move to the next display",
                    "- F3 - licence,  Q - quit",
                    "The session keys (Space, arrows, R, 1-9) do nothing here: the main PC has the controls.",
                    "# Sound",
                    "Sound is off on a second screen unless \"Play the sounds on this screen too\" is ticked in "
                    + "\"More options\" (or press M). Turn it on for a speaker at the far end of the gym.",
                    "# Showing only some stations",
                    "For a program where every station has its own clock (Parallel), a second screen can show "
                    + "just some of the stations, with bigger tiles. In \"More options\" set \"Show stations\", for "
                    + "example 3 to 4, and leave the main PC showing them all or set a range there too. "
                    + "Other programs always show the same picture as the main PC.",
                    "# If the link drops",
                    "The second screen keeps counting by itself, shows \"Reconnecting\", and catches up without a "
                    + "jump when the link returns. A second screen switched on late joins at the right point."
                }
            },
            new LinkHelpTopic
            {
                Id = InStep, Title = "Checking it's in step",
                Lines = new[]
                {
                    "# The link test",
                    "The link test runs by itself as soon as a second screen links. Open the Link displays window "
                    + "(L) on the second PC to read it:",
                    "- GOOD - the screens will stay in step.",
                    "- USABLE - it works, but the connection is slow or uneven.",
                    "- POOR - too slow or unsteady. Use another network, a phone hotspot or Bluetooth.",
                    "# During a session",
                    "On the second PC press H. The bottom line shows, for example, \"IN STEP WITHIN 15 MS\". "
                    + "That is the most the two screens can differ, in thousandths of a second. Anything under 100 "
                    + "cannot be seen.",
                    "# By eye",
                    "Stand where you can see both screens and watch the seconds change. They should change together."
                }
            }
        };

        public static LinkHelpTopic Find(string id)
        {
            foreach (LinkHelpTopic topic in Topics)
            {
                if (topic.Id == id) return topic;
            }
            return Topics[0];
        }
    }
}
