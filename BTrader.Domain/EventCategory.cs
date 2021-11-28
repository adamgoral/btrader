using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BTrader.Domain
{
    public class EventCategory
    {
        public EventCategory(string id, string name)
        {
            this.Id = id;
            this.Name = name;
        }

        public string Id { get; }

        public string Name { get; }

        public override string ToString()
        {
            return $"{this.Id}: {this.Name}";
        }
    }
}