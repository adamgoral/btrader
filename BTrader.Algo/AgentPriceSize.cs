using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.Algo
{
    public class AgentPriceSize
    {
        public AgentPriceSize(PriceSize market, decimal agentSize)
        {
            Market = market;
            AgentSize = agentSize;
        }

        public PriceSize Market { get; }
        public decimal AgentSize { get; }
    }
}
