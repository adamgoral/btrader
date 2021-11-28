using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace BTrader.UI.ViewModels
{
    public class MarketNavigatorViewModel : BaseViewModel
    {
        private readonly Dictionary<string, ISession> sourceSessions;
        private EventCategoryNavItem selectedEventCategory;
        private MarketNavItem selectedMarket;
        private Subject<MarketNavItem> marketSelection = new Subject<MarketNavItem>();

        public MarketNavigatorViewModel(IDictionary<string, ISession> otherSessions)
        {
            this.sourceSessions = new Dictionary<string, ISession>();

            foreach (var kvp in otherSessions)
            {
                this.sourceSessions.Add(kvp.Key, kvp.Value);
            }

            this.RefreshCommand = new DelegateCommand(o => true, o => this.Refresh());
            this.MarketSelection = this.marketSelection;
        }

        private void Refresh()
        {
            this.EventCategories.Clear();
            var eventCategories = new List<EventCategoryNavItem>();
            var consolidatedCategories = new Dictionary<string, EventCategoryNavItem>();
            foreach(var kvp in this.sourceSessions)
            {
                var session = kvp.Value;
                foreach (var eventCategory in session.GetEventCategories())
                {
                    EventCategoryNavItem item;
                    if (!consolidatedCategories.TryGetValue(eventCategory.Name, out item))
                    {
                        item = new EventCategoryNavItem
                        {
                            Name = eventCategory.Name
                        };
                        eventCategories.Add(item);
                        consolidatedCategories[eventCategory.Name] = item;
                    }

                    item.EventCategories[kvp.Key] = eventCategory;
                }
            }

            foreach(var item in eventCategories.OrderBy(e => e.Name))
            {
                this.EventCategories.Add(item);
            }
        }

        public DelegateCommand RefreshCommand { get; }

        public ObservableCollection<EventCategoryNavItem> EventCategories { get; } = new ObservableCollection<EventCategoryNavItem>();
        public ObservableCollection<MarketNavItem> Markets { get; } = new ObservableCollection<MarketNavItem>();

        public void ShowMarkets(EventCategoryNavItem eventCategoryNavItem)
        {
            this.Markets.Clear();
            if (eventCategoryNavItem == null) return;
            var marketObservations = new List<MarketNavItem>();
            foreach(var kvp in this.sourceSessions)
            {
                var session = kvp.Value;
                var exchange = kvp.Key;
                EventCategory eventCategory;
                if(eventCategoryNavItem.EventCategories.TryGetValue(exchange, out eventCategory))
                {
                    foreach (var market in session
                        .GetEvents(new[] { eventCategory })
                        .SelectMany(o => o.Markets.Values)
                        .OrderBy(m => m.Start))
                    {
                        var item = new MarketNavItem
                        {
                            Name = market.Name,
                            Exchange = exchange,
                            Start = market.Start.Value,
                            Status = market.Status,
                            Type = market.Type,
                            Market = market,
                            Volume = market.Volume ?? 0
                        };

                        marketObservations.Add(item);
                    }
                }

            }

            foreach (var item in marketObservations.OrderBy(m => m.Start))
            {
                this.Markets.Add(item);
            }
        }

        public EventCategoryNavItem SelectedEventCategory
        {
            get => this.selectedEventCategory;
            set
            {
                if(this.selectedEventCategory != value)
                {
                    this.selectedEventCategory = value;
                    this.OnPropertyChanged("SelectedEventCategory");
                    this.ShowMarkets(value);
                }
            }
        }

        public MarketNavItem SelectedMarket
        {
            get => this.selectedMarket;
            set
            {
                if(this.selectedMarket != value)
                {
                    this.selectedMarket = value;
                    this.OnPropertyChanged("SelectedMarket");
                    this.marketSelection.OnNext(value);
                }
            }
        }

        public IObservable<MarketNavItem> MarketSelection { get; }
    }
}
