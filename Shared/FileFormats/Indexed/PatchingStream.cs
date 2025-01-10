namespace Shared.FileFormats.Indexed
{
    /// <summary>
    /// A read-only stream wrapper that can 'patch' specific parts of the underlying stream.
    /// For example, if the underlying stream contains the data [00 01 02 03 04 05 06 07],
    /// and patch data [EE FF] is added at offset 3, then reading the patching stream will produce [00 01 02 EE FF 05 06 07].
    /// </summary>
    internal class PatchingStream : Stream
    {
        public override bool CanRead => Stream.CanRead;
        public override bool CanWrite => false;
        public override bool CanSeek => Stream.CanSeek;
        public override bool CanTimeout => Stream.CanTimeout;

        public override long Length => Stream.Length;
        public override long Position
        {
            get => Stream.Position;
            set => Stream.Position = value;
        }

        public override int ReadTimeout
        {
            get => Stream.ReadTimeout;
            set => Stream.ReadTimeout = value;
        }

        public override int WriteTimeout
        {
            get => Stream.WriteTimeout;
            set => Stream.WriteTimeout = value;
        }


        private Stream Stream { get; }
        private bool LeaveOpen { get; }
        private List<Patch> Patches { get; } = new();


        public PatchingStream(Stream stream, bool leaveOpen = false)
        {
            Stream = stream;
            LeaveOpen = leaveOpen;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!LeaveOpen)
                Stream.Dispose();
        }


        // TODO: Performance can be improved by sorting patches by offset, and by merging patches that overlap!
        public void AddPatch(int offset, byte[] data)
        {
            Patches.Add(new Patch(offset, data));
        }


        public override void Flush() => Stream.Flush();


        public override IAsyncResult BeginRead(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state) => throw new NotImplementedException();

        public override int EndRead(IAsyncResult asyncResult) => throw new NotImplementedException();

        public override IAsyncResult BeginWrite(byte[] buffer, int offset, int count, AsyncCallback? callback, object? state) => throw new NotImplementedException();

        public override void EndWrite(IAsyncResult asyncResult) => throw new NotImplementedException();


        public override long Seek(long offset, SeekOrigin origin) => Stream.Seek(offset, origin);

        public override void SetLength(long value) => throw new NotImplementedException();


        public override int Read(byte[] buffer, int offset, int count)
        {
            var startPosition = Stream.Position;
            var bytesRead = Stream.Read(buffer, offset, count);

            ApplyPatchData(buffer, startPosition, bytesRead);

            return bytesRead;
        }

        public override void Write(byte[] buffer, int offset, int count) => throw new NotImplementedException();


        private void ApplyPatchData(byte[] buffer, long startPosition, int bytesRead)
        {
            foreach (var patch in Patches)
            {
                // NOTE: We can bail out once we're past the data that has been read, if patches have been sorted by offset (and if they have been merged properly)!
                if (patch.Offset >= startPosition + bytesRead || patch.Offset + patch.Length <= startPosition)
                    continue;

                var patchOffset = Math.Max(0, startPosition - patch.Offset);
                var bufferOffset = Math.Max(0, patch.Offset - startPosition);
                var length = Math.Min(patch.Length - patchOffset, bytesRead - bufferOffset);

                Array.Copy(patch.Data, patchOffset, buffer, bufferOffset, length);
            }
        }


        private class Patch
        {
            public int Offset { get; }
            public int Length => Data.Length;

            public byte[] Data { get; }


            public Patch(int offset, byte[] data)
            {
                Offset = offset;
                Data = data;
            }
        }
    }
}
