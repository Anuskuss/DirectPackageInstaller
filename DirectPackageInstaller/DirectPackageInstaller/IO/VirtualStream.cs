﻿using System;
using System.IO;
using System.Threading.Tasks;

namespace DirectPackageInstaller
{
    public class VirtualStream : Stream
    {
        public readonly Stream Base;
        public long FilePos { get; private set; } = 0;
        private long Len;

        public VirtualStream(Stream Base, long Pos, long Len)
        {
            if (!Base.CanRead)
                throw new IOException("The Stream bust be Readable");

            this.Base = Base;
            FilePos = Pos;
            this.Len = Len;
        }

        ~VirtualStream()
        {
            Dispose();
        }

        public override bool CanRead
        {
            get
            {
                return true;
            }
        }

        public override bool CanSeek
        {
            get
            {
                return true;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return false;
            }
        }
        public  override long Length
        {
            get
            {
                return Len;
            }
        }

        internal long Pos = 0;
        public override long Position
        {
            get
            {
                return Pos;
            }
            set
            {
                Seek(value, SeekOrigin.Begin);
            }
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public bool ForceAmount { get; set; } = false;

        public override int Read(byte[] buffer, int offset, int count)
        {
            long ReadPos = FilePos + Pos;

            while (ForceAmount && ReadPos >= Base.Length)
                Task.Delay(10).Wait();

            if (ReadPos != Base.Position)
                Base.Position = ReadPos;

            if (Pos + count > Length)
                count = (int)(Length - Pos);

            if (count + offset >= buffer.Length)
                count = buffer.Length - offset;

            if (count < 0)
                count = 0;

            int Readed = 0;

            do
            {
                
                int lReaded = Base.Read(buffer, offset + Readed, count);
                Readed += lReaded;
                count -= lReaded;

                if (lReaded == 0)
                    Task.Delay(10).Wait();

            } while (count > 0 && ForceAmount);

            Pos += Readed;
            return Readed;
        }

        /// <summary>
        /// Seek the file another location
        /// </summary>
        /// <param name="offset">Value to change the pointer location</param>
        /// <param name="origin">Change from</param>
        /// <returns>New Position</returns>
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (offset < 0 || offset > Length)
                throw new Exception("Invalid Position");
            switch (origin)
            {
                case SeekOrigin.Begin:
                    Base.Position = FilePos + offset;
                    this.Pos = offset;
                    break;
                case SeekOrigin.Current:
                    if (Position + offset > Length)
                        throw new Exception("Out of Range");
                    Base.Position += offset;
                    this.Pos += offset;
                    break;
                case SeekOrigin.End:
                    long Pos = Length - offset;
                    this.Pos = Pos;
                    long FP = FilePos + Pos;
                    if (Pos < 0)
                        throw new Exception("Out of Range");
                    Base.Position = FP;
                    break;
            }

            while (ForceAmount && (Base.Position > Base.Length))
                Task.Delay(100).Wait();

            return Pos;
        }

        public override void SetLength(long value)
        {
            if (FilePos + value > Base.Length)
                throw new Exception("Invalid Length");

            Len = value;
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        protected override void Dispose(bool Disposing)
        {
            Close();
            Base?.Dispose();

            base.Dispose(Disposing);
        }

        public override void Close()
        {
            Base?.Close();
        }
    }
}
