using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.Matchbook
{

    //    {
    //  "total": 13,
    //  "per-page": 20,
    //  "offset": 0,
    //  "sports": [
    //    {
    //      "name": "Basketball",
    //      "type": "SPORT",
    //      "id": 4
    //    },
    //    {
    //      "name": "Boxing",
    //      "type": "SPORT",
    //      "id": 14
    //    },
    //    {
    //      "name": "Cricket",
    //      "type": "SPORT",
    //      "id": 110
    //    },
    //    {
    //      "name": "Darts",
    //      "type": "SPORT",
    //      "id": 116
    //    },
    //    {
    //      "name": "Golf",
    //      "type": "SPORT",
    //      "id": 8
    //    },
    //    ...
    //  ]
    //}

    [DataContract]
    public class GetSportsResponse : PagedResponse
    {
        [DataMember(Name = "sports")]
        public Sport[] Sports { get; set; }
    }
}
