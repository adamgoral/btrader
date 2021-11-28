using BTrader.Algo;
using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.UI.ViewModels
{
    public class AgentViewModel : BaseViewModel, IDisposable
    {
        private readonly IDictionary<string, ISession> sessions;
        private readonly Market market;
        private readonly Outcome outcome;
        private readonly Action<AgentViewModel> unsassignCallback;
        private readonly CompositeDisposable disposables = new CompositeDisposable();

        private string GetBasePath()
        {
            var choices = new[]
            {
                @"c:\users\adam\data\btrader",
                @"D:\Data\btrader"
            };

            foreach (var choice in choices)
            {
                if (Directory.Exists(choice)) return choice;
            }

            throw new ApplicationException("Could not identify base path");
        }

        public AgentViewModel(IDictionary<string, ISession> sessions, Market market, Outcome outcome, Action<AgentViewModel> unsassignCallback)
        {
            this.sessions = sessions;
            this.market = market;
            this.outcome = outcome;
            this.unsassignCallback = unsassignCallback;
            var context = new AgentContext(sessions, Observable.Never<DateTime>(), $@"{GetBasePath()}\reports\trading\mmagent-{market.Id}-{outcome.Id}.csv");
            this.disposables.Add(context);
            this.Agent = new MMKalmanAgent(context, 2, 2, market, outcome);
            this.disposables.Add(this.Agent);
            this.disposables.Add(Observable.Timer(TimeSpan.Zero, TimeSpan.FromSeconds(1)).Subscribe(this.OnTimer));
            this.StartCommand = new DelegateCommand(o => true, o => this.Agent.State = AgentState.Running);
            this.StopCommand = new DelegateCommand(o => true, o => this.Agent.State = AgentState.Stopped);
            this.UnassignCommand = new DelegateCommand(o => true, o => this.unsassignCallback(this));
        }
        public string Name => $"{this.market.Name} : {this.market.Start.ToString("HH:mm")} : {this.outcome.Name}";
        public string MarketId => this.market.Id;
        public string OutcomeId => this.outcome.Id;
        public TimeSpan TimeLeft { get; private set; }
        public MMKalmanAgent Agent { get; }
        private void OnTimer(long value)
        {
            this.TimeLeft = this.market.Start - DateTime.UtcNow;
            this.OnPropertyChanged("TimeLeft");
        }

        public void Dispose()
        {
            this.disposables.Dispose();
        }

        public DelegateCommand StartCommand { get; }
        public DelegateCommand StopCommand { get; }
        public DelegateCommand UnassignCommand { get; }

    }
}
