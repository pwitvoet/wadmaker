using System;
using System.Drawing;
using System.IO;
using System.Text;

namespace WadMaker
{
    static class StreamExtensions
    {
        #region Reading

        public static ushort ReadUshort(this Stream stream) => BitConverter.ToUInt16(stream.ReadBytes(2), 0);

        public static int ReadInt(this Stream stream) => BitConverter.ToInt32(stream.ReadBytes(4), 0);

        public static uint ReadUint(this Stream stream) => BitConverter.ToUInt32(stream.ReadBytes(4), 0);

        public static float ReadFloat(this Stream stream) => BitConverter.ToSingle(stream.ReadBytes(4), 0);

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
            var bytesRead = stream.Read(buffer, 0, count);
            if (bytesRead < count)
                throw new EndOfStreamException($"Expected {count} bytes but found only {bytesRead}.");
            return buffer;
        }


        public static Color ReadColor(this Stream stream)
        {
            var data = stream.ReadBytes(3);
            return Color.FromArgb(data[0], data[1], data[2]);
        }

        #endregion


        #region Writing

        public static void Write(this Stream stream, ushort value) => stream.Write(BitConverter.GetBytes(value));

        public static void Write(this Stream stream, int value) => stream.Write(BitConverter.GetBytes(value));

        public static void Write(this Stream stream, uint value) => stream.Write(BitConverter.GetBytes(value));

        public static void Write(this Stream stream, float value) => stream.Write(BitConverter.GetBytes(value));

        public static void Write(this Stream stream, string value) => stream.Write(Encoding.ASCII.GetBytes(value));

        public static void Write(this Stream stream, string value, int length)
        {
            var bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, Math.Min(bytes.Length, length));
            if (bytes.Length < length)
                stream.Write(new byte[length - bytes.Length], 0, length - bytes.Length);
        }

        public static void Write(this Stream stream, byte[] bytes) => stream.Write(bytes, 0, bytes.Length);


        public static void Write(this Stream stream, Color value) => stream.Write(new byte[] { value.R, value.G, value.B });

        #endregion
    }
}
