using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CraftHub.Models
{
    public class ProgressResult
    {
        public bool IsSuccess { get; set; }
        public bool IsCanceled { get; set; }
        public string? ErrorMessage { get; set; }

        public static ProgressResult Success() => new ProgressResult { IsSuccess = true };
        public static ProgressResult Canceled() => new ProgressResult { IsCanceled = true };
        public static ProgressResult Error(string message) => new ProgressResult { ErrorMessage = message };
    }
}
