using System;
using NUnit.Framework;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Net.Security;
using System.Collections.Generic;


namespace Fleck.Tests
{
    [TestFixture]
    public class SocketWrapperTests
    {
        private Socket _socket;
        private Socket _listener;
        private Socket _client;
        private SslStream _clientSslStream;
        private EndPoint _endpoint;
        private SocketWrapper _wrapper;

        [SetUp]
        public void Setup()
        {
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
            _endpoint = new IPEndPoint(IPAddress.Loopback, 45982);
            _listener.Bind(_endpoint);
            _listener.Listen(10);

            _client = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.IP);
            ThreadPool.QueueUserWorkItem(x => {
                Thread.Sleep(100);
                _client.Connect(_endpoint);
            });
            _socket = _listener.Accept();

            _wrapper = new SocketWrapper(_socket);
        }

        private void Secure(Action success, Action<Exception> error)
        {
            var networkStream = new NetworkStream(_client);
            _clientSslStream = new SslStream(networkStream, false, RemoteCertificateValidationCallback);     
            var clientAuthentication = _clientSslStream.BeginAuthenticateAsClient("localhost", null, null);    
            var cert = new System.Security.Cryptography.X509Certificates.X509Certificate2("local.pfx", "local");
            _wrapper.Authenticate(cert, RemoteCertificateValidationCallback, success, error);
        }

        private bool RemoteCertificateValidationCallback(object sender, System.Security.Cryptography.X509Certificates.X509Certificate certificate, System.Security.Cryptography.X509Certificates.X509Chain chain, SslPolicyErrors sslPolicyErrors)
        {
            return true;
        }

        [TearDown]
        public void TearDown()
        {
            _socket.Dispose();
            _client.Dispose();
            _listener.Dispose();
            _wrapper.Dispose();
        }

        [Test]
        public void ShouldNotWriteToClosedSocketIfCancelled()
        {
            Exception ex = null;
            _wrapper.Dispose();
            
            var task = _wrapper.Send(new byte[1], () => {}, e => {ex = e;});
            
            Assert.IsNull(task);
            Assert.IsNull(ex);
        }
        [Test]
        public void ShouldHandleObjectDisposedOnReceive()
        {
            Exception ex = null;
            _wrapper.Dispose();
            _wrapper.Receive(new byte[1], i => {}, e => {ex = e;}, 0);
            Assert.IsInstanceOf<ObjectDisposedException>(ex);
        }

        [Test]
        public void ShouldHandleMultipleSecureWrites()
        {
            var complete = new ManualResetEvent(false);
            var exceptions = new List<Exception>();
            Action<Exception> setException = exc =>
            {
                exceptions.Add(exc);
                complete.Set();
            };
            int count = 42;
            Secure(() =>
            {
                var data = new byte[100];
                var tasks = Enumerable.Range(0, count).Select(i =>
                    Task.Factory.StartNew(() =>
                    {
                        _wrapper.Send(data,
                            () => {
                                if (Interlocked.Decrement(ref count) == 0)
                                {
                                    complete.Set();
                                }
                            },
                            setException);
                    })
                    ).ToArray();
                Task.WaitAll(tasks);
                Assert.IsFalse(tasks.Any(t => t.IsFaulted));
            }, setException);
            complete.WaitOne();
            if (exceptions.Count > 0)
            {
                var exception = exceptions[0];
                if (exception is AggregateException)
                {
                    Assert.Fail(exception.InnerException.Message);
                }
                else
                {
                    Assert.Fail(exception.Message);
                }
            }
            else
            {
                Assert.AreEqual(0, count);
            }
        }
    }
}

