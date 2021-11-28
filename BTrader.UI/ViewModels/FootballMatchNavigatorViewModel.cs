using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.UI.ViewModels
{
    public class FootballMatchNavigatorViewModel : BaseViewModel
    {
        private Dictionary<string, ISession> sessions;
        private EventNavItem selectedEvent;
        private Subject<EventNavItem> eventSelection = new Subject<EventNavItem>();

        public FootballMatchNavigatorViewModel(Dictionary<string, ISession> sessions)
        {
            this.sessions = sessions;
            this.RefreshCommand = new DelegateCommand(v => true, o => this.Refresh());
        }

        private void Refresh()
        {
            var sessionId = "Betfair";
            var betfairSession = this.sessions[sessionId];
            this.Events.Clear();
            var events = betfairSession.GetEvents(new[] { new EventCategory("1", "Soccer") });
            foreach(var e in events)
            {
                var navItem = new EventNavItem
                {
                    Start = e.StartDateTime,
                    Name = e.Name
                };

                navItem.Events[sessionId] = e;

                this.Events.Add(navItem);
            }
        }

        public ObservableCollection<EventNavItem> Events { get; } = new ObservableCollection<EventNavItem>();

        public DelegateCommand RefreshCommand { get; private set; }

        public EventNavItem SelectedEvent
        {
            get => this.selectedEvent;
            set
            {
                if(this.selectedEvent != value)
                {
                    this.selectedEvent = value;
                    this.OnPropertyChanged("SelectedEvent");
                    this.eventSelection.OnNext(value);
                }
            }
        }

        public IObservable<EventNavItem> EventSelection => this.eventSelection;
    }
}
