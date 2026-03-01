using System.Collections.Generic;
using Pulse.Core.Modules;

namespace Pulse.Core.Boq
{
    /// <summary>
    /// Contract for a class that converts raw <see cref="ModuleData"/> into a
    /// flat list of <see cref="BoqItem"/> rows ready for display in the BOQ DataGrid.
    ///
    /// Each module (FireAlarm, HVAC, etc.) provides its own implementation that
    /// knows how to map module-specific entities to the generic BoqItem shape.
    /// </summary>
    public interface IBoqDataProvider
    {
        /// <summary>
        /// Module key this provider is responsible for (e.g. "FireAlarm").
        /// Must match <see cref="BoqSettings.ModuleKey"/> for settings to be applied correctly.
        /// </summary>
        string ModuleKey { get; }

        /// <summary>
        /// Convert the supplied <paramref name="data"/> into a flat list of BOQ rows.
        /// Should never return null — return an empty list if there are no items.
        /// </summary>
        IReadOnlyList<BoqItem> GetItems(ModuleData data);

        /// <summary>
        /// Returns all parameter keys present in the dataset so the column chooser
        /// can populate the "Available Parameters" list.
        ///
        /// The default implementation collects all keys from the <see cref="BoqItem.Parameters"/>
        /// dictionaries returned by <see cref="GetItems"/>.  Override for faster discovery.
        /// </summary>
        IReadOnlyList<string> DiscoverParameterKeys(ModuleData data);
    }
}
