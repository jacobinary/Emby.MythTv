using System;

namespace Jellyfin.MythTv.Model
{
    public class StorageGroupFile
    {
        public string FileName { get; set; }
        public string StorageGroup { get; set; }
        public string HostName { get; set; }
        public DateTime LastModified { get; set; }
        public long Size { get; set; }
    }
}
