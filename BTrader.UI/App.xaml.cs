using BTrader.Domain;
using BTrader.UI.ViewModels;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Markup;

namespace BTrader.UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public App()
        {
            // Set culture info to ensure local dateformat is applied in displays
            FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(
                XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.ToString(), "Unhandled error", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public static Lazy<Dictionary<string, ISession>> Sessions = new Lazy<Dictionary<string, ISession>>(CreateSessions);

        public static Dictionary<string, ISession> CreateSessions()
        {
            return new Dictionary<string, ISession>
            {
                //{"Matchbook", Matchbook.Session.Create() },
                {"Betfair", Betfair.Session.Create() }
            };
        }

        public static Subject<MarketAndOutcomeSelectionArgs> MarketAndOutcomeSelection = new Subject<MarketAndOutcomeSelectionArgs>();

        private static ConcurrentDictionary<Type, BaseViewModel> instances = new ConcurrentDictionary<Type, BaseViewModel>();

        public static T GetViewModel<T>() where T : BaseViewModel
        {
            var type = typeof(T);
            if (type == typeof(MarketNavigatorViewModel))
            {
                return (T)instances.GetOrAdd(type, t => new MarketNavigatorViewModel(Sessions.Value));
            }
            else if(type == typeof(MarketSummaryViewModel))
            {
                var navigator = GetViewModel<MarketNavigatorViewModel>();
                return (T)instances.GetOrAdd(type, t => new MarketSummaryViewModel(navigator.MarketSelection, Sessions.Value, MarketAndOutcomeSelection, App.Current.Dispatcher));
            }
            else if(type == typeof(AgentsViewModel))
            {
                return (T)instances.GetOrAdd(type, t => new AgentsViewModel(Sessions.Value, MarketAndOutcomeSelection));
            }
            else if(type == typeof(FootballMatchNavigatorViewModel))
            {
                return (T)instances.GetOrAdd(type, t => new FootballMatchNavigatorViewModel(Sessions.Value));
            }
            else if(type == typeof(FootballMatchOutcomesProbabilityViewModel))
            {
                var navigator = GetViewModel<FootballMatchNavigatorViewModel>();
                return (T)instances.GetOrAdd(type, t => new FootballMatchOutcomesProbabilityViewModel(navigator.EventSelection, Sessions.Value, App.Current.Dispatcher));
            }

            throw new NotSupportedException();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            foreach(var vm in instances.Values)
            {
                var disposable = vm as IDisposable;
                disposable?.Dispose();
            }

            foreach(var session in Sessions.Value.Values)
            {
                try
                {
                    session.Disconnect();
                }
                catch { }
            }

            base.OnExit(e);
            Environment.Exit(0);
        }
    }
}
