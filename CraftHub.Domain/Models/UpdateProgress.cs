
namespace CraftHub.Domain.Models
{
    public class UpdateProgress
    {
        public int PercentComplete { get; set; } = -1;
        public string Status { get; set; } = "";
        public string Message { get; set; } = "";
        public bool IsIndeterminate { get; set; } = false;
        public long BytesReceived { get; set; }
        public long TotalBytes { get; set; }
    }
}
