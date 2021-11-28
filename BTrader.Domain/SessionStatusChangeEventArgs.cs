using System;

namespace BTrader.Domain
{
    public class SessionStatusChangeEventArgs : EventArgs
    {
        public SessionStatusChangeEventArgs(SessionStatus status)
        {
            Status = status;
        }

        public SessionStatus Status { get; }
    }
}