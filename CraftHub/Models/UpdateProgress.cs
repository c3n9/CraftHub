using CraftHub.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CraftHub.Models
{
    public class UpdateProgress
    {
        public int PercentComplete { get; set; } = -1;
        public string Status { get; set; } = "";
        public string Message { get; set; } = "";
        public bool IsIndeterminate { get; set; } = false;
        public long BytesReceived { get; set; }
        public long TotalBytes { get; set; }

        public string FormattedProgress
        {
            get
            {
                if (TotalBytes > 0 && BytesReceived > 0)
                {
                    return $"{FileSizeHelper.FormatFileSize(BytesReceived)} / {FileSizeHelper.FormatFileSize(TotalBytes)}";
                }
                return "";
            }
        }
    }
}
