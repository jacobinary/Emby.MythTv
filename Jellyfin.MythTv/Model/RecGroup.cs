using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.MythTv.Model
{
    public class RecGroup
    {
        public string Name { get; set; }
        public bool Enabled { get; set; }

        private static List<string> DefaultDisabled = new List<string> {"LiveTV", "Deleted"};

        public RecGroup(string Name)
        {
            this.Name = Name;
            Enabled = !DefaultDisabled.Any(x => Name.Contains(x));
        }

        public RecGroup() {}
    }
}
