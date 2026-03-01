# Pulse

**Modern MEP UX platform for addressable systems.**

Pulse is a modular Revit plugin that provides a system control center for MEP addressable systems. It replaces element-first workflows with system-first topology visualization, real-time validation, and progressive disclosure of detail.

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
| `Core.Modules` | `IModuleDefinition`, `PulseAppController`, `ModuleCatalog`, `ModuleCapabilities`, `TopologyAssignmentsService`, `SymbolMappingOrchestrator` |
| `Core.Modules.Metrics` | `SystemMetricsCalculator`, `CapacityMetrics`, `HealthIssueItem`, `DistributionGroup`, `CablingMetrics`, `SystemCheckPromptBuilder` — System Intelligence metrics engine |
| `Core.Settings` | `ModuleSettings`, `ParameterMapping`, `TopologyAssignmentsStore`, `DeviceConfigStore`, `CustomSymbolDefinition`, `LevelVisibilitySettings`, `DiagramCanvasSettings`, `UiStateService`; `ControlPanelConfig.MaxAddresses` for per-panel address cap override |
| `Core.Logging` | `ILogger` abstraction |

### Pulse.Revit

Revit API integration:

| Component | Purpose |
|-----------|---------|
| `RefreshPipeline` | Owns `CollectDevicesHandler` + `ExternalEvent` for the refresh cycle |
| `SelectionHighlightFacade` | Owns select, override, and reset handlers |
| `StorageFacade` | Read/write helpers for ES and JSON persistence |
| `RevitCollectorService` | Implements `ICollectorContext` — extracts elements and parameters |
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
| `TopologyView` | TreeView control — Panel → Loop → Device hierarchy |
| `InspectorPanel` | Entity details, properties, warnings, capacity gauges |
| `DiagramPanel` | Schematic wiring diagram canvas |
| `SystemMetricsPanel` | 6-section System Intelligence Dashboard (capacity gauges, health status, device distribution, cabling & spatial, quick actions) |
| `StatusStrip` | Bottom bar with status text, device/warning/error counts |
| `MainViewModel` | Root ViewModel — holds service refs, wires callbacks, exposes bindings/commands |
| `TopologyViewModel` | Topology tree + internal `CanvasGraphModel` projection |
| `DiagramViewModel` | Diagram canvas state: levels, panels, loops, flip/wire/rank assignments |
| `InspectorViewModel` | Selected entity details and capacity data |
| `MetricsPanelViewModel` | System Intelligence Dashboard VM — capacity gauges, health issues (including capacity overload rows), device distribution, cabling info, AI prompt export |
| `AiPromptInfoWindow` | Themed borderless popup shown after the AI system-check prompt is copied to clipboard |
| `MetricsConverters` | `CapacityStatusToBrushConverter`, `HealthStatusToBrushConverter`, `CountToVisibilityConverter` |
| `SettingsViewModel` | Parameter mapping editor |
| `DeviceConfigViewModel` | Panels / Loop Modules / Wires / Paper Sizes configurator |
| `SymbolMappingViewModel` | Device-type to custom symbol mapping |
| `SymbolDesignerViewModel` | Custom symbol drawing canvas |
| `DiagramFeatureService` | Diagram ↔ topology wire-assignment orchestration |
| `BoqWindow` | Modeless Bill of Quantities window with aggregated grouping and settings panel |
| `BoqWindowViewModel` | BOQ root VM — column management, aggregated grouping, sorting, settings persistence |
| `BoqRowViewModel` | Single BOQ row wrapper; exposes string indexer `[FieldKey]` for DataGrid; carries `Count` for aggregated rows |
| `BoqColumnViewModel` | Column state VM — `IsVisible`, `DisplayOrder`, `Header`, `FieldKey`, `IsCustom` |

### Pulse.Modules.FireAlarm

First implemented module:

| Component | Purpose |
|-----------|---------|
| `FireAlarmModuleDefinition` | Registers the module and provides factory methods |
| `FireAlarmCollector` | Collects fire alarm devices from configured Revit categories |
| `FireAlarmTopologyBuilder` | Builds Panel → Loop → Device graph; attaches cable length to each loop node |
| `CableLengthCalculator` | Calculates per-loop cable length using Manhattan (right-angle) routing in Revit internal units, returns metres |
| `FireAlarmRulePack` | Validates data with 5 rules |
| `FireAlarmParameterKeys` | Centralized logical parameter key constants || `FireAlarmBoqDataProvider` | Implements `IBoqDataProvider` — maps `ModuleData` to `BoqItem` rows with Revit family name, type name, level, panel, loop, address, and all discovered parameters |
---

## Features

### Bill of Quantities (BOQ) Window

The **BOQ window** is a modeless panel opened from the main toolbar that shows all devices in a configurable DataGrid.

**Column management (settings panel, right-side drawer):**
- Toggle column visibility by checking/unchecking columns in the settings list
- Re-order columns with Move Up / Move Down
- Add custom formula columns via the Custom Column editor
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

The Fire Alarm module calculates the total cable length for each loop using **Manhattan (right-angle) routing**:

- Route: Panel origin → devices sorted by address → return to Panel
- Distance metric: |Δx| + |Δy| + |Δz| in Revit internal units (feet), converted to metres
- Result is displayed in each loop card header as `XX.X m`
- Powered by `CableLengthCalculator.CalculateAll(panels)`

### Per-Loop 3D Wire Routing Visualisation

Each loop card header contains a **wire routing toggle button**. Clicking it draws orthogonal model lines in the active Revit 3D view — one set per loop:

- Each loop's lines use a dedicated line-style subcategory (`Pulse Wire – {panel} - {loop}`) so they can be toggled on/off independently
- Lines follow the same Panel → devices → Panel Manhattan route as the cable calculator
- **State is persisted** to Revit Extensible Storage (`LoopWireRoutingVisible` in `TopologyAssignmentsStore`) — re-opening the plugin restores all visible loops automatically
- The toggle icon turns red when wires are visible; clicking again clears only that loop's lines

### System Intelligence Dashboard

The **System Intelligence Dashboard** (`SystemMetricsPanel`) provides a live overview of every selected panel, driven by `SystemMetricsCalculator` and `MetricsPanelViewModel`.

**Six sections (scrollable body + sticky Quick Actions footer):**

| Section | Content |
|---------|---------|
| Capacity | Address and mA gauges (used / max, colour-coded by threshold) |
| Health Status | Rule violations + capacity overload rows, each with count and highlight command |
| Device Distribution | Breakdown of device types with percentage bars |
| Cabling & Spatial | Per-loop cable lengths, numerically sorted; total project cabling |
| AI System Check | Copies a structured prompt to the clipboard; once-per-session info popup |
| Quick Actions (pinned) | Refresh, Highlight Warnings, Copy Report, Open Settings |

**Thresholds** (`MetricsThresholds`): Warning at 70 %, Critical at 85 %.

**`ControlPanelConfig.MaxAddresses`**: when non-zero, overrides the automatic `AddressesPerLoop × loopCount` calculation for the panel capacity gauge and AI prompt. Set to `0` (default) for automatic behaviour.

**Behaviour details:**
- Collapsed header shows bold centred text with the icon hidden
- Panel enforces a minimum expanded height of 300 px
- Cabling loops are sorted numerically (e.g. L2 < L10)
- Capacity overload conditions surface as Warning/Error rows in the Health Status section

---

## How to Add a Module

1. Create a new folder under `Modules/` (e.g., `Modules/EmergencyLighting/`).
2. Implement `IModuleDefinition` with:
   - `ModuleId`, `DisplayName`, `Description`, `Version`
   - `Capabilities` — declare supported `ModuleCapabilities` flags
   - `GetDefaultSettings()` returning default categories and parameter mappings
   - Factory methods for collector, topology builder, and rule pack
3. Optionally implement feature interfaces (`IProvidesWiringFeatures`, `IProvidesSymbolMapping`, etc.).
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

Default Fire Alarm parameter mappings:

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

## Rules (Stage 1)

| Rule | Severity | Description |
|------|----------|-------------|
| MissingPanel | Error | Device has no panel assignment |
| MissingLoop | Error | Device has no loop assignment |
| MissingAddress | Warning | Device has no address |
| DuplicateAddress | Error | Multiple devices share same address on a loop |
| MissingRequiredParameter | Warning | A required parameter value is empty |

---

## UX Principles

1. **System-first** — represent Panel, Loop, Zone, Devices (not raw elements)
2. **State always visible** — warnings and health shown without opening dialogs
3. **Immediate feedback** — explicit but fast refresh
4. **Visual topology** — tree-based system relationships
5. **Progressive disclosure** — clean default, inspector shows deeper detail
6. **Direct manipulation ready** — architecture supports future drag/drop
7. **Zero hidden state** — all warnings surface in UI and inspector
