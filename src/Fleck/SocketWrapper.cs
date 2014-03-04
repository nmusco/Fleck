using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System.Threading;

namespace Fleck
{
    public class SocketWrapper : ISocket
    {
        private readonly Socket _socket;
        private Stream _stream;
        private CancellationTokenSource _tokenSource;
        private TaskFactory _taskFactory;
        
        public string RemoteIpAddress
        {
            get
            {
                var endpoint = _socket.RemoteEndPoint as IPEndPoint;
                return endpoint != null ? endpoint.Address.ToString() : null;
            }
        }

        public int RemotePort
        {
            get
            {
                var endpoint = _socket.RemoteEndPoint as IPEndPoint;
                return endpoint != null ? endpoint.Port : -1;
            }
        }


        public SocketWrapper(Socket socket)
        {
            _tokenSource = new CancellationTokenSource();
            _taskFactory = new TaskFactory(_tokenSource.Token);
            _socket = socket;
            if (_socket.Connected)
                _stream = new NetworkStream(_socket);
        }

        public Task Authenticate(X509Certificate2 certificate, RemoteCertificateValidationCallback remoteCertificateValidationCallback, Action callback, Action<Exception> error)
        {
            var ssl = new SslStream(_stream, false, remoteCertificateValidationCallback);
            var queued = new QueuedStream(ssl);
            _stream = queued;
            Func<AsyncCallback, object, IAsyncResult> begin =
                (cb, s) => ssl.BeginAuthenticateAsServer(certificate, false, SslProtocols.Tls, false, cb, s);
                
            Task task = Task.Factory.FromAsync(begin, ssl.EndAuthenticateAsServer, null);
            task.ContinueWith(t => callback(), TaskContinuationOptions.NotOnFaulted)
                .ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            task.ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);

            return task;
        }

        public void Listen(int backlog)
        {
            _socket.Listen(backlog);
        }

        public void Bind(EndPoint endPoint)
        {
            _socket.Bind(endPoint);
        }

        public bool Connected
        {
            get { return _socket.Connected; }
        }
        
        public Stream Stream
        {
            get { return _stream; }
        }

        public bool NoDelay
        {
            get { return _socket.NoDelay; }
            set { _socket.NoDelay = value; }
        }

        bool _keepalive = false;
        public bool KeepAlive
        {
            get
            {
                return _keepalive;
            }
            set
            {
                try
                {
                    const ulong time = 1000;        // Response Time
                    const ulong interval = 30000;   // Send Interval

                    // resulting structure
                    byte[] SIO_KEEPALIVE_VALS = new byte[3 * 4];

                    // array to hold input values
                    ulong[] input = new ulong[3];

                    // put input arguments in input array
                    if (value && time > 0 && interval > 0) // enable disable keep-alive
                        input[0] = (1UL);   // on
                    else
                        input[0] = (0UL);   // off

                    input[1] = (time);      // time millis
                    input[2] = (interval);  // interval millis

                    // pack input into byte struct
                    for (int i = 0; i < input.Length; i++)
                    {
                        SIO_KEEPALIVE_VALS[(i * 4) + 3] = (byte)(input[i] >> ((4 - 1) * 8) & 0xff);
                        SIO_KEEPALIVE_VALS[(i * 4) + 2] = (byte)(input[i] >> ((4 - 2) * 8) & 0xff);
                        SIO_KEEPALIVE_VALS[(i * 4) + 1] = (byte)(input[i] >> ((4 - 3) * 8) & 0xff);
                        SIO_KEEPALIVE_VALS[(i * 4) + 0] = (byte)(input[i] >> ((4 - 4) * 8) & 0xff);
                    }

                    // create bytestruct for result (bytes pending on server socket)
                    byte[] result = BitConverter.GetBytes(0);

                    // write SIO_VALS to Socket IOControl
                    _socket.IOControl(IOControlCode.KeepAliveValues, SIO_KEEPALIVE_VALS, result);

                    _keepalive = true;
                }
                catch (Exception)
                {
                    _keepalive = false;
                }
            }
        }

        public Task<int> Receive(byte[] buffer, Action<int> callback, Action<Exception> error, int offset)
        {
            try
            {
                Func<AsyncCallback, object, IAsyncResult> begin =
               (cb, s) => _stream.BeginRead(buffer, offset, buffer.Length, cb, s);

                Task<int> task = Task.Factory.FromAsync<int>(begin, _stream.EndRead, null);
                task.ContinueWith(t => callback(t.Result), TaskContinuationOptions.NotOnFaulted)
                    .ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
                task.ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
                return task;
            }
            catch (Exception e)
            {
                error(e);
                return null;
            }
        }

        public Task<ISocket> Accept(Action<ISocket> callback, Action<Exception> error)
        {
            Func<IAsyncResult, ISocket> end = r => {
                _tokenSource.Token.ThrowIfCancellationRequested();
                return new SocketWrapper(_socket.EndAccept(r));
            };
            var task = _taskFactory.FromAsync(_socket.BeginAccept, end, null);
            task.ContinueWith(t => callback(t.Result), TaskContinuationOptions.OnlyOnRanToCompletion)
                .ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            task.ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
            return task;
        }

        public void Dispose()
        {
            _tokenSource.Cancel();
            if (_stream != null) _stream.Dispose();
            if (_socket != null) _socket.Dispose();
        }

        public void Close()
        {
            _tokenSource.Cancel();
            if (_stream != null) _stream.Close();
            if (_socket != null) _socket.Close();
        }

        public int EndSend(IAsyncResult asyncResult)
        {
            _stream.EndWrite(asyncResult);
            return 0;
        }

        public Task Send(byte[] buffer, Action callback, Action<Exception> error)
        {
            if (_tokenSource.IsCancellationRequested)
                return null;

            try
            {
                Func<AsyncCallback, object, IAsyncResult> begin =
                    (cb, s) => _stream.BeginWrite(buffer, 0, buffer.Length, cb, s);

                Task task = Task.Factory.FromAsync(begin, _stream.EndWrite, null);
                task.ContinueWith(t => callback(), TaskContinuationOptions.NotOnFaulted)
                    .ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);
                task.ContinueWith(t => error(t.Exception), TaskContinuationOptions.OnlyOnFaulted);

                return task;
            }
            catch (Exception e)
            {
                error(e);
                return null;
            }
        }
    }
}
