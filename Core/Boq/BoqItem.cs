using System;
using System.Collections.Generic;

namespace Pulse.Core.Boq
{
    /// <summary>
    /// Generic row model for the Bill of Quantities view.
    /// Each instance represents one addressable device (or other module element).
    ///
    /// Standard fields (Category, FamilyName, TypeName, Level, Panel, Loop) are
    /// surfaced as first-class properties for type-safe access.  Every additional
    /// Revit parameter is stored in the <see cref="Parameters"/> dictionary so that
    /// the UI can discover and display arbitrary fields without code changes.
    /// </summary>
    public class BoqItem
    {
        // ── Identity ─────────────────────────────────────────────────────────

        /// <summary>Revit ElementId value, or null if not linked to a model element.</summary>
        public long? ElementId { get; set; }

        // ── Standard columns ─────────────────────────────────────────────────

        /// <summary>Revit category name (e.g. "Fire Alarm Devices").</summary>
        public string Category { get; set; } = string.Empty;

        /// <summary>Revit family name.</summary>
        public string FamilyName { get; set; } = string.Empty;

        /// <summary>Revit type name.</summary>
        public string TypeName { get; set; } = string.Empty;

        /// <summary>Revit level name the device is hosted on.</summary>
        public string Level { get; set; } = string.Empty;

        /// <summary>Assigned control panel name (logical topology).</summary>
        public string Panel { get; set; } = string.Empty;

        /// <summary>Assigned loop label (logical topology).</summary>
        public string Loop { get; set; } = string.Empty;

        // ── Generic parameters ────────────────────────────────────────────────

        /// <summary>
        /// All additional Revit parameters extracted for this element.
        /// Keys are Revit parameter names (raw, not logical keys).
        /// Values are always strings — numeric parameters are stored as string representations.
        /// </summary>
        public Dictionary<string, object> Parameters { get; } =
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Retrieve a parameter value by key, checking standard fields first
        /// and falling back to the <see cref="Parameters"/> dictionary.
        /// Returns null if not found.
        /// </summary>
        public object GetValue(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;

            // Standard field shortcuts (case-insensitive)
            switch (key.ToLowerInvariant())
            {
                case "category":    return Category;
                case "familyname":  return FamilyName;
                case "typename":    return TypeName;
                case "level":       return Level;
                case "panel":       return Panel;
                case "loop":        return Loop;
                case "elementid":   return ElementId?.ToString();
            }

            return Parameters.TryGetValue(key, out var val) ? val : null;
        }

        public override string ToString() =>
            $"{FamilyName} – {TypeName} | {Level} | {Panel} / {Loop}";
    }
}
