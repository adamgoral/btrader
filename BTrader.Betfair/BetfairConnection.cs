using BTrader.Domain;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.Betfair
{
    public class BetfairConnection : IStreamConnection
    {
        private bool disposedValue = false; // To detect redundant calls
        private IObservable<string> messages;
        private readonly ILog log = new DebugLogger();
        private readonly TimeSpan messagingTimeout = TimeSpan.FromSeconds(30);
        private StreamWriter writer;
        private StreamReader reader;
        private readonly string hostName;
        private readonly int port;
        private readonly int receiveBufferSize = 1024 * 1000 * 2;
        private Stream stream;
        private TcpClient tcpClient;
        private bool disposing;

        public BetfairConnection(string hostName, int port)
        {
            if (string.IsNullOrWhiteSpace(hostName))
            {
                throw new ArgumentException("message", nameof(hostName));
            }

            this.hostName = hostName;
            this.port = port;
        }

        public void Connect()
        {
            this.ConnectIfNeeded();
        }

        private void ConnectIfNeeded()
        {
            if (this.stream == null)
            {
                lock (this)
                {
                    if (this.stream == null)
                    {
                        this.stream = OpenStream();
                        this.writer = new StreamWriter(stream, Encoding.UTF8);
                        this.reader = new StreamReader(stream, Encoding.UTF8, false, receiveBufferSize);
                        this.messages = this.ObserveStream(reader);
                    }
                }
            }
        }

        private Stream OpenStream()
        {
            this.log.Info($"Creating message stream");
            tcpClient = new TcpClient(this.hostName, this.port);
            tcpClient.ReceiveBufferSize = receiveBufferSize; //shaves about 20s off firehose image.
            tcpClient.SendTimeout = (int)messagingTimeout.TotalMilliseconds;
            tcpClient.ReceiveTimeout = (int)messagingTimeout.TotalMilliseconds;
            Stream stream = tcpClient.GetStream();

            if (this.port == 443)
            {
                // Create an SSL stream that will close the client's stream.
                var sslStream = new SslStream(stream, false);

                //Setup ssl
                sslStream.AuthenticateAsClient(this.hostName);

                stream = sslStream;
            }

            return stream;
        }

        private IObservable<string> ObserveStream(StreamReader reader)
        {
            var result = Observable.Create<string>(o =>
            {
                Task.Factory.StartNew(() => 
                {
                    try
                    {
                        while (!reader.EndOfStream)
                        {
                            o.OnNext(reader.ReadLine());
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!this.disposing)
                        {
                            this.log.Error($"StreamReader read error {ex}");
                            o.OnError(ex);
                        }
                    }

                    this.log.Info("StreamReader end of stream");
                    if (this.disposing)
                    {
                        o.OnCompleted();
                    }
                    else
                    {
                        o.OnError(new ApplicationException("Unexpected end of stream"));
                    }
                });

                return Disposable.Empty;
            }).ObserveOn(ThreadPoolScheduler.Instance).Publish().RefCount();

            return result;
            //return new StreamReaderEnumberable(reader).ToObservable(ThreadPoolScheduler.Instance);
        }

        protected virtual void Dispose(bool disposing)
        {
            this.disposing = true;
            if (!disposedValue)
            {
                if (disposing)
                {
                    this.reader?.Close();
                    this.tcpClient?.Dispose();
                }

                disposedValue = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public void OnNext(string value)
        {
            this.ConnectIfNeeded();
            this.log.Info($"Sending {value}");
            this.writer.WriteLine(value);
            this.writer.Flush();
        }

        public void OnError(Exception error)
        {
            this.log.Error($"OnError {error}. Closing stream");
            this.Dispose();
        }

        public void OnCompleted()
        {
            this.log.Info($"OnCompleted. Closing stream");
            this.Dispose();
        }

        public IDisposable Subscribe(IObserver<string> observer)
        {
            this.ConnectIfNeeded();
            return this.messages.Subscribe(observer);
        }
    }
}

