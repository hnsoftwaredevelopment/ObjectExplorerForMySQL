using System;
using System.Collections.Generic;
using System.Linq;

namespace ObjectExplorerForMySql.Core.Scripting;

/// <summary>Orders objects so that an object comes after the objects it depends on (US-0603).</summary>
public static class DependencyOrder
{
    /// <summary>
    /// Returns <paramref name="names"/> so that every name comes after the names it depends on (foreign keys for tables,
    /// used tables and views for views). Among names whose dependencies are met, the order is alphabetical.
    /// Self-references and dependencies on names outside the list are ignored. Names in a cycle, and names that depend
    /// on them, cannot be ordered; they follow at the end, alphabetically (the script turns foreign key checks off, so tables in a cycle still load).
    /// </summary>
    public static IReadOnlyList<string> Sort(IEnumerable<string> names, IEnumerable<(string Name, string DependsOn)> dependencies)
    {
        var all = new SortedSet<string>(names ?? throw new ArgumentNullException(nameof(names)), StringComparer.OrdinalIgnoreCase);
        var waitingFor = all.ToDictionary(n => n, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        foreach (var (name, dependsOn) in dependencies ?? [])
        {
            if (waitingFor.TryGetValue(name, out var set) && all.Contains(dependsOn) && !string.Equals(name, dependsOn, StringComparison.OrdinalIgnoreCase))
            {
                set.Add(dependsOn);
            }
        }

        var result = new List<string>(all.Count);
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var next = all.FirstOrDefault(n => !done.Contains(n) && waitingFor[n].All(done.Contains));
            if (next is null)
            {
                break;
            }

            result.Add(next);
            done.Add(next);
        }

        result.AddRange(all.Where(n => !done.Contains(n)));
        return result;
    }
}
