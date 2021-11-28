using Betfair.ESASwagger.Model;
using BTrader.Domain;
using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;

namespace BTrader.Betfair
{

    public class BetfairStreamSession : IStreamSession
    {
        public event EventHandler<BetfarStreamSessionStatusEventArgs> StatusChanged;

        public const string OPERATION = "op";

        public const string REQUEST_AUTHENTICATION = "authentication";
        public const string REQUEST_MARKET_SUBSCRIPTION = "marketSubscription";
        public const string REQUEST_ORDER_SUBSCRIPTION = "orderSubscription";
        public const string REQUEST_HEARTBEAT = "heartbeat";

        public const string RESPONSE_CONNECTION = "connection";
        public const string RESPONSE_STATUS = "status";
        public const string RESPONSE_MARKET_CHANGE_MESSAGE = "mcm";
        public const string RESPONSE_ORDER_CHANGE_MESSAGE = "ocm";

        private ConcurrentDictionary<int, Request> pendingRequests = new ConcurrentDictionary<int, Request>();
        private readonly Subject<MarketChangeMessage> marketChangesSubject = new Subject<MarketChangeMessage>();
        private readonly Subject<OrderChangeMessage> orderChangeSubject = new Subject<OrderChangeMessage>();
        private readonly Func<IStreamConnection> connectionFactory;
        private readonly IAppKeySessionProvider appKeySessionProvider;
        private readonly ILog log;
        private IStreamConnection currentConnection;
        private IObservable<string> stream;
        private readonly TimeSpan waitTimeBeforeReconnecting;
        private int id;
        private TaskCompletionSource<bool> initialOpen = new TaskCompletionSource<bool>();
        private DateTime lastRequestTime;
        private readonly System.Timers.Timer heartbeatTimer = new System.Timers.Timer();

        public BetfairStreamSession(Func<IStreamConnection> connectionFactory, IAppKeySessionProvider appKeySessionProvider, ILog log) 
            : this(connectionFactory, appKeySessionProvider, log, TimeSpan.FromSeconds(15))
        {
        }

        public BetfairStreamSession(Func<IStreamConnection> connectionFactory, IAppKeySessionProvider appKeySessionProvider, ILog log, TimeSpan waitTimeBeforeReconnecting)
        {
            this.connectionFactory = connectionFactory;
            this.appKeySessionProvider = appKeySessionProvider;
            this.log = log;
            this.waitTimeBeforeReconnecting = waitTimeBeforeReconnecting;
            this.heartbeatTimer.Elapsed += OnHeartbeatTimer;
            this.heartbeatTimer.Interval = TimeSpan.FromSeconds(30).TotalMilliseconds;
        }

        private void OnHeartbeatTimer(object sender, System.Timers.ElapsedEventArgs e)
        {
            try
            {
                this.Heartbeat(new HeartbeatMessage());
            }
            catch(Exception ex)
            {
                this.log.Error($"Heartbeat timer exception {ex}");
            }
        }

        private int NextID()
        {
            return Interlocked.Increment(ref this.id);
        }

        private IObservable<string> CreateReconnectingStream()
        {
            return Observable.Using(this.connectionFactory, connection =>
            {
                this.currentConnection = connection;
                connection.Connect();
                return connection;
            }).Catch<string, Exception>(ex =>
            {
                this.OnStatusChanged(BetfairStreamSessionStatus.Reconnecting);
                this.log.Error($"Error processing stream {ex}. Attempting to reconnect in {this.waitTimeBeforeReconnecting}");
                Thread.Sleep(this.waitTimeBeforeReconnecting);
                return this.CreateReconnectingStream();
            });
        }

        private void SendRequest(Request request)
        {
            if (!this.pendingRequests.TryAdd(request.Id, request))
            {
                throw new ApplicationException($"Could not store pending request id {request.Id}");
            }

            this.currentConnection.OnNext(request.RequestMessage.ToJson());
        }

        private Task SendRequest(string op, RequestMessage requestMessage)
        {
            this.lastRequestTime = DateTime.UtcNow;
            var id = this.NextID();
            requestMessage.Id = id;
            requestMessage.Op = op;
            var request = new Request(requestMessage.Id.Value, requestMessage);
            this.SendRequest(request);
            return request.Task;
        }

        private Task Authenticate()
        {
            var sessionId = this.appKeySessionProvider.GetOrCreateSession();
            var authenticationMessage = new AuthenticationMessage(REQUEST_AUTHENTICATION, null, sessionId, this.appKeySessionProvider.AppKey);
            this.heartbeatTimer.Enabled = true;
            return this.SendRequest(REQUEST_AUTHENTICATION, authenticationMessage);
        }

        public Task Heartbeat(HeartbeatMessage message)
        {
            return this.SendRequest(REQUEST_HEARTBEAT, message);
        }

        public Task MarketSubscription(MarketSubscriptionMessage message)
        {
            return this.SendRequest(REQUEST_MARKET_SUBSCRIPTION, message);
        }

        public Task OrderSubscription(OrderSubscriptionMessage message)
        {
            return this.SendRequest(REQUEST_ORDER_SUBSCRIPTION, message);
        }

        private void OnStatusChanged(BetfairStreamSessionStatus status)
        {
            this.StatusChanged?.Invoke(this, new BetfarStreamSessionStatusEventArgs(status));
        }

        private string GetOperation(JsonReader jreader)
        {
            string operation = null;
            if (jreader.Read())
            {
                //rip off start
                while (jreader.Read())
                {
                    if (jreader.TokenType == JsonToken.PropertyName && OPERATION.Equals(jreader.Value))
                    {
                        if (jreader.Read()) //rip out op's value
                        {
                            operation = (string)jreader.Value;
                        }
                        break;
                    }
                    else
                    {
                        jreader.Skip();
                    }
                }
            }
            return operation;
        }

        private T ReadResponseMessage<T>(string line) where T : ResponseMessage
        {
            T response = JsonConvert.DeserializeObject<T>(line);
            return response;
        }

        private void ProcessMessage(ConnectionMessage message)
        {
            this.Authenticate().ContinueWith(t => 
            {
                this.OnStatusChanged(BetfairStreamSessionStatus.Open);
                if(!initialOpen.Task.IsCompleted)
                    this.initialOpen.SetResult(true);
            });
        }

        private void ProcessMessage(StatusMessage message)
        {
            if (message.Id == null)
            {
                this.LogUnexpectedMessage(message);
                return;
            }

            Request pendingRequest;
            if(this.pendingRequests.TryRemove(message.Id.Value, out pendingRequest))
            {
                pendingRequest.ProcessResponse(message);
            }
            else
            {
                this.LogUnexpectedMessage(message);
            }
        }

        private void LogUnexpectedMessage(StatusMessage message)
        {

        }

        private void ProcessMessage(MarketChangeMessage message)
        {
            message.ReceiveTime = (long)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
            this.marketChangesSubject.OnNext(message);
        }

        private void ProcessMessage(OrderChangeMessage message)
        {
            this.orderChangeSubject.OnNext(message);
        }

        public IObservable<OrderChangeMessage> OrderChanges => this.orderChangeSubject;
        public IObservable<MarketChangeMessage> MarketChanges => this.marketChangesSubject;

        private void ProcessMessage(string message)
        {
            this.log.Info(message);
            var operation = GetOperation(new JsonTextReader(new StringReader(message)));
            switch (operation)
            {
                case RESPONSE_CONNECTION:
                    this.ProcessMessage(ReadResponseMessage<ConnectionMessage>(message));
                    break;
                case RESPONSE_STATUS:
                    this.ProcessMessage(ReadResponseMessage<StatusMessage>(message));
                    break;
                case RESPONSE_MARKET_CHANGE_MESSAGE:
                    this.ProcessMessage(ReadResponseMessage<MarketChangeMessage>(message));
                    break;
                case RESPONSE_ORDER_CHANGE_MESSAGE:
                    this.ProcessMessage(ReadResponseMessage<OrderChangeMessage>(message));
                    break;
                default:
                    throw new NotSupportedException($"{operation} is not supported");
            }
        }

        public Task Open()
        {
            if (this.stream == null)
            {
                this.OnStatusChanged(BetfairStreamSessionStatus.Opening);
                this.stream = CreateReconnectingStream();
                this.stream.Subscribe(this.ProcessMessage, ex => this.log.Error($"Session stream error {ex}"), () => this.log.Info("Session stream completed"));
            }

            return this.initialOpen.Task;
        }

        public void Close()
        {
            this.initialOpen = new TaskCompletionSource<bool>();
            this.currentConnection?.Dispose();
            this.stream = null;
            this.OnStatusChanged(BetfairStreamSessionStatus.Closed);
        }
    }
}

