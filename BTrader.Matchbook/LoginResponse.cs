using System.Runtime.Serialization;

namespace BTrader.Matchbook
{
    //    {
    //  "session-token": "68fef7b56bd069e9296182b55b194a",
    //  "user-id": 1234,
    //  "role": "USER",
    //  "account": {
    //    "id": 5678,
    //    "username": "jbloggs",
    //    "name": {
    //      "first": "Joe",
    //      "last": "Bloggs"
    //    },
    //    "email": "jbloggs@matchbook.com",
    //    "phone-number": "555555555",
    //    "address": {
    //      "address-id": 789,
    //      "address-line-1": "1 Magic Avenue",
    //      "region-name": "Great Place",
    //      "post-code": "",
    //      "country": {
    //        "country-id": 44,
    //        "name": "United Kingdom",
    //        "country-code": "UK"
    //      },
    //    },
    //    "date-of-birth": "1970-01-01T00:00:00.000Z",
    //    "currency": "GBP",
    //    "balance": 100.00,
    //    "exposure": 0.00,
    //    "commission-credit": 0.00,
    //    "free-funds": 100.00,
    //    "language": "English",
    //    "odds-type": "US",
    //    "bet-confirmation": false,
    //    "display-p-and-l": true,
    //    "exchange-type": "back-lay",
    //    "odds-rounding": false,
    //    "bet-slip-pinned": true
    //  },
    //  "last-login": "2017-01-01T00:00:00.000Z"
    //}

    [DataContract]
    public class LoginResponse
    {
        [DataMember(Name = "session-token")]
        public string SessionToken { get; set; }
        [DataMember(Name = "user-id")]
        public int UserId { get; set; }
    }
}
