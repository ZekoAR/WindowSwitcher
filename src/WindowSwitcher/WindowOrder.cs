using WindowSwitcher.Native;

namespace WindowSwitcher;

/// <summary>
/// Orders the popup. Every window gets an increasing sort order the first time the popup sees it, and so
/// does every program (row). With <see cref="RowOrder.Fixed"/> rows and the windows in them are sorted on
/// those numbers, so they keep their places for as long as the app runs. With
/// <see cref="RowOrder.Horizontal"/> both go left to right by where the windows are on the screen, the
/// numbers breaking ties. Windows seen for the first time together are numbered in z-order.
/// </summary>
internal sealed class WindowOrder
{
    // A window handle can be reused after its window is destroyed, so a window is keyed by its handle
    // and its process, and entries for windows that no longer exist are dropped.
    readonly Dictionary<(nint Hwnd, uint ProcessId), long> _windows = [];
    readonly Dictionary<string, long> _programs = new(StringComparer.OrdinalIgnoreCase);
    long _nextWindow;
    long _nextProgram;

    /// <summary>Numbers anything new, then sorts the rows and each row's windows in place.</summary>
    public void Apply(List<ProgramGroup> groups, RowOrder rowOrder)
    {
        Prune();
        foreach (var group in groups)
        {
            if (!_programs.ContainsKey(group.Key))
                _programs[group.Key] = _nextProgram++;
            foreach (var window in group.Windows)
                _windows.TryAdd((window.Hwnd, window.ProcessId), _nextWindow++);
        }

        foreach (var group in groups)
        {
            group.Windows.Sort((a, b) =>
            {
                int byPosition = rowOrder == RowOrder.Horizontal ? a.Bounds.Left.CompareTo(b.Bounds.Left) : 0;
                return byPosition != 0 ? byPosition : _windows[(a.Hwnd, a.ProcessId)].CompareTo(_windows[(b.Hwnd, b.ProcessId)]);
            });
        }

        if (rowOrder == RowOrder.Horizontal)
        {
            var left = groups.ToDictionary(g => g, LeftEdge);
            groups.Sort((a, b) =>
            {
                int byPosition = left[a].CompareTo(left[b]);
                return byPosition != 0 ? byPosition : _programs[a.Key].CompareTo(_programs[b.Key]);
            });
        }
        else
        {
            groups.Sort((a, b) => _programs[a.Key].CompareTo(_programs[b.Key]));
        }
    }

    /// <summary>
    /// Where a row sits on the screen: the left edge of its leftmost window that is not minimized, or,
    /// when all are minimized, of the leftmost place one of them restores to.
    /// </summary>
    static int LeftEdge(ProgramGroup group)
    {
        int visible = int.MaxValue, restored = int.MaxValue;
        foreach (var w in group.Windows)
        {
            if (w.Minimized) restored = Math.Min(restored, w.Bounds.Left);
            else visible = Math.Min(visible, w.Bounds.Left);
        }
        return visible != int.MaxValue ? visible : restored;
    }

    void Prune()
    {
        List<(nint, uint)>? gone = null;
        foreach (var key in _windows.Keys)
        {
            if (User32.IsWindow(key.Hwnd)
                && User32.GetWindowThreadProcessId(key.Hwnd, out uint pid) != 0
                && pid == key.ProcessId)
                continue;
            (gone ??= []).Add(key);
        }
        if (gone is null) return;
        foreach (var key in gone) _windows.Remove(key);
    }
}
