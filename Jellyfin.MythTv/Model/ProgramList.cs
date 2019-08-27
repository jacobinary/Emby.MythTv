using System.Collections.Generic;

namespace Jellyfin.MythTv.Model
{
    public class ProgramList
    {
        public string StartIndex { get; set; }
        public string Count { get; set; }
        public string TotalAvailable { get; set; }
        public string AsOf { get; set; }
        public string Version { get; set; }
        public string ProtoVer { get; set; }
        public List<Program> Programs { get; set; }
    }
}
