using System.Runtime.Serialization;

namespace BTrader.Matchbook
{
    [DataContract]
    public class GetMarketsResponse : PagedResponse
    {
        [DataMember(Name = "markets")]
        public MatchbookOrderBook[] Markets { get; set; }
    }
}