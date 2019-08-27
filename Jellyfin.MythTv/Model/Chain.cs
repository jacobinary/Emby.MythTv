using System;

namespace Jellyfin.MythTv.Model
{
    class Chain
    {
        public string UID { get; private set; }

        public Chain()
        {
            UID = $"{System.Net.Dns.GetHostName()}-{DateTime.UtcNow.ToString("o")}";
        }
    }
}
