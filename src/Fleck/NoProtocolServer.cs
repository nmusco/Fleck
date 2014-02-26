using System;
using System.Net;
using System.Net.Sockets;

namespace Fleck
{
    public class SimpleServer : IWebSocketServer
    {
        private Action<IWebSocketConnection> _config;

        public SimpleServer()
            : this(843)
        {
        }

        public SimpleServer(int port)
        {
            Port = port;
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
            ListenerSocket = new SocketWrapper(socket);
        }

        public ISocket ListenerSocket { get; set; }
        public int Port { get; private set; }

        public void Dispose()
        {
            ListenerSocket.Dispose();
        }

        public void Start(Action<IWebSocketConnection> config)
        {
            var ipLocal = new IPEndPoint(IPAddress.Any, Port);
            ListenerSocket.Bind(ipLocal);
            ListenerSocket.Listen(100);
            FleckLog.Info("Server started at " + Port);
            ListenForClients();
            _config = config;
        }

        private void ListenForClients()
        {
            ListenerSocket.Accept(OnClientConnect, e => FleckLog.Error("Listener socket is closed", e));
        }

        private void OnClientConnect(ISocket clientSocket)
        {
            FleckLog.Debug(String.Format("Client connected from {0}:{1}", clientSocket.RemoteIpAddress, clientSocket.RemotePort.ToString()));
            ListenForClients();

            var connection = new SimpleSocketConnection(clientSocket);
            _config(connection);
            connection.StartReceiving();
        }
    }
}
