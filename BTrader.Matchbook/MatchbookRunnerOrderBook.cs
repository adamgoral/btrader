using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace BTrader.Matchbook
{
    [DataContract]
    public class MatchbookRunnerOrderBook
    {
        [DataMember(Name = "id")]
        public long RunnerId { get; set; }
        [DataMember(Name = "name")]
        public string Name { get; set; }
        [DataMember(Name = "status")]
        public string Status { get; set; }
        [DataMember(Name = "volume")]
        public decimal Volume { get; set; }
        [DataMember(Name = "prices")]
        public MatchbookPrice[] Prices { get; set; }
        [DataMember(Name = "event-participant-id")]
        public long? ParticipantId { get; set; }

        public OutcomeObservation ToObservation(DateTime timestamp, Dictionary<long, EventParticipant> participants)
        {
            var status = OutcomeStatus.Active;
            var toLay = new List<PriceSize>();
            var toBack = new List<PriceSize>();
            var traded = new List<PriceSize>();
            foreach(var price in this.Prices)
            {
                var priceChange = new PriceSize(price.Price, price.Volume);
                if (price.Side == MatchbookOrderSides.Lay)
                {
                    toLay.Add(priceChange);
                }
                else
                {
                    toBack.Add(priceChange);
                }
            }

            var name = this.Name;
            if(this.ParticipantId!= null && participants != null)
            {
                EventParticipant participant = null;
                if(participants.TryGetValue(this.ParticipantId.Value, out participant))
                {
                    name = participant.Name;
                }
            }

            var orderBook = new OrderBookObservation(toLay, toBack, traded, this.Volume, true);
            var result = new OutcomeObservation(this.RunnerId.ToString(), timestamp, name, status, orderBook, new Order[0], null, false);
            return result;
        }
    }
}
