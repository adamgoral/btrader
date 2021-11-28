using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace BTrader.UI.ViewModels
{

    public class MarketSummaryViewModel : BaseViewModel, IDisposable
    {
        private IObservable<MarketNavItem> marketSelection;
        private readonly Dictionary<string, ISession> sessions;
        private readonly IObserver<MarketAndOutcomeSelectionArgs> marketAndOutcomeSelection;
        private readonly Dispatcher dispatcher;
        private CompositeDisposable disposables = new CompositeDisposable();
        private Dictionary<string, CompositeDisposable> activeOutcomes = new Dictionary<string, CompositeDisposable>();

        public MarketSummaryViewModel(IObservable<MarketNavItem> marketSelection, Dictionary<string, ISession> sessions, IObserver<MarketAndOutcomeSelectionArgs> marketAndOutcomeSelection, Dispatcher dispatcher)
        {
            this.marketSelection = marketSelection;
            this.sessions = sessions;
            this.marketAndOutcomeSelection = marketAndOutcomeSelection;
            this.dispatcher = dispatcher;
            this.disposables.Add(this.marketSelection.Subscribe(this.OnMarketSelected));
        }

        public ObservableCollection<OutcomeSummaryViewModel> Outcomes { get; } = new ObservableCollection<OutcomeSummaryViewModel>();

        private void OnMarketSelected(MarketNavItem marketItem)
        {
            if (marketItem == null) return;

            var exchange = marketItem.Exchange;
            CompositeDisposable activeOutcomes = null;
            if(this.activeOutcomes.TryGetValue(exchange, out activeOutcomes))
            {
                activeOutcomes.Dispose();
                this.activeOutcomes.Remove(exchange);
            }

            var toClear = this.Outcomes.Where(o => o.Exchange == exchange).ToArray();
            foreach(var item in toClear)
            {
                this.Outcomes.Remove(item);
            }
            
            var market = Market.FromObservation(marketItem.Market, this.sessions[marketItem.Exchange].GetMarketChangeStream(marketItem.Market.Id));
            this.activeOutcomes[exchange] = new CompositeDisposable();
            foreach (var outcome in market.Outcomes.Values)
            {
                var o = new OutcomeSummaryViewModel(market, outcome, exchange, this.dispatcher, this.marketAndOutcomeSelection);
                this.activeOutcomes[exchange].Add(o);
                this.Outcomes.Add(o);
            }
        }

        public void Dispose()
        {
            this.disposables.Dispose();
        }
    }
}
