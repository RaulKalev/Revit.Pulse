# Pulse — Architecture Guide

> Last updated after the **back-on-track-nonbreaking** refactor branch.

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
├────────────────────────────────────────────────────┤
│  Core/            — NO Revit dependency             │
│    Modules        — PulseAppController, ModuleCatalog│
│                     IModuleDefinition, Capabilities │
│    Graph          — Node, Edge (topology tree)      │
│    Graph/Canvas   — DiagramScene, LevelAnchor,      │
│                     PanelCluster, LoopCluster …     │
│    SystemModel    — Panel, Loop, AddressableDevice  │
│    Rules          — IRule, RuleResult, Severity     │
│    Settings       — ModuleSettings, TopologyStore   │
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
            │
            ├─ ModuleCatalog.Discover(expectedModuleIds, [FireAlarmFallback])
            │    └─ reflection scan → register into PulseAppController
            │
            ├─ StorageFacade.ReadSettings(doc)
            │    └─ PulseAppController.ApplySettings() → active module selected
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
  └─ MainViewModel.RefreshCompleted(data)               ← Dispatcher thread
       ├─ PulseAppController.DataCollected
       ├─ DiagramViewModel.LoadLevels / LoadPanels
       ├─ TopologyViewModel.BuildTree
       ├─ InspectorViewModel refresh
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
| `DisplayName` | User-facing name |
| `Capabilities` | `ModuleCapabilities` flags |
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
`IProvidesWiringFeatures`, `IProvidesSymbolMapping`) are retrieved via
`PulseAppController.GetFeature<T>()`.

---

## 4. Extensible Storage Strategy

### 4.1 Schema Versioning

Each schema has two GUIDs — **V1 (legacy)** and **V2 (current)**:

| Schema | V1 GUID | V2 GUID |
|--------|---------|---------|
| Module Settings | `A7E3B1C2-…-0E1F2A3B4C5E` | `A7E3B1C2-…-1F2A3B4C5E6F` |
| Diagram Settings | `B8F4C2D3-…-1F2A3B4C5D6E` | `B8F4C2D3-…-2F3A4B5C6D7E` |
| Topology Assignments | `C9D5E3F4-…-2A3B4C5D6E7F` | `C9D5E3F4-…-3A4B5C6D7E8F` |

### 4.2 V2 Hardening

V2 schemas differ from V1 in two ways:

1. **New GUIDs**: completely separate from V1 data to avoid cross-version corruption
2. **Marker field**: every V2 entity contains `PulseMarker = "Pulse"`, validated on read

### 4.3 Read / Write Flow

```
Read:
  1. Try V2 schema (Schema.Lookup) → validate marker → return JSON
  2. Fall back to V1 schema → return JSON (no marker expected)
  3. If neither exists → return null / defaults

Write:
  Always writes to V2 schema with marker field.
  V1 entities are never deleted — they remain for safety.
```

### 4.4 Migration Pipeline

`UpgradeSettingsJson(json, fromVersion)` steps sequentially through
each version increment. Currently v1 → v2 is a format-unchanged no-op.
Future versions add cases to the migration chain.

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

---

## 6. Settings Persistence

| Store | Backend | Scope |
|-------|---------|-------|
| Module settings (categories, param maps) | ES — ModuleSettings schema | Per-document |
| Diagram visibility (level line/text) | ES — DiagramSettings schema | Per-document |
| Topology assignments (configs, flips, wires, ranks) | ES — TopologyAssignments schema | Per-document |
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

## 8. How to Add a Module

1. Create `Modules/YourModule/` with a class implementing `IModuleDefinition`.
2. Declare `Capabilities` flags for the features your module supports.
3. Optionally implement feature interfaces (`IProvidesWiringFeatures`, etc.).
4. Implement `IModuleCollector`, `ITopologyBuilder`, `IRulePack`.
5. `ModuleCatalog.Discover()` finds it automatically via reflection.
