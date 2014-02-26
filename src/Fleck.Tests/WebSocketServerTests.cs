using System;
using System.Net;
using Moq;
using NUnit.Framework;
using System.Security.Cryptography.X509Certificates;

namespace Fleck.Tests
{
    [TestFixture]
    public class WebSocketServerTests
    {
        private WebSocketServer _server;
        private MockRepository _repository;

        [SetUp]
        public void Setup()
        {
            _repository = new MockRepository(MockBehavior.Default);
            _server = new WebSocketServer(IPAddress.Any, 8000);
        }

        [Test]
        public void ShouldStart()
        {
            var socketMock = _repository.Create<ISocket>();

            _server.ListenerSocket = socketMock.Object;
            _server.Start(connection => { });

            socketMock.Verify(s => s.Bind(It.Is<IPEndPoint>(i => i.Port == 8000)));
            socketMock.Verify(s => s.Accept(It.IsAny<Action<ISocket>>(), It.IsAny<Action<Exception>>()));
        }

		[Test]
		public void ShouldBeSecureWithCertificate()
		{
			var server = new WebSocketServer(IPAddress.Any, 8000);
			server.Certificate = new X509Certificate2();
			Assert.IsTrue(server.IsSecure);
		}

		[Test]
		public void ShouldNotBeSecureWithNoCertificate()
		{
			var server = new WebSocketServer(IPAddress.Any, 8000);
			Assert.IsFalse(server.IsSecure);
		}
    }
}
