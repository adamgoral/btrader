using System;
using System.Threading.Tasks;
using Betfair.ESASwagger.Model;

namespace BTrader.Betfair
{
    public class Request
    {
        private readonly TaskCompletionSource<StatusMessage> completionSource = new TaskCompletionSource<StatusMessage>();

        public Request(int id, RequestMessage requestMessage)
        {
            Id = id;
            RequestMessage = requestMessage;
        }

        public Task Task => this.completionSource.Task;

        public int Id { get; }
        public RequestMessage RequestMessage { get; }

        public  void ProcessResponse(StatusMessage message)
        {
            if(message.StatusCode == StatusMessage.StatusCodeEnum.Failure)
            {
                this.completionSource.SetException(new Exception(message.ErrorMessage));
            }
            else
            {
                this.completionSource.SetResult(message);
            }
        }
    }
}