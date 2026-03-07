# Pulse — Proposals & Roadmap

> Last updated: March 7, 2026.
> PSU host element work (circuit metrics, battery sizing, EN 54-4 formula, standard-size even-count battery recommendation) is complete and shipped. BOQ enhancements (control panels, loop modules, cables grouped by wire type, batteries) and CSV export are complete and shipped.

---

## ✅ Completed

| Area | Feature |
|------|---------|
| Core | `ModuleData.Payload` pattern — FA types decoupled from Core |
| Fire Alarm | Panel → Loop → Device topology, config assignment, V-drop |
| Fire Alarm | NAC SubCircuit CRUD, V-drop, wire assignment, EOL resistor |
| Fire Alarm | PSU combobox on host element (SubCircuitService lookup fix) |
| Fire Alarm | PSU host element — combined NAC load (normal + alarm) in Inspector and Circuit Metrics |
| Fire Alarm | PSU host element — Battery / PSU section with EN 54-4 sizing |
| Fire Alarm | Standard-size even-count battery recommendation (VRLA/AGM 1.2–100 Ah) |
| Fire Alarm | EN 54-4 formula breakdown displayed in dashboard (4-step trace) |
| Fire Alarm | Per-unit battery voltage derived from PSU config VoltageV |
| Fire Alarm | PSU config on host device propagates to all hosted SubCircuits in one action |
| Fire Alarm | Battery / PSU sizing data (FormulaBreakdown + RecommendedCapacitySummary) shown in dashboard |
| Fire Alarm | Loop balance health check — Warning row + Highlight when spread ≥ 40 % and overloaded loop ≥ 70 %; included in AI System Check prompt |
| UI | System Intelligence Dashboard — 6 sections |
| UI | BOQ window — grouping, custom formula columns, settings export/import, CSV data export |
| UI | BOQ window — enhanced row groups: Control Panels, Loop Modules, Cables (by wire type with length), Batteries (FACP + field PSU) |
| UI | BOQ column list shows all visible columns; − button removes selected column; structural columns auto-visible on first load |
| UI | 3D Manhattan wire routing with auto-measured cable length |
| Fire Alarm | SubCircuit cable length read back from drawn routing lines — routed length preferred over Manhattan estimate |
| UI | Custom symbol designer + mapping |
| UI | AI System Check prompt export |
| Docs | README, ARCHITECTURE, MANUAL updated for PSU host features |

---

## 🔵 High Priority — Natural Next Steps

**1. BOQ CSV/Excel Export** ~~— CSV export shipped~~

~~CSV export is now implemented (Export CSV toolbar button, respects visible columns, grouping, and sorting; UTF-8 with proper escaping). Excel (.xlsx via ClosedXML) export remains open if needed.~~

**2. Diagram PDF/Image Export**
Paper sizes are already configured. The diagram canvas renders to WPF visuals. Exporting to PDF (via `PrintDialog` + `XpsDocument`) or PNG/SVG would complete the print workflow. The `PaperSizeConfig` model is already in place.

---

## 🟡 Medium Priority — Fire Alarm Enhancements

**5. Zone / Cause-Effect Modelling**
The `Zone` model exists in `Core.SystemModel` but is unused. Integrating zone-to-device assignment, zone counts, and cause-effect (input zone → output zones) would unlock fire-fighting mode mapping and code compliance checks.

~~**6. Loop Balance Health Checks** — completed~~

~~**7. SubCircuit Cable Length from Routed Lines** — completed~~

~~SubCircuit routing lines are drawn and togglable; on Refresh, routed wire lengths are read back from `"Pulse Wire – "` model lines (via `RevitCollectorService.GetRoutedWireLengths`) and preferred over the Manhattan estimate in `FireAlarmTopologyBuilder`. Full parity with loop routing.~~

**8. Multi-PSU Scenarios (daisy-chained PSUs)**
Currently one PSU config per host element. Modelling scenarios where a second PSU charges the same battery bank, or where SubCircuits span two output modules, is a future topology enhancement.

**9. NAC Fault Monitor Health Rule Improvement**
`SubCircuitMissingFaultMonitor` uses keyword matching (`psu`, `fault`, `monitor`...). Replacing this with an explicit device-type role field (set in symbol mapping or device configurator) would make the rule more reliable and configurable.

---

## 🟠 Medium Priority — Platform / Architecture

**10. Storage Hardening (V2 Schema)**
Documented in ARCHITECTURE §4.5 — add a `PulseMarker` field, swap to vendor `AccessLevel`, and introduce V2 GUIDs with migration pipeline. Currently deprioritised for stability.

**11. Interactive Diagram Canvas (Drag/Drop)**
The `CanvasGraphModel` and `DiagramScene` are designed for a future layout engine that separates positioning from rendering. Enabling drag-to-reorder devices or loops in the diagram is architecturally ready but not yet implemented.

**12. Diagram Overlay Annotations**
`DiagramOverlay` / `CanvasOverlay` types exist but render nothing. Populating them with validation warning badges and capacity gauges inline on the schematic canvas would make the diagram self-documenting.

**13. Device Config Cloud Sync / Share**
Currently `device-config.json` is machine-wide only. Adding an import-from-URL or import-from-shared-network-path option would help teams keep hardware libraries in sync.

---

## 🟢 Lower Priority — Future Modules

**14. Battery Report Export**
The battery sizing data (`FormulaBreakdown`, `RecommendedCapacitySummary`, standby/alarm currents) is already computed and displayed in the dashboard. Adding a clipboard-copy or PDF/text export per PSU — mirroring the AI System Check export — would complete the commissioning documentation workflow.

**15. Emergency Lighting Module**
Closest to Fire Alarm in architecture — similar circuit topology, battery standby calculations (BS 5266 / EN 50172), lux-level check per zone. The `IModuleDefinition` pattern is fully ready; no Core changes needed.

**16. Security / Access Control Module**
Door controllers, readers, lock types, zones. Different topology (reader → controller rather than device → loop) but the graph model is flexible enough.

**17. BMS / HVAC Monitoring Module**
Sensor point mapping, setpoint validation, equipment hierarchy. Likely needs a new `ZoneAnchor` renderer in the diagram canvas.

---

## 🔲 Deferred / Under Review

| Item | Reason |
|------|--------|
| Revit parameter write-back for PSU assignments | Stored in ES only. Write-back to a Revit parameter on the PSU element needs a new parameter key and mapping. |
| Automatic EOL resistor value suggestion | Would need a device-type → EOL resistor lookup table in the device configurator. |
| Address gap detection rule | "Address 5 is missing from loop 1 — possible decommissioned device." Low priority until requested. |
| Real-time Revit model sync (no Refresh button) | Requires `IUpdater` / dynamic model events. High complexity, low stability gain. |
