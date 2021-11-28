using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Disposables;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.UI.ViewModels
{

    public class AgentsViewModel : BaseViewModel, IDisposable
    {
        private readonly CompositeDisposable disposables = new CompositeDisposable();
        private readonly IDictionary<string, ISession> sessions;

        public AgentsViewModel(IDictionary<string, ISession> sessions, IObservable<MarketAndOutcomeSelectionArgs> marketAndOutcomeSelection)
        {
            this.disposables.Add(marketAndOutcomeSelection.Subscribe(this.OnMarketAndOutcomeSelected));
            this.sessions = sessions;
        }

        public ObservableCollection<AgentViewModel> Agents { get; } = new ObservableCollection<AgentViewModel>();

        private void OnMarketAndOutcomeSelected(MarketAndOutcomeSelectionArgs args)
        {
            if(!this.Agents.Any(a => a.MarketId == args.Market.Id && a.OutcomeId == args.Outcome.Id))
            {
                this.Agents.Add(new AgentViewModel(this.sessions, args.Market, args.Outcome, this.Unassign));
            }
        }

        private void Unassign(AgentViewModel agentViewModel)
        {
            this.Agents.Remove(agentViewModel);
            agentViewModel.Dispose();
        }

        public void Dispose()
        {
            this.disposables.Dispose();
        }
    }
}
