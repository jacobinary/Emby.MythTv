using System.Collections.Generic;

namespace Jellyfin.MythTv.Model
{
    public class RecRuleList
    {
        public string StartIndex { get; set; }
        public string Count { get; set; }
        public string TotalAvailable { get; set; }
        public string AsOf { get; set; }
        public string Version { get; set; }
        public string ProtoVer { get; set; }
        public List<RecRule> RecRules { get; set; }
    }
}
