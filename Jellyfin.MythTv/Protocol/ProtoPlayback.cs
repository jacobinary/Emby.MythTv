using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.MythTv.Protocol
{
    class ProtoPlayback : ProtoBase
    {

        public ProtoPlayback(string server, int port, ILogger logger) : base(server, port, AnnounceMode.Playback, logger)
        {
        }

    }
}
