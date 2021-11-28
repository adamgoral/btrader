using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.Matchbook
{
//    {
//  "session-token": "68fef7b56bd069e9296182b55b194a",
//  "user-id": 1234,
//  "username": "jbloggs"
//}

[DataContract]
    public class LogoutResponse
    {
        [DataMember(Name = "session-token")]
        public string SessionToken { get; set; }
        [DataMember(Name = "user-id")]
        public int UserId { get; set; }
        [DataMember(Name = "username")]
        public string UserName { get; set; }
    }
}
