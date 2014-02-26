using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Fleck
{
    /// <summary>
    /// Wraps a stream and queues multiple write operations.
    /// Useful for wrapping SslStream as it does not support multiple simultaneous write operations.
    /// </summary>
    public class QueuedStream : Stream
    {
        private Stream _stream;
        private Queue<WriteData> _queue = new Queue<WriteData>();
        private bool _pendingWrite = false;
        private bool _disposed = false;

        public QueuedStream(Stream stream)
        {
            _stream = stream;
        }

        public override bool CanRead
        {
            get { return _stream.CanRead; }
        }

        public override bool CanSeek
        {
            get { return _stream.CanSeek; }
        }

        public override bool CanWrite
        {
            get { return _stream.CanWrite; }
        }

        public override void Flush()
        {
            _stream.Flush();
        }

        public override long Length
        {
            get { return _stream.Length; }
        }

        public override long Position
        {
            get
            {
                return _stream.Position;
            }
            set
            {
                _stream.Position = value;
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _stream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _stream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("Queued stream does not support synchronous write operations yet.");
        }

        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            return _stream.BeginRead(buffer, offset, count, callback, state);
        }

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            lock (_queue)
            {
                if (_pendingWrite)
                {
                    var data = new WriteData(buffer, offset, count, callback, state);
                    _queue.Enqueue(data);
                    return data.AsyncResult;
                }
                else
                {
                    _pendingWrite = true;
                    return BeginWriteInternal(buffer, offset, count, callback, state);
                }

            }
        }

        public override int EndRead(IAsyncResult asyncResult)
        {
            return _stream.EndRead(asyncResult);
        }

        public override void EndWrite(IAsyncResult asyncResult)
        {
            throw new NotSupportedException("Queued stream does not support synchronous write operations yet.");
        }

        public override void Close()
        {
            _stream.Close();
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _stream.Dispose();
                }
                _disposed = true;
            }
            base.Dispose(disposing);
        }

        private IAsyncResult BeginWriteInternal(byte[] buffer, int offset, int count, AsyncCallback callback, object state)
        {
            var result = _stream.BeginWrite(buffer, offset, count, ar => {
                lock (_queue)
                {
                    if (_queue.Count > 0)
                    {
                        // one down, another is good to go
                        var data = _queue.Dequeue();
                        data.AsyncResult.ActualResult = BeginWriteInternal(data.Buffer, data.Offset, data.Count, data.Callback, data.State);
                    }
                    else
                    {
                        _pendingWrite = false;
                    }
                    callback(ar);
                }
            }, state);
            return result;
        }

        private class WriteData 
        {
            public readonly byte[] Buffer;
            public readonly int Offset;
            public readonly int Count;
            public readonly AsyncCallback Callback;
            public readonly object State;
            public readonly QueuedWriteResult AsyncResult;

            public WriteData (byte[] buffer, int offset, int count, AsyncCallback callback, object state)
	        {
                Buffer = buffer;
                Offset = offset;
                Count = count;
                Callback = callback;
                State = state;
                AsyncResult = new QueuedWriteResult(state);
	        }
        }

        private class QueuedWriteResult : IAsyncResult 
        {
            private object _state;
            private IAsyncResult _actualResult;

            public QueuedWriteResult(object state)
            {
                _state = state;
            }

            public IAsyncResult ActualResult
            {
                get
                {
                    return _actualResult;
                }
                set
                {
                    _actualResult = value;
                }
            }
            
            public object AsyncState
            {
                get { return _state; }
            }

            public WaitHandle AsyncWaitHandle
            {
                get 
                {
                    throw new NotSupportedException("Queued write operations does not support wait handles yet.");
                }
            }

            public bool CompletedSynchronously
            {
                get { return false; }
            }

            public bool IsCompleted
            {
                get { return ActualResult == null ? false : ActualResult.IsCompleted; }
            }
        }
    }
}
