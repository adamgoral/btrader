using Api_ng_sample_code.TO;
using System;
using System.Collections.Generic;
using Betfair.ESASwagger.Model;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.IO;
using System.Collections.Concurrent;
using System.Threading;
using System.Reactive.Linq;
using System.Reactive.Disposables;
using BTrader;

namespace BTrader.Betfair
{
    public class BetfairHistoryReaderWriter : Domain.IMarketHistoryWriter<EventTypeResult, MarketCatalogue, MarketChangeMessage>, Domain.IMarketHistoryReader<EventTypeResult, MarketCatalogue, MarketChangeMessage>
    {
        public const string RESPONSE_MARKET_CHANGE_MESSAGE = "mcm";
        public const string OPERATION = "op";

        private readonly string basePath;
        private readonly BlockingCollection<MarketChangeMessage> writeQueue = new BlockingCollection<MarketChangeMessage>();
        private readonly Thread messageWriterThread;
        private readonly Dictionary<string, StreamWriter> writers = new Dictionary<string, StreamWriter>();
        private readonly bool useTimer = false;

        public BetfairHistoryReaderWriter(string basePath, bool useTimer)
        {
            this.basePath = basePath;
            this.useTimer = useTimer;
            this.messageWriterThread = new Thread(this.ProcessMessages);
            this.messageWriterThread.Start();
        }

        private void ProcessMessages(object args)
        {
            foreach(var change in this.writeQueue.GetConsumingEnumerable())
            {
                if (change.Mc != null)
                {
                    foreach (var mc in change.Mc)
                    {
                        var copy = new MarketChangeMessage(change.Op, change.Id, change.Ct, change.Clk, change.HeartbeatMs, change.Pt, change.InitialClk, null, change.ConflateMs, change.SegmentType);
                        copy.ReceiveTime = change.ReceiveTime;
                        copy.Mc = new List<MarketChange>(new[] { mc });
                        var serialized = JsonConvert.SerializeObject(copy);
                        StreamWriter writer;
                        if (!writers.TryGetValue(mc.Id, out writer))
                        {
                            var formattedDate = DateTime.Today.ToString("yyyyMMdd");
                            var path = this.GetOrCreatePath($"{formattedDate}\\marketstreams");
                            writer = new StreamWriter($"{path}\\{mc.Id}.json", false);
                            this.writers[mc.Id] = writer;
                        }

                        writer.WriteLine(serialized);
                        writer.Flush();
                    }
                }
            }
        }

        public void OnCompleted()
        {
            //throw new NotImplementedException();
        }

        public void OnError(System.Exception error)
        {
            //throw new NotImplementedException();
        }

        public void OnNext(MarketChangeMessage value)
        {
            this.writeQueue.Add(value);
        }

        private string GetOrCreatePath(string relativePath)
        {
            var path = $"{this.basePath}\\{relativePath}";
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return path;
        }

        public void Write(IEnumerable<EventTypeResult> eventTypes)
        {
            var toWrite = eventTypes.ToArray();
            Task.Factory.StartNew(() =>
            {
                var formattedDate = DateTime.Today.ToString("yyyyMMdd");
                var path = this.GetOrCreatePath(formattedDate);
                var serializer = JsonSerializer.Create();
                using(var writer = new StreamWriter($"{path}\\eventtypes.json", false))
                {
                    serializer.Serialize(writer, toWrite);
                }
            });
        }

        public void Write(string eventId, IEnumerable<MarketCatalogue> marketCatalogues)
        {
            var toWrite = marketCatalogues.ToArray();
            Task.Factory.StartNew(() =>
            {
                var serializer = JsonSerializer.Create();

                foreach (var market in toWrite)
                {
                    var formattedDate = market.Description.SuspendTime.Value.ToString("yyyyMMdd");
                    var path = this.GetOrCreatePath($"{formattedDate}\\markets\\{eventId}");
                    using (var writer = new StreamWriter($"{path}\\{market.MarketId}.json", false))
                    {
                        serializer.Serialize(writer, market);
                    }
                }
            });
        }

        public IList<MarketCatalogue> GetEvents(DateTime date, string eventId)
        {
            var formattedDate = date.ToString("yyyyMMdd");
            var path = $"{this.basePath}\\{formattedDate}\\markets\\{eventId}";
            if (!Directory.Exists(path)) return new List<MarketCatalogue>();
            var files = Directory.GetFiles(path);
            var result = new List<MarketCatalogue>();
            var serializer = JsonSerializer.Create();
            foreach (var file in files)
            {
                using (var reader = new JsonTextReader(new StreamReader(file)))
                {
                    var market = serializer.Deserialize<MarketCatalogue>(reader);
                    result.Add(market);
                }
            }

            return result;
        }

        public IList<EventTypeResult> GetEventTypes(DateTime date)
        {
            var formattedDate = date.ToString("yyyyMMdd");
            var path = $"{this.basePath}\\eventtypes.json";
            var serializer = JsonSerializer.Create();
            using(var reader = new JsonTextReader(new StreamReader(path)))
            {
                var categories = serializer.Deserialize<EventTypeResult[]>(reader).ToList();
                return categories;
            }
        }

        private string GetOperation(JsonReader jreader)
        {
            string operation = null;
            if (jreader.Read())
            {
                //rip off start
                while (jreader.Read())
                {
                    if (jreader.TokenType == JsonToken.PropertyName && OPERATION.Equals(jreader.Value))
                    {
                        if (jreader.Read()) //rip out op's value
                        {
                            operation = (string)jreader.Value;
                        }
                        break;
                    }
                    else
                    {
                        jreader.Skip();
                    }
                }
            }
            return operation;
        }

        private T ReadResponseMessage<T>(string line) where T : ResponseMessage
        {
            T response = JsonConvert.DeserializeObject<T>(line);
            return response;
        }

        public IEnumerable<MarketChangeMessage> ReadMessages(string marketId)
        {
            var fileNames = Directory.GetFiles(this.basePath, $"{marketId}.json", SearchOption.AllDirectories).ToArray();
            var fileName = fileNames.Single(s => s.Contains("marketstreams"));
            using (var reader = new StreamReader(fileName))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    MarketChangeMessage marketMessage = null;
                    var operation = GetOperation(new JsonTextReader(new StringReader(line)));
                    switch (operation)
                    {
                        case RESPONSE_MARKET_CHANGE_MESSAGE:
                            marketMessage = ReadResponseMessage<MarketChangeMessage>(line);
                            break;
                        default:
                            marketMessage = null;
                            break;
                    }

                    if (marketMessage != null)
                    {
                        yield return marketMessage;
                    }
                }
            }
        }

        public IObservable<MarketChangeMessage> GetMarketChangeStream(string marketId)
        {
            var result = Observable.Create<MarketChangeMessage>(observer =>
            {
                var cancellationSource = new CancellationTokenSource();
                Task.Factory.StartNew(() =>
                {
                    long? t = null;
                    foreach (var marketMessage in this.ReadMessages(marketId))
                    {
                        if (cancellationSource.IsCancellationRequested) break;
                        long waitTime = 0;
                        if (marketMessage.Pt != null)
                        {
                            if (t != null)
                            {
                                waitTime = Math.Max(0, marketMessage.ReceiveTime.Value - t.Value);
                            }

                            t = marketMessage.Pt;
                        }

                        if (this.useTimer)
                            Thread.Sleep((int)waitTime);
                        observer.OnNext(marketMessage);
                    }

                    observer.OnCompleted();
                }, cancellationSource.Token);

                return Disposable.Create(cancellationSource.Cancel);
            });

            return result;
        }

        public bool HasStream(string marketId)
        {
            var existingStreams = Directory.GetFiles(basePath, "*.json", SearchOption.AllDirectories).Where(f => f.Contains("marketstreams"));
            return existingStreams.Any(path => path.Contains(marketId));
        }
    }
}
