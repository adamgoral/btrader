using System;
using System.Reactive.Subjects;

namespace BTrader.Betfair
{
    public interface IStreamConnection : ISubject<string>, IDisposable
    {
        void Connect();
    }
}

