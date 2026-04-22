using System.Collections.Generic;
using System.ComponentModel;

namespace CraftHub.Domain.Models;

/// <summary>
/// A row of data with dynamic properties accessed via string indexer.
/// Used as DataGrid row items for dynamic column binding.
/// </summary>
public class DynamicDataRow : INotifyPropertyChanged
{
    private readonly Dictionary<string, string> _values = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key]
    {
        get => _values.TryGetValue(key, out var val) ? val : "";
        set
        {
            _values[key] = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }
    }

    public void InitializeProperty(string name, string value = "")
    {
        _values[name] = value;
    }

    public bool HasProperty(string name) => _values.ContainsKey(name);

    public void RemoveProperty(string name) => _values.Remove(name);

    public IReadOnlyDictionary<string, string> GetAllValues() => _values;
}
