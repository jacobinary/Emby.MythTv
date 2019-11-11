
using Jellyfin.MythTv.Model;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.MythTv.Protocol
{
    class ProtoRecorder : ProtoBase
    {
        public int Id { get; set; }
        public bool IsPlaying { get; private set; } = false;
        public bool IsLiveRecording { get; private set; } = false;

        public ProtoRecorder(int id, string server, int port, ILogger logger) : base(server, port, AnnounceModeType.Playback, EventModeType.None, logger) {
            Id = id;
        }

        ~ProtoRecorder()
        {
            Dispose(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && IsPlaying)
            {
                Task.WaitAll(StopLiveTVAsync());
            }

            base.Dispose(disposing);
        }

        public async Task<bool> SpawnLiveTVAsync(Chain chain, string channum)
        {
            if (!IsOpen) {
                await OpenAsync();
            }

            return await SpawnLiveTV75Async(chain, channum);
        }

        private async Task<bool> SpawnLiveTV75Async(Chain chain, string channum)
        {
            var cmd = $"QUERY_RECORDER {Id}{DELIMITER}SPAWN_LIVETV{DELIMITER}{chain.UID}{DELIMITER}0{DELIMITER}{channum}";

            IsPlaying = true;

            if ((await SendCommandAsync(cmd))[0] != "OK")
                IsPlaying = false;

            return IsPlaying;
        }

        private async Task<bool> StopLiveTV75Async()
        {
            var cmd = $"QUERY_RECORDER {Id}{DELIMITER}STOP_LIVETV";
            var result = await SendCommandAsync(cmd);
            if (result[0] != "OK")
                return false;

            IsPlaying = false;
            return true;
        }

        public async Task StopLiveTVAsync()
        {
            var stopped = await StopLiveTV75Async();
            if (stopped) {
                await CloseAsync();
            }
        }

        private async Task<Program> GetCurrentRecording75Async()
        {
            var cmd = $"QUERY_RECORDER {Id}{DELIMITER}GET_CURRENT_RECORDING";
            var result = await SendCommandAsync(cmd);

            return RcvProgramInfo86(result);
        }

        public async Task<Program> GetCurrentRecordingAsync()
        {
            return await GetCurrentRecording75Async();
        }

        public async Task<StorageGroupFile> QuerySGFile75Async(string hostname, string storageGroup, string filename)
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

        public async Task<StorageGroupFile> QuerySGFileAsync(string hostname, string storageGroup, string filename) {
            return await QuerySGFile75Async(hostname, storageGroup, filename);
        }
    }
}
