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
| `Core.Settings` | `ModuleSettings`, `ParameterMapping`, `TopologyAssignmentsStore`, `DeviceConfigStore`, `CustomSymbolDefinition`, `LevelVisibilitySettings`, `DiagramCanvasSettings`, `UiStateService` |
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
| `ExternalEvent handlers` | `CollectDevicesHandler`, `SelectElementHandler`, `TemporaryOverrideHandler`, `ResetOverridesHandler`, `SaveSettingsHandler`, `SaveDiagramSettingsHandler`, `SaveTopologyAssignmentsHandler`, `WriteParameterHandler` |

All Revit write operations use `ExternalEvent` to ensure they run on the Revit API thread.

### Pulse.UI

WPF user interface using Material Design:

| Component | Purpose |
|-----------|---------|
| `MainWindow` | Borderless window shell with title bar, resize grips, theme support |
| `TopologyView` | TreeView control — Panel → Loop → Device hierarchy |
| `InspectorPanel` | Entity details, properties, warnings, capacity gauges |
| `DiagramPanel` | Schematic wiring diagram canvas |
| `StatusStrip` | Bottom bar with status text, device/warning/error counts |
| `MainViewModel` | Root ViewModel — holds service refs, wires callbacks, exposes bindings/commands |
| `TopologyViewModel` | Topology tree + internal `CanvasGraphModel` projection |
| `DiagramViewModel` | Diagram canvas state: levels, panels, loops, flip/wire/rank assignments |
| `InspectorViewModel` | Selected entity details and capacity data |
| `SettingsViewModel` | Parameter mapping editor |
| `DeviceConfigViewModel` | Panels / Loop Modules / Wires / Paper Sizes configurator |
| `SymbolMappingViewModel` | Device-type to custom symbol mapping |
| `SymbolDesignerViewModel` | Custom symbol drawing canvas |
| `DiagramFeatureService` | Diagram ↔ topology wire-assignment orchestration |

### Pulse.Modules.FireAlarm

First implemented module:

| Component | Purpose |
|-----------|---------|
| `FireAlarmModuleDefinition` | Registers the module and provides factory methods |
| `FireAlarmCollector` | Collects fire alarm devices from configured Revit categories |
| `FireAlarmTopologyBuilder` | Builds Panel -> Loop -> Device graph |
| `FireAlarmRulePack` | Validates data with 5 rules |
| `FireAlarmParameterKeys` | Centralized logical parameter key constants |

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
