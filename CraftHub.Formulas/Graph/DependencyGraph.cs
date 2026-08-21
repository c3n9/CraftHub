using System;
using System.Collections.Generic;
using System.Linq;

namespace CraftHub.Formulas.Graph;

/// <summary>
/// Tracks which formula depends on which — generic over the node key so this project stays free of
/// any notion of "path" or "cell" (the app instantiates <c>DependencyGraph&lt;JsonPath&gt;</c>).
/// Recalculation after an edit is: find everything downstream of the changed cell
/// (<see cref="GetAllDependents"/>), topologically sort just that set
/// (<see cref="TopologicalOrder"/>), evaluate in that order. Call <see cref="TryFindCycle"/> first
/// if a cycle is possible — <see cref="TopologicalOrder"/> on a cyclic set produces *a* order
/// without crashing, but it is not a meaningful one.
/// </summary>
public sealed class DependencyGraph<TKey> where TKey : notnull
{
    // node -> nodes its formula reads (forward edges)
    private readonly Dictionary<TKey, HashSet<TKey>> _dependsOn = new();
    // node -> nodes whose formula reads it (reverse edges — "what needs recalculating if I change")
    private readonly Dictionary<TKey, HashSet<TKey>> _dependents = new();

    /// <summary>Replaces every dependency <paramref name="node"/> had with exactly this new set —
    /// call once per formula (re)assignment, not incrementally, so a formula that used to read
    /// three cells and now reads one doesn't leave stale reverse edges behind.</summary>
    public void SetDependencies(TKey node, IEnumerable<TKey> dependsOn)
    {
        if (_dependsOn.TryGetValue(node, out var old))
            foreach (var dep in old)
                if (_dependents.TryGetValue(dep, out var reverse))
                    reverse.Remove(node);

        var next = new HashSet<TKey>(dependsOn);
        _dependsOn[node] = next;

        foreach (var dep in next)
        {
            if (!_dependents.TryGetValue(dep, out var reverse))
                _dependents[dep] = reverse = new HashSet<TKey>();
            reverse.Add(node);
        }
    }

    /// <summary>Drops every node and edge — call when the whole document a graph was tracking is
    /// gone (a new file was loaded into the same session, formulas were detached) rather than
    /// removing nodes one at a time.</summary>
    public void Clear()
    {
        _dependsOn.Clear();
        _dependents.Clear();
    }

    /// <summary>Drops a node entirely (its own dependencies and anyone else's dependency on it) —
    /// call when a cell's formula is removed or the cell itself no longer exists.</summary>
    public void RemoveNode(TKey node)
    {
        SetDependencies(node, Array.Empty<TKey>());
        _dependsOn.Remove(node);

        if (_dependents.TryGetValue(node, out var reverse))
            foreach (var dependent in reverse)
                if (_dependsOn.TryGetValue(dependent, out var forward))
                    forward.Remove(node);

        _dependents.Remove(node);
    }

    public IReadOnlyCollection<TKey> GetDirectDependents(TKey node) =>
        _dependents.TryGetValue(node, out var set) ? set : Array.Empty<TKey>();

    public IReadOnlyCollection<TKey> GetDirectDependencies(TKey node) =>
        _dependsOn.TryGetValue(node, out var set) ? set : Array.Empty<TKey>();

    /// <summary>Every node transitively downstream of <paramref name="changed"/> (dependents of
    /// dependents of ...) — everything that needs recalculating, in no particular order. Does not
    /// include the changed nodes themselves; the caller adds those.</summary>
    public HashSet<TKey> GetAllDependents(IEnumerable<TKey> changed)
    {
        var result = new HashSet<TKey>();
        var queue = new Queue<TKey>(changed);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var dependent in GetDirectDependents(current))
                if (result.Add(dependent))
                    queue.Enqueue(dependent);
        }

        return result;
    }

    /// <summary>Topologically sorts <paramref name="nodes"/> (a dependency always comes before
    /// whatever reads it) using only edges between members of that set — nodes outside it are
    /// treated as already-settled values, not walked into. On a cyclic subset this still returns
    /// every node exactly once (it just doesn't recurse back into an in-progress node a second
    /// time), but the resulting order is not meaningful for that subset — check
    /// <see cref="TryFindCycle"/> first.</summary>
    public IReadOnlyList<TKey> TopologicalOrder(IEnumerable<TKey> nodes)
    {
        var set = new HashSet<TKey>(nodes);
        var visited = new HashSet<TKey>();
        var onStack = new HashSet<TKey>();
        var result = new List<TKey>(set.Count);

        foreach (var n in set)
            Visit(n);

        return result;

        void Visit(TKey n)
        {
            if (visited.Contains(n)) return;
            if (!onStack.Add(n)) return; // already being visited higher up the call stack — a cycle; don't recurse again

            foreach (var dep in _dependsOn.TryGetValue(n, out var deps) ? deps : Enumerable.Empty<TKey>())
                if (set.Contains(dep))
                    Visit(dep);

            onStack.Remove(n);
            visited.Add(n);
            result.Add(n);
        }
    }

    /// <summary>Finds a cycle reachable from <paramref name="start"/> by following dependency
    /// edges, if one exists. <paramref name="chain"/> is the loop itself, in dependency order and
    /// closed (first and last elements are the same node) — e.g. <c>[A, B, C, A]</c> for
    /// <c>A → B → C → A</c> — suitable for printing directly as the error message.</summary>
    public bool TryFindCycle(TKey start, out IReadOnlyList<TKey> chain)
    {
        var stack = new List<TKey>();
        var onStack = new HashSet<TKey>();
        var visited = new HashSet<TKey>();

        if (Dfs(start))
        {
            chain = stack;
            return true;
        }

        chain = Array.Empty<TKey>();
        return false;

        bool Dfs(TKey n)
        {
            if (onStack.Contains(n))
            {
                var loopStart = stack.IndexOf(n);
                var loop = stack.Skip(loopStart).ToList();
                loop.Add(n);
                stack.Clear();
                stack.AddRange(loop);
                return true;
            }

            if (!visited.Add(n)) return false;

            stack.Add(n);
            onStack.Add(n);

            foreach (var dep in _dependsOn.TryGetValue(n, out var deps) ? deps : Enumerable.Empty<TKey>())
                if (Dfs(dep))
                    return true;

            stack.RemoveAt(stack.Count - 1);
            onStack.Remove(n);
            return false;
        }
    }
}
