using System;
using System.Collections.Generic;

namespace Jellyfin.MythTv.Model {
	enum BackendMessage
	{
		CLEAR_SETTINGS_CACHE,
		COMMFLAG_REQUEST,
		DONE_RECORDING,
		DOWNLOAD_FILE,
		FILE_CLOSED,
		FILE_WRITTEN,
		GENERATED_PIXMAP,
		LIVETV_CHAIN,
		LIVETV_WATCH,
		MASTER_UPDATE_PROG_INFO,
		RECORDING_LIST_CHANGE,
		SCHEDULE_CHANGE,
		SIGNAL,
		SYSTEM_EVENT,
		UPDATE_FILE_SIZE,
		VIDEO_LIST_CHANGE
	}
	
	class ProtoMessage : EventArgs  
	{  
        public BackendMessage Name { get; set; }
				public List<string> Data { get; set; }
	}
}