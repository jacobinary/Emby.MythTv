
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
                _ = StopLiveTVAsync().ConfigureAwait(false);
            }

            base.Dispose(disposing);
        }

        public async Task<bool> SpawnLiveTVAsync(Chain chain, string channum)
        {
            if (!IsOpen) {
                await OpenAsync().ConfigureAwait(false);
            }

            return await SpawnLiveTV75Async(chain, channum).ConfigureAwait(false);
        }

        private async Task<bool> SpawnLiveTV75Async(Chain chain, string channum)
        {

            IsPlaying = true;

            var cmd = $"QUERY_RECORDER {Id}{DELIMITER}SPAWN_LIVETV{DELIMITER}{chain.UID}{DELIMITER}0{DELIMITER}{channum}";

            var status = (await SendCommandAsync(cmd).ConfigureAwait(false))[0];
            if (status != "OK")
                IsPlaying = false;

            return IsPlaying;
        }

        private async Task<bool> StopLiveTV75Async()
        {
            var cmd = $"QUERY_RECORDER {Id}{DELIMITER}STOP_LIVETV";

            var status = (await SendCommandAsync(cmd).ConfigureAwait(false))[0];
            if (status != "OK")
                return false;

            IsPlaying = false;
            return true;
        }

        public async Task StopLiveTVAsync()
        {
            var stopped = await StopLiveTV75Async().ConfigureAwait(false);
            if (stopped) {
                await CloseAsync();
            }
        }

        private async Task<Program> GetCurrentRecording75Async()
        {
            var cmd = $"QUERY_RECORDER {Id}{DELIMITER}GET_CURRENT_RECORDING";
            var result = await SendCommandAsync(cmd).ConfigureAwait(false);

            return RcvProgramInfo86(result);
        }

        public Task<Program> GetCurrentRecordingAsync()
        {
            return GetCurrentRecording75Async();
        }

        public async Task<StorageGroupFile> QuerySGFile75Async(string hostname, string storageGroup, string filename)
        {
            var cmd = $"QUERY_SG_FILEQUERY{DELIMITER}{hostname}{DELIMITER}{storageGroup}{DELIMITER}{filename}";
            var result = await SendCommandAsync(cmd).ConfigureAwait(false);

            return new StorageGroupFile {
                FileName = result[0],
                StorageGroup = storageGroup,
                HostName = hostname,
                LastModified = UnixTimeStampToDateTime(int.Parse(result[1])),
                Size = long.Parse(result[2])
            };
        }

        public Task<StorageGroupFile> QuerySGFileAsync(string hostname, string storageGroup, string filename) {
            return QuerySGFile75Async(hostname, storageGroup, filename);
        }
    }
}
