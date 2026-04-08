
namespace CraftHub.Helpers
{
    public static class FileSizeHelper
    {
        public static string FormatFileSize(long bytes)
        {
            if (bytes < 0) return "0 B";

            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            double len = bytes;
            int order = 0;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len = len / 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }
    }
}
