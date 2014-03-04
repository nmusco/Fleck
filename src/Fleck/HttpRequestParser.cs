using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HttpMachine;

namespace Fleck
{
    class HttpRequestParser : IHttpParserDelegate
    {
        readonly static byte[] seperator = new byte[] { 13, 10, 13, 10 };

        readonly HttpParser _parser;
        
        readonly Action<HttpRequest> _onRequestBeginHandler;
        readonly Action<ArraySegment<byte>> _onRequestBodyReceivedHandler;
        readonly Action _onRequestEndHandler;
        readonly Action<string> _onFragmentReceivedHandler;
        readonly Action<Exception> _onParserException;

        readonly bool _secure;

        bool _headersComplete;
        string _headerName;
        HttpRequest _request;
        
        string GetScheme(bool upgrade)
        {
            return _secure
                ? upgrade ? "wss" : "https"
                : upgrade ? "ws" : "http";
        }

        public void OnBody(HttpParser parser, ArraySegment<byte> data)
        {
            if (_headersComplete && data != null && data.Count > 0 && _onRequestBodyReceivedHandler != null)
            {
                _request.ReceivedLength += data.Count;
                _onRequestBodyReceivedHandler(data);
            }
        }
        public void OnFragment(HttpParser parser, string fragment)
        {
            if (_onFragmentReceivedHandler != null)
                _onFragmentReceivedHandler(fragment);
        }
        public void OnHeaderName(HttpParser parser, string name)
        {
            if (!_request.Headers.ContainsKey(name))
                _request.Headers.Add(name, string.Empty);
            _headerName = name;
        }
        public void OnHeaderValue(HttpParser parser, string value)
        {
            string existingValue = _request.Headers[_headerName];
            if (string.IsNullOrWhiteSpace(existingValue))
                _request.Headers[_headerName] = value;
            else
                _request.Headers[_headerName] = existingValue + "," + value;
        }
        public void OnHeadersEnd(HttpParser parser)
        {
            string upgradeType;

            _headersComplete = true;
            _request.Version = new Version(parser.MajorVersion, parser.MinorVersion);
            _request.KeepAlive = parser.ShouldKeepAlive;
            _request.Upgrade = _request.Headers.TryGetValue("upgrade", out upgradeType);
            _request.UpgradeType = upgradeType;
            _request.Secure = _secure;

            if (_onRequestBeginHandler != null)
                _onRequestBeginHandler(_request);
        }
        public void OnMessageBegin(HttpParser parser)
        {
            _request = new HttpRequest();
            _headersComplete = false;
            _headerName = null;
        }
        public void OnMessageEnd(HttpParser parser)
        {
            if (_onRequestEndHandler != null) 
                _onRequestEndHandler();
        }
        public void OnPath(HttpParser parser, string path)
        {
            _request.Path = path;
        }
        public void OnQueryString(HttpParser parser, string queryString)
        {
            _request.QueryString = queryString;
        }
        public void OnRequestUri(HttpParser parser, string requestUri)
        {
            _request.RequestUri = requestUri;
        }
        public void OnMethod(HttpParser parser, string method)
        {
            _request.Method = method;
        }

        public void Parse(ArraySegment<byte> buffer)
        {
            _parser.Execute(buffer);
            _parser.Execute(default(ArraySegment<byte>));
        }

        public HttpRequestParser(Action<HttpRequest> onRequestBeginHandler, Action<ArraySegment<byte>> onRequestBodyReceivedHandler, Action onRequestEndHandler, Action<string> onFragmentReceivedHandler, Action<Exception> onParserException, bool secure)
        {
            _parser = new HttpParser(this);
            _secure = secure;
            _onRequestBeginHandler = onRequestBeginHandler;
            _onRequestBodyReceivedHandler = onRequestBodyReceivedHandler;
            _onRequestEndHandler = onRequestEndHandler;
            _onParserException = onParserException;
        }
    }
}
