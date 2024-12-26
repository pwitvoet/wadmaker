namespace Shared.FileSystem
{
    public class FileInfo
    {
        public static FileInfo FromFile(string path)
        {
            var fileHash = FileHash.FromFile(path, out var fileSize);
            var lastModified = File.GetLastWriteTimeUtc(path);
            return new FileInfo(path, fileSize, fileHash, lastModified);
        }


        public string Path { get; }
        public int FileSize { get; }
        public FileHash FileHash { get; }
        public DateTimeOffset LastModified { get; }


        public FileInfo(string path, int fileSize, FileHash fileHash, DateTimeOffset lastModified)
        {
            Path = path;
            FileSize = fileSize;
            FileHash = fileHash;
            LastModified = lastModified;
        }

        /// <summary>
        /// Checks whether the given file matches the size and hash of this file info.
        /// </summary>
        public bool HasMatchingFileHash(string path)
        {
            try
            {
                var hash = FileHash.FromFile(path, out var fileSize);
                return FileHash.Equals(hash) && FileSize == fileSize;
            }
            catch
            {
                return false;
            }
        }
    }
}
