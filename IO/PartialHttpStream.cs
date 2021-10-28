﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web;

namespace DirectPackageInstaller
{
    //98% Stolen From: https://codereview.stackexchange.com/a/204766
    public class PartialHttpStream : Stream, IDisposable
    {
        public CookieContainer Cookies = new CookieContainer();
        string _fn;
        public string Filename
        {
            get {
                if (Length == 0)
                    return null;
                return _fn;
            }
        }

        private const int CacheLen = 1024 * 8;

        // Cache for short requests.
        private readonly byte[] cache;
        private readonly int cacheLen;
        private Stream stream;
        //private WebResponse response;
        private long position = 0;
        private long? length;
        private long cachePosition;
        private int cacheCount;

        public PartialHttpStream(string url, int cacheLen = CacheLen)
        {
            if (string.IsNullOrEmpty(url))
                throw new ArgumentException("url empty");
            if (cacheLen <= 0)
                throw new ArgumentException("cacheLen must be greater than 0");

            Url = url;
            this.cacheLen = cacheLen;
            cache = new byte[cacheLen];
        }

        ~PartialHttpStream()
        {
            Dispose();
        }

        public string Url { get; private set; }

        public override bool CanRead { get { return true; } }
        public override bool CanWrite { get { return false; } }
        public override bool CanSeek { get { return true; } }

        public override long Position { get; set; }

        /// <summary>
        /// Lazy initialized length of the resource.
        /// </summary>
        public override long Length
        {
            get
            {
                if (length == null)
                    length = HttpGetLength();
                return length.Value;
            }
        }

        /// <summary>
        /// Count of HTTP requests. Just for statistics reasons.
        /// </summary>
        public int HttpRequestsCount { get; private set; }

        public override void SetLength(long value)
        { throw new NotImplementedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (count == 0)
                return 0;

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset >= buffer.Length)
                throw new ArgumentException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length)
                throw new ArgumentException(nameof(count));

            long curPosition = Position;
            Position += ReadFromCache(buffer, ref offset, ref count);

            if (count > cacheLen)
            {
                // large request, do not cache
                while (count > 0)
                    Position += HttpRead(buffer, ref offset, ref count);
            }
            else if (count > 0)
            {
                // read to cache
                cachePosition = Position;
                int off = 0;
                int len = cacheLen;
                cacheCount = HttpRead(cache, ref off, ref len);
                Position += ReadFromCache(buffer, ref offset, ref count);
            }

            return (int)(Position - curPosition);
        }

        public new async Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            if (count == 0)
                return 0;

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset >= buffer.Length)
                throw new ArgumentException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length)
                throw new ArgumentException(nameof(count));

            long curPosition = Position;
            Position += ReadFromCache(buffer, ref offset, ref count);

            if (count > cacheLen)
            {
                // large request, do not cache
                while (count > 0)
                {
                    var Result = await HttpReadAsync(buffer, offset, count);
                    Position += Result.Readed;
                    offset = Result.Offset;
                    count = Result.Count;
                }
            }
            else if (count > 0)
            {
                // read to cache
                cachePosition = Position;

                var Result = await HttpReadAsync(buffer, offset, count);
                cacheCount = Result.Readed;
                offset = Result.Offset;
                count = Result.Count;

                Position += ReadFromCache(buffer, ref offset, ref count);
            }

            return (int)(Position - curPosition);
        }

        public override void Write(byte[] buffer, int offset, int count)
        { throw new NotImplementedException(); }

        public override long Seek(long pos, SeekOrigin origin)
        {
            switch (origin)
            {
                case SeekOrigin.End:
                    Position = Length + pos;
                    break;

                case SeekOrigin.Begin:
                    Position = pos;
                    break;

                case SeekOrigin.Current:
                    Position += pos;
                    break;
            }
            return Position;
        }

        public override void Flush()
        {
        }

        private int ReadFromCache(byte[] buffer, ref int offset, ref int count)
        {
            if (cachePosition > Position || (cachePosition + cacheCount) <= Position)
                return 0; // cache miss
            int ccOffset = (int)(Position - cachePosition);
            int ccCount = Math.Min(cacheCount - ccOffset, count);
            Array.Copy(cache, ccOffset, buffer, offset, ccCount);
            offset += ccCount;
            count -= ccCount;
            return ccCount;
        }

        HttpWebRequest req = null;
        WebResponse resp = null;
        Stream ResponseStream = null;
        long RespPos = 0;

        private int HttpRead(byte[] buffer, ref int offset, ref int count, int Tries = 0)
        {
            try
            {
                if (RespPos != Position || ResponseStream == null)
                {
                    HttpRequestsCount++;

                    if (req != null)
                        req.ServicePoint.CloseConnectionGroup(req.ConnectionGroupName);

                    if (ResponseStream != null)
                    {
                        ResponseStream?.Close();
                        ResponseStream?.Dispose();
                    }

                    if (resp != null)
                    {
                        resp?.Close();
                        resp?.Dispose();
                    }

                    req = HttpWebRequest.CreateHttp(Url);
                    req.ConnectionGroupName = new Guid().ToString();
                    req.CookieContainer = Cookies;
                    req.KeepAlive = false;
                    req.ServicePoint.SetTcpKeepAlive(false, 1000 * 120, 1000 * 5);


                    req.AddRange(Position, Length - 1);
                    resp = req.GetResponse();

                    ResponseStream = resp.GetResponseStream();
                    ResponseStream.ReadTimeout = (int)TimeSpan.FromMinutes(5).TotalMilliseconds;
                }

                int nread = 0;

                int Readed = 0;
                do
                {
                    Readed = ResponseStream.Read(buffer, offset + nread, count - nread);
                    nread += Readed;
                } while (Readed > 0 && count > 0);

                offset += nread;
                count -= nread;

                if (Program.IsUnix && nread == 0 && count > 0)
                    throw new WebException();

                RespPos = Position + nread;

                return nread;

            }
            catch (WebException ex)
            {
                if (req != null)
                    req.ServicePoint.CloseConnectionGroup(req.ConnectionGroupName);

                ResponseStream?.Dispose();
                ResponseStream = null;

                if (Tries < 2)
                    return HttpRead(buffer, ref offset, ref count, Tries + 1);

                var response = (HttpWebResponse)ex.Response;
                if (response?.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    return 0;
                throw ex;
            }
        }

        private async Task<(int Readed, int Offset, int Count)> HttpReadAsync(byte[] buffer, int offset, int count, int Tries = 0)
        {
            try
            {
                if (RespPos != Position || ResponseStream == null)
                {
                    HttpRequestsCount++;

                    if (ResponseStream != null)
                    {
                        ResponseStream?.Close();
                        ResponseStream?.Dispose();
                    }

                    if (resp != null)
                    {
                        resp?.Close();
                        resp?.Dispose();
                    }

                    req = HttpWebRequest.CreateHttp(Url);
                    req.CookieContainer = Cookies;


                    req.AddRange(Position, Length - 1);
                    resp = await req.GetResponseAsync();

                    ResponseStream = resp.GetResponseStream();
                }

                int nread = 0;

                int Readed = 0;
                do
                {
                    Readed = await ResponseStream.ReadAsync(buffer, offset + nread, count - nread);
                    nread += Readed;
                } while (Readed > 0 && count > 0);

                offset += nread;
                count -= nread;

                RespPos = Position + nread;

                return (nread, offset, count);

            }
            catch (WebException ex)
            {
                ResponseStream?.Close();
                ResponseStream?.Dispose();
                ResponseStream = null;

                if (Tries < 2)
                    return await HttpReadAsync(buffer, offset, count, Tries + 1).ConfigureAwait(false);

                var response = (HttpWebResponse)ex.Response;
                if (response?.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    return (0, offset, count);
                throw ex;
            }
        }

        static Dictionary<string, (string Filename, long Length)> HeadCache = new Dictionary<string, (string Filename, long Length)>();
        private long HttpGetLength()
        {
            if (HeadCache.ContainsKey(Url))
            {
                _fn = HeadCache[Url].Filename;
                return HeadCache[Url].Length;
            }

            HttpRequestsCount++;
            HttpWebRequest request = WebRequest.CreateHttp(Url);
            request.ConnectionGroupName = new Guid().ToString();
            request.KeepAlive = false;
            request.CookieContainer = Cookies;
            request.Method = "HEAD";
            using var response = request.GetResponse();
            if (response.Headers.AllKeys.Contains("Content-Disposition"))
            {
                _fn = response.Headers["Content-Disposition"];
                const string prefix = "filename=";
                _fn = _fn.Substring(_fn.IndexOf(prefix) + prefix.Length).Trim('"');
                _fn = HttpUtility.UrlDecode(_fn.Split(';').First().Trim('"'));
            }
            
            var Length = response.ContentLength;
            response?.Close();

            request.ServicePoint.CloseConnectionGroup(request.ConnectionGroupName);

            HeadCache[Url] = (_fn, Length);

            return Length;
        }

        private new void Dispose()
        {
            if (stream != null)
            {
                stream.Dispose();
                stream = null;
            }

            if (ResponseStream != null)
            {
                ResponseStream.Close();
                ResponseStream?.Dispose();
                ResponseStream = null;
            }

            if (resp != null)
            {
                resp?.Close();
                resp?.Dispose();
                resp = null;
            }

            base.Dispose();
        }
    }
}