using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Runtime.Serialization;

namespace BTrader.Matchbook
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum MatchbookOrderSides
    {
        [EnumMember(Value = "lay")]
        Lay,
        [EnumMember(Value = "back")]
        Back
    }
}
