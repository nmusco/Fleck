using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Fleck
{
	public class WebSocketConnection : SocketConnection, IWebSocketConnection
	{
		public WebSocketConnection(ISocket socket, Action<IWebSocketConnection> initialize, Func<byte[], WebSocketHttpRequest> parseRequest, Func<WebSocketHttpRequest, IHandler> handlerFactory, Func<IEnumerable<string>, string> negotiateSubProtocol)
			: base(socket)
		{
			OnBinary = (x) => { };
			OnPing = SendPong;
			OnPong = (x) => { };

			OnRead = HandleRead;
			OnReadError = HandleReadError;

			_initialize = initialize;
			_handlerFactory = handlerFactory;
			_parseRequest = parseRequest;
			_negotiateSubProtocol = negotiateSubProtocol;
		}

		private readonly Action<IWebSocketConnection> _initialize;
		private readonly Func<WebSocketHttpRequest, IHandler> _handlerFactory;
		private readonly Func<IEnumerable<string>, string> _negotiateSubProtocol;
		private readonly Func<byte[], WebSocketHttpRequest> _parseRequest;

		public IHandler Handler { get; set; }

		public Action<byte[]> OnBinary { get; set; }
		public Action<byte[]> OnPing { get; set; }
		public Action<byte[]> OnPong { get; set; }

		public void Send(string message, string logMessage = null)
		{
			Send(message, (b) => { return Handler.FrameText(b); }, logMessage);
		}

		public void Send(byte[] message, string logMessage = null)
		{
			Send(message, (b) => { return Handler.FrameBinary(b); }, logMessage);
		}

		public void SendPing(byte[] message)
		{
			Send(message, (b) => { return Handler.FramePing(b); }, "ping");
		}

		public void SendPong(byte[] message)
		{
			Send(message, (b) => { return Handler.FramePong(b); }, "pong");
		}

		private void Send<T>(T message, Func<T, byte[]> createFrame, string logMessage = null)
		{
			if (Handler == null)
				throw new InvalidOperationException("Cannot send before handshake");

			if (!IsAvailable)
			{
				FleckLog.Warn("Data sent while closing or after close. Ignoring. " + logMessage);
				return;
			}

			var bytes = createFrame(message);
			base.Send(bytes, logMessage);
		}

		public void CreateHandler(IEnumerable<byte> data)
		{
			var request = _parseRequest(data.ToArray());
			if (request == null)
				return;

			Handler = _handlerFactory(request);
			if (Handler == null)
				return;

			var subProtocol = _negotiateSubProtocol(request.SubProtocols);
			ConnectionInfo = WebSocketConnectionInfo.Create(request, Socket.RemoteIpAddress, Socket.RemotePort, subProtocol);

			_initialize(this);

			var handshake = Handler.CreateHandshake(subProtocol);
			Send(handshake, "handshake", OnOpen);
		}

		private void HandleRead(List<byte> data, IEnumerable<byte> readBytes)
		{
			if (Handler != null)
			{
				Handler.Receive(readBytes);
			}
			else
			{
				data.AddRange(readBytes);
				CreateHandler(data);
			}
		}

		private void HandleReadError(Exception e)
		{
			if (e is AggregateException)
			{
				var agg = e as AggregateException;
				HandleReadError(agg.InnerException);
				return;
			}

			if (e is ObjectDisposedException)
			{
				FleckLog.Debug("Swallowing ObjectDisposedException", e);
				return;
			}

			OnError(e);

			if (e is HandshakeException)
			{
				FleckLog.Debug("Error while reading", e);
			}
			else if (e is WebSocketException)
			{
				FleckLog.Debug("Error while reading", e);
				Close(((WebSocketException)e).StatusCode);
			}
			else if (e is SubProtocolNegotiationFailureException)
			{
				FleckLog.Debug(e.Message);
				Close(WebSocketStatusCodes.ProtocolError);
			}
			else if (e is IOException)
			{
				FleckLog.Debug("Error while reading", e);
				Close(WebSocketStatusCodes.AbnormalClosure);
			}
			else
			{
				FleckLog.Error("Application Error", e);
				Close(WebSocketStatusCodes.InternalServerError);
			}
		}

		public new void Close()
		{
			Close(WebSocketStatusCodes.NormalClosure);
		}

		public void Close(int code)
		{
			if (IsAvailable)
			{
				IsClosing = true;

				if (Handler != null)
				{
					var bytes = Handler.FrameClose(code);
					if (bytes.Length == 0)
						base.Close();
					else
						base.Send(bytes, "close", base.Close);
				}
				else
				{
					base.Close();
				}
			}
		}
	}
}
