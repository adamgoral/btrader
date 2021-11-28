using BTrader.Domain;
using System.Collections.Generic;

namespace BTrader.UI.ViewModels
{
    public class EventCategoryNavItem
    {
        public Dictionary<string, EventCategory> EventCategories { get; } = new Dictionary<string, EventCategory>();

        public string Name { get; set; }

        public string Exchanges
        {
            get
            {
                return string.Join(",", this.EventCategories.Keys);
            }
        }
    }
}
