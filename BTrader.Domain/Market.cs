using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reactive.Linq;
using System.Collections.ObjectModel;

namespace BTrader.Domain
{
    public class Market
    {
        public static Market FromObservation(MarketObservation observation, IObservable<MarketObservation> changes)
        {
            return new Market(observation.Timestamp, observation.Id, observation.Name, observation.Status.Value, observation.Start.Value, observation.Outcomes, changes);
        }

        public Market(DateTime timestamp, string id, string name, MarketStatus status, DateTime start, IEnumerable<OutcomeObservation> outcomes, IObservable<MarketObservation> observations)
        {
            this.Id = id;
            this.Name = name;
            this.Status = status;
            Start = start;
            this.LastUpdate = timestamp;
            this.Changes = observations.Select(this.OnChange);
            var outcomeChanges = this.Changes.SelectMany(c => c.Outcomes);
            this.Outcomes = outcomes.Select(o => Outcome.FromObservation(o, outcomeChanges.Where(oc => oc.Id == o.Id)))
                .ToDictionary(o => o.Id, o => o, StringComparer.CurrentCultureIgnoreCase);
        }

        public string Id { get; }

        public string Name { get; }

        public MarketStatus Status { get; private set; }
        public DateTime Start { get; }
        public DateTime LastUpdate { get; private set; }
        public IReadOnlyDictionary<string, Outcome> Outcomes { get; }

        public IObservable<MarketChange> Changes { get; }

        private MarketChange OnChange(MarketObservation change)
        {
            lock (this)
            {
                if (change.Status != null) this.Status = change.Status.Value;
                this.LastUpdate = change.Timestamp;
                var resultOutcomeChanges = new List<OutcomeChange>();
                foreach (var outcome in change.Outcomes)
                {
                    Outcome existingOutcome;
                    if(this.Outcomes.TryGetValue(outcome.Id, out existingOutcome))
                    {
                        var outcomeChange = existingOutcome.OnChange(outcome);
                        resultOutcomeChanges.Add(outcomeChange);
                    }
                }

                return new MarketChange(change.Timestamp, this.Status, new ReadOnlyCollection<OutcomeChange>(resultOutcomeChanges));
            }
        }
    }
}