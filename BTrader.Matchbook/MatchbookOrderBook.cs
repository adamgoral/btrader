using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.Matchbook
{
    //{
    //  "event-id": 395729780570010,
    //  "id": 395729860260010,
    //  "name": "Match Odds",
    //  "runners": [
    //    {
    //      "withdrawn": false,
    //      "prices": [
    //        {
    //          "available-amount": 20,
    //          "currency": "EUR",
    //          "odds-type": "DECIMAL",
    //          "odds": 3,
    //          "decimal-odds": 3,
    //          "side": "lay",
    //          "exchange-type": "back-lay"
    //        }
    //      ],
    //      "event-id": 395729780570010,
    //      "id": 395729860800010,
    //      "market-id": 395729860260010,
    //      "name": "CSKA Moscow",
    //      "status": "open",
    //      "volume": 0,
    //      "event-participant-id": 395729781340010
    //    },
    //    {
    //      "withdrawn": false,
    //      "prices": [],
    //      "event-id": 395729780570010,
    //      "id": 395729861280010,
    //      "market-id": 395729860260010,
    //      "name": "Bayer 04 Leverkusen",
    //      "status": "open",
    //      "volume": 0,
    //      "event-participant-id": 395729781460010
    //    },
    //    {
    //      "withdrawn": false,
    //      "prices": [
    //        {
    //          "available-amount": 60,
    //          "currency": "EUR",
    //          "odds-type": "DECIMAL",
    //          "odds": 2,
    //          "decimal-odds": 2,
    //          "side": "lay",
    //          "exchange-type": "back-lay"
    //        }
    //      ],
    //      "event-id": 395729780570010,
    //      "id": 395729861330010,
    //      "market-id": 395729860260010,
    //      "name": "DRAW (CSK/Bay)",
    //      "status": "open",
    //      "volume": 0,
    //      "event-participant-id": 0
    //    }
    //  ],
    //  "start": "2016-11-22T17:00:00.000Z",
    //  "status": "open",
    //  "market-type": "one_x_two",
    //  "type": "multirunner",
    //  "in-running-flag": false,
    //  "allow-live-betting": true,
    //  "volume": 0
    //}

    [DataContract]
    public class MatchbookOrderBook
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }
        [DataMember(Name = "start")]
        public DateTime Start { get; set; }
        [DataMember(Name = "market-type")]
        public string MarketType { get; set; }
        [DataMember(Name = "timestamp")]
        public DateTime Timestamp { get; set; }
        [DataMember(Name = "event-id")]
        public long EventId { get; set; }
        [DataMember(Name = "id")]
        public long MarketId { get; set; }
        [DataMember(Name = "status")]
        public string Status { get; set; }
        [DataMember(Name = "volume")]
        public decimal Volume { get; set; }
        [DataMember(Name = "runners")]
        public MatchbookRunnerOrderBook[] Runners { get; set; }
        public MatchbookMarketId GetMatchbookId()
        {
            return new MatchbookMarketId(this.EventId, this.MarketId);
        }

        public MarketObservation ToObservation()
        {
            return this.ToObservation(null);
        }

        public MarketObservation ToObservation(Dictionary<long, EventParticipant> participants)
        {
            var status = MarketStatus.Suspended;
            if (this.Status == "open")
            {
                status = MarketStatus.Open;
            }

            var marketType = this.MarketType;
            if(marketType == "outright_ded_fact")
            {
                marketType = "WIN";
            }

            var outcomeList = this.Runners.Select(r => r.ToObservation(this.Timestamp, participants)).ToList();

            var result = new MarketObservation(this.GetMatchbookId().ToString(), this.Start, this.Timestamp, this.Name, status, marketType, this.Volume, outcomeList);
            return result;
        }
    }
}
