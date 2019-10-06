
using Jellyfin.MythTv.Model;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace Jellyfin.MythTv.Protocol
{
    class ProtoRecorder : ProtoPlayback
    {
        public int Num { get; set; }
        public bool IsPlaying { get; private set; }
        public bool IsLiveRecording { get; private set; }

        public ProtoRecorder(int num, string server, int port, ILogger logger) : base(server, port, logger)
        {
            Num = num;
            IsPlaying = false;
            IsLiveRecording = false;

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

            var cmd = $"QUERY_RECORDER {Num}{DELIMITER}SPAWN_LIVETV{DELIMITER}{chainid}{DELIMITER}0{DELIMITER}{channum}";

            IsPlaying = true;

            if ((await SendCommand(cmd))[0] != "OK")
                IsPlaying = false;

            return IsPlaying;
        }

        private async Task<bool> StopLiveTV75()
        {
            var cmd = $"QUERY_RECORDER {Num}{DELIMITER}STOP_LIVETV";
            var result = await SendCommand(cmd);
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
            var cmd = $"QUERY_RECORDER {Num}{DELIMITER}GET_CURRENT_RECORDING";
            var result = await SendCommand(cmd);

            return RcvProgramInfo86(result);
        }

        public async Task<StorageGroupFile> QuerySGFile75(string hostname, string storageGroup, string filename)
        {
            var cmd = $"QUERY_SG_FILEQUERY{DELIMITER}{hostname}{DELIMITER}{storageGroup}{DELIMITER}{filename}";
            var result = await SendCommand(cmd);

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
