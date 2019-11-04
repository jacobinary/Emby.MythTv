using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.MythTv.Protocol {
    class ProtoEventFormatException : FormatException {
        public ProtoEventFormatException(List<string> eventStrings) : base("[Mythtv] Unrecognized backend message: {eventStrings.ToString()}") {}
    }

	class ProtoEventArgs : EventArgs  
	{  
        public string Name { get; set; }
        public string Message { get; set; }
	}

	class ProtoEvent : ProtoBase
    {
		public bool IsRunning { get; private set; }

        public delegate void EventHandler(object sender, ProtoEventArgs e);

        public event EventHandler Event;
		
		public ProtoEvent()
		{
            AnnounceMode = AnnounceModeType.Monitor;
		}

        private ProtoEventArgs formatProtoEventStrings(List<string> eventStrings) {
            if (eventStrings.Count == 0) {
                throw new ProtoEventFormatException(eventStrings);
            }

            var name = eventStrings[0];
            var message = eventStrings.Count > 1 ? eventStrings[1] : "";
            
            return new ProtoEventArgs
            {
                Name = name,
                Message = message
            };
        }

		public async Task StartAsync()
        {
			while (IsRunning)
            {
                try {
                    var eventHandler = Event;
                    if (eventHandler != null && IsRunning) {
                        eventHandler(this, formatProtoEventStrings(await ListenAsync()));
                    }
                } catch(ProtoEventFormatException ex) {
                    // If invalid proto event, just log and continue
                    Logger.LogInformation(ex.Message);
                }
			}
		}

		public void StopAsync()
        {
			IsRunning = false;
		}
	}
}