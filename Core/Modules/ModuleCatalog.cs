using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Pulse.Core.Logging;

namespace Pulse.Core.Modules
{
    /// <summary>
    /// Discovers and instantiates <see cref="IModuleDefinition"/> implementations
    /// using assembly reflection. Falls back to a manual registration list if
    /// discovery fails or yields an empty set.
    ///
    /// Discovery rules:
    /// 1. Scan the provided assemblies for non-abstract classes implementing IModuleDefinition.
    /// 2. Instantiate each via parameterless constructor.
    /// 3. Sort deterministically by <see cref="IModuleDefinition.DisplayName"/>.
    /// 4. Validate discovered set against an expected ModuleId list (if provided).
    /// 5. If the discovered set is empty or does not match expectations, fall back
    ///    to the manual modules supplied in the constructor.
    /// </summary>
    public class ModuleCatalog
    {
        private readonly ILogger _logger;
        private readonly List<IModuleDefinition> _fallbackModules;
        private readonly List<IModuleDefinition> _discovered = new List<IModuleDefinition>();

        /// <summary>
        /// All modules available after <see cref="Discover"/> (or the fallback list).
        /// </summary>
        public IReadOnlyList<IModuleDefinition> Modules => _discovered;

        /// <summary>
        /// True if the last <see cref="Discover"/> call used the fallback path.
        /// </summary>
        public bool UsedFallback { get; private set; }

        /// <summary>
        /// Create a catalog with a set of manually-registered fallback modules.
        /// </summary>
        /// <param name="fallbackModules">Modules to use if reflection fails.</param>
        /// <param name="logger">Optional logger.</param>
        public ModuleCatalog(IEnumerable<IModuleDefinition> fallbackModules, ILogger logger = null)
        {
            _fallbackModules = fallbackModules?.ToList() ?? new List<IModuleDefinition>();
            _logger = logger ?? new DebugLogger("Pulse.ModuleCatalog");
        }

        /// <summary>
        /// Scan the given assemblies for IModuleDefinition implementations.
        /// If <paramref name="expectedModuleIds"/> is non-null, validates that
        /// every expected ID was found. Falls back to manual list on any failure.
        /// </summary>
        /// <param name="assemblies">Assemblies to scan. If null/empty, scans the entry assembly.</param>
        /// <param name="expectedModuleIds">Optional set of ModuleIds that must be present.</param>
        /// <returns>This catalog (fluent).</returns>
        public ModuleCatalog Discover(
            IEnumerable<Assembly> assemblies = null,
            IEnumerable<string> expectedModuleIds = null)
        {
            _discovered.Clear();
            UsedFallback = false;

            try
            {
                var asmList = assemblies?.ToList();
                if (asmList == null || asmList.Count == 0)
                {
                    asmList = new List<Assembly> { Assembly.GetExecutingAssembly() };

                    // Also include the entry assembly if it differs
                    var entry = Assembly.GetEntryAssembly();
                    if (entry != null && entry != asmList[0])
                        asmList.Add(entry);
                }

                var found = new List<IModuleDefinition>();

                foreach (var asm in asmList)
                {
                    try
                    {
                        ScanAssembly(asm, found);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to scan assembly '{asm.GetName().Name}': {ex.Message}");
                    }
                }

                if (found.Count == 0)
                {
                    _logger.Warning("Reflection discovery found 0 modules. Falling back to manual registration.");
                    ApplyFallback();
                    return this;
                }

                // Deterministic ordering by DisplayName
                found.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));

                // Validate expected set if provided
                if (expectedModuleIds != null)
                {
                    var expectedSet = new HashSet<string>(expectedModuleIds, StringComparer.OrdinalIgnoreCase);
                    var foundIds = new HashSet<string>(found.Select(m => m.ModuleId), StringComparer.OrdinalIgnoreCase);

                    foreach (string expected in expectedSet)
                    {
                        if (!foundIds.Contains(expected))
                        {
                            _logger.Warning($"Expected module '{expected}' not found by reflection. Falling back.");
                            ApplyFallback();
                            return this;
                        }
                    }
                }

                _discovered.AddRange(found);
                _logger.Info($"Module discovery found {_discovered.Count} module(s): {string.Join(", ", _discovered.Select(m => m.ModuleId))}");
            }
            catch (Exception ex)
            {
                _logger.Error("Module discovery failed. Falling back to manual registration.", ex);
                ApplyFallback();
            }

            return this;
        }

        private void ScanAssembly(Assembly asm, List<IModuleDefinition> found)
        {
            var moduleType = typeof(IModuleDefinition);

            foreach (var type in asm.GetTypes())
            {
                if (type.IsAbstract || type.IsInterface) continue;
                if (!moduleType.IsAssignableFrom(type)) continue;

                // Require parameterless constructor
                var ctor = type.GetConstructor(Type.EmptyTypes);
                if (ctor == null)
                {
                    _logger.Warning($"Module type '{type.FullName}' has no parameterless constructor; skipping.");
                    continue;
                }

                try
                {
                    var instance = (IModuleDefinition)Activator.CreateInstance(type);

                    // De-duplicate by ModuleId
                    if (found.Any(m => string.Equals(m.ModuleId, instance.ModuleId, StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.Warning($"Duplicate module '{instance.ModuleId}' in assembly '{asm.GetName().Name}'; skipping.");
                        continue;
                    }

                    found.Add(instance);
                    _logger.Info($"Discovered module '{instance.ModuleId}' ({type.FullName}).");
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to instantiate module '{type.FullName}'.", ex);
                }
            }
        }

        private void ApplyFallback()
        {
            UsedFallback = true;
            _discovered.Clear();
            _discovered.AddRange(_fallbackModules);
            _logger.Info($"Using fallback modules: {string.Join(", ", _discovered.Select(m => m.ModuleId))}");
        }
    }
}
