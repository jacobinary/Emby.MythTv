
using Jellyfin.MythTv.Model;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Jellyfin.MythTv.Protocol
{
    class ProtoRecorder : ProtoBase
    {
        public int Id { get; set; }
        public bool IsPlaying { get; private set; } = false;
        public bool IsLiveRecording { get; private set; } = false;

        public ProtoRecorder()
        {
            AnnounceMode = AnnounceModeType.Playback;

            Task.WaitAll(Open());
        }

        ~ProtoRecorder()
        {
            Dispose(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && IsPlaying)
            {
                Task.WaitAll(StopLiveTV());
            }

            base.Dispose(disposing);
        }

        public async Task<bool> SpawnLiveTV(string chainid, string channum)
        {
            return await SpawnLiveTV75(chainid, channum);
        }

        private async Task<bool> SpawnLiveTV75(string chainid, string channum)
        {
            if (!IsOpen)
                return false;

            var cmd = $"QUERY_RECORDER {Id}{DELIMITER}SPAWN_LIVETV{DELIMITER}{chainid}{DELIMITER}0{DELIMITER}{channum}";

            IsPlaying = true;

            if ((await SendCommandAsync(cmd))[0] != "OK")
                IsPlaying = false;

            return IsPlaying;
        }

        private async Task<bool> StopLiveTV75()
        {
            var cmd = $"QUERY_RECORDER {Id}{DELIMITER}STOP_LIVETV";
            var result = await SendCommandAsync(cmd);
            if (result[0] != "OK")
                return false;

            IsPlaying = false;
            return true;
        }

        public async Task<bool> StopLiveTV()
        {
            return await StopLiveTV75();
        }

        public async Task<Program> GetCurrentRecording75()
        {
            var cmd = $"QUERY_RECORDER {Id}{DELIMITER}GET_CURRENT_RECORDING";
            var result = await SendCommandAsync(cmd);

            return RcvProgramInfo86(result);
        }

        public async Task<StorageGroupFile> QuerySGFile75(string hostname, string storageGroup, string filename)
        {
            var cmd = $"QUERY_SG_FILEQUERY{DELIMITER}{hostname}{DELIMITER}{storageGroup}{DELIMITER}{filename}";
            var result = await SendCommandAsync(cmd);

            return new StorageGroupFile {
                FileName = result[0],
                StorageGroup = storageGroup,
                HostName = hostname,
                LastModified = UnixTimeStampToDateTime(int.Parse(result[1])),
                Size = long.Parse(result[2])
            };
        }
    }
}
