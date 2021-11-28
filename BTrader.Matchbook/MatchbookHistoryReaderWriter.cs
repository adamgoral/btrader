using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System.IO;
using Newtonsoft.Json;
using System.Collections.Concurrent;
using System.Threading;
using System.Reactive.Disposables;
using BTrader.Domain;

namespace BTrader.Matchbook
{
    public class MatchbookHistoryReaderWriter : IMarketHistoryWriter<Sport, MatchbookEvent, MatchbookOrderBook>, IMarketHistoryReader<Sport, MatchbookEvent, MatchbookOrderBook>
    {
        private readonly BlockingCollection<MatchbookOrderBook> writeQueue = new BlockingCollection<MatchbookOrderBook>();
        private readonly Thread messageWriterThread;
        private readonly Dictionary<string, StreamWriter> writers = new Dictionary<string, StreamWriter>();

        private readonly string basePath;
        private readonly bool useTimer;

        public MatchbookHistoryReaderWriter(string basePath, bool useTimer)
        {
            this.basePath = basePath;
            this.useTimer = useTimer;
            this.messageWriterThread = new Thread(this.ProcessMessages);
            this.messageWriterThread.Start();
        }

        private void ProcessMessages(object args)
        {
            foreach (var change in this.writeQueue.GetConsumingEnumerable())
            {
                var serialized = JsonConvert.SerializeObject(change);
                StreamWriter writer;
                var id = change.GetMatchbookId().ToString();
                if (!writers.TryGetValue(id, out writer))
                {
                    var formattedDate = DateTime.Today.ToString("yyyyMMdd");
                    var path = this.GetOrCreatePath($"{formattedDate}\\marketstreams");
                    writer = new StreamWriter($"{path}\\{id}.json", false);
                    this.writers[id] = writer;
                }

                writer.WriteLine(serialized);
                writer.Flush();
            }
        }

        public void OnCompleted()
        {
            //throw new NotImplementedException();
        }

        public void OnError(Exception error)
        {
            //throw new NotImplementedException();
        }

        public void OnNext(MatchbookOrderBook value)
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

        public void Write(IEnumerable<Sport> eventTypes)
        {
            var toWrite = eventTypes.ToArray();
            Task.Factory.StartNew(() =>
            {
                var formattedDate = DateTime.Today.ToString("yyyyMMdd");
                var path = this.GetOrCreatePath(formattedDate);
                var serializer = JsonSerializer.Create();
                using (var writer = new StreamWriter($"{path}\\eventtypes.json", false))
                {
                    serializer.Serialize(writer, toWrite);
                }
            });
        }

        public void Write(string eventId, IEnumerable<MatchbookEvent> marketCatalogues)
        {
            var toWrite = marketCatalogues.ToArray();
            Task.Factory.StartNew(() =>
            {
                var serializer = JsonSerializer.Create();
                foreach (var market in toWrite)
                {
                    var formattedDate = market.Start.ToString("yyyyMMdd");
                    var path = this.GetOrCreatePath($"{formattedDate}\\markets\\{eventId}");
                    using (var writer = new StreamWriter($"{path}\\{market.Id}.json", false))
                    {
                        serializer.Serialize(writer, market);
                    }
                }
            });
        }

        public IList<MatchbookEvent> GetEvents(DateTime date, string eventId)
        {
            var formattedDate = date.ToString("yyyyMMdd");
            var path = $"{this.basePath}\\{formattedDate}\\markets\\{eventId}";
            if (!Directory.Exists(path)) return new List<MatchbookEvent>();
            var files = Directory.GetFiles(path);
            var result = new List<MatchbookEvent>();
            var serializer = JsonSerializer.Create();
            foreach (var file in files)
            {
                using (var reader = new JsonTextReader(new StreamReader(file)))
                {
                    try
                    {
                        var market = serializer.Deserialize<MatchbookEvent>(reader);
                        result.Add(market);
                    }
                    catch
                    {
                        var market = serializer.Deserialize<MatchbookEvent[]>(reader);
                        result.AddRange(market);
                    }
                }
            }

            return result.Where(m => m != null).GroupBy(m => m.Id).Select(m => m.First()).ToList();
        }

        public IList<Sport> GetEventTypes(DateTime date)
        {
            var formattedDate = date.ToString("yyyyMMdd");
            var path = $"{this.basePath}\\eventtypes.json";
            var serializer = JsonSerializer.Create();
            using (var reader = new JsonTextReader(new StreamReader(path)))
            {
                var categories = serializer.Deserialize<Sport[]>(reader).ToList();
                return categories;
            }
        }

        private T ReadResponseMessage<T>(string line)
        {
            T response = JsonConvert.DeserializeObject<T>(line);
            return response;
        }

        public IEnumerable<MatchbookOrderBook> ReadMessages(string marketId)
        {
            var fileNames = Directory.GetFiles(this.basePath, $"{marketId}.json", SearchOption.AllDirectories).ToArray();
            var fileName = fileNames.Single(s => s.Contains("marketstreams"));
            using (var reader = new StreamReader(fileName))
            {
                while (!reader.EndOfStream)
                {
                    var line = reader.ReadLine();
                    var marketMessage = ReadResponseMessage<MatchbookOrderBook>(line);
                    if (marketMessage != null)
                    {
                        yield return marketMessage;
                    }
                }
            }
        }

        public IObservable<MatchbookOrderBook> GetMarketChangeStream(string marketId)
        {
            var result = Observable.Create<MatchbookOrderBook>(observer =>
            {

                var cancellationSource = new CancellationTokenSource();
                Task.Factory.StartNew(() =>
                {
                    DateTime? t = null;
                    foreach (var marketMessage in this.ReadMessages(marketId))
                    {
                        if (cancellationSource.IsCancellationRequested) return;
                        double waitTime = 0;
                        if (marketMessage.Timestamp != null)
                        {
                            if (t != null)
                            {
                                waitTime = Math.Max(0, (marketMessage.Timestamp - t.Value).TotalMilliseconds);
                            }

                            t = marketMessage.Timestamp;
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
