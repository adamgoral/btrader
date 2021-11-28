using System.Diagnostics;

namespace BTrader.Domain
{
    public class DebugLogger : ILog
    {
        public void Error(string message)
        {
            Debug.WriteLine($"ERROR: {message}");
        }

        public void Info(string message)
        {
            Debug.WriteLine($"INFO: {message}");
        }

        public void Warn(string message)
        {
            Debug.WriteLine($"WARN: {message}");
        }
    }
}

