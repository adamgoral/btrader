using Api_ng_sample_code;
using Api_ng_sample_code.TO;
using BFSwagger = Betfair.ESASwagger;
using BTrader.Domain;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Betfair.ESASwagger.Model;
using System.Diagnostics;

namespace BTrader.Betfair
{

    public class Session : ISession, IDisposable
    {
        public event EventHandler<SessionStatusChangeEventArgs> SessionStatusChanged;

        private readonly IStreamSession streamSession;
        private readonly IClient client;
        private readonly IMarketHistoryWriter<EventTypeResult, MarketCatalogue, MarketChangeMessage> historyWriter;
        private readonly CompositeDisposable disposables = new CompositeDisposable();
        private readonly ReplaySubject<Domain.MarketObservation> marketChanges = new ReplaySubject<Domain.MarketObservation>(10000);
        private readonly ReplaySubject<Domain.Order> orderChanges = new ReplaySubject<Domain.Order>(10000);

        public SessionStatus Status { get; private set; }

        public static Session Create()
        {
            return Create(new BetfairHistoryReaderWriter("data\\betfair", false));
        }

        public static Session Create(IMarketHistoryWriter<EventTypeResult, MarketCatalogue, MarketChangeMessage> historyWriter)
        {
            var sessionProvider = new AppKeySessionProvider(AppKeySessionProvider.SSO_HOST_COM, Betfair.AppKeySessionProvider.LIVEAPPKEY, "", "");
            var streamSession = new BetfairStreamSession(() => new BetfairConnection("stream-api.betfair.com", 443),
                                sessionProvider,
                                new DebugLogger());
            var jsonClient = new JsonRpcClient("https://api.betfair.com/exchange/betting", Betfair.AppKeySessionProvider.LIVEAPPKEY, sessionProvider.GetOrCreateSession());
            var session = new Session(streamSession, jsonClient, historyWriter);
            session.Connect();
            return session;
        }

        public Session(IStreamSession streamSession, IClient client, IMarketHistoryWriter<EventTypeResult, MarketCatalogue, MarketChangeMessage> historyWriter)
        {
            this.streamSession = streamSession;
            this.client = client;
            this.historyWriter = historyWriter;
            this.disposables.Add(this.streamSession.MarketChanges.Subscribe(this.ProcessMarketChange));
            if (historyWriter != null)
            {
                this.disposables.Add(this.streamSession.MarketChanges.Subscribe(historyWriter));
            }

            this.disposables.Add(this.streamSession.OrderChanges.Subscribe(this.ProcessOrderChange));
            this.streamSession.StatusChanged += StreamSession_StatusChanged;
        }

        private void StreamSession_StatusChanged(object sender, BetfarStreamSessionStatusEventArgs e)
        {
            var status = Domain.SessionStatus.Disconnected;
            if(e.Status == BetfairStreamSessionStatus.Closed)
            {
                status = SessionStatus.Disconnected;
            }
            else if(e.Status == BetfairStreamSessionStatus.Opening || e.Status == BetfairStreamSessionStatus.Reconnecting)
            {
                status = SessionStatus.Connecting;
            }
            this.Status = status;
            this.SessionStatusChanged?.Invoke(this, new SessionStatusChangeEventArgs(status));
        }

        private void ProcessOrderChange(BFSwagger.Model.OrderChangeMessage orderChangeMessage)
        {
            if (orderChangeMessage.Oc == null) return;

            var orderByMarket = orderChangeMessage.Oc.GroupBy(o => o.Id);
            foreach(var g in orderByMarket)
            {
                Debug.Print($"retrieving orders snap for {g.Key}");
                var fullSet = this.GetOrders(g.Key);
                this.marketChanges.OnNext(fullSet.ToMarketObservation(g.Key, DateTime.UtcNow, true));
            }

            // at the moment cannot trust details in order streaming updates - cancelled orders are showing up
            //var timeStamp = new DateTime(1970, 1, 1).AddMilliseconds(orderChangeMessage.Pt.Value);
            //foreach(var orderChange in orderChangeMessage.Oc)
            //{
            //    var orders = orderChange.ToOrders().ToArray();
            //    foreach(var order in orders)
            //    {
            //        this.orderChanges.OnNext(order);
            //    }
            //}
        }

        public void Connect()
        {
            this.streamSession.Open().Wait();
            this.streamSession.OrderSubscription(new BFSwagger.Model.OrderSubscriptionMessage { });
        }

        public void Disconnect()
        {
            this.streamSession.Close();
        }

        public void Dispose()
        {
            this.streamSession.StatusChanged -= this.StreamSession_StatusChanged;
        }

        public ReadOnlyCollection<EventCategory> GetEventCategories()
        {
            var eventTypes = this.client
                .listEventTypes(new Api_ng_sample_code.TO.MarketFilter());
            this.historyWriter?.Write(eventTypes);
            var result = eventTypes
                .Select(c => new EventCategory(c.EventType.Id, c.EventType.Name))
                .ToList();
            return new ReadOnlyCollection<EventCategory>(result);
        }

        public ReadOnlyCollection<Domain.Event> GetEvents(IEnumerable<EventCategory> categories)
        {
            var events = new List<Domain.Event>();
            foreach (var category in categories)
            {
                foreach (var e in this.GetEvents(category))
                {
                    events.Add(e);
                }
            }

            return new ReadOnlyCollection<Domain.Event>(events);
        }

        public ReadOnlyCollection<Domain.Event> GetEvents(EventCategory category)
        {
            var events = new List<Domain.Event>();
            var timeNow = DateTime.UtcNow;
            var hourIncrement = 1;
            for(var i = -1; i < 24; i += hourIncrement)
            {
                var time = new TimeRange();
                time.From = timeNow + TimeSpan.FromHours(i);
                time.To = time.From + TimeSpan.FromHours(hourIncrement);

                var marketFilter = new Api_ng_sample_code.TO.MarketFilter();
                marketFilter.EventTypeIds = new HashSet<string>(new[] { category.Id });
                marketFilter.MarketStartTime = time;
                ISet<MarketProjection> marketProjections = new HashSet<MarketProjection>();
                marketProjections.Add(MarketProjection.EVENT);
                marketProjections.Add(MarketProjection.RUNNER_DESCRIPTION);
                marketProjections.Add(MarketProjection.MARKET_DESCRIPTION);

                var marketSort = MarketSort.FIRST_TO_START;
                var maxResults = "200";
                var eventsList = client.listEvents(marketFilter);
                foreach(var item in eventsList)
                {
                    marketFilter = new Api_ng_sample_code.TO.MarketFilter();
                    marketFilter.EventIds = new HashSet<string>(new[] { item.Event.Id });
                    var marketCatalogues = client.listMarketCatalogue(marketFilter, marketProjections, marketSort, maxResults);
                    this.historyWriter?.Write(category.Id, marketCatalogues);
                    var timestamp = DateTime.UtcNow;
                    var marketBooks = new Dictionary<string, MarketBook>();
                    var ev = new Domain.Event(item.Event.Id, item.Event.Name, category, item.Event.OpenDate.Value, marketCatalogues.ToMarketObservation(marketBooks, timestamp));
                    events.Add(ev);
                }
            }

            return new ReadOnlyCollection<Domain.Event>(events.OrderBy(e=>e.StartDateTime).ToList());
        }

        public ReadOnlyDictionary<string, ReadOnlyDictionary<string, OrderBookObservation>> GetObservations(IEnumerable<string> marketIds)
        {
            var timestamp = DateTime.UtcNow;
            var marketBook = this.GetBestPrices(marketIds);

            var result = new ReadOnlyDictionary<string, ReadOnlyDictionary<string, OrderBookObservation>>(marketBook.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToOrderBookObservation()));
            return result;
        }

        public Dictionary<string, MarketBook> GetBestPrices(IEnumerable<string> marketIds)
        {
            ISet<PriceData> priceData = new HashSet<PriceData>();
            //get all prices from the exchange
            priceData.Add(PriceData.EX_BEST_OFFERS);
            priceData.Add(PriceData.EX_TRADED);

            var priceProjection = new PriceProjection();
            priceProjection.PriceData = priceData;
            priceProjection.Virtualise = true;

            var result = new List<MarketBook>();
            var batched = this.Batched(marketIds, 10);
            foreach(var batch in batched)
            {
                var marketBooks = client.listMarketBook(batch.ToList(), priceProjection);
                result.AddRange(marketBooks);
            }
            
            return result.ToDictionary(i => i.MarketId, i => i);
        }

        private IEnumerable<T[]> Batched<T>(IEnumerable<T> source, int batchSize)
        {
            var result = new List<T>();
            foreach(T item in source)
            {
                if(result.Count == batchSize)
                {
                    yield return result.ToArray();
                    result = new List<T>();
                }

                result.Add(item);
            }

            yield return result.ToArray();
        }


        public ReadOnlyCollection<Market> GetMarkets(IEnumerable<Domain.Event> events)
        {
            throw new NotImplementedException();
        }

        private void ProcessMarketChange(BFSwagger.Model.MarketChangeMessage marketChangeMessage)
        {
            if (marketChangeMessage.Mc == null) return;
            var timeStamp = new DateTime(1970, 1, 1).AddMilliseconds(marketChangeMessage.Pt.Value);
            
            foreach (var marketChange in marketChangeMessage.Mc)
            {
                this.marketChanges.OnNext(marketChange.ToMarketObservation(timeStamp));
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
                var changes = Observable
                    .Merge(this.marketChanges
                               .Where(m => m.Id == marketId),
                           this.orderChanges
                               .Where(o => o.MarketId == marketId)
                               .Select(o => new Domain.Order[] { o }.ToMarketObservation(marketId, DateTime.UtcNow, false)));
                var subscription = changes.Subscribe(observer);
                this.streamSession.MarketSubscription(new BFSwagger.Model.MarketSubscriptionMessage { MarketFilter = new BFSwagger.Model.MarketFilter { MarketIds = marketSubscriptions.Keys.ToList() } });

                return Disposable.Create(() =>
                {
                    IObservable<MarketObservation> existing = null;
                    if (this.marketSubscriptions.TryRemove(marketId, out existing))
                    {
                        subscription.Dispose();
                    }
                });
            }).Publish().RefCount();
        }

        public IObservable<Domain.Order> GetOrderChangeStream(string marketId)
        {
            return this.orderChanges.Where(o => o.MarketId == marketId);
        }

        public ReadOnlyCollection<Domain.Order> GetOrders(string marketId)
        {
            var ordersList = this.client.listCurrentOrders(new HashSet<string>(), new HashSet<string>(new[] { marketId }));
            if (ordersList.MoreAvailable) throw new NotImplementedException("handling of paged orders not implemented");
            var result = new List<Domain.Order>();
            foreach(var order in ordersList.CurrentOrders)
            {
                result.Add(order.ToOrder());
            }
            return new ReadOnlyCollection<Domain.Order>(result);
        }

        public void PlaceOrders(IEnumerable<OrderRequest> orderRequests)
        {
            IList<PlaceInstruction> placeInstructions = new List<PlaceInstruction>();

            foreach (var g in orderRequests.GroupBy(o => o.MarketId))
            {
                foreach (var request in g)
                {
                    var placeInstruction = new PlaceInstruction();

                    placeInstruction.Handicap = 0;
                    placeInstruction.Side = request.Side == OrderSide.Back ? Side.BACK : Side.LAY;
                    placeInstruction.OrderType = OrderType.LIMIT;

                    var limitOrder = new LimitOrder();
                    limitOrder.PersistenceType = PersistenceType.LAPSE;
                    limitOrder.Price = (double)request.Price;
                    limitOrder.Size = (double)request.Size;

                    placeInstruction.LimitOrder = limitOrder;
                    placeInstruction.SelectionId = long.Parse(request.OutcomeId);
                    placeInstructions.Add(placeInstruction);
                }

                string customerRef = null;
                var placeExecutionReport = client.placeOrders(g.Key, customerRef, placeInstructions);
                if (placeExecutionReport.Status == ExecutionReportStatus.SUCCESS)
                {
                    var orders = placeExecutionReport.InstructionReports.ToOrders(g.Key);
                    this.marketChanges.OnNext(orders.ToMarketObservation(g.Key, DateTime.UtcNow, false));
                    continue;
                }

                ExecutionReportErrorCode executionErrorcode = placeExecutionReport.ErrorCode;
                InstructionReportErrorCode instructionErroCode = placeExecutionReport.InstructionReports[0].ErrorCode;
                Debug.WriteLine($"Error placing orders {executionErrorcode} {instructionErroCode} - recovering full market orders");
                var fullSet = this.GetOrders(g.Key);
                this.marketChanges.OnNext(fullSet.ToMarketObservation(g.Key, DateTime.UtcNow, true));

            }
        }

        public void CancelOrders(IEnumerable<Domain.Order> orders)
        {
            foreach(var g in orders.GroupBy(o => o.MarketId))
            {
                var cancelInstructions = g.Select(o => new CancelInstruction { BetId = o.Id }).ToList();
                var cancellationReport = this.client.cancelOrders(g.Key, cancelInstructions, null);

                if (cancellationReport.Status == ExecutionReportStatus.SUCCESS)
                {
                    
                    //var fullSet = this.GetOrders(g.Key);
                    //this.marketChanges.OnNext(fullSet.ToMarketObservation(g.Key, DateTime.UtcNow));
                    continue;
                }

                ExecutionReportErrorCode executionErrorcode = cancellationReport.ErrorCode;
                InstructionReportErrorCode instructionErroCode = cancellationReport.InstructionReports[0].ErrorCode;
                Debug.WriteLine($"Error placing orders {executionErrorcode} {instructionErroCode} - recovering full market orders");
                var fullSet = this.GetOrders(g.Key);
                this.marketChanges.OnNext(fullSet.ToMarketObservation(g.Key, DateTime.UtcNow, true));
                //throw new ApplicationException($"Failed to cancel order {executionErrorcode} {instructionErroCode}");
            }
        }
    }
}
