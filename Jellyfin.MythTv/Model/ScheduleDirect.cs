using System.Collections.Generic;

namespace Jellyfin.MythTv.Model
{
    public class ScheduleDirect
    {
        public class Caption
        {
            public string content { get; set; }
            public string lang { get; set; }
        }

        public class ImageData
        {
            public string width { get; set; }
            public string height { get; set; }
            public string uri { get; set; }
            public string size { get; set; }
            public string aspect { get; set; }
            public string category { get; set; }
            public string text { get; set; }
            public string primary { get; set; }
            public string tier { get; set; }
            public Caption caption { get; set; }
        }

        public class ShowImages
        {
            public string programID { get; set; }
            public List<ImageData> data { get; set; }
        }
    }
}
