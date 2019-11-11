using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Jellyfin.MythTv.Model;
using Microsoft.Extensions.Logging;
using MediaBrowser.Model.System;

namespace Jellyfin.MythTv.Protocol
{
    class ProtoManager : ProtoInput
    {
        private Dictionary<int, ProtoRecorder> recorders = new Dictionary<int, ProtoRecorder>();
        private int nextRecorderId = 0;

        private ProtoEvent events;

        private void EventHandler(object sender, ProtoMessage e)
        {
            Logger.LogInformation($"[MythTV] Event {e.Name}: {String.Join(", ", e.Data)}");
        }
        
        public ProtoManager(string server, int port, ILogger logger) : base (server, port, logger) {}

        private async Task StartEventListenerAsync() {
            if (events == null) {
                events = new ProtoEvent(Server, Port, EventModeType.ExcludeSystem, Logger);
                events.Event += EventHandler;
                
                await events.StartAsync();
            }
        }

        private async Task StopEventListenerAsync() {
            if (events != null) {
                events.Event -= EventHandler;

                await events.StopAsync();
                
                events = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) {
                if (recorders != null) {
                    foreach (var recorder in recorders)
                        recorder.Value.Dispose();
                    recorders = null;
                }

                Task.WaitAll(StopEventListenerAsync());
            }

            base.Dispose(disposing);
        }

        public async Task<int> SpawnLiveTV(string chanNum)
        {
            if (!IsOpen) {
                await OpenAsync();
            }

            await StartEventListenerAsync();

            var chain = new Chain(await GetFreeInputAsync());

            if (chain.Input == null) {
                return 0;
            }

            var recorder = new ProtoRecorder(chain.Input.CardId, Server, Port, Logger);

            if (await recorder.SpawnLiveTVAsync(chain, chanNum))
            {
                recorders.Add(++nextRecorderId, recorder);
                
                return nextRecorderId;
            }

            await recorder.StopLiveTVAsync();
            return 0;
        }

        public async Task<string> GetCurrentRecordingAsync(int id, List<StorageGroupMap> groups)
        {
            var recorder = recorders[id];
            if (recorder == null && recorder.IsPlaying) {
                throw new InvalidOperationException("That Live TV instance is not playing");
            }
            
            StorageGroupFile file = null;
            do
            {
                var program = await recorder.GetCurrentRecordingAsync();
                file = await recorder.QuerySGFileAsync(program.HostName, program.Recording.StorageGroup, program.FileName);
                await Task.Delay(500);
            }
            while (file.Size == 0);

            var map = groups.FirstOrDefault(x => x.GroupName == file.StorageGroup);
            return file.FileName.Replace(map.DirName, map.DirNameOverride);
        }

        public async Task StopLiveTVAsync(int id)
        {
            var recorder = recorders.ContainsKey(id) ? recorders[id] : null;
            if (recorder != null && recorder.IsPlaying)
            {
                await recorder.StopLiveTVAsync();
                recorders.Remove(id);
            }

            if (recorders.Count == 0) {
                await StopEventListenerAsync();
            }
        }

    }
}
