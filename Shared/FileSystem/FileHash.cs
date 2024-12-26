using System.Security.Cryptography;

namespace Shared.FileSystem
{
    public struct FileHash : IEquatable<FileHash>
    {
        /// <summary>
        /// Returns the SHA256 hash of the given file.
        /// </summary>
        public static FileHash FromFile(string path, out int fileSize)
        {
            using (var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sha256 = SHA256.Create())
            {
                fileSize = (int)file.Length;
                return new FileHash(sha256.ComputeHash(file));
            }
        }

        /// <summary>
        /// Parses a SHA256 hash from a hex string.
        /// </summary>
        public static FileHash Parse(string hexString) => new FileHash(Convert.FromHexString(hexString));


        private byte[] _hash;


        public FileHash() => _hash = Array.Empty<byte>();

        public FileHash(IEnumerable<byte> hash) => _hash = hash.ToArray();

        public bool Equals(FileHash other) => _hash.SequenceEqual(other._hash);

        public override bool Equals(object? obj) => obj is FileHash other && Equals(other);

        public override int GetHashCode() => _hash.Length >= 4 ? BitConverter.ToInt32(_hash, 0) : _hash.Length > 0 ? _hash[0] : 0;

        public override string ToString() => Convert.ToHexString(_hash);

        public static bool operator ==(FileHash left, FileHash right) => left.Equals(right);
        public static bool operator !=(FileHash left, FileHash right) => !left.Equals(right);
    }
}
