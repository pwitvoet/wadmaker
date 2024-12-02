namespace Shared
{
    public class Logger
    {
        private Action<string?> LogFunction { get; }


        public Logger(Action<string?> logFunction)
        {
            LogFunction = logFunction;
        }

        public void Log(string? message) => LogFunction(message);
    }
}
