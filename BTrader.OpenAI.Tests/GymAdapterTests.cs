using System;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace BTrader.OpenAI.Tests
{
    [TestClass]
    public class GymAdapterTests
    {
        [TestMethod]
        public void TestGetMarkets()
        {
            var markets = GymAdapter.GetMarkets(new DateTime(2019,10,17)).ToArray();
        }
    }
}
