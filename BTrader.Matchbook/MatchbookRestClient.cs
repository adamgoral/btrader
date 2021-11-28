using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BTrader.Matchbook
{

    public class MatchbookRestClient
    {
        private readonly JsonRestClient jsonRestClient;
        private readonly TimeSpan minRequestInterval;
        private readonly ILog log;
        private DateTime lastRequest = DateTime.MinValue;
        private string sessionToken;

        public MatchbookRestClient(JsonRestClient jsonRestClient, TimeSpan minRequestInterval, ILog log)
        {
            this.jsonRestClient = jsonRestClient;
            this.minRequestInterval = minRequestInterval;
            this.log = log;
        }

        private Dictionary<string, string> GetHeaders()
        {
            var result = new Dictionary<string, string>
            {
            };

            if (!string.IsNullOrWhiteSpace(this.sessionToken))
            {
                result["session-token"] = this.sessionToken;
            }

            return result;
        }

        private void WaitInterval()
        {
            lock (this.jsonRestClient)
            {
                var timeSinceLastRequest = DateTime.Now - this.lastRequest;
                if(timeSinceLastRequest < this.minRequestInterval)
                {
                    var waitTime = this.minRequestInterval - timeSinceLastRequest;
                    this.log.Info($"Waiting {waitTime} between requests");
                    Thread.Sleep(waitTime);
                }

                this.lastRequest = DateTime.Now;
            }
        }

        public LoginResponse Login(LoginRequest loginRequest)
        {
            var url = new Uri("https://api.matchbook.com/bpapi/rest/security/session");
            var headers = this.GetHeaders();
            this.log.Info($"Sending login request to {url}");
            var result = this.jsonRestClient.Post<LoginRequest, LoginResponse>(url, headers, loginRequest);
            this.sessionToken = result.SessionToken;
            return result;
        }

        public LogoutResponse Logout()
        {
            var url = new Uri("https://api.matchbook.com/bpapi/rest/security/session");
            var headers = this.GetHeaders();
            this.log.Info($"Sending logout request to {url}");
            return this.jsonRestClient.Delete<LogoutResponse>(url, headers);
        }

        private IEnumerable<T> GetPagedResults<T, TResponse>(Uri url, int pageSize, Dictionary<string, string> headers, Func<TResponse, IEnumerable<T>> propertySelector)
            where TResponse : PagedResponse
        {
            var offset = 0;
            var pagedUrl = new Uri(url.ToString() + $"per-page={pageSize}&offset={offset}");
            this.WaitInterval();
            this.log.Info($"Getting {pagedUrl}");
            var response = this.jsonRestClient.Get<TResponse>(pagedUrl, headers);
            foreach(var item in propertySelector(response))
            {
                yield return item;
            }

            while (response.Total - response.Offset > response.PerPage)
            {
                offset = response.Offset + response.PerPage;
                pagedUrl = new Uri(url.ToString() + $"per-page={pageSize}&offset={offset}");
                this.WaitInterval();
                this.log.Info($"Getting {pagedUrl}");
                response = this.jsonRestClient.Get<TResponse>(pagedUrl, headers);
                foreach (var item in propertySelector(response))
                {
                    yield return item;
                }
            }
        }

        public IEnumerable<Sport> GetSports()
        {
            var url = new Uri($"https://api.matchbook.com/edge/rest/lookups/sports?status=active&");
            var headers = this.GetHeaders();
            return GetPagedResults<Sport, GetSportsResponse>(url, 20, headers, response => response.Sports);
        }

        public IEnumerable<MatchbookEvent> GetEvents(IEnumerable<long> sportIds)
        {
            var sports = string.Join(",", sportIds);
            var after = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var before = (DateTimeOffset.UtcNow + TimeSpan.FromDays(1)).ToUnixTimeSeconds();
            var url = new Uri($"https://api.matchbook.com/edge/rest/events?include-event-participants=true&include-prices=true&after={after}&before={before}&sport-ids={sports}&");
            var headers = this.GetHeaders();
            return GetPagedResults<MatchbookEvent, GetEventsResponse>(url, 20, headers, response => response.Events)
                .Select(e =>
                {
                    foreach (var m in e.Markets)
                    {
                        m.Timestamp = DateTime.UtcNow;
                    }
                    return e;
                });
        }

        public IEnumerable<MatchbookOrderBook> GetMarkets(long eventId)
        {
            var url = new Uri($"https://api.matchbook.com/edge/rest/events/{eventId}/markets?");
            var headers = this.GetHeaders();
            return GetPagedResults<MatchbookOrderBook, GetMarketsResponse>(url, 20, headers, response => response.Markets)
                .Select(m =>
                {
                    m.Timestamp = DateTime.UtcNow;
                    return m;
                });
        }

        public MatchbookOrderBook GetOrderBook(long eventId, long marketId)
        {
            var url = new Uri($"https://api.matchbook.com/edge/rest/events/{eventId}/markets/{marketId}?include-prices=true&price-mode=expanded&price-depth=500");
            var headers = this.GetHeaders();
            this.WaitInterval();
            this.log.Info($"Getting {url}");
            var result = this.jsonRestClient.Get<MatchbookOrderBook>(url, headers);
            result.Timestamp = DateTime.UtcNow;
            return result;
        }

        public IEnumerable<MatchbookOrderBook> GetOrderBooks(IEnumerable<MatchbookMarketId> marketIds)
        {
            var marketIdsSet = new HashSet<MatchbookMarketId>(marketIds);
            var eventIds = marketIdsSet.Select(id => id.EventId).Distinct().ToArray();
            var eventIdsList = string.Join(",", eventIds);
            var url = new Uri($"https://api.matchbook.com/edge/rest/events?ids={eventIdsList}&price-mode=expanded&price-depth=50&");
            var headers = this.GetHeaders();
            return GetPagedResults<MatchbookEvent, GetEventsResponse>(url, 20, headers, response => response.Events)
                .SelectMany(e => e.Markets)
                .Select(m =>
                {
                    m.Timestamp = DateTime.UtcNow;
                    return m;
                })
                .Where(m => marketIdsSet.Contains(m.GetMatchbookId()));
        }

        // GET prices for multiple events:
        // https://api.matchbook.com/edge/rest/events?ids=1237939299950018,1238799955170018&price-mode=expanded&price-depth=50
    }
}
