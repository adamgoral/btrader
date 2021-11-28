using System.Runtime.Serialization;

namespace BTrader.Matchbook
{
    //        {
    //          "available-amount": 60,
    //          "currency": "EUR",
    //          "odds-type": "DECIMAL",
    //          "odds": 2,
    //          "decimal-odds": 2,
    //          "side": "lay",
    //          "exchange-type": "back-lay"
    //        }

    [DataContract]
    public class MatchbookPrice
    {
        [DataMember(Name = "side")]
        public MatchbookOrderSides Side { get; set; }
        [DataMember(Name = "decimal-odds")]
        public decimal Price { get; set; }
        [DataMember(Name = "available-amount")]
        public decimal Volume { get; set; }
    }
}
