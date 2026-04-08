using System.Collections.Generic;

namespace CraftHub.Core;

public class FileFilter
{
    public string Name { get; }
    public IReadOnlyList<string>? Patterns { get; }

    public FileFilter(string name, IReadOnlyList<string>? patterns = null)
    {
        Name = name;
        Patterns = patterns;
    }
}
