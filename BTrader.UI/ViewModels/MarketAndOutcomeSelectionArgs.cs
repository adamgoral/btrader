using BTrader.Domain;
using System;

namespace BTrader.UI.ViewModels
{
    public class MarketAndOutcomeSelectionArgs : EventArgs
    {
        public MarketAndOutcomeSelectionArgs(Market market, Outcome outcome)
        {
            Market = market;
            Outcome = outcome;
        }

        public Market Market { get; }
        public Outcome Outcome { get; }
    }
}
