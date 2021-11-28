using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net;
using System.Reactive.Linq;
using System.Text;
using System.Timers;
using System.Reactive.Subjects;
using System.Reactive.Disposables;
using System.Collections.Concurrent;

namespace BTrader.Matchbook
{

    public class Session : ISession, IDisposable
    {
        private readonly MatchbookRestClient restClient;
        private readonly IMarketHistoryWriter<Sport, MatchbookEvent, MatchbookOrderBook> historyWriter;
        private SessionStatus status;
        private readonly Timer pollingTimer = new Timer();
        private readonly ReplaySubject<MatchbookOrderBook> marketChanges = new ReplaySubject<MatchbookOrderBook>(100);
        private readonly HashSet<MatchbookMarketId> marketsToPoll = new HashSet<MatchbookMarketId>();

        public static Session Create()
        {
            var historyWriter = new MatchbookHistoryReaderWriter("data\\matchbook", false);
            return Create(historyWriter);
        }

        public static Session Create(IMarketHistoryWriter<Sport, MatchbookEvent, MatchbookOrderBook> historyWriter)
        {
            var matchbookClient = new MatchbookRestClient(new JsonRestClient(""), TimeSpan.FromMilliseconds(10), new DebugLogger());
            return new Session(matchbookClient, TimeSpan.FromSeconds(1), historyWriter);
        }

        public Session(MatchbookRestClient restClient, TimeSpan pollingInterval, IMarketHistoryWriter<Sport, MatchbookEvent, MatchbookOrderBook> historyWriter)
        {
            this.restClient = restClient;
            this.historyWriter = historyWriter;
            this.pollingTimer.Elapsed += OnTimer;
            this.pollingTimer.Interval = pollingInterval.TotalMilliseconds;
        }

        private void OnTimer(object sender, ElapsedEventArgs e)
        {
            MatchbookMarketId[] marketKeys = null;
            lock (this.marketsToPoll)
            {
                marketKeys = this.marketsToPoll.ToArray();
            }

            if (!System.Threading.Monitor.TryEnter(this.pollingTimer)) return;

            try
            {
                foreach(var orderBook in this.restClient.GetOrderBooks(marketKeys))
                {
                    if (orderBook.Status == "closed")
                    {
                        this.RemoveFromPolling(orderBook.GetMatchbookId());
                    }

                    if (this.historyWriter != null)
                    {
                        this.historyWriter.OnNext(orderBook);
                    }

                    this.marketChanges.OnNext(orderBook);
                }
            }
            finally
            {
                System.Threading.Monitor.Exit(this.pollingTimer);
            }
        }

        public SessionStatus Status
        {
            get { return this.status; }
            private set
            {
                if (this.status != value)
                {
                    this.status = value;
                    this.SessionStatusChanged?.Invoke(this, new SessionStatusChangeEventArgs(this.status));
                }
            }
        }

        public event EventHandler<SessionStatusChangeEventArgs> SessionStatusChanged;

        public void CancelOrders(IEnumerable<Order> orders)
        {
            throw new NotImplementedException();
        }

        public void Connect()
        {
            var loginRequest = new LoginRequest
            {
                Username = "",
                Password = ""
            };
            this.Status = SessionStatus.Connecting;
            var loginResponse = this.restClient.Login(loginRequest);
            this.Status = SessionStatus.Connected;
        }

        public void Disconnect()
        {
            this.restClient.Logout();
            this.Status = SessionStatus.Disconnected;
            var cats = this.GetEventCategories();
        }

        public void Dispose()
        {
            this.pollingTimer.Dispose();
        }

        public ReadOnlyCollection<EventCategory> GetEventCategories()
        {
            var sports = this.restClient.GetSports().ToArray();
            if (this.historyWriter != null)
            {
                this.historyWriter.Write(sports);
            }

            var result = sports.Select(s => s.ToEventCategory()).ToList();
            return new ReadOnlyCollection<EventCategory>(result);
        }

        public ReadOnlyCollection<Event> GetEvents(IEnumerable<EventCategory> categories)
        {
            var categoryLookup = new Dictionary<string, EventCategory>(StringComparer.CurrentCultureIgnoreCase);
            foreach (var category in categories)
            {
                categoryLookup[category.Id] = category;
            }

            var events = this.restClient.GetEvents(categoryLookup.Values.Select(c => long.Parse(c.Id))).ToArray();
            if (this.historyWriter != null)
            {
                foreach(var g in events.GroupBy(e => e.SportId))
                {
                    this.historyWriter.Write(g.Key.ToString(), g);
                }
            }

            var list = new List<Event>();
            foreach(var e in events)
            {
                var participants = new Dictionary<long, EventParticipant>();
                if (e.Participants != null)
                {
                    participants = e.Participants.ToDictionary(p => p.Id, p => p);
                }
                var domainMarkets = new List<MarketObservation>();
                foreach(var market in e.Markets)
                {
                    var domainMarket = market.ToObservation(participants);
                    domainMarkets.Add(domainMarket);
                }

                list.Add(new Event(e.Id.ToString(), e.Name, categoryLookup[e.SportId.ToString()], e.Start, domainMarkets));
            }

            return new ReadOnlyCollection<Event>(list);
        }

        private void AddToPolling(MatchbookMarketId marketKey)
        {
            lock (this.marketsToPoll)
            {
                this.marketsToPoll.Add(marketKey);
                if (!this.pollingTimer.Enabled)
                {
                    this.pollingTimer.Enabled = true;
                }
            }
        }

        private void RemoveFromPolling(MatchbookMarketId marketId)
        {
            lock (this.marketsToPoll)
            {
                this.marketsToPoll.Remove(marketId);
                if (!this.marketsToPoll.Any() && this.pollingTimer.Enabled)
                {
                    this.pollingTimer.Enabled = false;
                }
            }
        }

        private readonly ConcurrentDictionary<string, IObservable<MarketObservation>> marketSubscriptions = new ConcurrentDictionary<string, IObservable<MarketObservation>>();


        public IObservable<MarketObservation> GetMarketChangeStream(string marketId)
        {
            return this.marketSubscriptions.GetOrAdd(marketId, this.CreateSubscription);
        }

        private IObservable<MarketObservation> CreateSubscription(string marketId)
        {
            return Observable.Create<MarketObservation>(observer =>
            {
                var id = new MatchbookMarketId(marketId);
                var subscription = this.marketChanges
                    .Where(m => m.EventId == m.EventId && m.MarketId == id.MarketId)
                    .Select(m => m.ToObservation())
                    .Subscribe(observer);
                this.AddToPolling(id);
                return Disposable.Create(() =>
                {
                    this.RemoveFromPolling(id);
                    subscription.Dispose();
                    IObservable<MarketObservation> existing;
                    this.marketSubscriptions.TryRemove(marketId, out existing);
                });
            }).Publish().RefCount();
        }

        public ReadOnlyCollection<Market> GetMarkets(IEnumerable<Event> events)
        {
            throw new NotImplementedException();
        }

        public IObservable<Order> GetOrderChangeStream(string marketId)
        {
            throw new NotImplementedException();
        }

        public ReadOnlyCollection<Order> GetOrders(string marketId)
        {
            throw new NotImplementedException();
        }

        public void PlaceOrders(IEnumerable<OrderRequest> orderRequests)
        {
            throw new NotImplementedException();
        }

        public ReadOnlyDictionary<string, ReadOnlyDictionary<string, OrderBookObservation>> GetObservations(IEnumerable<string> marketIds)
        {
            throw new NotImplementedException();
        }
    }
}
