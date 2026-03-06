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

## Glossary

| Term | Meaning |
|------|---------|
| **Panel** | The fire alarm control panel (FACP) — the central unit that supervises all loops and devices. |
| **Loop / SLC** | Signalling Line Circuit — the addressable wiring ring that connects field devices back to the panel. |
| **Address** | A unique number assigned to each device on a loop so the panel can identify it individually. |
| **Config** | A named hardware model (panel or loop module) stored in the Device Configurator. Selecting a config unlocks that model's capacity limits. |
| **Wire type** | A named cable entry (e.g. FP200 2×1.5 mm²) stored in the Device Configurator. Used for diagram labelling and voltage-drop calculations. |
| **SubCircuit (NAC)** | Notification Appliance Circuit — a one-way output circuit driven by a PSU or output module that powers sounders, strobes, and horns. Modelled separately from the addressable loop. |
| **PSU** | Power Supply Unit — a device in the tree that can host one or more NAC SubCircuit outputs. |
| **PSU Config** | A named hardware specification for a NAC PSU stored in the Device Configurator (§4.5). Assigning a PSU Config to SubCircuits enables the Battery/PSU section on the host element, including EN 54-4 battery sizing and load checks. |
| **V-drop** | Voltage drop across a cable run. Pulse calculates this for SubCircuits to confirm the end-of-line voltage stays above the device minimum. |
| **EOL resistor** | End-of-Line resistor — placed at the far end of a NAC circuit to allow the panel to supervise the wiring. Its value affects V-drop calculations. |
| **Topology Tree** | The left-hand panel in Pulse showing the Panel → Loop → Device hierarchy of all collected fire alarm elements. |
| **Inspector** | The detail panel that shows properties of whatever is currently selected in the Topology Tree. |
| **Dashboard** | The System Intelligence panel showing capacity gauges, health issues, distribution, and cabling info for the selected panel, loop, or SubCircuit. |
| **BOQ** | Bill of Quantities — a configurable schedule of all collected devices with grouping, sorting, and custom formula columns. |
| **Refresh** | The action that re-reads the Revit model and rebuilds the topology, diagram, and dashboard. Run this after any model changes. |
| **Symbol Mapping** | The table that links a DeviceType string from Revit to a vector symbol shown in the diagram. |
| **Symbol Designer** | The built-in editor for creating and modifying vector symbols used in the diagram. |
| **Run System Check** | Exports a structured plain-text system summary to the clipboard for pasting into an AI assistant. |
| **Level line** | A horizontal reference line drawn in the diagram at the elevation of a Revit level where devices exist. |
| **Rank** | The vertical order of a loop in the diagram when two or more loops share the same level elevation. |

---

## Table of Contents

0. [Glossary](#glossary)
1. [Overview](#1-overview)
2. [Launching the Plugin](#2-launching-the-plugin)
3. [Initial Setup — Parameter Mapping](#3-initial-setup--parameter-mapping)
4. [Device Configurator](#4-device-configurator)
   - 4.1 [Control Panels](#41-control-panels)
   - 4.2 [Loop Modules](#42-loop-modules)
   - 4.3 [Wire Types](#43-wire-types)
   - 4.4 [Paper Sizes](#44-paper-sizes)
   - 4.5 [PSU Configs](#45-psu-configs)
5. [Main Window Layout](#5-main-window-layout)
6. [Topology Tree](#6-topology-tree)
   - 6.1 [Hierarchy](#61-hierarchy)
   - 6.2 [Selecting & Highlighting in Revit](#62-selecting--highlighting-in-revit)
   - 6.3 [Assigning Panel & Loop Configurations](#63-assigning-panel--loop-configurations)
   - 6.4 [Assigning Wire Types to Loops](#64-assigning-wire-types-to-loops)
7. [Inspector Panel](#7-inspector-panel)
8. [Diagram Panel](#8-diagram-panel)
   - 8.1 [Navigation](#81-navigation)
   - 8.2 [Flip, Extra Wires & Rank](#82-flip-extra-wires--rank)
   - 8.3 [Level Lines & Elevation Offsets](#83-level-lines--elevation-offsets)
9. [System Intelligence Dashboard](#9-system-intelligence-dashboard)
   - 9.1 [Capacity Section](#91-capacity-section)
   - 9.2 [Health Status Section](#92-health-status-section)
   - 9.3 [Distribution Section](#93-distribution-section)
   - 9.4 [Cabling & Spatial Section](#94-cabling--spatial-section)
10. [Cable Length Calculation](#10-cable-length-calculation)
11. [NAC SubCircuit Management](#11-nac-subcircuit-management)
    - 11.1 [Creating a SubCircuit](#111-creating-a-subcircuit)
    - 11.2 [Adding & Removing Devices](#112-adding--removing-devices)
    - 11.3 [Wire Type & V-Drop Settings](#113-wire-type--v-drop-settings)
12. [Circuit Metrics (SubCircuit Gauges)](#12-circuit-metrics-subcircuit-gauges)
    - 12.1 [Normal Load & Alarm Load](#121-normal-load--alarm-load)
    - 12.2 [V-Drop Calculation](#122-v-drop-calculation)
    - 12.3 [Remaining Voltage](#123-remaining-voltage)
    - 12.4 [PSU Host Element — Battery / PSU Section](#124-psu-host-element--battery--psu-section)
13. [3D Wire Routing Visualisation](#13-3d-wire-routing-visualisation)
14. [Custom Symbol Mapping](#14-custom-symbol-mapping)
15. [Custom Symbol Designer](#15-custom-symbol-designer)
16. [Bill of Quantities (BOQ) Window](#16-bill-of-quantities-boq-window)
    - 16.1 [Column Management](#161-column-management)
    - 16.2 [Custom Formula Columns](#162-custom-formula-columns)
    - 16.3 [Grouping & Aggregation](#163-grouping--aggregation)
    - 16.4 [Sorting](#164-sorting)
    - 16.5 [Export & Import Settings](#165-export--import-settings)
17. [AI System Check (Run System Check)](#17-ai-system-check-run-system-check)
18. [Validation Rules Reference](#18-validation-rules-reference)
19. [Data Persistence Reference](#19-data-persistence-reference)

---

## 1. Overview

**Pulse — Fire Alarm** is a Revit plugin for managing addressable fire alarm systems. Instead of working element-by-element, Pulse gives you a **system-first view**: every device is displayed in a live Panel → Loop → Device hierarchy, schematic diagrams are generated automatically, and all capacity limits, cable lengths, and wiring calculations are updated instantly whenever you press Refresh.

The main capabilities are:

| Capability | What it does |
|------------|-------------|
| **Topology tree** | Live hierarchy of all collected fire-alarm devices, organized by panel and loop |
| **Schematic diagram** | Auto-generated wiring diagram drawn to level lines |
| **Validation engine** | 9 built-in rules that flag address conflicts, missing data, and SubCircuit wiring issues |
| **System Intelligence Dashboard** | Live capacity gauges, health issues, device distribution breakdown, and cable-length summary |
| **NAC SubCircuit management** | Model notification appliance circuits hanging off PSU/output modules, with V-drop calculation |
| **3D wire routing** | Draw and measure orthogonal model lines in Revit for each loop or SubCircuit |
| **Custom symbols** | Design your own vector symbols and map them to device types in the diagram |
| **Bill of Quantities** | Configurable device schedule with grouping, sorting, and custom formula columns |
| **AI System Check** | Export a structured system summary to your clipboard and paste into any AI assistant |

---

## 2. Launching the Plugin

1. Open Autodesk Revit and load your fire-alarm model.
2. Go to the **RK Tools** ribbon tab.
3. Click the **Fire Alarm** button.
4. The Pulse main window appears. Pulse is a **singleton window** — clicking the button while the window is already open simply brings it to the front.
5. On first launch Pulse automatically runs a **Refresh** to collect all fire-alarm devices from the model.

> **Note:** Pulse reads the active Revit document. If you switch documents inside Revit, close and re-open the Pulse window to refresh against the new document.

---

## 3. Initial Setup — Parameter Mapping

Before Pulse can read your model data, it needs to know which Revit parameters on your devices correspond to the logical fields it uses internally. This is done in the **Settings** dialog.

### Opening Settings

Click the **Settings (⚙)** button in the Pulse toolbar.

### What to configure

The dialog presents a table with two columns:

| Column | Meaning |
|--------|---------|
| **Logical Name** | The internal field Pulse needs (read-only) |
| **Revit Parameter Name** | The actual parameter name in your Revit family (editable) |

Configure each row so the Revit parameter name matches the parameter that carries that value in your model:

| Logical Name | What it maps to | Required? |
|---|---|:---:|
| **Panel** | Parameter on devices that names the control panel they belong to (e.g. `"Panel"`, `"FA_Panel"`) | ✔ |
| **Loop** | Parameter that names or numbers the loop (e.g. `"Loop"`, `"Loop Nr"`) | ✔ |
| **Address** | The device address on the loop (e.g. `"Aadress"`, `"Device Address"`) | ✔ |
| **DeviceType** | A classification string like `"Smoke Detector"` or `"Sounder"` | — |
| **CurrentDrawNormal** | Normal-mode current in mA (used for loop load gauges) | — |
| **CurrentDrawAlarm** | Alarm-mode current in mA (used for loop load gauges and V-drop) | — |
| **DeviceId** | An optional unique device identifier | — |
| **PanelConfig** | Parameter where Pulse writes the assigned control-panel config name back to the FACP element | — |
| **LoopModuleConfig** | Parameter where Pulse writes the assigned loop-module config name back to devices | — |
| **PanelElementCategory** | The Revit category where your FACP panel board lives (e.g. `"Electrical Equipment"`) | — |
| **PanelElementNameParam** | Parameter on FACP elements that holds the panel name (e.g. `"Mark"`) | — |
| **CircuitElementId** | Parameter on devices that stores the integer ElementId of their electrical circuit | — |
| **Wire** | Parameter where Pulse writes the assigned wire-type name back to loop devices | — |
| **NominalVoltage** | Parameter on PSU/output module elements that stores the supply voltage (e.g. 24 V) — used for V-drop | — |

Also set the **Revit Category** field at the top to the category Pulse should scan for fire-alarm devices (default: `"Fire Alarm Devices"`).

### Saving

Click **Save**. Pulse immediately applies the new mappings and re-runs a Refresh. Click **Reset to Defaults** at any time to revert the built-in default names.

> Settings are stored in `%APPDATA%\Pulse\device-config.json` and are carried across documents and Revit sessions.

---

## 4. Device Configurator

The **Device Configurator** (toolbar button: **Configure Devices**) is where you define your equipment library — the specific panel models, loop-card models, and cable types that are installed in your project. This information powers the capacity gauges and V-drop calculations.

### 4.1 Control Panels

Each entry represents one model of Fire Alarm Control Panel (FACP).

| Field | Description |
|-------|-----------|
| **Name** | Identifier shown in the assignment dropdowns (e.g. `"Aritech CD3246"`) |
| **Panel Addresses** | The address slot(s) this panel occupies in the system |
| **Addresses Per Loop** | Maximum SLC device addresses per loop card |
| **Max Loop Count** | Maximum number of loop cards the panel can accept |
| **Max mA Per Loop** | Maximum milliamps the loop circuitry can supply per loop |
| **Max Addresses (override)** | Set this to a number greater than 0 to fix the total address ceiling for the whole panel. When 0, the ceiling is calculated as `Addresses Per Loop × actual loop count`. |

> **Capacity formula (panel-level):**
>
> $$\text{Address ceiling} = \begin{cases} \text{Max Addresses} & \text{if Max Addresses} > 0 \\ \text{Addresses Per Loop} \times \text{loop count} & \text{otherwise} \end{cases}$$
>
> $$\text{mA ceiling} = \text{Max mA Per Loop} \times \text{loop count}$$

Add, edit, or delete entries with the **+**, pencil, and trash buttons. **Save** commits to `device-config.json`.

### 4.2 Loop Modules

Each entry represents a loop expansion card or loop controller model.

| Field | Description |
|-------|-----------|
| **Name** | Identifier shown in loop assignment dropdowns |
| **Panel Addresses** | Address(es) this module occupies on the parent panel |
| **Addresses Per Loop** | Maximum device addresses on this module's loop |
| **Max Loop Count** | Number of loop outputs this module provides |
| **Max mA Per Loop** | Maximum milliamps per loop output |

> **Capacity formula (loop-level):**
>
> $$\text{Address usage\%} = \frac{\text{devices on loop}}{\text{Addresses Per Loop}} \times 100$$
>
> $$\text{mA usage\%} = \frac{\sum \text{CurrentDraw}_{\text{normal}}}{\text{Max mA Per Loop}} \times 100$$

### 4.3 Wire Types

Wire type entries are used in two places: the **loop wire assignment** dropdown in the topology tree, and the **SubCircuit wire assignment** for V-drop calculations.

| Field | Description |
|-------|-----------|
| **Name** | Identifier (e.g. `"FP200 2×1.5mm²"`) |
| **Core Count** | Number of conductors (typically 2 for a loop) |
| **Core Size mm²** | Cross-section area of each conductor in mm² — used to derive resistance when no direct value is given |
| **Resistance Per Metre (Ω/m)** | Measured resistance per metre at 20 °C. If you enter this from a datasheet it overrides the calculated value and gives more accurate V-drop results. Leave at 0 to use the calculated value. |
| **Color** | Cable colour designation (e.g. `"Red/Black"`) — shown on the diagram |
| **Shielding** | Tick if the cable has an overall screen/shield |
| **Fire Resistance** | Designation such as `"FP200"` or `"E30"`, or leave blank |

> **Resistance calculation when no datasheet value is given:**
>
> $$R_{\text{per metre}} = \frac{\rho_{\text{Cu}}}{A} = \frac{0.0175\ \Omega{\cdot}\text{mm}^2/\text{m}}{A\ [\text{mm}^2]}$$

### 4.4 Paper Sizes

Define named paper sizes used for diagram printing and export.

| Field | Description |
|-------|-----------|
| **Name** | e.g. `"A1"`, `"A3 Landscape"` |
| **Width / Height (mm)** | Paper dimensions in millimetres |
| **Margins (mm)** | Left, top, right, bottom margins from the paper edge to the drawing area |
---

### 4.5 PSU Configs

Each entry represents a **NAC Power Supply Unit** (or output module) model. PSU configs power the Battery/PSU section of the dashboard when assigned to SubCircuits on a host element.

| Field | Description |
|-------|-------------|
| **Name** | Identifier shown in the PSU assignment dropdown on SubCircuit cards (e.g. `"Hochiki HPS-4"`) |
| **Voltage (V)** | Nominal output voltage of the PSU, typically `24` V for an EN 54-4 system. Pulse uses this to calculate per-battery-unit voltage (`V / 2`) and V-drop gauges. |
| **Output Current (A)** | Maximum rated output current in Amperes. Used as the gauge ceiling for the alarm load gauge and for PSU overload health checks. |
| **Battery Unit (Ah)** | Capacity of one individual battery unit. Set to `0` to skip battery sizing for this PSU. |
| **Required Standby (h)** | Standby duration the battery must support (EN 54-4 default: 24 h). |
| **Required Alarm (min)** | Alarm duration the battery must support (EN 54-4 default: 30 min). |
| **Safety Factor** | Multiplier applied to the raw calculated capacity (EN 54-4 default: 1.25). |

Add, edit, or delete PSU configs with the **+**, pencil, and trash buttons. **Save** commits to `device-config.json`.

> **Assigning a PSU config to SubCircuits:** on the SubCircuit card in the Topology Tree, use the **PSU** dropdown to select the appropriate config. All SubCircuits hosted by the same output module should share one PSU config; they are all used together when calculating combined battery requirements.
---

## 5. Main Window Layout

```
┌──────────────────────────────────────────────────────────┐
│  Title Bar — Pulse · Fire Alarm        [−] [□] [×]       │
├────────────────────┬─────────────────────────────────────┤
│  Toolbar           │  Refresh · Settings · Configurator · │
│                    │  Symbols · BOQ · System Check        │
├────────────────────┼──────────────────────────────────────┤
│                    │                                       │
│  TOPOLOGY TREE     │   DIAGRAM PANEL  (schematic)         │
│  Panel             │                                       │
│   └─ Loop          │                                       │
│       └─ Device    │                                       │
│                    │                                       │
├────────────────────┼──────────────────────────────────────┤
│  INSPECTOR         │  SYSTEM INTELLIGENCE DASHBOARD        │
│  (selected entity) │  Capacity · Health · Distribution ·  │
│                    │  Cabling & Spatial · Quick Actions    │
├────────────────────┴──────────────────────────────────────┤
│  Status bar: device count · warnings · errors             │
└──────────────────────────────────────────────────────────┘
```

- **Topology Tree** — left pane, top half: Panel → Loop → Device hierarchy.
- **Diagram Panel** — right pane, top half: auto-generated schematic wiring diagram.
- **Inspector** — left pane, bottom half: properties of the currently selected entity.
- **System Intelligence Dashboard** — right pane, bottom half: live capacity gauges, health issues, distribution, and cabling data.
- **Status bar** — across the bottom: total device count, active warning count, and error count.

---

## 6. Topology Tree

### 6.1 Hierarchy

After a Refresh the tree is populated as:

```
Panel 1
 ├── Loop 1  [loop module config dropdown]  [wire dropdown]  [route wire button]
 │    ├── Device  001 — Smoke Detector
 │    ├── Device  002 — Call Point
 │    └── ...
 ├── Loop 2  ...
 └── ...
Panel 2
 └── ...
```

- Panels and loops are sorted **numerically** (Panel 1, Panel 2, …; Loop 1, Loop 2, …).
- Devices within each loop are sorted by **address** (numerically first, then alphabetically as a fallback).
- A PSU/Output-Module device also shows its **SubCircuit ports** when expanded.

Node type indicators visible in the tree:

| Icon / label | Meaning |
|---|---|
| Warning badge | One or more validation warnings on this node or its children |
| Error badge | One or more validation errors |
| Device count sub-text on a loop | How many devices are on that loop |

### 6.2 Selecting & Highlighting in Revit

Clicking any node in the topology tree:
1. **Selects** the corresponding element(s) in Revit and zooms the Revit view to them.
2. **Highlights** the element with a temporary colour override.
3. **Updates the Inspector** panel to show details for that entity.

Click **Reset Overrides** (toolbar) to clear all temporary colour overrides in the model.

Enable the **"Show Warnings Only"** toggle (toolbar) to collapse all nodes that have no warnings, making it easier to navigate to problems.

### 6.3 Assigning Panel & Loop Configurations

Assigning a configuration links a physical panel or loop in your model to a hardware spec in your device library, which enables the capacity gauges.

**To assign a Control Panel config:**
1. Expand the panel node in the tree.
2. Use the config dropdown next to the panel name.
3. Select the matching panel model from the list.
4. Pulse immediately writes the config name back to the FACP element's `PanelConfig` Revit parameter and refreshes the capacity gauges.

**To assign a Loop Module config:**
1. Find the loop node inside the panel.
2. Use the config dropdown next to the loop name.
3. Select the matching loop-card model.
4. Pulse writes the config name to the `LoopModuleConfig` parameter on every device in that loop.

### 6.4 Assigning Wire Types to Loops

Each loop also has a **wire type dropdown**:
1. Find the loop node.
2. Select a wire from the dropdown (populated from your Wire Types library).
3. Pulse writes the wire name to the `Wire` Revit parameter on all devices in the loop, and updates the diagram canvas with the wire's colour.

---

## 7. Inspector Panel

The Inspector shows details for whatever node is currently selected in the topology tree or diagram.

| Entity | Information shown |
|--------|------------------|
| **Panel** | Name, entity ID, device count, warning/error count, capacity gauges (addresses & mA) if a config is assigned |
| **Loop** | Name, entity ID, device count, warning/error count, capacity gauges if a loop-module config is assigned |
| **Device** | Name, Revit element ID, address, loop, panel, device type, current draw (normal & alarm modes in mA) |
| **SubCircuit** | Name (editable), device count, cable length, wire type, V-Drop limit %, cable temperature, EOL resistor |

### Editing SubCircuit properties in the Inspector

When a SubCircuit is selected, several fields in the **PROPERTIES** section are editable inline:

- **Name** — double-click the title to rename. Press **Enter** or click elsewhere to confirm; **Escape** to cancel.
- **V-Drop Limit %** — double-click the value to edit. Default is `16.7 %` (≈ 4 V on a 24 V NAC — the typical EN 54-4 limit).
- **Cable Temperature (°C)** — double-click to edit. Default is `20 °C`. Increasing this derate the resistance and widens the V-drop result.
- **EOL Resistor (Ω)** — double-click to enter an end-of-line resistor value. When non-zero, a supervisory current ($V_{\text{nom}} / R_{\text{EOL}}$) is added to the normal-mode gauge.

### Editing device current draw

When a Device is selected, the **Current Draw (normal)** and **Current Draw (alarm)** values are shown and can be edited inline. Saving writes the values back to the corresponding Revit parameters.

---

## 8. Diagram Panel

The Diagram panel renders an auto-generated schematic wiring diagram based on the current topology data and Revit building levels.

### 8.1 Navigation

| Action | Result |
|--------|--------|
| **Ctrl + Scroll** | Zoom in / out |
| **Middle-drag** | Pan |
| **Double-click middle mouse** | Fit the diagram to the paper boundary |
| **Right-click on canvas** | Context menu (Flip loop, Edit wires, etc.) |

### 8.2 Flip, Extra Wires & Rank

**Flip** — by default loops draw on the left side of their panel box. Right-click a loop wire → **Flip** to move it to the right side. The setting persists per loop.

**Extra wire lines** — a loop normally shows 2 wire lines. Right-click → **Add Wire Line** to add up to 6 extra lines (8 total), useful to indicate multi-conductor cables. **Remove Wire Line** reduces the count.

**Loop rank** — when multiple loops appear on the same side at the same elevation, their vertical order is determined by rank. Right-click a loop → **Move Up / Move Down** to swap its rank.

All diagram settings (flip, extra lines, rank) are saved per document in Revit Extensible Storage and restored automatically the next time the file is opened.

### 8.3 Level Lines & Elevation Offsets

The diagram draws a horizontal line for each Revit level where devices exist.

**Show / hide / delete level lines** — right-click a level line for options:
- **Hide line** — hides the level line but retains its position data (can be restored).
- **Hide text** — hides the level name label only.
- **Delete** — removes the level line; can be restored from the non-visible list.
- **Restore** — restores a previously hidden or deleted line.

**Drag to move** — switch to **Move** mode (toolbar toggle) then drag a level line up or down to adjust its elevation offset. The position is saved to Extensible Storage and persists across sessions.

---

## 9. System Intelligence Dashboard

The System Intelligence Dashboard is the right-side lower panel, providing a live read-out whenever a panel, loop, or SubCircuit is selected.

### 9.1 Capacity Section

Only visible when a panel or loop with an **assigned configuration** is selected.

When a **PSU host element** (an output module that owns NAC SubCircuits) is selected, the section switches to **CIRCUIT METRICS** mode instead, showing combined Normal Load and Alarm Load gauges across all hosted SubCircuits. See [Section 12.4](#124-psu-host-element--battery--psu-section) for the Battery/PSU section that also appears.

| Gauge | What it shows |
|-------|-------------|
| **Addresses** | Devices used vs. the maximum addresses for the selection |
| **mA Load** | Total normal-mode current draw vs. the maximum milliamps |

Both gauges display a formatted summary like `"178 / 254 (70%)"` and change colour based on usage:

| Usage | Colour | Threshold |
|-------|--------|-----------|
| Normal | Green | < 70 % |
| Warning | Amber | 70 % – 84 % |
| Critical | Red | ≥ 85 % |

> **Address utilisation formula:**
>
> $$\text{Utilisation \%} = \frac{\text{Addresses Used}}{\text{Address Max}} \times 100$$
>
> $$\text{mA Utilisation \%} = \frac{\sum \text{CurrentDraw}_{\text{normal}}}{\text{mA Max}} \times 100$$

Remaining-capacity summaries ("X addresses remaining", "Y mA remaining") appear below the gauges.

### 9.2 Health Status Section

Lists all active rule violations scoped to the selected panel or loop (or system-wide when nothing is selected). Each row shows:
- A coloured status badge (⚠ Warning or ✖ Error).
- A description of the issue and how many occurrences were found.
- A **Highlight** button that temporarily colours the affected elements in Revit, letting you navigate to them.

Health issues also include capacity overload rows when address or mA load exceeds the warning/critical thresholds.

### 9.3 Distribution Section

A breakdown of device types within the selected scope, sorted from most to least common.

Each row shows a device type name, the count, and the fraction it represents of the total.

> **Fraction formula:**
>
> $$\text{Fraction} = \frac{\text{count of type}}{\text{total devices in scope}}$$

### 9.4 Cabling & Spatial Section

Shows cable-length estimates for each loop in the selected panel, or for a single loop when one is selected.

| Field | Meaning |
|-------|---------|
| **Length (m)** | Estimated or measured cable length for this loop |
| **Device count** | Number of devices with valid coordinates that were used in the calculation |
| **m/device** | Metres of cable per device — a useful cross-check |
| **Total** | Sum of all loop lengths in scope |
| **Longest loop** | The loop with the greatest cable length |

> **Metres-per-device formula:**
>
> $$\text{m/device} = \frac{\text{Cable Length (m)}}{\text{Routed Device Count}}$$

---

## 10. Cable Length Calculation

Pulse calculates cable length for each loop and SubCircuit using one of two methods, applied in this priority order:

### Method 1 — Routed Wire Lines (primary)

If you have used the **3D Wire Routing** feature to draw model lines for a loop or SubCircuit, Pulse reads those lines back on every Refresh and sums their total curve length. This gives you the most accurate measurement because it reflects the actual routed path in the model.

The lines are created in Revit internal units (feet) and converted to metres on display.

### Method 2 — Manhattan Estimate (fallback)

When no routed lines exist, Pulse estimates cable length using a **Manhattan (right-angle) routing** algorithm:

1. Start at the panel origin.
2. Visit each device in **ascending address order**.
3. Return to the panel origin.
4. For every consecutive pair of waypoints, compute:

$$d = |\Delta x| + |\Delta y| + |\Delta z|$$

where Δx, Δy, Δz are the coordinate differences in Revit internal feet.

5. Sum all segment distances and convert to metres:

$$\text{Cable Length (m)} = \left( \sum_{i=1}^{n} d_i \right) \times 0.3048$$

This represents a cable running along walls at right angles (horizontal X run + horizontal Y run + vertical rise/drop), which matches real-world conduit/cable-tray routing.

Devices that have no location coordinates are skipped and noted as "skipped." If the panel element has no location, routing starts from the first device instead.

---

## 11. NAC SubCircuit Management

A **SubCircuit** models a one-way Notification Appliance Circuit (NAC) output — a run of sounder/strobe/horn devices driven by a PSU or output module on the loop.

Unlike a detector loop (which is bi-directional and closed), a NAC is a single cable run and is not part of the addressable loop. Pulse handles it separately so you can calculate V-drop and monitor load independently.

### 11.1 Creating a SubCircuit

1. In the Topology tree, locate the **PSU or output-module device** that has the NAC outputs.
2. Expand the device node to reveal its output ports.
3. Click the **+** button on an output port.
4. Type a name for the SubCircuit (e.g. `"NAC-01 Level 3"`) and confirm.
5. The new SubCircuit appears as a child node under the device.

### 11.2 Adding & Removing Devices

**Add devices:**
- Click the **+** button on the SubCircuit card.
- A dropdown lists all sounder/NAC devices that are not yet assigned to any SubCircuit. Select one to add it.
- Alternatively, click **Pick from Revit** to minimise Pulse and click directly on elements in the Revit viewport. You can select multiple elements at once.

**Remove a device:**
- Click the **−** button beside a device row in the SubCircuit card to remove it. The device is released back to the unassigned pool.

**Delete the SubCircuit:**
- Click the **trash (🗑)** icon on the SubCircuit card header. All members are released.

### 11.3 Wire Type & V-Drop Settings

Each SubCircuit has its own independent settings:

| Setting | Where to change | Default |
|---------|----------------|---------|
| **Wire type** | Dropdown on the SubCircuit card in the tree | — |
| **Name** | Double-click the title in the Inspector | — |
| **V-Drop Limit %** | Double-click value in Inspector PROPERTIES | 16.7 % (≈ 4 V on 24 V) |
| **Cable Temperature (°C)** | Double-click value in Inspector PROPERTIES | 20 °C |
| **EOL Resistor (Ω)** | Double-click value in Inspector PROPERTIES | 0 (no EOL) |
| **Min Device Voltage (V)** | Stored internally | 16 V |

All SubCircuit data is saved to Revit Extensible Storage and reloaded when the document is reopened.

---

## 12. Circuit Metrics (SubCircuit Gauges)

When a **SubCircuit** node is selected, the System Intelligence Dashboard switches to **CIRCUIT METRICS** mode and shows four gauges.

### 12.1 Normal Load & Alarm Load

These gauges show the aggregate current draw of all devices assigned to the SubCircuit, measured against the loop module's mA capacity.

| Gauge | Current source | Max |
|-------|---------------|-----|
| **Normal Load** | Sum of `CurrentDrawNormal` for all SubCircuit members (+supervisory current if EOL resistor set) | Host loop module `MaxMaPerLoop` |
| **Alarm Load** | Sum of `CurrentDrawAlarm` for all SubCircuit members | Host loop module `MaxMaPerLoop` |

> **Supervisory current (when EOL resistor > 0):**
>
> $$I_{\text{supervisory}} = \frac{V_{\text{nom}}}{R_{\text{EOL}}}$$
>
> This is added to the normal-mode load shown on the Normal Load gauge.

### 12.2 V-Drop Calculation

The **V-Drop gauge** shows how much voltage is lost along the NAC cable run under alarm conditions (all devices drawing alarm current simultaneously — worst case).

**Full formula:**

$$V_{\text{drop}} = I_{\text{alarm}} \times 2 \times R_{\text{per metre}} \times L$$

where:
- $I_{\text{alarm}}$ = total alarm-mode current in amps (sum of all SubCircuit members' alarm current ÷ 1000)
- $R_{\text{per metre}}$ = resistance per metre of one conductor (Ω/m), temperature-derated (see below)
- $L$ = cable length in metres (from routed lines or Manhattan estimate)
- The factor of **2** accounts for the outgoing and return conductors

**Wire resistance (from datasheet):** if the wire type has a `Resistance Per Metre` entered in the Device Configurator, that value is used directly.

**Wire resistance (calculated):** if no datasheet value is given, resistance is derived from copper resistivity:

$$R_{\text{per metre, 20°C}} = \frac{\rho_{\text{Cu}}}{A} = \frac{0.0175\ \Omega{\cdot}\text{mm}^2/\text{m}}{A\ [\text{mm}^2]}$$

**Temperature derating:** copper resistance rises with temperature. If a cable temperature other than 20 °C is set in the Inspector, the resistance is derated:

$$R(T) = R_{20°C} \times \bigl(1 + 0.00393 \times (T - 20)\bigr)$$

where 0.00393 /°C is the temperature coefficient of resistance for copper (per BS 7671 Table 4C1).

**Gauge maximum** is scaled to the configured V-Drop limit:

$$V_{\text{drop, max}} = V_{\text{nom}} \times \frac{\text{VDropLimitPct}}{100}$$

For example, with a 24 V supply and the default 16.7 % limit: $24 \times 0.167 = 4.0\ \text{V}$.

When the V-drop exceeds this limit the gauge turns red and a matching **Health Issue** row appears in the Health Status section.

### 12.3 Remaining Voltage

The **Remaining V gauge** shows the voltage arriving at the far end of the NAC run:

$$V_{\text{remaining}} = V_{\text{nom}} - V_{\text{drop}}$$

The gauge maximum equals the nominal supply voltage. Pulse warns you when this value falls below the device minimum voltage (default 16 V — the typical EN 54-4 / UL 864 NAC appliance minimum).

> **`NominalVoltage` must be mapped** — this value is read from the PSU element via the `NominalVoltage` parameter mapping set in Settings. If your PSU element does not have this parameter, voltage gauges will not appear.

---

### 12.4 PSU Host Element — Battery / PSU Section

When you select the **PSU or output-module element** (the host device that owns one or more SubCircuit outputs) and it has a **PSU Config** assigned, the System Intelligence Dashboard shows a **BATTERY / PSU** section.

#### Header and load gauges

The header reads `"NAC Host · N circuits · M devices"`. Two gauges show the combined load aggregated across all hosted SubCircuits:

| Gauge | What it shows |
|-------|---------------|
| **Normal Load** | Total standby current of all devices on all hosted SubCircuits |
| **Alarm Load** | Total alarm current of all devices on all hosted SubCircuits |

The gauge ceiling (`ScMaMax`) is resolved in this priority order:
1. `OutputCurrentMaxMa` Revit parameter on the host element (if mapped and non-zero).
2. `OutputCurrentA × 1000` from the assigned PSU Config.
3. `max(Normal, Alarm) × 1.25` (80 % heuristic fallback).

#### Battery / PSU rows

| Row | Example | Notes |
|-----|---------|-------|
| **Capacity** | `2× 12V/12.0Ah = 24.0 Ah  (req. 15.47 Ah)` | Recommended battery count, per-unit voltage and capacity, total installed Ah, required Ah (from formula) |
| **Standby load** | `510 mA standby` | Combined standby current |
| **Alarm load** | `270 mA alarm` | Combined alarm current |
| **PSU output** | `270 / 2000 mA (14 %)` | Alarm current vs. rated PSU output current |
| **Formula** | Four-line equation trace (see below) | Shown as a small note at the bottom |

#### EN 54-4 battery sizing formula

The required battery capacity is calculated as:

$$C_{\text{req}} = \left( \frac{I_s}{1000} \times t_s + \frac{I_a}{1000} \times \frac{t_a}{60} \right) \times f$$

where:
- $I_s$ = total standby (normal) current in mA
- $t_s$ = required standby duration in hours (from PSU Config, default 24 h)
- $I_a$ = total alarm current in mA
- $t_a$ = required alarm duration in minutes (from PSU Config, default 30 min)
- $f$ = safety factor (from PSU Config, default 1.25 per EN 54-4)

The dashboard renders the full four-step equation trace:
```
C = (Is/1000 × ts + Ia/1000 × ta/60) × f
  = (510/1000 × 24 + 270/1000 × 30/60) × 1.25
  = (12.240 + 0.135) × 1.25
  = 15.47 Ah
```

#### Battery recommendation algorithm

Pulse selects the smallest standard VRLA/AGM battery unit size that meets the requirement when paired with an **even** number of units (EN 54-4 requires a minimum of 2 batteries):

1. Iterate counts: 2, 4, 6, …, 40.
2. For each count, test standard sizes in ascending order: 1.2, 2.1, 2.3, 3.2, 4.5, 7.0, 7.2, 12, 17, 18, 24, 26, 33, 38, 40, 45, 55, 65, 80, 100 Ah.
3. The first pair where `count × size ≥ C_req` is selected.

The per-unit voltage is `VoltageV / 2` — for a 24 V system each battery unit operates at 12 V (two batteries in series forming the 24 V bus).

> **Requires a PSU Config to be assigned.** If no PSU Config is assigned to the hosted SubCircuits, the Battery/PSU section is hidden. Assign one via the PSU dropdown on any SubCircuit card in the Topology Tree.

---

## 13. 3D Wire Routing Visualisation

Every loop card and SubCircuit in the topology tree has a **route wire toggle button** (wire icon). Clicking it draws a set of orthogonal model lines directly into your Revit model.

### How it works

1. Click the **wire route button** on a loop or SubCircuit card.
2. Pulse calculates a Manhattan-routed path (panel → devices in address order → panel) and draws model lines following that path in the active 3D view.
3. Each loop or SubCircuit gets its own Revit line-style sub-category: `Pulse Wire – {panel} – {loop}` or `Pulse Wire – {device} – {subcircuit}`. This lets you toggle individual loops on and off in Revit's Visibility/Graphics settings independently.
4. The button icon turns **red** when routing lines are visible for that node.
5. Click the button again to **clear** the lines for just that loop or SubCircuit.

### Cable length feedback

On the next **Refresh**, Pulse reads the drawn model lines back automatically. Their total measured length replaces the Manhattan estimate for that loop or SubCircuit's cable length number everywhere (topology card, cabling section, V-drop calculation). This is the recommended workflow for finalising cable length figures.

### Persistence

The toggle state (visible/hidden) is stored in Revit Extensible Storage. Re-opening the project restores all routing lines to their previous visibility state.

---

## 14. Custom Symbol Mapping

The **Symbol Mapping** window (toolbar button: **Symbol Mapping**) lets you assign a custom vector symbol to each device type, so the schematic diagram renders recognisable icons instead of generic squares.

### How to map symbols

1. Open Symbol Mapping from the toolbar.
2. The grid shows one row per unique device-type string found in the model (and how many devices use it).
3. Use the **search box** to filter device types.
4. In the symbol dropdown for each row, select a symbol from your custom library. You can also type a name freely.
5. Click **Save**. Mappings are persisted to Revit Extensible Storage and applied instantly in the diagram.

### Managing the library

- Click **+ New Symbol** to open the Symbol Designer and draw a new symbol.
- Click the **pencil (✏)** icon on an existing symbol row to re-open the Designer for editing.
- Click **Clear All** to remove all current mappings.

---

## 15. Custom Symbol Designer

The **Symbol Designer** lets you draw custom vector symbols using geometric primitives.

### Canvas

The canvas represents a viewbox with a configurable width and height in millimetres (default 20 × 20 mm). The snap grid helps you draw precisely.

### Drawing tools

| Tool | What it draws | How to use |
|------|-------------|-----------|
| **Line** | A straight line segment | Click start point, click end point |
| **Polyline** | A connected series of line segments | Click vertices; double-click to finish |
| **Circle** | A circle | Click centre point, click a point on the edge to set radius |
| **Rectangle** | An axis-aligned rectangle | Click top-left corner, click bottom-right corner |

### Shape properties

Select any shape to edit:
- **Stroke colour** (HTML hex, e.g. `#FF0000`)
- **Stroke thickness** (mm)
- **Fill colour** (when fill is enabled)
- **Is Filled** — toggle fill on/off for circles, rectangles, and closed polylines
- **Is Closed** — for polylines, connect the last vertex back to the first  

### Snap origin

The snap origin point defines the alignment anchor — when the symbol is placed on the diagram, this point is centred on the device's location. Set it by editing the **Snap Origin X/Y** values in the properties panel.

### Import from DXF or SVG

Click **Import DXF** or **Import SVG** to import geometry from an external file. Imported geometry is converted to the supported primitive types.

### Undo / Redo

Press **Ctrl+Z** to undo and **Ctrl+Y** (or **Ctrl+Shift+Z**) to redo drawing operations.

### Saving

Click **Save** to add the symbol to your library (`%APPDATA%\Pulse\custom-symbols.json`). Existing symbols with the same ID are replaced. The symbol immediately becomes available in the Symbol Mapping window.

---

## 16. Bill of Quantities (BOQ) Window

The **BOQ window** (toolbar button: **Open BOQ**) is a modeless, always-on-top schedule panel showing every fire-alarm device in a configurable grid.

### Opening & Refreshing

Click **Open BOQ** from the Pulse toolbar. The window stays open independently of the main Pulse window. Click **Refresh** inside the BOQ window to re-pull data from Revit.

### 16.1 Column Management

Click the **Settings (⚙)** button in the BOQ toolbar to open the settings drawer on the right side.

**Standard columns** (always available):

| Column | Content |
|--------|---------|
| Category | Revit category name |
| Family | Revit family name |
| Type | Revit type name |
| Level | Revit level name |
| Panel | Panel assignment from parameter mapping |
| Loop | Loop assignment from parameter mapping |
| Address | Device address |
| … | Any other parameters Pulse discovers on device elements are added automatically |

**Show / hide columns:**
- In the settings drawer, each column has a checkbox. Tick to show, untick to hide.
- The grid updates immediately.

**Re-order columns:**
- Select a column in the settings list, then use **Move Up** and **Move Down** to change its position.

**Apply** saves the column layout and triggers a visual refresh of the grid.

### 16.2 Custom Formula Columns

Click **+ Add Custom Column** in the settings drawer to create a column whose value is computed from other columns.

| Kind | What it does | Example |
|------|-------------|---------|
| **Concat** | Joins the source field values with a delimiter, even if some are empty | `FamilyName` + `" "` + `TypeName` → `"Detector S57°"` |
| **Join Delimited** | Same as Concat but skips empty/null source values | `Panel` + `"/"` + `Loop` → `"P1/L2"` |
| **Sum** | Adds numeric source fields; non-numeric values treated as 0 | `CurrentDrawNormal` + `CurrentDrawAlarm` |

After creation the custom column appears in the grid and in the settings column list. Use the **pencil** button to edit it or the **delete** button to remove it.

### 16.3 Grouping & Aggregation

Grouping collapses individual device rows into a summary table where each row represents a unique combination of grouped field values.

**To add a grouping rule:**
1. In the settings drawer, click **+ Add Group**.
2. Select the field key from the dropdown (only currently **visible** columns are listed).
3. Set a priority number (lower = outer group, higher = inner group).
4. Click **Apply**.

When grouping is active:
- One row per unique combination is shown.
- A **Count** column appears automatically at the far right, showing how many individual devices collapsed into each row.
- All other column values in a grouped row reflect the **first** device in the group.

Removing all grouping rules and clicking **Apply** restores individual device rows and hides the Count column.

### 16.4 Sorting

Sorting applies in addition to grouping.

**To add a sorting rule:**
1. Click **+ Add Sort**.
2. Select the field key.
3. Choose **Ascending** or **Descending**.
4. Set a priority (lower priority sorts first).
5. Click **Apply**.

Multiple sort rules are applied in priority order. Sort rules persist across Refresh and Revit sessions.

### 16.5 Export & Import Settings

- **Export Settings** — saves the current column visibility, order, grouping, and sorting configuration as a JSON file. Use this to share the layout with a colleague or back it up.
- **Import Settings** — loads a previously exported JSON file and applies it.

Settings are also auto-saved when the BOQ window is closed and restored on re-opening.

---

## 17. AI System Check (Run System Check)

The **Run System Check** button (toolbar or Quick Actions in the Dashboard) generates a structured plain-text summary of your entire fire alarm system and copies it to the clipboard.

The summary includes:
- Total device count, panel count, loop count, error and warning counts.
- Per-panel: assigned config name, address utilisation (used/max with %), mA load utilisation.
- Per-loop: device count, address and mA utilisation, estimated cable length, any duplicate address or missing address violations, assigned wire type, device-type distribution.
- All active health issues, categorised by severity.
- A review checklist asking the AI to assess compliance (NFPA 72 / EN 54), capacity headroom, loop balance, cable length concerns, and optimisation opportunities.

You paste the copied text directly into any AI assistant (ChatGPT, Claude, Copilot, etc.) and receive a structured compliance review or design suggestions. **No data leaves your machine** — Pulse only builds the text and copies it; it does not call any external API.

After copying, a confirmation popup appears briefly to confirm the text is on the clipboard.

---

## 18. Validation Rules Reference

Pulse runs nine built-in rules on every Refresh. Warnings and errors appear in the status bar, topology tree badges, and the Health Status section of the Dashboard.

---

### Device rules (always active)

| Rule | Severity | Triggered when |
|------|----------|---------------|
| **Missing Panel** | Warning | A device has no panel parameter value (empty or `"(No Panel)"`) |
| **Missing Loop** | Warning | A device has no loop parameter value (empty or `"(No Loop)"`) |
| **Missing Address** | Warning | A device has no address parameter value |
| **Duplicate Address** | **Error** | Two or more devices share the same address on the same loop |
| **Missing Required Parameter** | Warning | A device is missing a value for any of the three required logical parameters: Panel, Loop, or Address |

---

### SubCircuit rules (active when SubCircuits exist)

| Rule | Severity | Triggered when |
|------|----------|---------------|
| **SubCircuit Missing Trigger** | Warning | A SubCircuit's host output-module device is not assigned to any loop — it will never be triggered during a fire event |
| **SubCircuit Missing Fault Monitor** | Warning | SubCircuits are defined but no device with a PSU/fault-monitor role (matching keywords: `psu`, `fault`, `monitor`, `input`, `sounder fault`, `nac monitor`) was found anywhere in the topology |
| **SubCircuit Duplicate Member** | **Error** | A device appears in more than one SubCircuit |
| **SubCircuit Orphan Sounder** | Warning | A device whose type contains a sounder/notification keyword (`sounder`, `nac`, `notification`, `horn`, `bell`, `siren`, `speaker`, `strobe`, `annunciator`, `buzzer`) has no loop assignment and is not assigned to any SubCircuit |

---

### Derived capacity health issues (shown in Dashboard only, not in tree badges)

| Issue | Severity | Threshold |
|-------|----------|-----------|
| **High address usage** | Warning | Address utilisation ≥ 70 % |
| **Critical address usage** | Error | Address utilisation ≥ 85 % |
| **High mA load** | Warning | mA utilisation ≥ 70 % |
| **Critical mA load** | Error | mA utilisation ≥ 85 % |
| **NAC low end-voltage** | Warning | Calculated end-of-circuit voltage ($V_{\text{nom}} - V_{\text{drop}}$) falls below the minimum device voltage (default 16 V) |

---

## 19. Data Persistence Reference

Understanding where Pulse stores its data helps when working across documents and machines.

| Data | Storage location | Scope |
|------|-----------------|-------|
| **Parameter mappings, device library (panels, loops, wires, paper sizes)** | `%APPDATA%\Pulse\device-config.json` | Machine-wide — applies to all documents on this PC |
| **Custom symbol library** | `%APPDATA%\Pulse\custom-symbols.json` | Machine-wide |
| **Topology assignments** (panel/loop config assignments, wire assignments, flip states, loop ranks, level offsets, symbol mappings, SubCircuits) | Revit Extensible Storage — `PulseTopologyAssignments` schema | Per-document — travels with the `.rvt` file |
| **Diagram settings** (level line visibility) | Revit Extensible Storage — `PulseDiagramSettings` schema | Per-document |
| **BOQ settings** (column visibility, order, grouping, sorting) | Revit Extensible Storage — `PulseBoqSettings` schema | Per-document |
| **Window position and size** | `%APPDATA%\Pulse\window_placement.json` | Machine-wide |
| **UI state** (tree expand/collapse state) | `%APPDATA%\Pulse\ui-state.json` | Machine-wide |

Because topology assignments are stored inside the Revit file, a colleague who opens the same `.rvt` will see the same panel/loop configurations, loop coloring, SubCircuit definitions, and routing visibility states — provided they also have Pulse installed.

---

*This manual was generated from the Pulse 1.0.0 source code. For architecture and developer notes, see [ARCHITECTURE.md](ARCHITECTURE.md).*
