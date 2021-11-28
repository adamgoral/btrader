using System;

namespace BTrader.Betfair
{
    public class BetfarStreamSessionStatusEventArgs : EventArgs
    {
        public BetfarStreamSessionStatusEventArgs(BetfairStreamSessionStatus status)
        {
            Status = status;
        }

        public BetfairStreamSessionStatus Status { get; }
    }
}

