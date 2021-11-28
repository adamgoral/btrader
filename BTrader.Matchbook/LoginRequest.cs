using System.Runtime.Serialization;

namespace BTrader.Matchbook
{

//{
//  "username": "jblogss",
//  "password": "verysecurepassword"
//}

[DataContract]
    public class LoginRequest
    {
        [DataMember(Name = "username")]
        public string Username { get; set; }
        [DataMember(Name = "password")]
        public string Password { get; set; }
    }
}
