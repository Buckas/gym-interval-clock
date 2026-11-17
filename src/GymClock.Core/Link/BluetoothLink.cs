using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

namespace GymClock.Link
{
    /// <summary>A Bluetooth device paired with this PC in Windows.</summary>
    public sealed class BluetoothDevice
    {
        public ulong Address { get; set; }
        public string Name { get; set; }
        /// <summary>True for a computer (laptop/desktop) rather than headphones, a phone and so on.</summary>
        public bool IsComputer { get; set; }

        public string AddressText { get { return BluetoothLink.FormatAddress(Address); } }

        public override string ToString()
        {
            return string.IsNullOrWhiteSpace(Name) ? AddressText : Name + "  (" + AddressText + ")";
        }
    }

    /// <summary>
    /// Linked displays over Bluetooth (plan Phase 4), for venues with no usable network. The two
    /// PCs are paired in Windows first; the main PC then listens on an RFCOMM channel advertised
    /// under <see cref="ServiceId"/>, and the second screen connects to the paired main PC by that id.
    /// After that the link is the same stream of signed lines as on the network.
    ///
    /// Uses Windows' own Bluetooth sockets (ws2_32) and device list (BluetoothApis) directly, so
    /// no extra library is needed. Everything here fails soft: no radio or Bluetooth switched off
    /// just means this transport is unavailable.
    /// </summary>
    public static class BluetoothLink
    {
        /// <summary>The service id Gym Interval Clock's link is advertised under.</summary>
        public static readonly Guid ServiceId = new Guid("6f0c7a52-3b1e-4d7a-9a57-47796d436c6b");

        public const string ServiceName = "Gym Interval Clock Link";

        /// <summary>Sizes of the Windows structures as this build lays them out (checked by the tests).</summary>
        public static int SocketAddressSize { get { return Marshal.SizeOf(typeof(BtNative.SOCKADDR_BTH)); } }
        public static int DeviceInfoSize { get { return Marshal.SizeOf(typeof(BtNative.BLUETOOTH_DEVICE_INFO)); } }
        public static int ServiceAddressSize { get { return Marshal.SizeOf(typeof(BtNative.CSADDR_INFO)); } }
        public static int ServiceQuerySize { get { return Marshal.SizeOf(typeof(BtNative.WSAQUERYSET)); } }

        /// <summary>"00:1A:7D:DA:71:13" from the 48-bit address.</summary>
        public static string FormatAddress(ulong address)
        {
            string[] parts = new string[6];
            for (int i = 0; i < 6; i++)
            {
                parts[i] = ((address >> (8 * (5 - i))) & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
            }
            return string.Join(":", parts);
        }

        /// <summary>Reads "00:1A:7D:DA:71:13", "00-1A-7D-DA-71-13" or "001A7DDA7113".</summary>
        public static bool TryParseAddress(string text, out ulong address)
        {
            address = 0;
            string hex = (text ?? string.Empty).Replace(":", string.Empty).Replace("-", string.Empty).Trim();
            if (hex.Length != 12) return false;
            return ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out address) && address != 0;
        }

        /// <summary>True when this PC has a Bluetooth radio that is switched on.</summary>
        public static bool HasRadio()
        {
            try
            {
                BtNative.BLUETOOTH_FIND_RADIO_PARAMS search = new BtNative.BLUETOOTH_FIND_RADIO_PARAMS();
                search.dwSize = Marshal.SizeOf(typeof(BtNative.BLUETOOTH_FIND_RADIO_PARAMS));
                IntPtr find = BtNative.BluetoothFindFirstRadio(ref search, out IntPtr radio);
                if (find == IntPtr.Zero) return false;
                BtNative.CloseHandle(radio);
                BtNative.BluetoothFindRadioClose(find);
                return true;
            }
            catch
            {
                return false; // no Bluetooth support on this Windows installation
            }
        }

        /// <summary>The devices paired with this PC, computers first. Empty if there is no radio.</summary>
        public static List<BluetoothDevice> PairedDevices()
        {
            List<BluetoothDevice> devices = new List<BluetoothDevice>();
            try
            {
                BtNative.BLUETOOTH_DEVICE_SEARCH_PARAMS search = new BtNative.BLUETOOTH_DEVICE_SEARCH_PARAMS();
                search.dwSize = Marshal.SizeOf(typeof(BtNative.BLUETOOTH_DEVICE_SEARCH_PARAMS));
                search.fReturnAuthenticated = 1; // paired
                search.fReturnRemembered = 1;
                search.fReturnConnected = 1;
                search.fReturnUnknown = 0;
                search.fIssueInquiry = 0;        // no scan: only what Windows already knows

                BtNative.BLUETOOTH_DEVICE_INFO info = new BtNative.BLUETOOTH_DEVICE_INFO();
                info.dwSize = Marshal.SizeOf(typeof(BtNative.BLUETOOTH_DEVICE_INFO));

                IntPtr find = BtNative.BluetoothFindFirstDevice(ref search, ref info);
                if (find == IntPtr.Zero) return devices;
                try
                {
                    do
                    {
                        if (info.Address != 0 && info.fAuthenticated != 0)
                        {
                            devices.Add(new BluetoothDevice
                            {
                                Address = info.Address,
                                Name = info.szName ?? string.Empty,
                                IsComputer = ((info.ulClassofDevice >> 8) & 0x1F) == 1 // major device class: computer
                            });
                        }
                        info.dwSize = Marshal.SizeOf(typeof(BtNative.BLUETOOTH_DEVICE_INFO));
                    }
                    while (BtNative.BluetoothFindNextDevice(find, ref info));
                }
                finally
                {
                    BtNative.BluetoothFindDeviceClose(find);
                }
            }
            catch { /* no Bluetooth support */ }

            devices.Sort((a, b) =>
            {
                if (a.IsComputer != b.IsComputer) return a.IsComputer ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return devices;
        }
    }

    /// <summary>The main PC's Bluetooth listener: an RFCOMM channel advertised under the link's service id.</summary>
    public sealed class BluetoothLinkListener : ILinkListener
    {
        private IntPtr _socket = BtNative.InvalidSocket;
        private IntPtr _registration = IntPtr.Zero;
        private readonly List<IntPtr> _registrationMemory = new List<IntPtr>();
        private readonly object _lock = new object();

        public string Kind { get { return "Bluetooth"; } }

        /// <summary>The RFCOMM channel in use once started (for diagnostics).</summary>
        public int Channel { get; private set; }

        public bool Start(out string error)
        {
            error = null;
            if (!BluetoothLink.HasRadio())
            {
                error = "this PC has no Bluetooth, or it is switched off.";
                return false;
            }

            try
            {
                BtNative.EnsureStarted();
                IntPtr socket = BtNative.socket(BtNative.AF_BTH, BtNative.SOCK_STREAM, BtNative.BTHPROTO_RFCOMM);
                if (socket == BtNative.InvalidSocket)
                {
                    error = "Bluetooth could not be opened (error " + Marshal.GetLastWin32Error() + ").";
                    return false;
                }
                _socket = socket;

                BtNative.SOCKADDR_BTH local = new BtNative.SOCKADDR_BTH
                {
                    AddressFamily = BtNative.AF_BTH, BtAddr = 0, ServiceClassId = Guid.Empty, Port = BtNative.BT_PORT_ANY
                };
                int size = Marshal.SizeOf(typeof(BtNative.SOCKADDR_BTH));
                if (BtNative.bind(socket, ref local, size) != 0 || BtNative.listen(socket, 4) != 0)
                {
                    error = "Bluetooth could not start listening (error " + Marshal.GetLastWin32Error() + ").";
                    Dispose();
                    return false;
                }

                // Which channel did Windows give us? Advertise it so second screens can find it by service id.
                if (BtNative.getsockname(socket, ref local, ref size) != 0)
                {
                    error = "Bluetooth channel could not be read (error " + Marshal.GetLastWin32Error() + ").";
                    Dispose();
                    return false;
                }
                Channel = (int)local.Port;

                if (!Register(local, out int registerError))
                {
                    error = "the Bluetooth service could not be advertised (error " + registerError + ").";
                    Dispose();
                    return false;
                }
                return true;
            }
            catch (Exception ex) when (ex is DllNotFoundException || ex is EntryPointNotFoundException)
            {
                error = "Bluetooth is not available on this PC.";
                return false;
            }
        }

        /// <summary>Publishes the SDP record: "service <see cref="BluetoothLink.ServiceId"/> is on this RFCOMM channel".</summary>
        private bool Register(BtNative.SOCKADDR_BTH local, out int error)
        {
            error = 0;
            int addressSize = Marshal.SizeOf(typeof(BtNative.SOCKADDR_BTH));

            IntPtr address = Allocate(addressSize);
            Marshal.StructureToPtr(local, address, false);

            IntPtr serviceId = Allocate(Marshal.SizeOf(typeof(Guid)));
            Marshal.StructureToPtr(BluetoothLink.ServiceId, serviceId, false);

            BtNative.CSADDR_INFO csAddr = new BtNative.CSADDR_INFO
            {
                LocalAddr = new BtNative.SOCKET_ADDRESS { lpSockaddr = address, iSockaddrLength = addressSize },
                RemoteAddr = new BtNative.SOCKET_ADDRESS { lpSockaddr = IntPtr.Zero, iSockaddrLength = 0 },
                iSocketType = BtNative.SOCK_STREAM, iProtocol = BtNative.BTHPROTO_RFCOMM
            };
            IntPtr csAddrPtr = Allocate(Marshal.SizeOf(typeof(BtNative.CSADDR_INFO)));
            Marshal.StructureToPtr(csAddr, csAddrPtr, false);

            IntPtr name = Marshal.StringToHGlobalUni(BluetoothLink.ServiceName);
            _registrationMemory.Add(name);

            BtNative.WSAQUERYSET query = new BtNative.WSAQUERYSET
            {
                dwSize = Marshal.SizeOf(typeof(BtNative.WSAQUERYSET)),
                lpszServiceInstanceName = name,
                lpServiceClassId = serviceId,
                dwNameSpace = BtNative.NS_BTH,
                dwNumberOfCsAddrs = 1,
                lpcsaBuffer = csAddrPtr
            };
            IntPtr queryPtr = Allocate(Marshal.SizeOf(typeof(BtNative.WSAQUERYSET)));
            Marshal.StructureToPtr(query, queryPtr, false);

            if (BtNative.WSASetService(queryPtr, BtNative.RNRSERVICE_REGISTER, 0) != 0)
            {
                error = Marshal.GetLastWin32Error();
                return false;
            }
            _registration = queryPtr;
            return true;
        }

        private IntPtr Allocate(int size)
        {
            IntPtr memory = Marshal.AllocHGlobal(size);
            _registrationMemory.Add(memory);
            return memory;
        }

        public ILinkConnection Accept()
        {
            IntPtr listening = _socket;
            if (listening == BtNative.InvalidSocket) return null;

            BtNative.SOCKADDR_BTH remote = new BtNative.SOCKADDR_BTH();
            int size = Marshal.SizeOf(typeof(BtNative.SOCKADDR_BTH));
            IntPtr accepted = BtNative.accept(listening, ref remote, ref size);
            if (accepted == BtNative.InvalidSocket) return null; // closed by Dispose
            return new BluetoothLinkConnection(accepted, remote.BtAddr);
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_registration != IntPtr.Zero)
                {
                    try { BtNative.WSASetService(_registration, BtNative.RNRSERVICE_DELETE, 0); } catch { }
                    _registration = IntPtr.Zero;
                }
                if (_socket != BtNative.InvalidSocket)
                {
                    try { BtNative.closesocket(_socket); } catch { } // also wakes a blocked Accept
                    _socket = BtNative.InvalidSocket;
                }
                foreach (IntPtr memory in _registrationMemory) Marshal.FreeHGlobal(memory);
                _registrationMemory.Clear();
            }
        }
    }

    /// <summary>An open Bluetooth (RFCOMM) connection, either end.</summary>
    public sealed class BluetoothLinkConnection : ILinkConnection
    {
        private readonly BluetoothSocketStream _stream;

        internal BluetoothLinkConnection(IntPtr socket, ulong remoteAddress)
        {
            _stream = new BluetoothSocketStream(socket);
            Remote = "Bluetooth " + BluetoothLink.FormatAddress(remoteAddress);
        }

        public Stream Stream { get { return _stream; } }
        public string Remote { get; }

        public void SetReceiveTimeout(int milliseconds)
        {
            _stream.SetReceiveTimeout(milliseconds);
        }

        /// <summary>
        /// Second screen: connects to the paired main PC. Windows looks up the link's channel on that
        /// PC by service id. Returns null when the main PC is out of range, off, or not linking.
        /// </summary>
        public static BluetoothLinkConnection Connect(ulong mainPcAddress, CancellationToken stop)
        {
            if (!BluetoothLink.HasRadio()) return null;

            BtNative.EnsureStarted();
            IntPtr socket = BtNative.socket(BtNative.AF_BTH, BtNative.SOCK_STREAM, BtNative.BTHPROTO_RFCOMM);
            if (socket == BtNative.InvalidSocket) return null;

            BtNative.SOCKADDR_BTH remote = new BtNative.SOCKADDR_BTH
            {
                AddressFamily = BtNative.AF_BTH, BtAddr = mainPcAddress, ServiceClassId = BluetoothLink.ServiceId, Port = 0
            };

            // A Bluetooth connect can take several seconds; closing the socket is the only way to give up early.
            bool connected;
            using (stop.Register(() => { try { BtNative.closesocket(socket); } catch { } }))
            {
                connected = BtNative.connect(socket, ref remote, Marshal.SizeOf(typeof(BtNative.SOCKADDR_BTH))) == 0;
            }

            if (stop.IsCancellationRequested) return null; // already closed by the cancellation
            if (!connected)
            {
                BtNative.closesocket(socket);
                return null;
            }
            return new BluetoothLinkConnection(socket, mainPcAddress);
        }

        public void Dispose()
        {
            _stream.Dispose();
        }
    }

    /// <summary>A Stream over a Windows Bluetooth socket handle.</summary>
    internal sealed class BluetoothSocketStream : Stream
    {
        private IntPtr _socket;
        private readonly object _closeLock = new object();

        public BluetoothSocketStream(IntPtr socket)
        {
            _socket = socket;
        }

        public void SetReceiveTimeout(int milliseconds)
        {
            int value = milliseconds;
            BtNative.setsockopt(_socket, BtNative.SOL_SOCKET, BtNative.SO_RCVTIMEO, ref value, sizeof(int));
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0) return 0;
            GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                int read = BtNative.recv(_socket, Marshal.UnsafeAddrOfPinnedArrayElement(buffer, offset), count, 0);
                if (read < 0) throw new IOException("Bluetooth read failed (error " + Marshal.GetLastWin32Error() + ").");
                return read; // 0 = the other end closed
            }
            finally
            {
                pin.Free();
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            GCHandle pin = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                int sent = 0;
                while (sent < count)
                {
                    int n = BtNative.send(_socket, Marshal.UnsafeAddrOfPinnedArrayElement(buffer, offset + sent), count - sent, 0);
                    if (n <= 0) throw new IOException("Bluetooth write failed (error " + Marshal.GetLastWin32Error() + ").");
                    sent += n;
                }
            }
            finally
            {
                pin.Free();
            }
        }

        protected override void Dispose(bool disposing)
        {
            lock (_closeLock)
            {
                if (_socket != BtNative.InvalidSocket)
                {
                    try { BtNative.closesocket(_socket); } catch { }
                    _socket = BtNative.InvalidSocket;
                }
            }
            base.Dispose(disposing);
        }

        public override bool CanRead { get { return true; } }
        public override bool CanWrite { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override void Flush() { }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position { get { throw new NotSupportedException(); } set { throw new NotSupportedException(); } }
        public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
        public override void SetLength(long value) { throw new NotSupportedException(); }
    }

    /// <summary>The Windows calls behind Bluetooth sockets and the paired-device list.</summary>
    internal static class BtNative
    {
        public const ushort AF_BTH = 32;
        public const int SOCK_STREAM = 1;
        public const int BTHPROTO_RFCOMM = 3;
        public const uint BT_PORT_ANY = 0xFFFFFFFF;
        public const int NS_BTH = 16;
        public const int RNRSERVICE_REGISTER = 0;
        public const int RNRSERVICE_DELETE = 2;
        public const int SOL_SOCKET = 0xFFFF;
        public const int SO_RCVTIMEO = 0x1006;
        public static readonly IntPtr InvalidSocket = new IntPtr(-1);

        /// <summary>A Bluetooth socket address. Packed: Windows declares it with 1-byte packing (30 bytes).</summary>
        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct SOCKADDR_BTH
        {
            public ushort AddressFamily;
            public ulong BtAddr;
            public Guid ServiceClassId;
            public uint Port;
        }

        /// <summary>A pointer to a socket address and its length (16 bytes on 64-bit, with padding).</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct SOCKET_ADDRESS
        {
            public IntPtr lpSockaddr;
            public int iSockaddrLength;
        }

        // The two addresses must stay as nested structures: each is padded to 16 bytes, which puts
        // iSocketType at offset 32. Flattening them moves it to 28 and Windows rejects the record.
        [StructLayout(LayoutKind.Sequential)]
        public struct CSADDR_INFO
        {
            public SOCKET_ADDRESS LocalAddr;
            public SOCKET_ADDRESS RemoteAddr;
            public int iSocketType;
            public int iProtocol;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WSAQUERYSET
        {
            public int dwSize;
            public IntPtr lpszServiceInstanceName;
            public IntPtr lpServiceClassId;
            public IntPtr lpVersion;
            public IntPtr lpszComment;
            public int dwNameSpace;
            public IntPtr lpNSProviderId;
            public IntPtr lpszContext;
            public int dwNumberOfProtocols;
            public IntPtr lpafpProtocols;
            public IntPtr lpszQueryString;
            public int dwNumberOfCsAddrs;
            public IntPtr lpcsaBuffer;
            public int dwOutputFlags;
            public IntPtr lpBlob;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BLUETOOTH_FIND_RADIO_PARAMS
        {
            public int dwSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            public int dwSize;
            public int fReturnAuthenticated;
            public int fReturnRemembered;
            public int fReturnUnknown;
            public int fReturnConnected;
            public int fIssueInquiry;
            public byte cTimeoutMultiplier;
            public IntPtr hRadio;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEMTIME
        {
            public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct BLUETOOTH_DEVICE_INFO
        {
            public int dwSize;
            public ulong Address;
            public uint ulClassofDevice;
            public int fConnected;
            public int fRemembered;
            public int fAuthenticated;
            public SYSTEMTIME stLastSeen;
            public SYSTEMTIME stLastUsed;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
            public string szName;
        }

        private static readonly object StartLock = new object();
        private static bool _started;

        /// <summary>Winsock must be started once per process before any socket call.</summary>
        public static void EnsureStarted()
        {
            lock (StartLock)
            {
                if (_started) return;
                WSAStartup(0x0202, new byte[512]);
                _started = true;
            }
        }

        [DllImport("ws2_32.dll", SetLastError = true)]
        private static extern int WSAStartup(ushort version, byte[] data);

        [DllImport("ws2_32.dll", SetLastError = true)]
        public static extern IntPtr socket(int af, int type, int protocol);

        [DllImport("ws2_32.dll", SetLastError = true)]
        public static extern int bind(IntPtr s, ref SOCKADDR_BTH name, int namelen);

        [DllImport("ws2_32.dll", SetLastError = true)]
        public static extern int listen(IntPtr s, int backlog);

        [DllImport("ws2_32.dll", SetLastError = true)]
        public static extern IntPtr accept(IntPtr s, ref SOCKADDR_BTH addr, ref int addrlen);

        [DllImport("ws2_32.dll", SetLastError = true)]
        public static extern int connect(IntPtr s, ref SOCKADDR_BTH name, int namelen);

        [DllImport("ws2_32.dll", SetLastError = true)]
        public static extern int getsockname(IntPtr s, ref SOCKADDR_BTH name, ref int namelen);

        [DllImport("ws2_32.dll", SetLastError = true)]
        public static extern int send(IntPtr s, IntPtr buf, int len, int flags);

        [DllImport("ws2_32.dll", SetLastError = true)]
        public static extern int recv(IntPtr s, IntPtr buf, int len, int flags);

        [DllImport("ws2_32.dll", SetLastError = true)]
        public static extern int closesocket(IntPtr s);

        [DllImport("ws2_32.dll", SetLastError = true)]
        public static extern int setsockopt(IntPtr s, int level, int optname, ref int optval, int optlen);

        [DllImport("ws2_32.dll", SetLastError = true, EntryPoint = "WSASetServiceW")]
        public static extern int WSASetService(IntPtr lpqsRegInfo, int essoperation, int dwControlFlags);

        [DllImport("BluetoothApis.dll", SetLastError = true)]
        public static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS pbtfrp, out IntPtr phRadio);

        [DllImport("BluetoothApis.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothFindRadioClose(IntPtr hFind);

        [DllImport("BluetoothApis.dll", SetLastError = true)]
        public static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("BluetoothApis.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("BluetoothApis.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool BluetoothFindDeviceClose(IntPtr hFind);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);
    }
}
