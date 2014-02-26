using System;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Generic;

namespace Fleck
{
    public class WebSocketServer : IWebSocketServer
    {
		private readonly IPEndPoint _ipEndPoint;
        private Action<IWebSocketConnection> _config;

		public WebSocketServer()
			: this(IPAddress.Any) { }
		public WebSocketServer(int port)
			: this(IPAddress.Any, port) { }

		public WebSocketServer(IPAddress ipAddress, int port = 8181)
		{
			_ipEndPoint = new IPEndPoint(ipAddress, port);

			var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
			ListenerSocket = new SocketWrapper(socket);
			SupportedSubProtocols = new string[0];
		}

        public ISocket ListenerSocket { get; set; }
		public X509Certificate2 Certificate { get; set; }
		public RemoteCertificateValidationCallback RemoteCertificateValidationCallback { get; set; }
		public IEnumerable<string> SupportedSubProtocols { get; set; }

		public bool IsSecure { get { return Certificate != null; } }
		public string Scheme { get { return IsSecure ? "wss" : "ws"; } }
		public IPAddress Address { get { return _ipEndPoint.Address; } }
		public int Port { get { return _ipEndPoint.Port; } }
		public string Location { get { return Scheme + "://" + Address + ":" + Port; } }

        public void Dispose()
        {
            ListenerSocket.Dispose();
        }

        public void Start(Action<IWebSocketConnection> config)
        {
            ListenerSocket.Bind(_ipEndPoint);
            ListenerSocket.Listen(100);

            FleckLog.Info("Server started at " + Location);

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

            WebSocketConnection connection = null;

            connection = new WebSocketConnection(
                clientSocket,
                _config,
                bytes => RequestParser.Parse(bytes, Scheme),
                r => HandlerFactory.BuildHandler(r,
                                                 s => connection.OnMessage(s),
                                                 connection.Close,
                                                 b => connection.OnBinary(b),
                                                 b => connection.OnPing(b),
                                                 b => connection.OnPong(b)),
                s => SubProtocolNegotiator.Negotiate(SupportedSubProtocols, s));

            if (IsSecure)
            {
				FleckLog.Debug("Authenticating Secure Connection");
				clientSocket.Authenticate(
					Certificate,
					RemoteCertificateValidationCallback,
					connection.StartReceiving,
					e => FleckLog.Warn("Failed to Authenticate", e));
            }
            else
            {
                connection.StartReceiving();
            }
        }
    }
}
