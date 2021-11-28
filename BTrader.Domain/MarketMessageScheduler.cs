using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BTrader.Domain
{

    public class MarketMessageScheduler
    {
        private List<Timestamped> messages = new List<Timestamped>();
        private Subject<Timestamped> subject = new Subject<Timestamped>();
        private bool running = false;

        public void Queue(IEnumerable<Timestamped> items)
        {
            if (this.running) throw new ApplicationException("Cannot queue messages to scheduler that is already running");
            messages.AddRange(items);
            messages = messages.OrderBy(m => m.Timestamp).ToList();
        }

        public IObservable<Timestamped> GetStream()
        {
            return this.subject;
        }

        public void Reset()
        {
            if (this.running) throw new ApplicationException("Only non running scheduler can be reset");
            this.subject = new Subject<Timestamped>();
            this.messages.Clear();
        }

        public Task Run(CancellationToken cancellationToken)
        {
            if (this.running) throw new ApplicationException("Scheduler is already running");
            this.running = true;
            return Task.Factory.StartNew(() =>
            {
                foreach (var message in this.messages)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    this.subject.OnNext(message);
                }

                this.subject.OnCompleted();
                this.running = false;
            });
        }
    }
}