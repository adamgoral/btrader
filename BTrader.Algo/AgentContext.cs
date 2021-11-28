using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.Algo
{
    public class AgentContext : IDisposable
    {
        private bool disposed;

        public AgentContext(IDictionary<string, ISession> sessions, IObservable<DateTime> timer, string logFile)
        {
            this.Sessions = sessions.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            Timer = timer;
            if (!string.IsNullOrWhiteSpace(logFile))
            {
                this.writer = new StreamWriter(logFile, false);
            }
        }

        public IReadOnlyDictionary<string, ISession> Sessions { get; }
        public IObservable<DateTime> Timer { get; }

        private StreamWriter writer;

        public void Log(string message)
        {
            if (this.disposed) return;
            this.writer?.WriteLine(message);
            this.writer?.Flush();
        }

        public void Dispose()
        {
            this.disposed = true;
            this.writer?.Dispose();
        }
    }
}
