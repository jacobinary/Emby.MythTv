using System.Collections.Generic;

namespace Jellyfin.MythTv.Model
{
    public class ProgramGuide
    {
        public string StartTime { get; set; }
        public string EndTime { get; set; }
        public string Details { get; set; }
        public string StartIndex { get; set; }
        public string Count { get; set; }
        public string TotalAvailable { get; set; }
        public string AsOf { get; set; }
        public string Version { get; set; }
        public string ProtoVer { get; set; }
        public List<Channel> Channels { get; set; }
    }
}
