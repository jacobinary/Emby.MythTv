namespace Jellyfin.MythTv.Model
{
    public class StorageGroupDir
    {
        public int Id { get; set; }
        public string GroupName { get; set; }
        public string HostName { get; set; }
        public string DirName { get; set; }
        public bool DirRead { get; set; }
        public bool DirWrite { get; set; }
        public int KiBFree { get; set; }

    }

    public class StorageGroupMap
    {
        public string GroupName { get; set; }
        public string DirName { get; set; }
        public string DirNameEmby { get; set; }
    }

}
