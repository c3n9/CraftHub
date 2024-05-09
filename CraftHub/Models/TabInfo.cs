using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CraftHub.Models
{
    public partial class TabInfo
    {
        private static HashSet<string> UsedTabNames = new HashSet<string>();

        private string _header;
        public string Header
        {
            get => _header;
            set
            {
                if (!UsedTabNames.Contains(value))
                {
                    UsedTabNames.Remove(_header);
                    _header = value;
                    UsedTabNames.Add(_header);
                }
                else
                {
                    throw new ArgumentException("Tab name must be unique.");
                }
            }
        }

        public Guid UniqueId { get; set; }
    }
}
