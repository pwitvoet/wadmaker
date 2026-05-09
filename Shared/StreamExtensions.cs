using SixLabors.ImageSharp.PixelFormats;
using System.Buffers.Binary;
using System.Text;

namespace Shared
{
    public static class StreamExtensions
    {
        #region Reading

        public static short ReadShort(this Stream stream)
        {
            Span<byte> data = stackalloc byte[2];
            stream.ReadExactly(data);
            return BinaryPrimitives.ReadInt16LittleEndian(data);
        }

        public static ushort ReadUshort(this Stream stream)
        {
            Span<byte> data = stackalloc byte[2];
            stream.ReadExactly(data);
            return BinaryPrimitives.ReadUInt16LittleEndian(data);
        }

        public static int ReadInt24(this Stream stream)
        {
            Span<byte> data = stackalloc byte[3];
            stream.ReadExactly(data);
            return (data[0]) | (data[1] << 8) | (data[2] << 16);
        }

        public static int ReadInt(this Stream stream)
        {
            Span<byte> data = stackalloc byte[4];
            stream.ReadExactly(data);
            return BinaryPrimitives.ReadInt32LittleEndian(data);
        }

        public static uint ReadUint(this Stream stream)
        {
            Span<byte> data = stackalloc byte[4];
            stream.ReadExactly(data);
            return BinaryPrimitives.ReadUInt32LittleEndian(data);
        }

        public static float ReadFloat(this Stream stream)
        {
            Span<byte> data = stackalloc byte[4];
            stream.ReadExactly(data);
            return BinaryPrimitives.ReadSingleLittleEndian(data);
        }

        public static string ReadString(this Stream stream, int length)
        {
            var bytes = stream.ReadBytes(length);

            var actualLength = 0;
            for (actualLength = 0; actualLength < bytes.Length; actualLength++)
                if (bytes[actualLength] == 0)
                    break;

            return Encoding.ASCII.GetString(bytes, 0, actualLength);
        }

        public static byte[] ReadBytes(this Stream stream, int count)
        {
            var buffer = new byte[count];
            stream.ReadExactly(buffer, 0, count);
            return buffer;
        }


        public static int ReadIntBigEndian(this Stream stream)
        {
            Span<byte> data = stackalloc byte[4];
            stream.ReadExactly(data);
            return BinaryPrimitives.ReadInt32BigEndian(data);
        }

        public static uint ReadUintBigEndian(this Stream stream)
        {
            Span<byte> data = stackalloc byte[4];
            stream.ReadExactly(data);
            return BinaryPrimitives.ReadUInt32BigEndian(data);
        }


        public static Rgba32 ReadColor(this Stream stream)
        {
            Span<byte> data = stackalloc byte[3];
            stream.ReadExactly(data);
            return new Rgba32(data[0], data[1], data[2]);
        }

        #endregion


        #region Writing

        public static void Write(this Stream stream, byte value) => stream.WriteByte(value);

        public static void Write(this Stream stream, ushort value)
        {
            Span<byte> data = stackalloc byte[2];
            BinaryPrimitives.WriteUInt16LittleEndian(data, value);
            stream.Write(data);
        }

        public static void Write(this Stream stream, int value)
        {
            Span<byte> data = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(data, value);
            stream.Write(data);
        }

        public static void Write(this Stream stream, uint value)
        {
            Span<byte> data = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(data, value);
            stream.Write(data);
        }

        public static void Write(this Stream stream, float value)
        {
            Span<byte> data = stackalloc byte[4];
            BinaryPrimitives.WriteSingleLittleEndian(data, value);
            stream.Write(data);
        }

        public static void Write(this Stream stream, string value) => stream.Write(Encoding.ASCII.GetBytes(value));

        public static void Write(this Stream stream, string value, int length)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, Math.Min(bytes.Length, length));
            if (bytes.Length < length)
                stream.Write(new byte[length - bytes.Length], 0, length - bytes.Length);
        }


        public static void Write(this Stream stream, Rgba32 value) => stream.Write([value.R, value.G, value.B]);

        #endregion


        // NOTE: Find a better place for this function!
        /// <summary>
        /// Returns the number of bytes that must be added to the given <paramref name="length"/> to make it a multiple of <paramref name="padToMultipleOf"/>.
        /// </summary>
        public static int RequiredPadding(int length, int padToMultipleOf)
        {
            var excess = length % padToMultipleOf;
            return excess == 0 ? 0 : padToMultipleOf - excess;
        }
    }
}
