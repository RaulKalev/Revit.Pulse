# Pulse Smoke Test Checklist

Manual verification steps to run after each refactor commit.
Every item must pass before the commit is considered safe.

---

## 1. Plugin Load
- [ ] Revit starts without errors.
- [ ] "RK Tools" ribbon tab exists with "Fire Alarm" button.
- [ ] Clicking "Fire Alarm" opens the Pulse main window.
- [ ] Re-clicking "Fire Alarm" brings existing window to front (singleton pattern).
- [ ] Module discovery resolves `FireAlarmModuleDefinition` (status bar shows "Fire Alarm").

## 2. Refresh / Data Collection
- [ ] Press **Refresh** — status bar shows "Collecting data from Revit...".
- [ ] After collection completes, status bar shows device/panel/loop/warning/error counts.
- [ ] Topology tree is populated with Panel > Loop > Device hierarchy.
- [ ] Device counts in loop sub-info match actual devices.
- [ ] Panels and loops are sorted numerically (Panel 1, Panel 2, …; Loop 1, Loop 2, …).
- [ ] Devices are sorted by address within each loop.

## 3. Topology Interaction
- [ ] Clicking a device in the tree selects it in Revit and zooms to it.
- [ ] Previous/next device neighbours are included in selection context.
- [ ] Inspector panel shows device properties, warnings, and capacity gauges for loops.
- [ ] Expanding/collapsing nodes persists across refresh (UI state saved to ui-state.json).
- [ ] Panel/Loop config combo-boxes show available configs from device-config.json.
- [ ] Assigning a panel config writes the value to Revit parameters on the FACP element (or descendant devices).
- [ ] Assigning a loop module config writes the value to all descendant devices.
- [ ] Wire combo-box on loop nodes shows available wires from device-config.json.
- [ ] Assigning a wire writes the value to all descendant devices in Revit.

## 4. Diagram View
- [ ] Diagram renders level lines at correct elevations.
- [ ] Panel boxes draw with battery + PSU symbols.
- [ ] Loop wires follow serpentine pattern through device symbols.
- [ ] Flipping a loop (right-click > Flip) moves wire to opposite side.
- [ ] Adding/removing extra wire lines works via context menu (max 8 total).
- [ ] Wire assignments persist and show correct wire color from WireConfig.Color.
- [ ] Zoom (Ctrl+Scroll) and pan (middle-drag) work smoothly.
- [ ] Double-click middle-mouse fits canvas to paper.
- [ ] Canvas settings (wire spacing, device spacing, address labels, repetitions) apply.
- [ ] Paper size selection from Device Configurator reflects in diagram.
- [ ] Level line visibility toggles (hide/delete/restore) work and persist in ES.
- [ ] Level elevation offset drag updates the level position and persists to assignments.

## 5. Diagram ↔ Topology Wire Sync
- [ ] Assigning a wire in the **topology** combobox updates the diagram canvas immediately.
- [ ] Assigning a wire in the **diagram** (canvas context menu) updates the topology combobox silently.
- [ ] Both directions write the wire value to Revit parameters on loop devices.

## 6. Loop Rank Overrides
- [ ] Swapping loop ranks via the diagram reorders the loops visually.
- [ ] Rank overrides persist to ES and survive refresh.

## 7. Selection & Highlighting
- [ ] Selecting a device in topology highlights it in Revit with color override.
- [ ] "Reset Overrides" clears all temporary highlights.
- [ ] "Show Warnings Only" filter shows only entities with warnings (ancestors visible).
- [ ] Clearing "Show Warnings Only" restores all nodes.

## 8. Settings
- [ ] **Settings** dialog opens with current parameter mappings for Fire Alarm.
- [ ] Editing a parameter name and saving persists to device-config.json.
- [ ] "Reset to Defaults" restores original parameter names.
- [ ] Missing parameter keys from newer versions are merged in from defaults.

## 9. Device Configurator
- [ ] Opens with tabs: Panels, Loop Modules, Wires, Paper Sizes.
- [ ] Add/Edit/Delete operations work for each tab.
- [ ] Saving persists to %APPDATA%\Pulse\device-config.json.
- [ ] Closing the configurator triggers diagram redraw if config changed.

## 10. Symbol Mapping
- [ ] Opens with device-type to symbol grid.
- [ ] Search filters device types.
- [ ] Assigning a symbol persists to topology assignments in ES.
- [ ] Launching Symbol Designer from mapping window works.
- [ ] SymbolMappingOrchestrator.UpsertSymbol adds/replaces library entries.

## 11. Symbol Designer
- [ ] Drawing tools: Line, Polyline, Circle, Rectangle all function.
- [ ] Undo/Redo works (Ctrl+Z / Ctrl+Y).
- [ ] DXF import loads geometry correctly.
- [ ] SVG import loads geometry correctly.
- [ ] Saving a symbol adds it to custom-symbols.json.
- [ ] Snap grid and rulers render correctly.

## 12. Window Management
- [ ] Window position and size persist across sessions (window_placement.json).
- [ ] Borderless resize works in all 5 directions.
- [ ] Title bar drag-to-move, minimize, close all function.
- [ ] Dark/Light theme resources load without XAML errors.

## 13. Extensible Storage Persistence
- [ ] Topology assignments (panel/loop configs, flip states, wire assignments, rank overrides, level offsets, symbol mappings) survive Revit save/close/reopen.
- [ ] Diagram settings (level visibility states) survive Revit save/close/reopen.
- [ ] No data loss when opening a document saved with an older plugin version.
- [ ] FlushPendingToRevit syncs in-memory state before a second MainViewModel reads it.

## 14. Capabilities & Feature Guards
- [ ] All features active for FireAlarm module (Capabilities = All).
- [ ] Symbol Mapping command is enabled (SymbolMapping capability).
- [ ] Diagram visibility saves are wired (Diagram capability).
- [ ] Wire-assignment writes are wired (Wiring capability).
- [ ] Config-assignment writes are wired (ConfigAssignment capability).

## 15. Internal Graph Model (CanvasGraphModel)
- [ ] After refresh, `TopologyViewModel.CanvasGraph` is populated (not Empty).
- [ ] `CanvasGraph.Panels` count matches data panel count.
- [ ] Each `PanelAnchor` has correct `LoopAnchor` children.
- [ ] `DeviceCluster` groups devices by elevation.
- [ ] `CanvasOverlay` list contains entries for rule results with warnings.

## 16. Multi-Target Build
- [ ] `dotnet build -c Debug` succeeds for both net48 and net8.0-windows.
- [ ] No warnings related to target-framework mismatches.
- [ ] Zero CS errors after each commit.

## 17. BOQ Window
- [ ] **Open BOQ** button in toolbar opens the modeless BOQ window.
- [ ] Re-clicking Open BOQ brings existing window to front (no duplicates).
- [ ] DataGrid is populated with one row per device after initial refresh.
- [ ] Standard columns visible by default: Category, Family, Type, Level, Panel, Loop.
- [ ] Family column shows Revit **family name**; Type column shows Revit **type name**.
- [ ] Discovered Revit parameter columns appear in the settings panel (hidden by default).
- [ ] Toggling column visibility in the settings panel shows/hides the column in the grid.
- [ ] **Apply** button in settings panel persists and re-applies column, grouping, and sorting state.
- [ ] Column visibility, grouping rules, and sorting rules survive Revit save/close/reopen.
- [ ] Grouping/sorting field-key dropdowns only list **visible** columns.
- [ ] Adding a grouping rule and pressing Apply aggregates rows; Count column appears at far right.
- [ ] Count value equals the number of individual devices in that group.
- [ ] Removing all grouping rules restores individual device rows; Count column disappears.
- [ ] Grouping and sorting rules are **preserved** when pressing Refresh — not cleared.
- [ ] Adding a sorting rule sorts grid rows in the chosen direction after Apply.
- [ ] Custom Column editor opens and the new formula column appears in the grid.
- [ ] Export Settings writes a valid JSON file; Import Settings restores that state.
- [ ] Closing the BOQ window auto-saves settings; re-opening shows last saved state.
