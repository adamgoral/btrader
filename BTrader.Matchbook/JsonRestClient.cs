using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;

namespace BTrader.Matchbook
{
    public class JsonRestClient
    {
        private readonly string userAgent;

        public JsonRestClient(string userAgent)
        {
            this.userAgent = userAgent;
        }

        public TResponse Get<TResponse>(Uri url, Dictionary<string, string> headers)
        {
            var webRequest = this.CreteWebRequest(url, RestMethods.GET, headers);

            using(var response = webRequest.GetResponse())
            {
                return ReadResponse<TResponse>(response);
            }
        }

        public TResponse Delete<TResponse>(Uri url, Dictionary<string, string> headers)
        {
            var webRequest = this.CreteWebRequest(url, RestMethods.DELETE, headers);

            using (var response = webRequest.GetResponse())
            {
                return ReadResponse<TResponse>(response);
            }
        }

        public TResponse Post<TRequest, TResponse>(Uri url, Dictionary<string, string> headers, TRequest request)
        {
            var webRequest = this.CreteWebRequest(url, RestMethods.POST, headers);
            this.WriteRequest(webRequest, request);
            using (var response = webRequest.GetResponse())
            {
                return ReadResponse<TResponse>(response);
            }
        }

        public TResponse Put<TRequest, TResponse>(Uri url, Dictionary<string, string> headers, TRequest request)
        {
            var webRequest = this.CreteWebRequest(url, RestMethods.PUT, headers);
            this.WriteRequest(webRequest, request);
            using (var response = webRequest.GetResponse())
            {
                return ReadResponse<TResponse>(response);
            }
        }

        private void WriteRequest<TRequest>(WebRequest webRequest, TRequest request)
        {
            using(var writer = new StreamWriter(webRequest.GetRequestStream()))
            {
                var payload = JsonConvert.SerializeObject(request);
                writer.Write(payload);
                writer.Flush();
            }
        }

        private TResponse ReadResponse<TResponse>(WebResponse webResponse)
        {
            using (var reader = new StreamReader(webResponse.GetResponseStream()))
            {
                var payload = reader.ReadToEnd();
                return JsonConvert.DeserializeObject<TResponse>(payload);
            }
        }

        private WebRequest CreteWebRequest(Uri url, RestMethods method, Dictionary<string, string> headers)
        {
            var request = (HttpWebRequest)HttpWebRequest.Create(url);
            request.Method = method.ToString();
            request.ContentType = "application/json";
            request.UserAgent = this.userAgent;
            foreach (var kvp in headers)
            {
                request.Headers.Add(kvp.Key, kvp.Value);
            }

            return request;
        }
    }
}
