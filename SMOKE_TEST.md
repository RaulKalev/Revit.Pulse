# Pulse Smoke Test Checklist

Manual verification steps to run after each refactor commit.
Every item must pass before the commit is considered safe.

## 1. Plugin Load
- [ ] Revit starts without errors.
- [ ] "RK Tools" ribbon tab exists with "Fire Alarm" button.
- [ ] Clicking "Fire Alarm" opens the Pulse main window.
- [ ] Re-clicking "Fire Alarm" brings existing window to front (singleton pattern).

## 2. Refresh / Data Collection
- [ ] Press **Refresh** — status bar shows "Collecting data from Revit...".
- [ ] After collection completes, status bar shows device/panel/loop/warning/error counts.
- [ ] Topology tree is populated with Panel > Loop > Device hierarchy.
- [ ] Device counts in loop sub-info match actual devices.

## 3. Topology Interaction
- [ ] Clicking a device in the tree selects it in Revit and zooms to it.
- [ ] Inspector panel shows device properties, warnings, and capacity gauges for loops.
- [ ] Expanding/collapsing nodes persists across refresh (UI state saved).
- [ ] Panel/Loop config combo-boxes show available configs from device-config.json.
- [ ] Assigning a config writes the value to Revit parameters on descendant elements.

## 4. Diagram View
- [ ] Diagram renders level lines at correct elevations.
- [ ] Panel boxes draw with battery + PSU symbols.
- [ ] Loop wires follow serpentine pattern through device symbols.
- [ ] Flipping a loop (right-click > Flip) moves wire to opposite side.
- [ ] Adding/removing extra wire lines works via context menu.
- [ ] Wire assignments persist and show correct wire color.
- [ ] Zoom (Ctrl+Scroll) and pan (middle-drag) work smoothly.
- [ ] Double-click middle-mouse fits canvas to paper.
- [ ] Canvas settings (wire spacing, device spacing, address labels, repetitions) apply.
- [ ] Paper size selection from Device Configurator reflects in diagram.
- [ ] Level line visibility toggles (hide/delete/restore) work and persist in ES.

## 5. Selection & Highlighting
- [ ] Selecting a device in topology highlights it in Revit with color override.
- [ ] "Reset Overrides" clears all temporary highlights.
- [ ] "Show Warnings Only" filter shows only entities with warnings.

## 6. Settings
- [ ] **Settings** dialog opens with current parameter mappings for Fire Alarm.
- [ ] Editing a parameter name and saving persists to device-config.json.
- [ ] "Reset to Defaults" restores original parameter names.

## 7. Device Configurator
- [ ] Opens with tabs: Panels, Loop Modules, Wires, Paper Sizes.
- [ ] Add/Edit/Delete operations work for each tab.
- [ ] Saving persists to %APPDATA%\Pulse\device-config.json.

## 8. Symbol Mapping
- [ ] Opens with device-type to symbol grid.
- [ ] Search filters device types.
- [ ] Assigning a symbol persists to topology assignments in ES.
- [ ] Launching Symbol Designer from mapping window works.

## 9. Symbol Designer
- [ ] Drawing tools: Line, Polyline, Circle, Rectangle all function.
- [ ] Undo/Redo works (Ctrl+Z / Ctrl+Y).
- [ ] DXF import loads geometry correctly.
- [ ] SVG import loads geometry correctly.
- [ ] Saving a symbol adds it to custom-symbols.json.
- [ ] Snap grid and rulers render correctly.

## 10. Window Management
- [ ] Window position and size persist across sessions (window_placement.json).
- [ ] Borderless resize works in all 5 directions.
- [ ] Title bar drag-to-move, minimize, close all function.
- [ ] Dark/Light theme resources load without XAML errors.

## 11. Extensible Storage Persistence
- [ ] Topology assignments (panel/loop configs, flip states, wire assignments, rank overrides, symbol mappings) survive Revit save/close/reopen.
- [ ] Diagram settings (level visibility states) survive Revit save/close/reopen.
- [ ] No data loss when opening a document saved with an older plugin version.

## 12. Multi-Target Build
- [ ] `dotnet build -c Debug` succeeds for both net48 and net8.0-windows.
- [ ] No warnings related to target-framework mismatches.
