using System;
using System.Collections.Generic;
using Pulse.Core.Boq;

namespace Pulse.UI.Boq
{
    /// <summary>
    /// ViewModel wrapper for a single BOQ data row.
    ///
    /// Exposes a string indexer so DataGrid columns can bind via
    /// <c>Binding Path="[FieldKey]"</c> without any code-behind helper.
    /// Custom-column values are computed lazily via <see cref="BoqCustomColumnEvaluator"/>.
    /// </summary>
    public class BoqRowViewModel
    {
        private readonly BoqItem _item;
        private readonly IReadOnlyList<BoqCustomColumnDefinition> _customColumns;

        // Pre-computed cache for custom column values (key = ColumnKey, value = computed string)
        private readonly Dictionary<string, object> _customCache =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Number of source rows aggregated into this row when grouping is active.
        /// Always 1 in ungrouped mode.
        /// </summary>
        public int Count { get; set; } = 1;

        public BoqRowViewModel(BoqItem item, IReadOnlyList<BoqCustomColumnDefinition> customColumns)
        {
            _item          = item          ?? throw new ArgumentNullException(nameof(item));
            _customColumns = customColumns ?? Array.Empty<BoqCustomColumnDefinition>();
        }

        /// <summary>
        /// Indexer used by DataGrid column bindings: <c>Binding Path="[FieldKey]"</c>.
        /// Returns the display value for the given field key, or an empty string if not found.
        /// </summary>
        public object this[string key]
        {
            get
            {
                if (string.IsNullOrEmpty(key)) return string.Empty;

                // Reserved synthetic key for the aggregated count column
                if (string.Equals(key, "_Count", StringComparison.OrdinalIgnoreCase))
                    return Count;

                // 1. Check custom columns first (may shadow a parameter with the same name)
                if (_customCache.TryGetValue(key, out var cached)) return cached;

                foreach (var cd in _customColumns)
                {
                    if (string.Equals(cd.ColumnKey, key, StringComparison.OrdinalIgnoreCase))
                    {
                        var result = BoqCustomColumnEvaluator.Evaluate(cd, _item);
                        _customCache[key] = result;
                        return result;
                    }
                }

                // 2. Standard + discovered parameters via BoqItem
                return _item.GetValue(key) ?? string.Empty;
            }
        }

        // ── Convenience passthroughs used by XAML / sorting ──────────────────

        public long?  ElementId  => _item.ElementId;
        public string Category   => _item.Category;
        public string FamilyName => _item.FamilyName;
        public string TypeName   => _item.TypeName;
        public string Level      => _item.Level;
        public string Panel      => _item.Panel;
        public string Loop       => _item.Loop;

        public override string ToString() => _item.ToString();
    }
}
