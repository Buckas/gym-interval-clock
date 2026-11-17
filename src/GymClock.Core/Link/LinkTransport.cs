using System;
using System.IO;
using System.Net;
using System.Net.Sockets;

namespace GymClock.Link
{
    /// <summary>
    /// One open connection between the main PC and a second screen. The link's messages are just
    /// lines of text on <see cref="Stream"/>, so anything that can carry a stream can carry the
    /// link: the local network (TCP) or Bluetooth (RFCOMM).
    /// </summary>
    public interface ILinkConnection : IDisposable
    {
        Stream Stream { get; }

        /// <summary>Who is at the other end, for messages ("192.168.1.20", "Bluetooth 00:1A:...").</summary>
        string Remote { get; }

        /// <summary>How long a read may wait before the connection counts as lost.</summary>
        void SetReceiveTimeout(int milliseconds);
    }

    /// <summary>The main PC's side of a transport: waits for second screens to connect.</summary>
    public interface ILinkListener : IDisposable
    {
        /// <summary>"network" or "Bluetooth", for messages.</summary>
        string Kind { get; }

        bool Start(out string error);

        /// <summary>Blocks until a second screen connects. Returns null (or throws) once the listener is disposed.</summary>
        ILinkConnection Accept();
    }

    // ---------------------------------------------------------------------- local network (TCP)

    public sealed class TcpLinkConnection : ILinkConnection
    {
        private readonly TcpClient _client;

        public TcpLinkConnection(TcpClient client)
        {
            _client = client;
            _client.NoDelay = true;
            Stream = client.GetStream();
            try { Remote = ((IPEndPoint)client.Client.RemoteEndPoint).Address.ToString(); }
            catch { Remote = "network"; }
        }

        public Stream Stream { get; }
        public string Remote { get; }

        public void SetReceiveTimeout(int milliseconds)
        {
            _client.ReceiveTimeout = milliseconds;
        }

        /// <summary>Connects to the main PC, or returns null if it does not answer in time.</summary>
        public static TcpLinkConnection Connect(IPEndPoint leader, int timeoutMs, System.Threading.CancellationToken stop)
        {
            TcpClient client = new TcpClient(AddressFamily.InterNetwork);
            try
            {
                if (!client.ConnectAsync(leader.Address, leader.Port).Wait(timeoutMs, stop))
                {
                    client.Close();
                    return null;
                }
                return new TcpLinkConnection(client);
            }
            catch
            {
                client.Close();
                throw;
            }
        }

        public void Dispose()
        {
            try { _client.Close(); } catch { }
        }
    }

    public sealed class TcpLinkListener : ILinkListener
    {
        private readonly int _port;
        private TcpListener _listener;

        public TcpLinkListener(int port)
        {
            _port = port;
        }

        public string Kind { get { return "network"; } }

        public bool Start(out string error)
        {
            error = null;
            try
            {
                _listener = new TcpListener(IPAddress.Any, _port);
                _listener.Start();
                return true;
            }
            catch (SocketException ex)
            {
                error = "Port " + _port + " could not be opened (" + ex.SocketErrorCode + ").";
                return false;
            }
        }

        public ILinkConnection Accept()
        {
            return new TcpLinkConnection(_listener.AcceptTcpClient());
        }

        public void Dispose()
        {
            try { _listener?.Stop(); } catch { }
        }
    }
}
