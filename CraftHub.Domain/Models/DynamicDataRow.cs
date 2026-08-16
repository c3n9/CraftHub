using System.Collections.Generic;
using System.ComponentModel;
using CraftHub.Domain.Enums;

namespace CraftHub.Domain.Models;

/// <summary>
/// A row of data with dynamic properties accessed via string indexer.
/// Used as DataGrid row items for dynamic column binding.
/// </summary>
public class DynamicDataRow : INotifyPropertyChanged
{
    private readonly Dictionary<string, string> _values = new();
    private readonly Dictionary<string, CellKind> _kinds = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Text value. Setting through this indexer always infers <see cref="CellKind.Value"/> or
    /// <see cref="CellKind.Empty"/> from the text — the only two kinds free-text editing can ever
    /// produce. Restoring <see cref="CellKind.Null"/> or <see cref="CellKind.Missing"/> (e.g. undo,
    /// import) needs <see cref="SetCell"/>, which takes the kind explicitly.
    /// </summary>
    public string this[string key]
    {
        get => _values.TryGetValue(key, out var val) ? val : "";
        set => SetCell(key, value, string.IsNullOrEmpty(value) ? CellKind.Empty : CellKind.Value);
    }

    /// <summary>What the cell actually holds — see <see cref="CellKind"/>. A key never initialized
    /// on this row reads as <see cref="CellKind.Missing"/>.</summary>
    public CellKind GetKind(string key) => _kinds.TryGetValue(key, out var k) ? k : CellKind.Missing;

    /// <summary>Sets both text and kind and raises PropertyChanged. Use this (not the indexer) when
    /// restoring an exact prior state — undo/redo, import — since the indexer alone cannot express
    /// <see cref="CellKind.Null"/> or <see cref="CellKind.Missing"/>.</summary>
    public void SetCell(string key, string value, CellKind kind)
    {
        _values[key] = value;
        _kinds[key] = value.Length == 0 ? kind : CellKind.Value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    /// <summary>Initial setup only (import, add row/column, duplicate) — does not raise
    /// PropertyChanged. <paramref name="kind"/> is only meaningful when <paramref name="value"/> is
    /// empty; a non-empty value is always <see cref="CellKind.Value"/>.</summary>
    public void InitializeProperty(string name, string value = "", CellKind kind = CellKind.Empty)
    {
        _values[name] = value;
        _kinds[name] = value.Length == 0 ? kind : CellKind.Value;
    }

    public bool HasProperty(string name) => _values.ContainsKey(name);

    public void RemoveProperty(string name)
    {
        _values.Remove(name);
        _kinds.Remove(name);
    }

    public void RenameProperty(string oldName, string newName)
    {
        if (!_values.TryGetValue(oldName, out var value)) return;
        var kind = GetKind(oldName);
        _values.Remove(oldName);
        _kinds.Remove(oldName);
        _values[newName] = value;
        _kinds[newName] = kind;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
    }

    public IReadOnlyDictionary<string, string> GetAllValues() => _values;
}
