using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Emby.MythTv.Model;
using MediaBrowser.Model.Logging;

namespace Emby.MythTv.Protocol
{
    class LiveTVPlayback : ProtoMonitor
    {
        private Dictionary<int, ProtoRecorder> m_recorders;
        private int m_idCounter = 0;

        public LiveTVPlayback(string server, int port, ILogger logger) : base(server, port, logger)
        {
            m_recorders = new Dictionary<int, ProtoRecorder>();
        }

        ~LiveTVPlayback()
        {
            Dispose(false);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && m_recorders != null) {
                foreach (var recorder in m_recorders)
                    recorder.Value.Dispose();
                m_recorders = null;
            }

            base.Dispose(disposing);
        }

        public override async Task<bool> Open()
        {
            return await base.Open();
        }

        public async Task<int> SpawnLiveTV(string chanNum)
        {
            if (!IsOpen)
                return 0;

            // just bodge it in and get the first free recorder
            var cards = await GetFreeInputs();

            var recorder = new ProtoRecorder(cards[0].CardId, Server, Port, _logger);
            var chain = new Chain();

            if (await recorder.SpawnLiveTV(chain.UID, chanNum))
            {
                m_idCounter++;
                m_recorders.Add(m_idCounter, recorder);
                
                return m_idCounter;
            }

            await recorder.StopLiveTV();
            return 0;
        }

        public async Task<string> GetCurrentRecording(int id, List<StorageGroupMap> groups)
        {
            var recorder = m_recorders[id];
            int fileSize = 0;
            Program program = null;
            string fileName;

            string liveTvStorageGroup = groups.Select(x => x.GroupName).Contains("LiveTV") ? "LiveTV" : "Default";

            do
            {
                program = await recorder.GetCurrentRecording75();
                fileName = program.FileName.Split('/').Last();
                fileSize = await recorder.QueryFileSize65(fileName, liveTvStorageGroup);
                await Task.Delay(500);
            }
            while (fileSize == 0);
            
            return Path.Combine(groups.SingleOrDefault(x => x.GroupName == liveTvStorageGroup).DirNameEmby, fileName);
        }

        public async Task StopLiveTV(int id)
        {
            if (m_recorders.ContainsKey(id) && m_recorders[id].IsPlaying)
            {
                await m_recorders[id].StopLiveTV();
                m_recorders.Remove(id);
            }
        }

    }
}
