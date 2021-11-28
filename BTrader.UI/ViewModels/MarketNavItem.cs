using BTrader.Domain;
using System;
using System.Collections.Generic;

namespace BTrader.UI.ViewModels
{
    public class MarketNavItem
    {
        public string Name { get; set; }
        public DateTime Start { get; set; }
        public MarketStatus? Status { get; set; }
        public decimal Volume { get; set; }
        public string Type { get; set; }
        public string Exchange { get; set; }
        public MarketObservation Market { get; set; }
    }
}