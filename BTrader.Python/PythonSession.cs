using Python.Runtime;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BTrader.Python
{
    public interface ILog
    {
        void Info(string message);
        void Warn(string message);
        void Error(string message);
    }

    public class Log : ILog
    {
        public void Error(string message)
        {
            Console.WriteLine(message);
        }

        public void Info(string message)
        {
            Console.WriteLine(message);
        }

        public void Warn(string message)
        {
            Console.WriteLine(message);
        }
    }

    public class PythonSession : IDisposable
    {
        private readonly string pythonHome;
        private readonly string pythonPath;
        private readonly ILog log;
        private BlockingCollection<Action> queue = new BlockingCollection<Action>();
        private Thread worker;
        private bool disposedValue;
        private CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

        public PythonSession(string pythonHome, string pythonPath, ILog log)
        {
            this.pythonHome = pythonHome;
            this.pythonPath = pythonPath;
            this.log = log;
            this.worker = new Thread(this.RunWorkerLoop);
            this.worker.Start();
        }

        void RunWorkerLoop()
        {
            var exeDir = AppDomain.CurrentDomain.BaseDirectory;
            Directory.SetCurrentDirectory(pythonHome);
            PythonEngine.PythonHome = this.pythonHome;
            PythonEngine.PythonPath = this.pythonPath;
            Directory.SetCurrentDirectory(exeDir);
            using (Py.GIL())
            {
                using (var scope = Py.CreateScope())
                {
                    dynamic sys = scope.Import("sys");
                    var version = scope.Eval<string>("sys.version[:5]");
                    log.Info($"Python session {version} configured");
                }
            }

            try
            {
                foreach (var action in this.queue.GetConsumingEnumerable(this.cancellationTokenSource.Token))
                {
                    action();
                }
            }
            catch(OperationCanceledException ex)
            {

            }
            catch(Exception ex)
            {
                log.Error($"Unexpected error {ex}");
            }
        }

        public Task<T> CallModuleFunction<T>(string moduleFileName, string functionName, params object[] args)
        {
            var result = new TaskCompletionSource<T>();

            Action action = () =>
            {
                try
                {
                    T resultValue;
                    using (Py.GIL())
                    {
                        var module = Py.Import(moduleFileName);
                        resultValue = module.InvokeMethod(functionName, args.Select(x => x.ToPython()).ToArray()).As<T>();
                    }

                    result.SetResult(resultValue);
                }
                catch (Exception ex)
                {
                    result.SetException(ex);
                }
            };

            this.queue.Add(action);
            return result.Task;
        }

        public Task LoadModule(string moduleFileName)
        {
            var result = new TaskCompletionSource<object>();
            Action action = () =>
            {
                try
                {
                    using (Py.GIL())
                    {
                        var module = Py.Import(moduleFileName);
                    }
                    result.SetResult(null);
                }
                catch(Exception ex)
                {
                    result.SetException(ex);
                }
            };

            this.queue.Add(action);

            return result.Task;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                    this.cancellationTokenSource.Cancel();
                    this.worker.Join();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~PythonSession()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
