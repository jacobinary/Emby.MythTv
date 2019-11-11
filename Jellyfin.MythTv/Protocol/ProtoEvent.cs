using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Jellyfin.MythTv.Protocol {
    class ProtoEventFormatException : FormatException {
        public ProtoEventFormatException(List<string> eventStrings) : base("[Mythtv] Unrecognized backend message: {eventStrings.ToString()}") {}
    }

	class ProtoEvent : ProtoBase
    {
        protected static readonly string MESSAGE_DELIMITER = " ";

		public bool IsListening { get; private set; } = false;

        public delegate void EventHandler(object sender, ProtoMessage e);

        public event EventHandler Event;
		
		public ProtoEvent(string server, int port, EventModeType eventMode, ILogger logger) : base (server, port, AnnounceModeType.Monitor, eventMode, logger) {}

        private ProtoMessage formatProtoEventStrings(List<string> eventStrings) {
            if (eventStrings.Count == 0) {
                throw new ProtoEventFormatException(eventStrings);
            }

            var messageStrings = eventStrings[1].Split(new[] { MESSAGE_DELIMITER }, StringSplitOptions.None).ToList();
            if (messageStrings.Count == 0) {
                throw new ProtoEventFormatException(eventStrings);
            }

            return new ProtoMessage
            {
                Name = (BackendMessage)Enum.Parse(typeof(BackendMessage), messageStrings[0], true),
                Data = messageStrings.Count >= 1 ? messageStrings.GetRange(1, messageStrings.Count - 2) : new List<string>()
            };
        }

        private void Broadcast(List<string> res) {
            var eventHandler = Event;
            if (eventHandler != null) {
                eventHandler(this, formatProtoEventStrings(res));
            }
        }

		public async Task StartAsync()
        {
            if (!IsOpen) {
                await OpenAsync();
            }
            
			_ = ListenAndHandleAsync();
		}

        protected async Task ListenAndHandleAsync() {
            Logger.LogInformation("[MythTV] Start listening for events");

            IsListening = true;

            while (IsOpen && IsListening)
            {
                try {
                    var eventHandler = Event;
                    if (eventHandler != null) {
                        Broadcast(await ListenAsync());
                    }
                } catch(ProtoEventFormatException ex) {
                    // If invalid proto event, just log and continue
                    Logger.LogInformation($"[MythTV] {ex.Message}");
                }
			}
        }

		public async Task StopAsync()
        {
            Logger.LogInformation("[MythTV] Stop listening for events");

            IsListening = false;

            if (IsOpen) {
                await CloseAsync();
            }
		}
	}
}