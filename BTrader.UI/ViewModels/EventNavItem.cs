using BTrader.Domain;
using System;
using System.Collections.Generic;

namespace BTrader.UI.ViewModels
{
    public class EventNavItem
    {
        public string Name { get; set; }
        public DateTime Start { get; set; }
        public Dictionary<string, Event> Events { get; } = new Dictionary<string, Event>();
    }
}
