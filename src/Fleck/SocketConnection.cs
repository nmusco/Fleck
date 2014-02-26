using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Fleck
{
	public class SocketConnection
	{
		protected const int READ_SIZE = 1024 * 4;

		public SocketConnection(ISocket socket)
		{
			Socket = socket;

			OnOpen = () => { };
			OnClose = () => { };
			OnMessage = (x) => { };
			OnError = (x) => { };

			// default read handler
			OnRead = (data, readBytes) =>
			{
				OnMessage(Encoding.UTF8.GetString(readBytes.ToArray<byte>()));
			};

			// default read error handler
			OnReadError = (e) =>
			{
				FleckLog.Error("Error while reading", e);
				OnError(e);
				Close();
			};
		}

		protected bool IsClosing { get; set; }
		protected bool IsClosed { get; set; }

		public ISocket Socket { get; set; }
		public IWebSocketConnectionInfo ConnectionInfo { get; protected set; }

		public bool IsAvailable { get { return !IsClosing && !IsClosed && Socket.Connected; } }

		public Action OnOpen { get; set; }
		public Action OnClose { get; set; }
		public Action<string> OnMessage { get; set; }
		public Action<Exception> OnError { get; set; }

		protected Action<List<byte>, IEnumerable<byte>> OnRead { get; set; }
		protected Action<Exception> OnReadError { get; set; }

		public void Send(string message, string logMessage, Action successCallback = null)
		{
			var bytes = Encoding.UTF8.GetBytes(message);
			Send(bytes, logMessage, successCallback);
		}

		public void Send(byte[] message, string logMessage, Action successCallback = null)
		{
			Socket.Send(message,
			() =>
			{
				FleckLog.Debug("Sent " + message.Length + " bytes. " + logMessage);
				if (successCallback != null)
					successCallback();
			},
			e =>
			{
				if (e is IOException)
					FleckLog.Debug("Failed to send. Disconnecting. " + logMessage, e);
				else
					FleckLog.Info("Failed to send. Disconnecting. " + logMessage, e);

				Close();
			});
		}

		public void StartReceiving()
		{
			Read(new List<byte>(READ_SIZE), new byte[READ_SIZE]);
		}

		private void Read(List<byte> data, byte[] buffer)
		{
			if (!IsAvailable)
				return;

			Socket.Receive(buffer, r =>
			{
				if (r > 0)
				{
					FleckLog.Debug(r + " bytes read.");

					var readBytes = buffer.Take(r);

					OnRead(data, readBytes);

					Read(data, buffer);
				}
				else
				{
					FleckLog.Debug("0 bytes read. Closing.");
					Close();
				}
			},
			OnReadError);
		}

		public void Close()
		{
			IsClosing = true;

			OnClose();
			IsClosed = true;

			Socket.Close();
			Socket.Dispose();

			IsClosing = false;
		}
	}
}
