using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.UI;
using Pulse.Core.Modules;
using Pulse.Core.Rules;
using Pulse.Revit.ExternalEvents;

namespace Pulse.Revit.Services
{
    /// <summary>
    /// Encapsulates element selection and temporary graphic overrides in Revit.
    /// Owns the SelectElement, TemporaryOverride, and ResetOverrides handlers.
    /// Extracted from MainViewModel so selection/highlight logic is reusable
    /// and testable in isolation.
    /// </summary>
    public class SelectionHighlightFacade
    {
        private readonly SelectElementHandler _selectHandler;
        private readonly ExternalEvent _selectEvent;

        private readonly TemporaryOverrideHandler _overrideHandler;
        private readonly ExternalEvent _overrideEvent;

        private readonly ResetOverridesHandler _resetHandler;
        private readonly ExternalEvent _resetEvent;

        public SelectionHighlightFacade()
        {
            _selectHandler = new SelectElementHandler();
            _selectEvent = ExternalEvent.Create(_selectHandler);

            _overrideHandler = new TemporaryOverrideHandler();
            _overrideEvent = ExternalEvent.Create(_overrideHandler);

            _resetHandler = new ResetOverridesHandler();
            _resetEvent = ExternalEvent.Create(_resetHandler);
        }

        /// <summary>
        /// Select an element in Revit and optionally zoom to include context neighbours.
        /// </summary>
        public void SelectElement(long elementId, IList<long> contextIds = null)
        {
            _selectHandler.ElementIdToSelect = elementId;
            _selectHandler.ContextIds = contextIds;
            _selectEvent.Raise();
        }

        /// <summary>
        /// Apply temporary color overrides to the specified elements.
        /// </summary>
        public void HighlightElements(IEnumerable<long> elementIds, byte r = 255, byte g = 100, byte b = 100)
        {
            _overrideHandler.ElementIds = elementIds?.ToList() ?? new List<long>();
            _overrideHandler.ColorR = r;
            _overrideHandler.ColorG = g;
            _overrideHandler.ColorB = b;
            _overrideEvent.Raise();
        }

        /// <summary>
        /// Highlight all elements referenced by rule results with Warning or Error severity.
        /// </summary>
        public void HighlightWarnings(ModuleData data)
        {
            if (data == null) return;

            var ids = data.RuleResults
                .Where(r => r.ElementId.HasValue && r.Severity >= Severity.Warning)
                .Select(r => r.ElementId.Value)
                .Distinct()
                .ToList();

            if (ids.Count == 0) return;

            HighlightElements(ids);
        }

        /// <summary>
        /// Reset all temporary graphic overrides.
        /// </summary>
        public void ResetOverrides()
        {
            _resetHandler.OverrideService = _overrideHandler.OverrideService;
            _resetEvent.Raise();
        }
    }
}
