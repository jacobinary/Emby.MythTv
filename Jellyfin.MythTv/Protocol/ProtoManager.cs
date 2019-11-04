using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.MythTv.Model;
using Microsoft.Extensions.Logging;

namespace Jellyfin.MythTv.Protocol
{
    class ProtoManager : ProtoInput
    {
        private Dictionary<int, ProtoRecorder> recorders = new Dictionary<int, ProtoRecorder>();
        private int nextRecorderId = 0;

        private ProtoEvent events;

        private void EventHandler(object sender, ProtoEventArgs e)
        {
            
        }
        
        public ProtoManager() : base() {
            events = new ProtoEvent
            {
                Server = Server,
                Port = Port,
                Logger = Logger
                
            };
            events.Event += EventHandler;
        }

        public override async Task<bool> Open()
        {
            return await base.Open();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                if (recorders != null) {
                    foreach (var recorder in recorders)
                        recorder.Value.Dispose();
                    recorders = null;
                }

                if (events != null) {
                    events.Event -= EventHandler;
                    events = null;
                }
            }

            base.Dispose(disposing);
        }

        public async Task<int> SpawnLiveTV(string chanNum)
        {
            if (!IsOpen)
                return 0;

            var cards = await GetFreeInputs();

            var recorder = new ProtoRecorder
            {
                Server = Server,
                Port = Port,
                Id = cards[0].CardId,
                Logger = Logger
                
            };
            var chain = new Chain();

            if (await recorder.SpawnLiveTV(chain.UID, chanNum))
            {
                recorders.Add(++nextRecorderId, recorder);
                
                return nextRecorderId;
            }

            await recorder.StopLiveTV();
            return 0;
        }

        public async Task<string> GetCurrentRecording(int id, List<StorageGroupMap> groups)
        {
            var recorder = recorders[id];
            
            StorageGroupFile file = null;
            do
            {
                var program = await recorder.GetCurrentRecording75();
                file = await recorder.QuerySGFile75(program.HostName, program.Recording.StorageGroup, program.FileName);
                await Task.Delay(500);
            }
            while (file.Size == 0);

            var map = groups.FirstOrDefault(x => x.GroupName == file.StorageGroup);
            return file.FileName.Replace(map.DirName, map.DirNameOverride);
        }

        public async Task StopLiveTV(int id)
        {
            if (recorders.ContainsKey(id) && recorders[id].IsPlaying)
            {
                await recorders[id].StopLiveTV();
                recorders.Remove(id);
            }
        }

    }
}
