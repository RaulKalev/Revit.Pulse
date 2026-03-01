using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Pulse.Core.Boq
{
    /// <summary>
    /// Evaluates <see cref="BoqCustomColumnDefinition"/> expressions against a
    /// <see cref="BoqItem"/> row.
    ///
    /// No scripting engine is used.  Only the three deterministic kinds defined in
    /// <see cref="CustomColumnKind"/> are supported.  The evaluator is stateless and
    /// thread-safe — create one instance and reuse it for the lifetime of a view.
    /// </summary>
    public static class BoqCustomColumnEvaluator
    {
        /// <summary>
        /// Compute the value for <paramref name="definition"/> using data from <paramref name="item"/>.
        /// Returns an empty string if computation fails for any reason.
        /// </summary>
        public static object Evaluate(BoqCustomColumnDefinition definition, BoqItem item)
        {
            if (definition == null || item == null) return string.Empty;
            if (definition.SourceKeys == null || definition.SourceKeys.Count == 0) return string.Empty;

            try
            {
                switch (definition.Kind)
                {
                    case CustomColumnKind.Concat:
                        return EvaluateConcat(definition, item);

                    case CustomColumnKind.Sum:
                        return EvaluateSum(definition, item);

                    case CustomColumnKind.JoinDelimited:
                        return EvaluateJoinDelimited(definition, item);

                    default:
                        return string.Empty;
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        // ─────────────────────────────────────────────────────────────────────

        private static string EvaluateConcat(BoqCustomColumnDefinition def, BoqItem item)
        {
            var parts = def.SourceKeys
                .Select(k => item.GetValue(k)?.ToString() ?? string.Empty);
            return string.Join(def.Delimiter ?? " ", parts);
        }

        private static double EvaluateSum(BoqCustomColumnDefinition def, BoqItem item)
        {
            double total = 0.0;
            foreach (var key in def.SourceKeys)
            {
                var raw = item.GetValue(key);
                if (raw == null) continue;

                string str = raw.ToString()?.Trim() ?? string.Empty;
                if (double.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out double d))
                    total += d;
            }
            return total;
        }

        private static string EvaluateJoinDelimited(BoqCustomColumnDefinition def, BoqItem item)
        {
            var parts = def.SourceKeys
                .Select(k => item.GetValue(k)?.ToString() ?? string.Empty)
                .Where(s => !string.IsNullOrEmpty(s));
            return string.Join(def.Delimiter ?? " ", parts);
        }
    }
}
