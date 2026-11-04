using System;
using System.Collections.Generic;

namespace ObjectExplorerForMySql.Core.Query;

/// <summary>
/// The tokenizer state at the start of every line of a document (US-0702). After an edit only the changed lines are
/// read again, plus the lines after them until the state matches the old state again, so typing in a large script
/// stays fast. A line that opens a comment or string changes the following lines; <see cref="Update"/> reports how far.
/// </summary>
public sealed class MySqlLineStates
{
    private List<MySqlLexState> _starts = [MySqlLexState.Normal];

    /// <summary>Gets the number of lines.</summary>
    public int Count => _starts.Count;

    /// <summary>Returns the state at the start of <paramref name="line"/>.</summary>
    public MySqlLexState StartOf(int line) => line >= 0 && line < _starts.Count ? _starts[line] : MySqlLexState.Normal;

    /// <summary>Reads all lines.</summary>
    public void Rebuild(int lineCount, Func<int, string> getLine)
    {
        if (getLine is null)
        {
            throw new ArgumentNullException(nameof(getLine));
        }

        var starts = new List<MySqlLexState>(Math.Max(1, lineCount));
        var state = MySqlLexState.Normal;
        for (var i = 0; i < Math.Max(1, lineCount); i++)
        {
            starts.Add(state);
            state = MySqlTokenizer.EndState(i < lineCount ? getLine(i) : string.Empty, state);
        }

        _starts = starts;
    }

    /// <summary>
    /// Updates the states after an edit. <paramref name="firstChangedLine"/> and <paramref name="lastChangedLine"/> are
    /// line numbers in the new text; <paramref name="lineCount"/> is the new number of lines.
    /// </summary>
    /// <returns>The last line whose start state was read again; at least <paramref name="lastChangedLine"/>.</returns>
    public int Update(int firstChangedLine, int lastChangedLine, int lineCount, Func<int, string> getLine)
    {
        if (getLine is null)
        {
            throw new ArgumentNullException(nameof(getLine));
        }

        var old = _starts;
        var delta = lineCount - old.Count;
        var first = Math.Max(0, Math.Min(firstChangedLine, Math.Min(old.Count, lineCount) - 1));
        var starts = new List<MySqlLexState>(lineCount);
        for (var i = 0; i < first; i++)
        {
            starts.Add(old[i]);
        }

        var state = first < old.Count ? old[first] : MySqlLexState.Normal;
        for (var i = first; i < lineCount; i++)
        {
            starts.Add(state);
            state = MySqlTokenizer.EndState(getLine(i), state);

            // Past the edited lines the old states are valid again once the state matches.
            var next = i + 1;
            var oldNext = next - delta;
            if (next > lastChangedLine && next < lineCount && oldNext >= 0 && oldNext < old.Count && old[oldNext] == state)
            {
                for (var j = oldNext; j < old.Count; j++)
                {
                    starts.Add(old[j]);
                }

                _starts = starts;
                return Math.Max(i, lastChangedLine);
            }
        }

        _starts = starts;
        return Math.Max(lineCount - 1, lastChangedLine);
    }
}
