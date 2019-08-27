using System.Threading.Tasks;
using MediaBrowser.Model.Logging;

namespace Jellyfin.MythTv.Protocol
{
    class ProtoPlayback : ProtoBase
    {

        public ProtoPlayback(string server, int port, ILogger logger) : base(server, port, logger)
        {
        }

        public async Task<bool> Open()
        {
            bool ok = false;
            if (!await OpenConnection())
            {
                return false;
            }

            if (ProtoVersion >= 75)
                ok = await Announce75();

            if (ok)
                return true;

            await Close();
            return false;
        }

        private async Task<bool> Announce75()
        {
            var result = await SendCommand("ANN Playback emby 0");
            return result[0] == "OK";
        }

    }
}
