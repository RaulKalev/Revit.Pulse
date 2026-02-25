using System;
using System.Collections.Generic;
using Pulse.Core.Settings;

namespace Pulse.Core.Modules
{
    /// <summary>
    /// Owns the custom symbol library lifecycle (load / save / mutate)
    /// and orchestrates the symbol-mapping dialog data flow.
    ///
    /// Extracted from MainViewModel so the ViewModel only holds a reference
    /// and delegates the "Open Symbol Mapping" action.
    /// </summary>
    public sealed class SymbolMappingOrchestrator
    {
        private List<CustomSymbolDefinition> _symbolLibrary;

        /// <summary>Current in-memory symbol library (never null).</summary>
        public IReadOnlyList<CustomSymbolDefinition> Library => _symbolLibrary;

        /// <summary>
        /// Mutable reference for callers that need to pass the list to dialogs.
        /// </summary>
        internal List<CustomSymbolDefinition> MutableLibrary => _symbolLibrary;

        public SymbolMappingOrchestrator()
        {
            _symbolLibrary = CustomSymbolLibraryService.Load();
        }

        /// <summary>
        /// Add or replace a symbol definition in the library and persist to disk.
        /// </summary>
        public void UpsertSymbol(CustomSymbolDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            _symbolLibrary.RemoveAll(s => s.Id == definition.Id || s.Name == definition.Name);
            _symbolLibrary.Add(definition);
            CustomSymbolLibraryService.Save(_symbolLibrary);
        }

        /// <summary>
        /// Accept saved mappings from the mapping dialog and write them into
        /// the provided assignments store. Caller is responsible for persisting
        /// the assignments store to ES.
        /// </summary>
        public void ApplyMappings(
            IDictionary<string, string> mappings,
            TopologyAssignmentsStore store)
        {
            if (store == null) throw new ArgumentNullException(nameof(store));
            store.SymbolMappings = new Dictionary<string, string>(
                mappings ?? new Dictionary<string, string>(),
                StringComparer.OrdinalIgnoreCase);
        }
    }
}
