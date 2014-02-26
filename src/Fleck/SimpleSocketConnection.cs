using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Fleck
{
    public class SimpleSocketConnection : IWebSocketConnection
    {
        public SimpleSocketConnection(ISocket socket)
        {
            Socket = socket;
            OnOpen = () => { };
            OnClose = () => { };
            OnMessage = x => { };
            OnError = x => { };
        }

        public ISocket Socket { get; set; }

        private bool _closing;
        private bool _closed;
        private const int ReadSize = 1024 * 4;

        public Action OnOpen { get; set; }
        public Action OnClose { get; set; }
        public Action<string> OnMessage { get; set; }
        public Action<Exception> OnError { get; set; }
        public IWebSocketConnectionInfo ConnectionInfo { get; private set; }

        public bool IsAvailable
        {
            get { return !_closing && !_closed && Socket.Connected; }
        }

        public void Send(string message)
        {
            Send(message, null);
        }

        public void Send(string message, string mid)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            SendBytes(bytes, mid);
        }

        public void Send(byte[] message)
        {
            SendBytes(message, null);
        }

        public void Send(byte[] message, string mid)
        {
            SendBytes(message, null);
        }

        public void StartReceiving()
        {
            var data = new List<byte>(ReadSize);
            var buffer = new byte[ReadSize];
            Read(data, buffer);
        }

        private void Read(List<byte> data, byte[] buffer)
        {
            if (!IsAvailable)
                return;
            Socket.Receive(buffer, r =>
            {
                if (r <= 0)
                {
                    FleckLog.Debug("0 bytes read. Closing.");
                    CloseSocket();
                    return;
                }
                FleckLog.Debug(r + " bytes read");
                var message = Encoding.UTF8.GetString(buffer, 0, r);
                OnMessage(message);
                Read(data, buffer);
            },
            e =>
            {
                OnError(e);
                if (e is HandshakeException)
                {
                    FleckLog.Debug("Error while reading", e);
                }
                else if (e is WebSocketException)
                {
                    FleckLog.Debug("Error while reading", e);
                    Close(WebSocketStatusCodes.ProtocolError);
                }
                else
                {
                    FleckLog.Error("Application Error", e);
                    Close(WebSocketStatusCodes.ApplicationError);
                }
            });
        }

        private void SendBytes(byte[] bytes, string mid, Action callback = null)
        {
            if (!IsAvailable)
            {
                FleckLog.Warn("Data sent after close. Ignoring " + mid);
                return;
            }

            Socket.Send(bytes, () =>
            {
                FleckLog.Debug("Sent " + mid + "(" + bytes.Length + " bytes)");
                if (callback != null)
                    callback();
            },
            e =>
            {
                FleckLog.Info("Failed to send " + mid +". Disconnecting.", e);
                CloseSocket();
            });
        }

        public void Close()
        {
            Close(1000);
        }

        public void Close(int code)
        {
            CloseSocket();
            return;
        }

        private void CloseSocket()
        {
            _closing = true;
            OnClose();
            _closed = true;
            Socket.Close();
            Socket.Dispose();
        }


        public Action<byte[]> OnBinary
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }
    }
}
