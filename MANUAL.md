# Pulse — Fire Alarm Plugin: User Manual

> **Version:** 1.0.0  
> **Platform:** Autodesk Revit (Windows)  
> **Ribbon:** RK Tools → Fire Alarm

---

## Quick Start (5 minutes)

Use this if you just want to get productive fast.

1) **Open Pulse**  
   Revit → **RK Tools → Fire Alarm**.

2) **Map your parameters (first time only)**  
   Pulse → **Settings (⚙)**  
   Set which Revit parameters are used for **Panel**, **Loop**, and **Address**. Save.

3) **Add / confirm your equipment library (first time only)**  
   Pulse → **Configure Devices**  
   Add your **panel model**, **loop module**, and **wire types** (names you want to select later). Save.

4) **Refresh**  
   Click **Refresh** any time you change the model or parameters.

5) **Assign configs (normal workflow)**  
   In the **Topology Tree**, select the correct **panel config** and **loop module config** from the dropdowns.  
   Assign a **wire type** to loops if you want consistent diagram labeling.

6) **Use the Diagram + Dashboard**  
   - Diagram: confirm the layout looks right (levels, loop sides, wire count).  
   - Dashboard: watch capacity + health issues (addresses, mA, warnings/errors).

7) **Optional: run System Check**  
   Click **Run System Check** to copy a system summary to clipboard and paste it into ChatGPT/Claude/etc.

---

## What Pulse is for

Pulse helps you manage an **addressable fire alarm system as a system**, not as individual elements. It gives you:

- A live **Panel → Loop → Device** hierarchy (Topology Tree)
- An auto-generated **schematic diagram**
- **Warnings and errors** (missing panel/loop/address, duplicates, SubCircuit issues)
- Live **capacity gauges** (addresses and mA)
- **NAC SubCircuits** (sounder/strobe lines from PSU/output modules) with voltage-drop checks

---

## Words you'll see (quick glossary)

- **Panel**: the fire alarm control panel (FACP).
- **Loop**: addressable loop / SLC.
- **Address**: device address on a loop.
- **Config**: a named hardware model you pick from your Device Configurator (panel model, loop module model).
- **Wire type**: named cable entry you assign to loops/SubCircuits.
- **SubCircuit (NAC)**: a one-way notification circuit driven by PSU/output module (sounders, strobes, horns).

---

## 1) Opening Pulse

1. Open Revit and your project.
2. Go to **RK Tools → Fire Alarm**.
3. Click **Fire Alarm**.

Only one Pulse window can be open at once. Clicking the button again brings it to the front.

**Note:** Pulse reads the **active Revit document**. If you switch to a different Revit file, close and re-open Pulse.

---

## 2) First-time setup: Parameter Mapping (Settings)

Pulse needs to know which Revit parameters contain the key values it uses.

Open **Settings (⚙)**.

### Required mappings (do these first)
Set these to match your families:

- **Panel** (which panel the device belongs to)
- **Loop** (loop number/name)
- **Address** (device address)

If these aren't mapped correctly, you'll see lots of "(No Panel) / (No Loop)" and missing address warnings.

### Helpful optional mappings (recommended)
If your families support them, map these too:

- **DeviceType** (e.g. Smoke Detector, Sounder)
- **CurrentDrawNormal / CurrentDrawAlarm** (mA loads)
- **Wire** (where Pulse writes selected wire type)
- **NominalVoltage** (for SubCircuit voltage-drop checks)
- Config write-backs (where Pulse writes selected panel/loop configs)

### Category to scan
At the top of Settings, choose the **Revit category** Pulse should scan (default: Fire Alarm Devices).

Click **Save**. Pulse applies the mappings and refreshes.

---

## 3) First-time setup: Device Configurator (your equipment library)

Open **Configure Devices**.

This is where you create the dropdown options you will select later in the Topology Tree:
- Control panel models
- Loop module models
- Wire types
- Paper sizes (used for diagram boundaries/printing)

### Control Panels (panel models)
Add one entry per panel model you use (example: "Aritech CD3246"). Keep values matching your datasheets.

### Loop Modules
Add loop expansion card / loop controller models you use.

### Wire Types
Add your standard loop cables (example: "FP200 2×1.5mm²"), including resistance if you have it from a datasheet for best V-drop accuracy.

### Paper Sizes
Add paper formats (A3/A1 etc.) that you want the diagram to fit to.

Click **Save** when done.

---

## 4) Main window at a glance

Pulse is split into four main areas:

- **Topology Tree** (left/top): Panel → Loop → Device (and SubCircuits)
- **Diagram** (right/top): generated schematic
- **Inspector** (left/bottom): details of what you selected
- **Dashboard** (right/bottom): capacity, health, distribution, cabling info

---

## 5) Topology Tree (your main workflow)

After **Refresh**, you'll see your system grouped as:

- Panels
  - Loops
    - Devices
    - (For PSU/output modules: SubCircuit outputs)

### Selecting things
Clicking an item:
- selects it in Revit,
- zooms to it,
- highlights it temporarily,
- updates the Inspector.

Use **Reset Overrides** to clear temporary highlights.

### Assign panel and loop configurations
This is what makes capacity gauges meaningful.

**Assign panel config**
1. Expand a panel.
2. Use its config dropdown.
3. Select the panel model from your Device Configurator.

**Assign loop module config**
1. Find a loop under the panel.
2. Use the loop config dropdown.
3. Select the correct loop module model.

### Assign wire types to loops
Each loop has a wire dropdown:
1. Pick the correct wire type.
2. Pulse writes it to your mapped Wire parameter and updates diagram labeling.

---

## 6) Diagram Panel (schematic)

The Diagram renders a wiring schematic using:
- your topology (panels/loops/devices)
- Revit levels where devices exist

### Navigation
- Ctrl + scroll: zoom
- Middle mouse drag: pan
- Middle double-click: fit to paper boundary

### Fixing layout (common actions)
Right-click on a loop wire or diagram area for options such as:

- **Flip loop** (move a loop to left/right side)
- **Add/Remove wire lines** (show extra conductors visually)
- **Move Up/Down** (set loop order when they overlap at same elevation)

### Level lines
Pulse draws a level line for levels where devices exist.

You can:
- hide/show a line or its text,
- delete/restore it,
- and drag level line positions (Move mode) to adjust spacing.

All diagram layout choices are saved with the Revit file.

---

## 7) Dashboard (System Intelligence)

The Dashboard changes depending on what you select:
- Panel → overall panel utilization
- Loop → loop utilization and distribution
- SubCircuit → circuit metrics (normal/alarm load, voltage drop)

Use it to quickly spot:
- near-capacity loops (addresses / mA),
- missing/duplicate addresses,
- SubCircuit low voltage conditions,
- uneven distribution.

---

## 8) NAC SubCircuits (sounders / strobes)

A SubCircuit models a one-way NAC output driven by a PSU or output module. It's separate from the addressable loop so you can track load and voltage drop properly.

### Create a SubCircuit
1. In the Topology Tree, find the PSU/output module device.
2. Expand it to see output ports.
3. Click **+** on an output port.
4. Name the SubCircuit.

### Add devices
- Click **+** on the SubCircuit card and select from a list of unassigned NAC devices, **or**
- Use **Pick from Revit** and select multiple devices directly in the model.

### Remove / delete
- Remove a single device with the **−** next to it.
- Delete the SubCircuit with the trash icon (members return to unassigned).

### SubCircuit settings (wire & limits)
Each SubCircuit has its own:
- Wire type
- V-drop limit %
- Cable temperature
- EOL resistor value

---

## 9) 3D Wire Routing (visualisation)

Use this to draw/measure orthogonal route lines in Revit for loops or SubCircuits (for planning and length estimation).  
(If you want more accurate results, keep routing consistent with your office standards.)

---

## 10) Custom symbols (optional)

### Symbol Mapping
Use Symbol Mapping to connect your model's **DeviceType** strings to diagram symbols:
1. Open **Symbol Mapping**
2. For each device type, choose a symbol
3. Save

### Symbol Designer
Use Symbol Designer to create/edit symbols (lines, polylines, circles, rectangles), set snap origin, and import DXF/SVG if needed.

---

## 11) BOQ (Bill of Quantities) window

BOQ is a flexible schedule for the collected devices.

### Columns
Use the BOQ settings drawer (⚙) to:
- show/hide columns,
- reorder columns,
- add custom computed columns.

### Grouping
Grouping creates one row per unique combination and adds a Count column automatically.

### Sorting
Add one or more sort rules with priority order.

### Export/import settings
Export settings to share layouts with colleagues, import to restore.

---

## 12) Run System Check (AI export)

**Run System Check** copies a structured summary of your system to the clipboard.  
Paste it into any AI assistant to get a review or suggestions.

It includes:
- totals (devices/panels/loops, warnings/errors),
- per-panel and per-loop utilization,
- device distribution and issues,
- a checklist the AI can follow.

Pulse does **not** send data anywhere — it only prepares text and copies it.

---

## 13) Understanding warnings and errors (reference)

Pulse checks the model on every Refresh. Warnings/errors appear in the status bar, tree badges, and Dashboard Health.

### Device rules
- Missing Panel (warning)
- Missing Loop (warning)
- Missing Address (warning)
- Duplicate Address (error)
- Missing Required Parameter (warning)

### SubCircuit rules
- SubCircuit Missing Trigger (warning)
- SubCircuit Missing Fault Monitor (warning)
- SubCircuit Duplicate Member (error)
- SubCircuit Orphan Sounder (warning)

### Capacity health issues (Dashboard)
- High address usage (warning ≥ 70%)
- Critical address usage (error ≥ 85%)
- High mA load (warning ≥ 70%)
- Critical mA load (error ≥ 85%)
- NAC low end-voltage (warning)

---

## Appendix A — Where Pulse stores data (for IT / power users)

You normally don't need this day-to-day, but it helps when moving between machines or collaborating.

- Parameter mappings + device library: `%APPDATA%\Pulse\device-config.json`
- Custom symbols library: `%APPDATA%\Pulse\custom-symbols.json`
- Window position: `%APPDATA%\Pulse\window_placement.json`
- UI state: `%APPDATA%\Pulse\ui-state.json`

Saved with the Revit model (shared with colleagues who open the same RVT):
- topology assignments (configs, wires, SubCircuits, diagram layout choices)
- diagram settings
- BOQ settings

---

For developer/architecture notes, see `ARCHITECTURE.md`.
