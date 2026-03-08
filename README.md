# Pulse

**Modern MEP UX platform for addressable systems.**

Pulse is a modular Revit plugin that provides a system control center for MEP addressable systems. It replaces element-first workflows with system-first topology visualization, real-time validation, and progressive disclosure of detail.

> **User manual →** see [MANUAL.md](MANUAL.md) for setup, features, and formula reference.

> **Architecture deep-dive →** see [ARCHITECTURE.md](ARCHITECTURE.md) for the runtime
> pipeline, module discovery, capabilities pattern, storage hardening strategy,
> and diagram scene graph.

---

## Architecture

Pulse is organized as a single Revit addin project with namespace-separated modules:

```
Pulse/
  Core/           -- Platform abstractions (no Revit dependency)
  Revit/          -- Revit API layer (collectors, ExternalEvents, Extensible Storage)
  UI/             -- WPF views and MVVM ViewModels
  Modules/        -- System-specific modules (Fire Alarm, etc.)
```

### Pulse.Core

Framework-agnostic contracts and models:

| Namespace | Purpose |
|-----------|---------|
| `Core.Graph` | `Node` and `Edge` — topology graph primitives |
| `Core.Graph.Canvas` | `DiagramScene`, `CanvasGraphModel`, `CanvasGraphBuilder` — diagram and topology scene graphs |
| `Core.SystemModel` | `ISystemEntity`, `Panel`, `Loop`, `Zone`, `AddressableDevice` |
| `Core.Rules` | `IRule`, `RuleResult`, `Severity` — validation engine |
| `Core.Modules` | `IModuleDefinition`, `PulseAppController`, `ModuleCatalog`, `ModuleCapabilities`, `TopologyAssignmentsService`, `SymbolMappingOrchestrator`; `ModuleData` — pipeline container with typed `Payload` slot (`GetPayload<T>()`) |
| `Core.Modules.Metrics` | `SystemMetricsCalculator`, `CapacityMetrics`, `HealthIssueItem`, `DistributionGroup`, `CablingMetrics`, `BatteryMetrics` — System Intelligence metrics engine; `BatteryMetrics` carries EN 54-4 battery sizing results including `RecommendedCapacitySummary` (standard-size even-count recommendation with voltage) and `FormulaBreakdown` (four-step equation trace) |
| `Core.Settings` | `ModuleSettings`, `ParameterMapping`, `TopologyAssignmentsStore`, `DeviceConfigStore` (+ `ModuleConfigBlobs` opaque per-module hardware blobs), `IModuleDeviceConfig`, `CustomSymbolDefinition`, `LevelVisibilitySettings`, `DiagramCanvasSettings`, `UiStateService`; `ControlPanelConfig.MaxAddresses` for per-panel address cap override |
| `Core.Logging` | `ILogger` abstraction |

### Pulse.Revit

Revit API integration:

| Component | Purpose |
|-----------|---------|
| `RefreshPipeline` | Owns `CollectDevicesHandler` + `ExternalEvent` for the refresh cycle |
| `SelectionHighlightFacade` | Owns select, override, and reset handlers |
| `StorageFacade` | Read/write helpers for ES and JSON persistence |
| `RevitCollectorService` | Implements `ICollectorContext` — extracts elements, parameters, and routed wire lengths |
| `SelectionService` | Selects elements in the Revit model |
| `TemporaryOverrideService` | Applies/resets graphic overrides to highlight elements |
| `ExtensibleStorageService` | Reads/writes module settings to Revit Extensible Storage (single-schema, Public access) |
| `ExternalEvent handlers` | `CollectDevicesHandler`, `SelectElementHandler`, `TemporaryOverrideHandler`, `ResetOverridesHandler`, `SaveSettingsHandler`, `SaveDiagramSettingsHandler`, `SaveTopologyAssignmentsHandler`, `WriteParameterHandler`, `DrawWireRoutingHandler` |
| `DrawWireRoutingHandler` | Draws or clears Manhattan-routed model lines in Revit for a single loop; each loop gets its own line-style subcategory so loops can be toggled independently |

All Revit write operations use `ExternalEvent` to ensure they run on the Revit API thread.

### Pulse.UI

WPF user interface using Material Design:

| Component | Purpose |
|-----------|---------|
| `MainWindow` | Borderless window shell with title bar, resize grips, theme support |
| `TopologyView` | TreeView control — Panel → Loop → Device hierarchy; SubCircuit cards with wire assignment, routing toggle, add/delete device controls |
| `InspectorPanel` | Entity details, properties (including editable V-Drop limit for SubCircuits), warnings, capacity gauges |
| `DiagramPanel` | Schematic wiring diagram canvas |
| `SystemMetricsPanel` | 6-section System Intelligence Dashboard (capacity gauges, health status, device distribution, cabling & spatial, quick actions) |
| `StatusStrip` | Bottom bar with status text, device/warning/error counts |
| `MainViewModel` | Root ViewModel — holds service refs, wires callbacks, exposes bindings/commands |
| `TopologyViewModel` | Topology tree + internal `CanvasGraphModel` projection |
| `DiagramViewModel` | Diagram canvas state: levels, panels, loops, flip/wire/rank assignments |
| `InspectorViewModel` | Selected entity details and capacity data |
| `MetricsPanelViewModel` | System Intelligence Dashboard VM — capacity gauges, health issues (including capacity overload rows), device distribution, cabling info, AI prompt export; SubCircuit (NAC) circuit metrics with Normal Load, Alarm Load, V-Drop, and Remaining Voltage gauges; PSU host-element path: combined NAC load from all hosted SubCircuits, gauge ceiling from PSU config `OutputCurrentA`, and full Battery/PSU section with EN 54-4 battery sizing, standard-size even-count recommendation, per-unit voltage, and formula breakdown |
| `AiPromptInfoWindow` | Themed borderless popup shown after the AI system-check prompt is copied to clipboard |
| `MetricsConverters` | `CapacityStatusToBrushConverter`, `HealthStatusToBrushConverter`, `CountToVisibilityConverter` |
| `SettingsViewModel` | Parameter mapping editor |
| `DeviceConfigViewModel` | Panels / Loop Modules / Wires / Paper Sizes configurator |
| `SymbolMappingViewModel` | Device-type to custom symbol mapping |
| `SymbolDesignerViewModel` | Custom symbol drawing canvas |
| `DiagramFeatureService` | Diagram ↔ topology wire-assignment orchestration |
| `BoqWindow` | Modeless Bill of Quantities window with aggregated grouping, settings panel, and CSV export |
| `BoqWindowViewModel` | BOQ root VM — column management (visible-only filter, default-visible structural keys, hide command), aggregated grouping, sorting, CSV export (`ExportCsvCommand`), settings persistence |
| `BoqRowViewModel` | Single BOQ row wrapper; exposes string indexer `[FieldKey]` for DataGrid; for pre-aggregated rows (cables, batteries) `_Count` returns the `Quantity` parameter (e.g. `125.3 m`) instead of the raw integer count |
| `BoqColumnViewModel` | Column state VM — `IsVisible`, `DisplayOrder`, `Header`, `FieldKey`, `IsCustom`, `IsDiscovered` |

### Pulse.Modules.FireAlarm

| Component | Purpose |
|-----------|---------|
| `FireAlarmModuleDefinition` | Registers the module and provides factory methods; implements `IProvidesDeviceConfig` |
| `FireAlarmDeviceConfig` | `IModuleDeviceConfig` wrapper — holds `ControlPanels`, `LoopModules`, `Wires`, and `PsuUnits` libraries; stored as a blob in `DeviceConfigStore.ModuleConfigBlobs["FireAlarm"]` |
| `FireAlarmCollector` | Collects fire alarm devices from configured Revit categories |
| `FireAlarmTopologyBuilder` | Builds Panel → Loop → Device graph; attaches cable length (routed or Manhattan) to loop and SubCircuit nodes |
| `CableLengthCalculator` | Calculates per-loop cable length using Manhattan (right-angle) routing in Revit internal units, returns metres |
| `FireAlarmRulePack` | Validates data with 5 rules |
| `FireAlarmParameterKeys` | Centralized logical parameter key constants |
| `FireAlarmBoqDataProvider` | Implements `IBoqDataProvider` — maps `ModuleData` to `BoqItem` rows across five groups: (1) fire alarm devices with Revit family/type/level/loop/panel/address; (2) control panels per assigned config; (3) loop modules per assigned config; (4) cables — one row per wire type with total length summed across assigned loops; (5) batteries — one row per FACP panel and one per field PSU group |

### Pulse.Modules.Lighting

Addressable lighting systems (DALI and future protocols):

| Component | Purpose |
|-----------|---------|
| `LightingModuleDefinition` | Registers the module; implements `IProvidesDeviceConfig`; capabilities: `CapacityGauges \| ConfigAssignment` |
| `LightingDeviceConfig` | `IModuleDeviceConfig` wrapper — holds `LightingControllerConfig` entries (default: DALI, MaxAddressesPerLine=64, MaxLines=4, MaxMaPerLine=250); stored in `DeviceConfigStore.ModuleConfigBlobs["Lighting"]` |
| `LightingCollector` | Groups elements by controller → line → device; resolves controller element IDs; captures elevation and level |
| `LightingTopologyBuilder` | Builds Controller → Line → Device graph; writes `_ElevationFt`, `_LoopId`, controller/line/address/current/system node properties |
| `LightingRulePack` | Validates data with 6 rules |
| `LightingParameterKeys` | Centralized logical parameter key constants: `Controller`, `Line`, `Address`, `DeviceType`, `CurrentDraw`, `DeviceId`, `SystemType` |
| `LightingBoqDataProvider` | Implements `IBoqDataProvider` — two row groups: devices + controllers |

---

## Features

### Bill of Quantities (BOQ) Window

The **BOQ window** is a modeless panel opened from the main toolbar that shows all devices in a configurable DataGrid.

**Row groups (Fire Alarm module):**
- **Fire Alarm Devices** — one row per device with Revit category, family, type, level, panel, loop, address, and all mapped parameters
- **Control Panels** — one row per assigned FACP config with capacity parameters (MaxAddresses, MaxLoopCount, MaxMaPerLoop)
- **Loop Modules** — one row per assigned loop module config with AddressesPerLoop and MaxMaPerLoop
- **Cables** — one row per wire type; the Count column shows total length in metres (`125.3 m`); wire properties (CoreCount, CoreSizeMm2, FireResistance, Color) are additional parameters
- **Batteries — Control Panel / Field PSU** — one row per panel or PSU group; shows Quantity (batteries needed), RequiredCapacity_Ah, TotalCapacity_Ah, BatteryUnitAh

**Toolbar buttons:**
- **Export CSV** — exports the current view (visible columns, active grouping/sorting) to a UTF-8 CSV file; filename auto-includes date/time
- **Export / Import** — export or import column settings as JSON

**Column management (settings panel, right-side drawer):**
- The COLUMNS list shows **all currently visible columns** (standard and discovered)
- **+** (parameter picker) — add parameters from Revit or from the discovered key list to the visible columns
- **−** (remove) — hides the selected column; it returns to the available list in the picker
- Re-order columns with Move Up / Move Down
- Add custom formula columns via the Custom Column editor
- Structural columns (Quantity, CableLength_m, capacity params) are **auto-shown** on first load without needing the picker
- Column state persisted to Revit Extensible Storage per document

**Grouping:**
- Add one or more grouping rules (field key + priority)
- When active, rows are **aggregated in-memory** — one flat row per unique key combination
- A **Count** column auto-appears at the far right showing how many devices collapsed into each group
- Removing all grouping rules restores individual device rows and hides the Count column
- No collapsible group headers — grouping produces a true BOQ-style summary table

**Sorting:**
- Independent sort rules with ascending/descending direction and priority order
- Sort rules persist alongside grouping rules and survive refresh and restart

**Field keys in grouping/sorting dropdowns show only currently visible columns** and update live as visibility changes.

**Family and Type columns:**
- **Family** = Revit family name (`FamilyInstance.Symbol.Family.Name`)
- **Type** = Revit type name (`element.Name`)
- Both injected as built-in `_FamilyName` / `_Name` keys by `RevitCollectorService`

**Settings lifecycle:**
- Settings auto-save on window close
- Settings read fresh from ES on each re-open so changes survive both restart and same-session re-open

---

### Cable Length Calculation

The Fire Alarm module determines cable length for each loop and SubCircuit using the following priority:

1. **Routed wire lines (primary)** — when the wire routing feature has been used to draw `"Pulse Wire – "` model lines for a loop or SubCircuit, `RevitCollectorService.GetRoutedWireLengths()` reads those model lines back at refresh time and sums their curve lengths (converted from Revit internal feet to metres). No user configuration required — the plugin reads its own drawn lines automatically.
2. **Manhattan estimate (fallback)** — if no routed lines exist, `CableLengthCalculator` computes an estimate via right-angle routing: Panel origin → devices sorted by address → back to Panel; distance = |Δx| + |Δy| + |Δz|.

The result is displayed in the loop or SubCircuit card as `XX.X m` and used by the V-Drop calculator.

### NAC SubCircuit Management

NAC (Notification Appliance Circuit) outputs on PSU/sounder modules can be modelled as **SubCircuits** — named groupings of sounder devices hanging off a specific PSU output element.

**Creating and editing SubCircuits:**
- Expand a PSU device node in the topology tree to reveal its output ports
- Click **+** on a port to create a new SubCircuit; give it a name
- Add sounder devices via the **+** button on the SubCircuit card (pick unassigned or select directly in Revit)
- Remove a device with the **−** button; delete the whole SubCircuit with the trash icon
- **Wire type** is selectable per SubCircuit from the configured wire library
- **Name** is editable in the inspector panel (double-click the title)
- **V-Drop limit %** is editable in the inspector PROPERTIES section (double-click the value); Enter or click away to confirm, Escape to cancel

**Persistence:** each SubCircuit (including its device list, wire assignment, and V-Drop limit %) is stored as an opaque JSON blob in `TopologyAssignmentsStore.ModuleBlobs["FireAlarm.SubCircuits"]`, managed entirely by `FireAlarmSubCircuitService` — serialised to Revit Extensible Storage and reloaded transparently on document open. Core never inspects the value. Documents saved before Gap-6 (old `SubCircuits` dict) and between Gap-6 and Gap-2 (old `SubCircuitsJson` field) are migrated automatically via JSON shims on first load.

---

### NAC Circuit Metrics (CIRCUIT METRICS)

When a SubCircuit node is selected, the **CIRCUIT METRICS** section of the System Intelligence Dashboard shows four live gauges:

| Gauge | Description |
|-------|-------------|
| Normal Load | Sum of device **standby** current draws — pure device current only (EOL supervisory not included) |
| Alarm Load | Sum of device **alarm** current draws |
| V-Drop | Calculated voltage drop along the circuit vs. configurable limit |
| Remaining V | Nominal supply voltage minus calculated V-Drop |

When a **PSU/Output-Module host element** is selected (the device that owns one or more SubCircuit outputs), the Dashboard switches to a host-level view:
- **Header** shows the circuit count and total device count across all hosted SubCircuits (e.g. `"NAC Host · 2 circuits · 6 devices"`).
- **Normal Load / Alarm Load** gauges aggregate mA from all hosted SubCircuits. The gauge ceiling (`ScMaMax`) comes from, in priority order: (1) the `OutputCurrentMaxMa` Revit parameter on the host element, (2) `OutputCurrentA` from the assigned PSU config, (3) `max(Normal, Alarm) × 1.25`.
- The **Inspector** shows individual `"NAC load (normal)"` and `"NAC load (alarm)"` summaries for the host.

**Current draw sourcing:**
- **Normal Load** and **Alarm Load** read per-device mA values from the `CurrentDraw` parameter (a single column split into standby/alarm by convention).
- **PSU output capacity** (`ScMaMax`) is sourced from the `_OutputCurrentMaxMa` raw device parameter on the PSU host element (set as `OutputCurrentMaxMa` in the topology node). When that parameter is not mapped, the gauge ceiling falls back to `max(Normal, Alarm) × 1.25` so the arc fills to ~80 % at peak load.
- When the PSU capacity is unknown (ceiling = 0), the gauge shows `"425 mA"` without a denominator or percent — no misleading `"425 / 0 mA 0%"` text.

**V-Drop calculation:**
- Formula: `V = I × (2ρL / A)` — copper resistivity ρ = 0.0175 Ω·mm²/m
- Wire parameters (cross-section area, resistance per metre) come from the configured wire type
- The gauge maximum is `NominalVoltage × (VDropLimitPct / 100)` — scaled to the PSU supply voltage
- `NominalVoltage` is read from the PSU element via the `NominalVoltage` parameter mapping
- `VDropLimitPct` defaults to 16.7 % (≈ 4 V on a 24 V NAC) and is editable per SubCircuit in the inspector

---

### PSU Host Element — Battery / PSU Section

When a PSU host element is selected and a **PSU Config** is assigned to its SubCircuits, the **BATTERY / PSU** section of the dashboard appears with:

| Row | Content |
|-----|---------|
| Capacity | `N× VV/X.X Ah = Y.Y Ah  (req. Z.ZZ Ah)` — recommended battery count, per-unit voltage and capacity, total installed Ah, and required Ah |
| Standby load | Total standby current across all hosted SubCircuit devices |
| Alarm load | Total alarm current across all hosted SubCircuit devices |
| PSU output | Alarm current vs. PSU rated output with utilisation % |
| Formula | Four-step EN 54-4 equation trace (see below) |

**Formula breakdown displayed in the dashboard:**
```
C = (Is/1000 × ts + Ia/1000 × ta/60) × f
  = (510/1000 × 24 + 270/1000 × 30/60) × 1.25
  = (12.240 + 0.135) × 1.25
  = 15.47 Ah
```

**Battery recommendation algorithm:** iterates even counts (2, 4, 6 … 40) × standard VRLA/AGM sizes (1.2 → 100 Ah). The first `(count, size)` pair where `count × size ≥ reqAh` wins, guaranteeing an even battery count (minimum 2 per EN 54-4). Per-unit voltage is derived as `VoltageV / 2` (e.g. 24 V system → 12 V per unit).

**Standard battery sizes (Ah):** 1.2, 2.1, 2.3, 3.2, 4.5, 7.0, 7.2, 12, 17, 18, 24, 26, 33, 38, 40, 45, 55, 65, 80, 100.

---

### Per-Loop / Per-SubCircuit 3D Wire Routing Visualisation

Each loop and SubCircuit card contains a **wire routing toggle button**. Clicking it draws orthogonal model lines in the active Revit 3D view:

- Each loop or SubCircuit gets its own line-style subcategory (`Pulse Wire – {panel} - {loop}` or `Pulse Wire – {device} - {subcircuit}`) so they can be toggled independently
- Lines follow a Manhattan-routed path through the assigned devices
- **Cable length is read back automatically** — on the next Refresh, `RevitCollectorService` reads these model lines and uses their total length as `CableLength` for that node, replacing the Manhattan estimate with the actual routed measurement
- **State is persisted** to Revit Extensible Storage (`LoopWireRoutingVisible` in `TopologyAssignmentsStore`) — re-opening the plugin restores all visible routing automatically
- The toggle icon turns red when wires are visible; clicking again clears only that loop's or SubCircuit's lines

### System Intelligence Dashboard

The **System Intelligence Dashboard** (`SystemMetricsPanel`) provides a live overview of every selected panel, driven by `SystemMetricsCalculator` and `MetricsPanelViewModel`.

**Six sections (scrollable body + sticky Quick Actions footer):**

| Section | Content |
|---------|---------|
| Capacity | Address and mA gauges (used / max, colour-coded by threshold) |
| Health Status | Rule violations + capacity overload rows + loop balance warnings (≥ 40 % spread, ≥ 70 % loaded loop), each with count and highlight command |
| Device Distribution | Breakdown of device types with percentage bars |
| Cabling & Spatial | Per-loop cable lengths, numerically sorted; total project cabling |
| AI System Check | Copies a structured prompt to the clipboard; once-per-session info popup |
| Quick Actions (pinned) | Refresh, Highlight Warnings, Copy Report, Open Settings |

**Thresholds** (`MetricsThresholds`): Warning at 70 %, Critical at 85 %, loop imbalance spread at 40 %.

**`ControlPanelConfig.MaxAddresses`**: when non-zero, overrides the automatic `AddressesPerLoop × loopCount` calculation for the panel capacity gauge and AI prompt. Set to `0` (default) for automatic behaviour.

**Behaviour details:**
- Collapsed header shows bold centred text with the icon hidden
- Panel enforces a minimum expanded height of 300 px
- Cabling loops are sorted numerically (e.g. L2 < L10)
- Capacity overload conditions surface as Warning/Error rows in the Health Status section
- Loop balance warnings surface when one loop is ≥ 70 % full and the spread across loops on the same panel is ≥ 40 %; affected devices are highlightable in Revit and the imbalance is included in the AI System Check prompt

---

## How to Add a Module

1. Create a new folder under `Modules/` (e.g., `Modules/EmergencyLighting/`).
2. Implement `IModuleDefinition` with:
   - `ModuleId`, `DisplayName`, `Description`, `Version`
   - `Capabilities` — declare supported `ModuleCapabilities` flags
   - `GetDefaultSettings()` returning default categories and parameter mappings
   - Factory methods for collector, topology builder, and rule pack
3. Optionally implement feature interfaces:
   - `IProvidesWiringFeatures` — wire parameter key for Revit write-back
   - `IProvidesSymbolMapping` — custom device-type symbol library
   - `IProvidesDeviceConfig` — hardware device config (panels, circuits, wires); implement `GetDefaultDeviceConfig()` returning your `IModuleDeviceConfig` subclass; load/save via `DeviceConfigService.LoadModuleConfig<T>()` / `SaveModuleConfig()` — stored in `DeviceConfigStore.ModuleConfigBlobs[ModuleId]`
4. Implement `IModuleCollector` to extract elements from Revit via `ICollectorContext`.
5. Implement `ITopologyBuilder` to build the graph from collected entities.
6. Implement `IRulePack` with validation rules implementing `IRule`.
7. `ModuleCatalog.Discover()` finds your module automatically via reflection — no registration needed.

No Core modifications are needed. The module contract is fully decoupled.

---

## Parameter Mapping System

Parameters are never hardcoded in logic. The flow is:

1. **Logical keys** are defined as constants (e.g., `FireAlarmParameterKeys.Panel`).
2. **`ModuleSettings.ParameterMappings`** maps logical keys to Revit parameter names.
3. The **collector** resolves Revit parameter names from settings before extracting values.
4. Users can customize mappings via settings (stored in Extensible Storage).

**Default Fire Alarm parameter mappings:**

| Logical Key | Default Revit Parameter | Required |
|-------------|------------------------|----------|
| Panel | Panel | Yes |
| Loop | Loop | Yes |
| Address | Aadress | Yes |
| DeviceType | Device type | No |
| CurrentDraw | Current draw | No |
| DeviceId | id | No |
| PanelConfig | FA_Panel_Config | No |
| LoopModuleConfig | FA_Loop_Config | No |
| Wire | FA_Wire | No |
| PanelElementCategory | Electrical Equipment | No |
| PanelElementNameParam | Mark | No |
| CircuitElementId | FA_Circuit_ElementId | No |
| NominalVoltage | Nominal voltage | No |

> **Note:** Cable length is derived automatically from `"Pulse Wire – "` model lines drawn by the routing feature — no parameter mapping is needed for cable routes.

**Default Lighting parameter mappings:**

| Logical Key | Default Revit Parameter | Required |
|-------------|------------------------|----------|
| Controller | DALI_Controller | Yes |
| Line | DALI_Line | Yes |
| Address | DALI_Address | No |
| DeviceType | Device type | No |
| CurrentDraw | Current draw (mA) | No |
| DeviceId | id | No |
| SystemType | System type | No |

---

## Extensible Storage Schema

Settings are persisted in the Revit document via Extensible Storage.
Three schemas are used, each with a **single GUID** and `AccessLevel.Public`
for both read and write:

| Schema | GUID (abridged) | Fields |
|--------|-----------------|--------|
| Module Settings | `A7E3B1C2-…-0E1F2A3B4C5E` | `SchemaVersion` (int) + `SettingsJson` (string) |
| Diagram Settings | `B8F4C2D3-…-1F2A3B4C5D6E` | `DiagramSchemaVersion` (int) + `DiagramSettingsJson` (string) |
| Topology Assignments | `C9D5E3F4-…-2A3B4C5D6E7F` | `TopologyAssignmentsVersion` (int) + `TopologyAssignmentsJson` (string) |
| BOQ Settings | `D0E6F4A5-…-3B4C5D6E7F8A` | `BoqSettingsVersion` (int) + `BoqSettingsJson` (string) |

The `TopologyAssignmentsStore` JSON blob includes:

| Key | Type | Purpose |
|-----|------|---------|
| `PanelAssignments` | `Dict<string, string>` | Panel → config name |
| `LoopAssignments` | `Dict<string, string>` | Loop → module config name |
| `LoopWireAssignments` | `Dict<string, string>` | `panel::loop` → wire type name |
| `LoopFlipStates` | `Dict<string, bool>` | Diagram flip state per loop |
| `LoopWireRoutingVisible` | `Dict<string, bool>` | `panel::loop` → wire routing lines visible |
| `LoopExtraLines` / `LoopRankOverrides` | `Dict<string, *>` | Diagram layout overrides |
| `LevelElevationOffsets` | `Dict<string, double>` | Per-level elevation adjustments |
| `SymbolMappings` | `Dict<string, string>` | Device type → custom symbol |
| `SubCircuits` | `Dict<string, SubCircuit>` | SubCircuit definitions; each entry stores `Id`, `HostElementId`, `Name`, `DeviceElementIds`, `WireTypeKey`, and `VDropLimitPct` |

The `BoqSettings` JSON blob includes:

| Key | Type | Purpose |
|-----|------|---------|
| `ModuleKey` | `string` | Identifies the owning module |
| `VisibleColumns` | `List<BoqColumnDefinition>` | Visibility, header, display order for every known column |
| `ColumnOrder` | `List<string>` | Ordered field keys controlling DataGrid column order |
| `GroupingRules` | `List<BoqGroupingRule>` | Field key + priority; drives in-memory aggregation |
| `SortingRules` | `List<BoqSortingRule>` | Field key + direction + priority |
| `CustomColumns` | `List<BoqCustomColumnDefinition>` | User-defined formula columns |

All data resides on a single `DataStorage` element named `"PulseSettings"`.

> See [ARCHITECTURE.md](ARCHITECTURE.md#4-extensible-storage-strategy) for full
> details including the future hardening roadmap.

### Safety guarantees:
- Missing schema or DataStorage → defaults used (never corrupts)
- All writes happen inside Revit transactions via ExternalEvent handlers
- Read failures silently fall back to defaults
- Synchronous flush available during re-entry (FlushPendingToRevit)

---

## Build Targets

| Target Framework | Revit Version |
|------------------|--------------|
| `net48` | Revit 2024 |
| `net8.0-windows` | Revit 2026 |

NuGet packages are used for Revit API references (`Nice3point.Revit.Api`).

---

## Rules

**Fire Alarm:**

| Rule | Severity | Description |
|------|----------|-------------|
| MissingPanel | Error | Device has no panel assignment |
| MissingLoop | Error | Device has no loop assignment |
| MissingAddress | Warning | Device has no address |
| DuplicateAddress | Error | Multiple devices share same address on a loop |
| MissingRequiredParameter | Warning | A required parameter value is empty |

**Lighting:**

| Rule | Severity | Description |
|------|----------|-------------|
| MissingControllerRule | Error | Device has no controller assignment |
| MissingLineRule | Error | Device has no line assignment |
| MissingAddressRule | Warning | Device has no DALI address |
| DuplicateAddressRule | Error | Multiple devices share same address on a line |
| LineAddressOverCapacityRule | Error | Line exceeds 64-address DALI limit |
| LineCurrentOverCapacityRule | Error | Line exceeds 250 mA current limit |

---

## UX Principles

1. **System-first** — represent Panel, Loop, Zone, Devices (not raw elements)
2. **State always visible** — warnings and health shown without opening dialogs
3. **Immediate feedback** — explicit but fast refresh
4. **Visual topology** — tree-based system relationships
5. **Progressive disclosure** — clean default, inspector shows deeper detail
6. **Direct manipulation ready** — architecture supports future drag/drop
7. **Zero hidden state** — all warnings surface in UI and inspector
