using System;

namespace BTrader.Domain
{
    public interface ISimulationSession: ISession
    {
        DateTime SimulationDate { get; set; }

        bool HasStream(string marketId);
    }
}