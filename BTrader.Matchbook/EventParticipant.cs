using System.Runtime.Serialization;

namespace BTrader.Matchbook
{
    [DataContract]
    public class EventParticipant
    {
        [DataMember(Name = "id")]
        public long Id { get; set; }
        [DataMember(Name = "participant-name")]
        public string Name { get; set; }
    }
}