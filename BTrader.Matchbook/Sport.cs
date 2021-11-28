using BTrader.Domain;
using System.Runtime.Serialization;

namespace BTrader.Matchbook
{
    [DataContract]
    public class Sport
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }
        [DataMember(Name = "type")]
        public string Type { get; set; }
        [DataMember(Name = "id")]
        public long Id { get; set; }

        public EventCategory ToEventCategory()
        {
            return new EventCategory(this.Id.ToString(), this.Name);
        }
    }
}
