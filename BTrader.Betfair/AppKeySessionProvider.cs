using Newtonsoft.Json;
using System;
using System.IO;
using System.Net;

namespace BTrader.Betfair
{
    public class AppKeySessionProvider : IAppKeySessionProvider
    {
        public const string LIVEAPPKEY = "oeEBR9siPRhuCasu";
        public const string TESTAPPKEY = "QeMrQ3rPTtWSYCos";
        public const string SSO_HOST_COM = "identitysso.betfair.com";
        public const string SSO_HOST_IT = "identitysso.betfair.it";
        public const string SSO_HOST_ES = "identitysso.betfair.es";
        private readonly string ssoHost;
        private readonly string userName;
        private readonly string password;
        private string sessionId;
        private DateTime sessionCreateTime;
        private readonly TimeSpan sessionExpiry = TimeSpan.FromHours(3);
        private TimeSpan timeout = TimeSpan.FromSeconds(30);

        public AppKeySessionProvider(string ssoHost, string appKey, string userName, string password)
        {
            this.ssoHost = ssoHost;
            this.AppKey = appKey;
            this.userName = userName;
            this.password = password;
        }

        public string AppKey { get; }

        public string GetOrCreateSession()
        {
            if(this.sessionId != null && this.sessionCreateTime + this.sessionExpiry > DateTime.UtcNow)
            {
                return this.sessionId;
            }

            try
            {
                var uri = string.Format($"https://{this.ssoHost}/api/login?username={this.userName}&password={this.password}");
                var loginRequest = (HttpWebRequest)WebRequest.Create(uri);
                loginRequest.Headers.Add("X-Application", this.AppKey);
                loginRequest.Accept = "application/json";
                loginRequest.Method = "POST";
                loginRequest.Timeout = (int)this.timeout.TotalMilliseconds;
                WebResponse thePage = loginRequest.GetResponse();
                using (StreamReader reader = new StreamReader(thePage.GetResponseStream()))
                {
                    string response = reader.ReadToEnd();
                    var sessionDetails = JsonConvert.DeserializeObject<SessionDetails>(response);
                    if("SUCCESS".Equals(sessionDetails?.status))
                    {
                        this.sessionCreateTime = DateTime.UtcNow;
                        this.sessionId = sessionDetails.token;
                        return this.sessionId;
                    }
                    else
                    {
                        throw new ApplicationException($"Failed to obtain session token. {sessionDetails?.error}");
                    }
                }
            }
            catch (Exception ex)
            {
                throw new ApplicationException("SSO Authentication - call failed:", ex);
            }
        }
    }
}
