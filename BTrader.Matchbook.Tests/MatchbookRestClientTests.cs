using System;
using System.Linq;
using BTrader.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BTrader.Matchbook.Tests
{
    [TestClass]
    public class MatchbookRestClientTests
    {
        public MatchbookRestClient CreateRestClient()
        {
            return new MatchbookRestClient(new JsonRestClient("api-doc-test-client"), TimeSpan.FromMilliseconds(100), new DebugLogger());
        }

        [TestMethod]
        public void LoginTest()
        {
            var loginRequest = new LoginRequest
            {
                Username = "",
                Password = ""
            };

            var restClient = this.CreateRestClient();
            var loginResponse = restClient.Login(loginRequest);
            restClient.Logout();
        }

        [TestMethod]
        public void GetSportsTest()
        {
            var loginRequest = new LoginRequest
            {
                Username = "",
                Password = ""
            };

            var restClient = this.CreateRestClient();
            var loginResponse = restClient.Login(loginRequest);
            var sports = restClient.GetSports().ToArray();
            restClient.Logout();
        }

        [TestMethod]
        public void GetEventsTest()
        {
            var loginRequest = new LoginRequest
            {
                Username = "",
                Password = ""
            };

            var restClient = this.CreateRestClient();
            var loginResponse = restClient.Login(loginRequest);
            var events = restClient.GetEvents(new[] { 24735152712200 }).ToArray();
            restClient.Logout();
        }

        [TestMethod]
        public void GetMarkets()
        {
            var loginRequest = new LoginRequest
            {
                Username = "",
                Password = ""
            };

            var restClient = this.CreateRestClient();
            var loginResponse = restClient.Login(loginRequest);
            var events = restClient.GetEvents(new[] { 24735152712200 }).ToArray();
            var markets = restClient.GetMarkets(events.First().Id).ToArray();
            restClient.Logout();
        }

        [TestMethod]
        public void GetMarketOrderBook()
        {
            var loginRequest = new LoginRequest
            {
                Username = "",
                Password = ""
            };

            var restClient = this.CreateRestClient();
            var loginResponse = restClient.Login(loginRequest);
            var ev = restClient.GetEvents(new[] { 24735152712200 }).OrderBy(e => e.Start).First();
            var markets = restClient.GetMarkets(ev.Id).OrderBy(m => m.Start).Take(3);
            var orderBook = restClient.GetOrderBooks(markets.Select(m => m.GetMatchbookId())).ToArray();
            restClient.Logout();
        }
    }
}