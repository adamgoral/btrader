namespace BTrader.Domain
{
    public interface ILog
    {
        void Error(string message);
        void Info(string message);
        void Warn(string message);
    }
}

