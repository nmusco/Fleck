using System;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Fleck
{
	public class PolicyServer : IDisposable
	{
		private static readonly Regex _regex = new Regex(@"^<policy-file-request\s?/>\x00$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

		private readonly IPEndPoint _ipEndPoint;

		public PolicyServer()
			: this(IPAddress.Any) { }
		public PolicyServer(int port)
			: this(IPAddress.Any, port) { }

		public PolicyServer(IPAddress ipAddress, int port = 843) // 843 is standard port for policy servers
		{
			_ipEndPoint = new IPEndPoint(ipAddress, port);

			var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
			ListenerSocket = new SocketWrapper(socket);
		}

		public ISocket ListenerSocket { get; set; }

		public IPAddress Address { get { return _ipEndPoint.Address; } }
		public int Port { get { return _ipEndPoint.Port; } }
		public string Location { get { return "xmlsocket://" + Address + ":" + Port; } }

		private string _policy = "\0";
		public string Policy
		{
			get { return _policy; }

			set
			{
				if (string.IsNullOrWhiteSpace(value))
					_policy = "\0";
				else
				{
					_policy = value.Trim();

					// must end with null char (as per spec)
					if (!_policy.EndsWith("\0"))
						_policy += "\0";
				}
			}
		}

		public void Dispose()
		{
			ListenerSocket.Dispose();
		}

		public void Start()
		{
			ListenerSocket.Bind(_ipEndPoint);
			ListenerSocket.Listen(100);

			FleckLog.Info("Policy-Server started at " + Location);

			ListenForClients();
		}

		private void ListenForClients()
		{
			ListenerSocket.Accept(OnClientConnect, e => FleckLog.Error("Policy-Listener socket is closed", e));
		}

		private void OnClientConnect(ISocket clientSocket)
		{
			FleckLog.Debug(String.Format("Policy-Client connected from {0}:{1}", clientSocket.RemoteIpAddress, clientSocket.RemotePort.ToString()));
			ListenForClients();

			var connection = new SocketConnection(clientSocket);
			connection.OnMessage = (message) =>
			{
				if (_regex.IsMatch(message))
				{
					connection.Send(_policy, "policy", connection.Close);
				}
				else
				{
					FleckLog.Info("Policy-Server invalid request: " + message);
					connection.Close();
				}
			};
			connection.StartReceiving();
		}
	}
}
