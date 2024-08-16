namespace Shared
{
    public class InvalidUsageException : Exception
    {
        public InvalidUsageException(string message)
            : base(message)
        {
        }
    }
}
