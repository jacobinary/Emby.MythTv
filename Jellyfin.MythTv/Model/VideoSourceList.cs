using System;
using System.Collections.Generic;

namespace Jellyfin.MythTv.Model
{
    public class VideoSourceList
    {
        public DateTime? AsOf { get; set; }
        public string Version { get; set; }
        public string ProtoVer { get; set; }
        public List<VideoSource> VideoSources { get; set; }
    }
}
