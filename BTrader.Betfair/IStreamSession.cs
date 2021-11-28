using Betfair.ESASwagger.Model;
using System;
using System.Threading.Tasks;

namespace BTrader.Betfair
{
    public interface IStreamSession : IMarketStreamSession, IOrderStreamSession
    {
        event EventHandler<BetfarStreamSessionStatusEventArgs> StatusChanged;

        Task Open();
        void Close();

        Task Heartbeat(HeartbeatMessage message);
    }

    public interface IMarketStreamSession
    {
        Task MarketSubscription(MarketSubscriptionMessage message);
        IObservable<MarketChangeMessage> MarketChanges { get; }
    }

    public interface IOrderStreamSession
    {
        Task OrderSubscription(OrderSubscriptionMessage message);
        IObservable<OrderChangeMessage> OrderChanges { get; }
    }
}

