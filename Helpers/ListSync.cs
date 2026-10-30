using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Controls.Primitives;

using AutoCommand.Agent.Core;
using AutoCommand.Contain.Core;
using AutoCommand.Forensics.Core;
using AutoCommand.NetWatch.Core;
using AutoCommand.Sitedoctor.Core;

namespace AutoCommand.Helpers
{
    /// <summary>
    /// Keyed in-place sync of a bound ObservableCollection against a freshly
    /// built list. Rows whose key and value are unchanged are not touched at
    /// all (WPF keeps their containers — no regeneration, no flicker, and
    /// selection/scroll survive by construction); changed rows are replaced
    /// individually; new rows are inserted and missing rows removed; order
    /// corrections go through Move, which reuses the existing containers.
    ///
    /// This replaces the full `ItemsSource = list` swap every refresh used
    /// to do — that regenerated every visual row on every tick and forced
    /// each view to hand-restore the user's selection afterwards.
    ///
    /// Requirements: keys are unique and stable across passes (a service
    /// key, a PID, a flow id); row items should implement value equality
    /// (Equals/GetHashCode) so an unchanged row is recognized as unchanged —
    /// otherwise every row is replaced every pass and this degrades to a
    /// per-row (still not per-list) refresh.
    /// </summary>
    public static class ListSync
    {
        public static void SyncByKey<T>(ObservableCollection<T> rows, IList<T> fresh,
                                        Func<T, string> keyOf, Selector selector = null)
        {
            string selectedKey = null;
            if (selector?.SelectedItem is T selected)
                selectedKey = keyOf(selected);

            var freshKeys = new HashSet<string>();
            foreach (var item in fresh) freshKeys.Add(keyOf(item));

            // drop rows that fell out of the fresh pass (backward: indexes stay valid)
            for (int i = rows.Count - 1; i >= 0; i--)
                if (!freshKeys.Contains(keyOf(rows[i]))) rows.RemoveAt(i);

            for (int i = 0; i < fresh.Count; i++)
            {
                var key = keyOf(fresh[i]);
                int ix = FindIndex(rows, key, keyOf);
                if (ix < 0)
                {
                    rows.Insert(i, fresh[i]);
                }
                else
                {
                    // value-unequal → new data for the same row: replace in place
                    if (!EqualityComparer<T>.Default.Equals(rows[ix], fresh[i])) rows[ix] = fresh[i];
                    if (ix != i) rows.Move(ix, i);
                }
            }

            // selection: unchanged rows kept their instance so WPF held the
            // selection through the Move; a replaced selected row needs the
            // new instance selected in its place
            if (selectedKey != null && selector?.SelectedItem is T now &&
                keyOf(now) != selectedKey)
            {
                int ix = FindIndex(rows, selectedKey, keyOf);
                if (ix >= 0) selector.SelectedItem = rows[ix];
            }
        }

        private static int FindIndex<T>(IList<T> rows, string key, Func<T, string> keyOf)
        {
            for (int i = 0; i < rows.Count; i++)
                if (string.Equals(keyOf(rows[i]), key, StringComparison.Ordinal)) return i;
            return -1;
        }
    }
}
