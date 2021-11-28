using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BTrader.Betfair
{
    public interface IAppKeySessionProvider
    {
        string AppKey { get; }

        string GetOrCreateSession();
    }
}
