using System;

namespace Jellyfin.MythTv.Model
{
    class Chain
    {
        public string UID { get; private set; }
        public Input Input { get; private set; }

        public Chain(Input input)
        {
            UID = $"{System.Net.Dns.GetHostName()}-{DateTime.UtcNow.ToString("o")}";
            Input = input;
        }
    }
}
