# Pulse — Architecture Guide

> Last updated after **BOQ enhancements** — new row groups (Control Panels, Loop Modules, Cables by wire type, Batteries), CSV export, column list visibility fix, Category name from Revit, and − button to remove columns.

This document describes the runtime pipeline, module system, storage
strategy, and diagram scene-graph that form the backbone of Pulse.

---

## 1. Layer Map

```
┌────────────────────────────────────────────────────┐
│  UI/                                               │
│    MainWindow, Panels, ViewModels                  │
│    (WPF + Material Design, MVVM)                   │
├────────────────────────────────────────────────────┤
│  Revit/                                            │
│    Services       — RefreshPipeline,               │
│                     SelectionHighlightFacade,      │
│                     StorageFacade                   │
│    ExternalEvents — IExternalEventHandler impls    │
│    Storage        — ExtensibleStorageService,      │
│                     SchemaDefinitions               │
├────────────────────────────────────────────────────┤
│  Modules/                                          │
│    FireAlarm/     — Collector, TopologyBuilder,     │
│                     RulePack, ParameterKeys         │
│                     SubCircuit (entity + CRUD),     │
│                     FireAlarmSubCircuitService       │
├────────────────────────────────────────────────────┤
│  Core/            — NO Revit dependency             │
│    Modules        — PulseAppController, ModuleCatalog│
│                     IModuleDefinition, Capabilities  │
│                     ModuleData (Payload slot)         │
│                     TopologyAssignmentsService        │
│                     (lifecycle only — no FA types)   │
│                     SymbolMappingOrchestrator         │
│    Modules/Metrics — SystemMetricsCalculator,         │
│                     CapacityMetrics, HealthIssueItem  │
│                     DistributionGroup, CablingMetrics │
│                     BatteryMetrics                    │
│                     (RecommendedCapacitySummary,       │
│                      FormulaBreakdown)                │
│    Graph          — Node, Edge (topology tree)       │
│    Graph/Canvas   — DiagramScene, CanvasGraphModel,  │
│                     LevelAnchor, PanelCluster …      │
│    SystemModel    — Panel, Loop, Zone, Device        │
│    Rules          — IRule, RuleResult, Severity      │
│    Settings       — ModuleSettings, TopologyAssignmentsStore│
│                     (ModuleBlobs dict — opaque per-module   │
│                      blobs, e.g. "FireAlarm.SubCircuits")   │
│                     DeviceConfigStore                        │
│                     (ModuleConfigBlobs dict — opaque per-   │
│                      module hardware blobs, keyed by ModuleId│
│                      e.g. "FireAlarm" → FireAlarmDeviceConfig│
│                      JSON; loaded via DeviceConfigService)  │
│                     IModuleDeviceConfig (marker interface)  │
│                     LevelVisibility│
│    Logging        — ILogger abstraction             │
└────────────────────────────────────────────────────┘
```

**Dependency rule:** arrows always point downward.
`Core` has zero references to `Revit`, `UI`, or `Modules`.

---

## 2. Runtime Pipeline

### 2.1 Startup

```
PulseFireAlarm.Execute()            ← IExternalCommand entry point
  └─ MainWindow created
       └─ MainViewModel(uiApp)
            ├─ PulseAppController    — module registry + state
            ├─ RefreshPipeline       — CollectDevicesHandler + ExternalEvent
            ├─ SelectionHighlightFacade
            ├─ StorageFacade         — read/write ES + JSON persistence
            ├─ TopologyAssignmentsService — per-document assignment lifecycle
            ├─ SymbolMappingOrchestrator  — custom symbol library + mapping
            ├─ DiagramFeatureService      — diagram wire orchestration
            ├─ MetricsPanelViewModel      — System Intelligence Dashboard
            │
            ├─ ModuleCatalog.Discover(expectedModuleIds, [FireAlarmFallback])
            │    └─ reflection scan → register into PulseAppController
            │
            ├─ Capability guards wired (Diagram, Wiring, SymbolMapping, ConfigAssignment)
            │
            ├─ DeviceConfigService.Load()            ← reads device-config.json
            │    └─ PulseAppController.ApplySettings() → parameter mappings applied
            │
            ├─ StorageFacade.ReadDiagramSettings(doc) → DiagramViewModel.LoadVisibility
            ├─ StorageFacade.ReadTopologyAssignments(doc) → TopologyAssignmentsService.Load
            ├─ StorageFacade.ReadBoqSettings(doc) → MainViewModel._boqSettings
            │
            └─ Automatic first refresh via RefreshPipeline
```

### 2.2 Refresh Cycle

```
RefreshPipeline.Execute(module, settings, onCompleted, onError)
  │
  ├─ CollectDevicesHandler.Execute(UIApplication)      ← Revit API thread
  │    ├─ module.CreateCollector().Collect(context)
  │    ├─ module.CreateTopologyBuilder().Build(entities)
  │    ├─ module.CreateRulePack().Rules.Evaluate(entities)
  │    └─ callback → ModuleData result
  │
  └─ MainViewModel.OnDataCollected(data)               ← Dispatcher thread
       ├─ PulseAppController.OnRefreshCompleted(data)
       ├─ DiagramViewModel.LoadLevels / LoadPanels
       ├─ TopologyViewModel.LoadFromModuleData
       │    └─ CanvasGraphBuilder.Build() → TopologyViewModel.CanvasGraph
       ├─ InspectorViewModel.LoadNode
       └─ StatusStrip counts
```

### 2.3 Selection & Highlight

```
User clicks topology node / diagram element
  → SelectionHighlightFacade.SelectElement(elementId)
  → SelectionHighlightFacade.HighlightElements(ids)
  → InspectorViewModel shows details
```

All Revit writes (select, highlight, reset) go through
`IExternalEventHandler` + `ExternalEvent`.

---

## 3. Module System

### 3.1 Discovery

`ModuleCatalog.Discover()` scans all loaded assemblies for public classes
implementing `IModuleDefinition`, validates against a set of expected
module IDs, and returns them sorted by `DisplayName`. If none are found,
a hardcoded fallback list is used.

### 3.2 IModuleDefinition Contract

| Member | Purpose |
|--------|---------|
| `ModuleId` | Unique string identifier |
| `DisplayName` | User-facing name || `Description` | Short description of the module |
| `Version` | Version string (semver) || `Capabilities` | `ModuleCapabilities` flags |
| `GetDefaultSettings()` | Default categories + parameter mappings |
| `CreateCollector()` | Factory → `IModuleCollector` |
| `CreateTopologyBuilder()` | Factory → `ITopologyBuilder` |
| `CreateRulePack()` | Factory → `IRulePack` |

### 3.3 Capabilities

`ModuleCapabilities` is a `[Flags]` enum:

| Flag | Value | Meaning |
|------|-------|---------|
| `Diagram` | 1 | Module supports schematic diagram rendering |
| `Wiring` | 2 | Module supports wire parameter assignment |
| `SymbolMapping` | 4 | Module supports custom device symbols |
| `CapacityGauges` | 8 | Module produces capacity data (future) |
| `ConfigAssignment` | 16 | Module uses control-panel config assignment |
| `All` | 31 | All flags combined |

Capabilities are queried via `PulseAppController.HasCapability()`.
Optional feature interfaces (`IProvidesDiagramFeatures`,
`IProvidesWiringFeatures`, `IProvidesSymbolMapping`,
`IProvidesDeviceConfig`) are retrieved via
`PulseAppController.GetFeature<T>()`.

---

## 4. Extensible Storage Strategy

### 4.1 Schema Registry

Each data category has a **single GUID** defined in `SchemaDefinitions`.
Schemas use `AccessLevel.Public` for both read and write so that any
add-in (or future Pulse module) can inspect the data.

| Schema | GUID | Schema Name | Fields |
|--------|------|-------------|--------|
| Module Settings | `A7E3B1C2-4D5F-6A7B-8C9D-0E1F2A3B4C5E` | `PulseModuleSettings` | `SchemaVersion` (int) + `SettingsJson` (string) |
| Diagram Settings | `B8F4C2D3-5E6A-7B8C-9D0E-1F2A3B4C5D6E` | `PulseDiagramSettings` | `DiagramSchemaVersion` (int) + `DiagramSettingsJson` (string) |
| Topology Assignments | `C9D5E3F4-6A7B-8C9D-0E1F-2A3B4C5D6E7F` | `PulseTopologyAssignments` | `TopologyAssignmentsVersion` (int) + `TopologyAssignmentsJson` (string) |
| BOQ Settings | `D0E6F4A5-7B8C-9D0E-1F2A-3B4C5D6E7F8A` | `PulseBoqSettings` | `BoqSettingsVersion` (int) + `BoqSettingsJson` (string) |

All schemas are attached to a single `DataStorage` element named
`"PulseSettings"`, located via `FilteredElementCollector`.

### 4.2 Access Level

Both read and write access levels are set to `AccessLevel.Public`.
This means any Revit add-in can read and write Pulse data.

### 4.3 Read / Write Flow

```
Read:
  1. Find DataStorage element named "PulseSettings"
  2. Schema.Lookup(guid) → get entity → deserialise JSON field
  3. If DataStorage or schema not found → return null / defaults

Write (async path — normal operation):
  StorageFacade raises ExternalEvent
    → handler calls ExtensibleStorageService
      → opens Transaction
      → serialises to JSON
      → sets Entity on DataStorage
      → commits

Write (sync path — flush on re-entry):
  TopologyAssignmentsService.FlushToRevit(doc)
    → StorageFacade.SyncWriteTopologyAssignments(doc, store)
```

### 4.4 Fallback Strategy

- JSON fields store the full object graph; `SchemaVersion` is always `1`.
- If the DataStorage element does not exist yet, it is created inside
  the write transaction.
- If `Schema.Lookup()` returns null on read, the data is treated as
  absent and defaults are used.
- There is no V2 schema and no marker-field validation at this time.
  The previous V2 hardening plan was reverted for stability.

### 4.5 TODO — Future Storage Hardening

> These items are documented for future implementation.  They are **not**
> active in the current codebase and should not be attempted until the
> single-schema approach has proven stable in production.

- [ ] **Marker field**: add a `PulseMarker = "Pulse"` string field to
      each schema; validate on read to guard against collisions with
      other add-ins.
- [ ] **Vendor access level**: switch write access to
      `AccessLevel.Vendor` with the Pulse vendor ID to prevent
      accidental overwrites by third-party tools.
- [ ] **V2 GUIDs + migration**: introduce new schema GUIDs with the
      marker field; implement a `MigrateV1ToV2()` pipeline that reads
      the old entity, writes to the new schema, and optionally deletes
      the V1 entity.
- [ ] **Schema version bumping**: when the JSON shape changes, increment
      `SchemaVersion` and add a case to a version-step migration chain.

---

## 5. Diagram Scene Graph

### 5.1 Purpose

The **DiagramScene** is a logical, position-free snapshot of what the
diagram canvas should render.  It sits between the ViewModel data
(`PanelInfo`, `LoopDrawInfo`) and the WPF rendering layer (`DrawLevels`).

```
DiagramViewModel data → DiagramSceneBuilder.Build() → DiagramScene
                                                          ↓
                                              (future) LayoutEngine
                                                          ↓
                                              (future) Renderer
```

Currently `DrawLevels()` still renders imperatively.  The scene graph
exists so a future layout engine can separate positioning from drawing.

### 5.2 Scene Types

| Type | Description |
|------|-------------|
| `DiagramScene` | Root container: levels + panels + overlays |
| `LevelAnchor` | Horizontal level line with name, elevation, visibility |
| `PanelCluster` | Panel column with config, loop count, child loops |
| `LoopCluster` | Loop wire: flip, rank, wire count, colour, device rows + flat device list |
| `DeviceRow` | Devices at one elevation (grouped, with type breakdown) |
| `DeviceSlot` | Single device: address, type, resolved symbol key |
| `DiagramOverlay` | Warning / capacity / info annotation (future use) |
| `OverlayKind` | Enum: Warning, CapacityGauge, Info |

The scene is rebuilt via `DiagramSceneBuilder.Build(vm)` at the end of
every `LoadPanels()` call and exposed as `DiagramViewModel.Scene`.

### 5.3 Canvas Graph Model (Hybrid Canvas Readiness)

The **CanvasGraphModel** (`Core/Graph/Canvas/CanvasGraphModel.cs`) is a
topology-oriented internal model that sits between `ModuleData` and the
presentation layer.  It is built once per refresh by `CanvasGraphBuilder.Build()`.

```
ModuleData → CanvasGraphBuilder.Build() → CanvasGraphModel
                                              │
                    ┌─────────────────────────┼───────────────────────┐
                    ▼                         ▼                       ▼
          TopologyViewModel           (future) CanvasRenderer    CanvasOverlay list
          (TreeView projection)       (visual drag/drop)         (warnings, capacity)
```

The TreeView remains the **only** active renderer.  `TopologyViewModel`
holds a `CanvasGraph` property that is rebuilt on every
`LoadFromModuleData()` call.

#### Canvas Graph Types

| Type | Description |
|------|-------------|
| `CanvasGraphModel` | Root: panels, zones, overlays, node index |
| `PanelAnchor` | Panel with elevation, warning count, child loops |
| `LoopAnchor` | Loop with device count, warning count, device clusters |
| `ZoneAnchor` | Zone with device count, warning count |
| `DeviceCluster` | Devices at one elevation |
| `DeviceChip` | Single device: address, type, Revit id, warning count |
| `CanvasOverlay` | Warning / capacity / info annotation |
| `CanvasOverlayKind` | Enum: Warning, CapacityGauge, Info |

This model contains **no pixel positions or layout** — it is a pure
data graph.  A future layout engine will assign positions for the visual
canvas while the TreeView projection ignores them.

---

## 6. Settings Persistence

| Store | Backend | Scope |
|-------|---------|-------|
| Module settings (categories, param maps) | ES — ModuleSettings schema | Per-document |
| Diagram visibility (level line/text) | ES — DiagramSettings schema | Per-document |
| Topology assignments (configs, flips, wires, ranks) + `ModuleBlobs` generic per-module blobs (e.g. `"FireAlarm.SubCircuits"`) | ES — TopologyAssignments schema | Per-document |
| BOQ settings (column visibility, grouping, sorting, custom columns) | ES — BoqSettings schema | Per-document |
| Device config (panels, wires, symbols) | JSON `%APPDATA%\Pulse\` | Per-machine |
| Diagram canvas settings (spacing, paper) | JSON `%APPDATA%\Pulse\` | Per-machine |
| Custom symbol definitions | JSON `%APPDATA%\Pulse\` | Per-machine |

All Extensible Storage writes go through `StorageFacade` → `ExternalEvent`
handlers → `ExtensibleStorageService` → Revit transaction.

---

## 7. Build Targets

| Target | Revit Version | Notes |
|--------|--------------|-------|
| `net48` | Revit 2024 | .NET Framework, Costura.Fody IL-merges dependencies |
| `net8.0-windows` | Revit 2026 | .NET 8, WPF |

---

## 8. Metrics Pipeline

### 9.1 Overview

The **System Intelligence Dashboard** runs on `MainViewModel.LoadNode` /
`Refresh` and is entirely stateless in the calculation layer.

```
MainViewModel.LoadNode(node) / Refresh()
  └─ MetricsPanelViewModel.LoadPanel(panelInfo, loopInfos, data, cfg)
       ├─ BuildCapacity()     → SystemMetricsCalculator.ComputeForPanel / ComputeForLoop
       ├─ BuildHealth()       → SystemMetricsCalculator.ComputeHealthIssues
       │                         + ComputeCapacityHealthIssues(_lastCap)
       ├─ BuildDistribution() → SystemMetricsCalculator.ComputeDistribution
       └─ BuildCabling()      → SystemMetricsCalculator.ComputeCabling (loops sorted numerically)
```

### 9.2 Metrics Layer

| Type | Location | Purpose |
|------|----------|---------|
| `MetricsThresholds` | `Core/Modules/Metrics/` | Warning=0.70, Critical=0.85 |
| `CapacityMetrics` | `Core/Modules/Metrics/` | Address/mA usage, status enum, computed summaries |
| `HealthIssueItem` | `Core/Modules/Metrics/` | Single health row: rule name, description, count, status, element ids |
| `DistributionGroup` | `Core/Modules/Metrics/` | Device-type group with name, count, fraction |
| `CablingMetrics` | `Core/Modules/Metrics/` | Per-loop cable data + aggregate total |
| `SystemMetricsCalculator` | `Core/Modules/Metrics/` | Stateless calculator — all `Compute*` methods |
| `SystemCheckPromptBuilder` | `Modules/FireAlarm/` | Builds structured AI review prompt string (FA-specific; moved out of Core) |

### 9.3 MaxAddresses Override

`ControlPanelConfig.MaxAddresses` (default `0`) lets users specify a
fixed address ceiling per panel. When non-zero it replaces the automatic
`AddressesPerLoop × loopCount` calculation in both `SystemMetricsCalculator`
and `InspectorViewModel`. Setting it back to `0` restores automatic behaviour.

### 9.4 Capacity Health Integration

After `BuildCapacity()` stores the computed `CapacityMetrics` in `_lastCap`,
`BuildHealth()` appends `ComputeCapacityHealthIssues(_lastCap)` to the
rule-based health items. This means address or mA overreach automatically
appears as Warning/Error rows in the Health Status section without any
duplication of threshold logic.

### 9.5 NAC Circuit Metrics Pipeline

When a SubCircuit node is selected, `MetricsPanelViewModel` computes four
circuit-level gauges from the node's properties and sub-device list:

| Property | Source |
|----------|--------|
| `MaNormal` | Sum of standby current draws across all assigned sounder devices (pure device current — EOL supervisory is **not** included) |
| `MaUsed` (Alarm Load) | Sum of alarm current draws across all assigned sounder devices |
| `ScMaMax` | Priority: (1) `OutputCurrentMaxMa` node property from `FireAlarmTopologyBuilder`; (2) assigned PSU config `OutputCurrentA × 1000`; (3) `max(MaNormal, MaUsed) × 1.25` |
| V-Drop / Remaining V | Derived from wire parameters (ρ, A) and cable length — unchanged |

When a **PSU host element** (`IsSubDeviceNode` = true) is selected, the same metrics pipeline aggregates across **all hosted SubCircuits** via `SubCircuitService.GetSubCircuitsByHost()`, and the dashboard also renders the Battery / PSU section (see §9.6).

**`GaugeControl` display when `Maximum = 0`:** shows `"value unit"` only
(e.g. `"425 mA"`), omitting the denominator and percent text, so an
unknown ceiling is still readable.

**3D Route toggle in Quick Actions:** `Metrics.Toggle3DRoutingRequested`
toggled `IsWireRoutingVisible` on the currently selected Loop or
SubCircuit node; if no compatible node is selected, a status-bar prompt
is shown instead.

---

### 9.6 Battery Metrics Pipeline

When a PSU host element is selected and a PSU config is assigned:

```
MetricsPanelViewModel.BuildBattery()
  └─ IsSubDeviceNode(_selectedNode)
       └─ SubCircuitService.GetSubCircuitsByHost(hostElementId)
            └─ SubCircuitPsuAssignments[hosted[0].Id] → psuName
                 └─ DeviceConfigService.LoadModuleConfig<FireAlarmDeviceConfig>()
                      └─ faDevCfg.PsuUnits.FirstOrDefault(p => p.Name == psuName)
                           └─ SystemMetricsCalculator.ComputePsuBatteryMetrics(hosted, fa, psuCfg)
                                └─ BatteryMetrics  (StandbyCurrentMa, AlarmCurrentMa, BatteryVoltageV, ...)
                                     └─ RecommendedCapacitySummary  e.g. "2× 12V/12.0Ah = 24.0 Ah  (req. 15.47 Ah)"
                                     └─ FormulaBreakdown            four-step equation trace
```

**`BatteryMetrics` key computed properties:**

| Property | Formula |
|----------|---------|
| `RequiredCapacityAh` | `(Is/1000 × ts + Ia/1000 × ta/60) × f` (EN 54-4) |
| `RecommendedBatteryCount` | Smallest even integer (2, 4, 6…) where `count × size ≥ reqAh` |
| `RecommendedBatteryUnitAh` | Matching standard size from sorted VRLA/AGM array |
| `RecommendedCapacitySummary` | `N× VV/X.XAh = Y.Y Ah  (req. Z.ZZ Ah)` with `unitV = VoltageV / 2` |
| `FormulaBreakdown` | Four lines: symbolic → values substituted → terms evaluated → result Ah |

---

## 9. How to Add a Module

1. Create `Modules/YourModule/` with a class implementing `IModuleDefinition`.
2. Declare `Capabilities` flags for the features your module supports.
3. Optionally implement feature interfaces:
   - `IProvidesWiringFeatures` — wire parameter key for Revit write-back
   - `IProvidesSymbolMapping` — custom device-type symbol library
   - `IProvidesDeviceConfig` — hardware device config; return your `IModuleDeviceConfig`
     subclass from `GetDefaultDeviceConfig()`; load/save via
     `DeviceConfigService.LoadModuleConfig<T>(moduleId)` /
     `DeviceConfigService.SaveModuleConfig(moduleId, config)` — stored as a JSON blob in
     `DeviceConfigStore.ModuleConfigBlobs[moduleId]`; Core never inspects the value.
4. Implement `IModuleCollector`, `ITopologyBuilder`, `IRulePack`.
5. In your `IModuleCollector.Collect()`, create a module-specific payload class
   (e.g. `YourModulePayload`) and assign it: `data.Payload = new YourModulePayload()`.
6. In your `ITopologyBuilder`, `IRulePack`, and any UI consumers, retrieve it with
   `data.GetPayload<YourModulePayload>()` — returns `null` for other modules.
7. `ModuleCatalog.Discover()` finds it automatically via reflection.

> **`ModuleData.Payload` pattern** — `ModuleData` carries only a single `object Payload`
> slot plus the generic graph (`Nodes`, `Edges`) and cross-cutting fields (`Levels`,
> `RuleResults`, etc.). All module-specific typed entities live in the payload class,
> keeping `ModuleData` module-agnostic.
