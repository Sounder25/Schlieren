# SCHLIEREN — Visual Identity & Frontend Direction

## The Naming Metaphor

Schlieren imaging makes invisible density gradients visible by turning refractive-index changes into light and shadow. The product does the same for Ethereum execution: it exposes gas flow, state mutations, context changes, call-depth transitions, and oracle divergences that ordinary traces leave invisible.

The visual identity should feel like **a scientific instrument** — not a developer dashboard. Something you'd expect to find in a protocol research lab, not a SaaS company's marketing page.

---

## Three Concepts

### Concept 1: FLOW FIELD
**File:** `concept-1-flow-field.html`

**Philosophy:** Execution is a spatial phenomenon. Gas flows through opcode channels like fluid through a pipe. State mutations are density changes. Divergences are shear lines — visible discontinuities in an otherwise laminar flow.

**Visual reference:** Schlieren photography of supersonic shockwaves, weather radar, fluid dynamics simulation.

**Key characteristics:**
- Near-black void with warm white text
- Single thermal gradient (cold blue → warm orange → peak white) encoding gas intensity
- Horizontal flow bars show opcode cost as physical width
- Divergence markers appear as shear lines cutting across the field
- Memory rendered as a heatmap grid (spatial, not textual)
- Call frames as depth regions in the left column
- Scrubber at the bottom for temporal navigation
- No decoration, no branding in the workspace — only data

**Strengths:** Immediately communicates *where gas goes* at a glance. Divergences are impossible to miss. The thermal metaphor is directly related to Schlieren imaging. Non-engineers can still understand "that big orange bar is expensive."

**Weaknesses:** Dense multi-thousand-step traces need scroll or zoom. May feel too abstract for users who want to read individual opcodes.

---

### Concept 2: PROTOCOL LABORATORY
**File:** `concept-2-protocol-lab.html`

**Philosophy:** A professional instrument. Bloomberg terminal density meets Keysight oscilloscope firmware. Every pixel of screen is informative. The user is a protocol engineer who wants to see everything at once.

**Visual reference:** Digital oscilloscope UI, Bloomberg terminal, CERN ROOT analysis sessions, hardware debugger firmware.

**Key characteristics:**
- Warm grey "instrument chassis" (not blue-grey — warm-tinted neutrals)
- Monospace-dominant typography — data IS the UI
- Tabular disassembly with PC, hex, mnemonic, args, gas, annotations
- Color is purely functional: blue = flow, amber = state, red = fault, green = memory
- Command-line interface bar for power users
- Multi-pane with resizable splits (disassembly, machine state, trace log)
- Conformance status integrated as a persistent badge bar
- Keyboard shortcut-driven workflow (F5/F10/F11 paradigm from Visual Studio debuggers)

**Strengths:** Familiar to anyone who has used a hardware debugger, IDA Pro, or Ghidra. Maximum information density. Every piece of state is always visible. Very fast once learned.

**Weaknesses:** Steep visual learning curve for newcomers. Not visually "exciting" — it wins on utility not on screenshots. Dense enough that it needs careful responsive handling.

---

### Concept 3: INTERFERENCE PATTERN
**File:** `concept-3-interference.html`

**Philosophy:** The entire execution is a continuous field, not a list. You read it the way a physicist reads an interferogram — smooth regions are understood, discontinuities demand investigation. The visualization IS the primary data representation.

**Visual reference:** Interferometry, spectral analysis, seismographs, medical imaging (MRI cross-sections), the literal Schlieren optical technique.

**Key characteristics:**
- Pure void background — absolute black
- Execution rendered as horizontal band spectrum (each band = one step, width = gas cost)
- Color channels encode operation class (cold blue = arithmetic, warm amber = state, flow teal = control)
- Oracle divergences appear as "fracture lines" — the ONLY red element in the entire UI
- Agreement is absence of signal (coherent = quiet)
- The word "divergence" is never used — it's "fracture"
- Measurement readout in a sidebar (like oscilloscope readings)
- Detail strip at bottom for stack/gas/oracle when you cursor into a band
- Y-axis represents call depth

**Strengths:** Utterly unlike any other EVM tool. Makes execution structure visible at a 10,000-foot view — you can literally see where gas spikes happen in a 2,000-step trace. Fracture points are viscerally obvious. The vocabulary (fracture, coherence, interference) builds a unique product language.

**Weaknesses:** Most radical departure from conventional debugger UIs. Requires users to learn a new visual grammar. Needs a secondary "detail mode" for when you want to read individual opcodes.

---

## Architecture Recommendation

**My recommendation: Start from Concept 3 (Interference Pattern) as the core identity, with Concept 2 (Protocol Laboratory) as the detailed-inspection layer underneath.**

Rationale:

1. **Differentiation.** Concept 3 is unmistakable. No other EVM tool looks like this. It creates a product vocabulary (fracture, coherence, field, band) that becomes memorable.

2. **The Schlieren metaphor is literal.** Concept 3 actually *looks* like a Schlieren photograph — smooth gradients with visible shear lines where the physics changes. The other two reference the metaphor conceptually but don't embody it visually.

3. **Information at two altitudes.** The interference field gives you a 10,000-foot structural view (where does gas go? where do oracles disagree?). When you need to zoom in, you drop into the Protocol Lab layer for per-opcode machine state. This is how real scientific instruments work — an overview mode and a measurement mode.

4. **Technical implementation path:** Build the frontend as a web layer (React + Canvas/WebGL for the band renderer + CSS for the chrome). Ship it as a companion to the existing Avalonia desktop app via a localhost web view, or replace the Avalonia UI with a Tauri/Electron shell later. The interference renderer is a canvas/GPU problem; the machine-state inspector is standard DOM.

### Proposed hybrid structure:

| Layer | Source Concept | Purpose |
|-------|---------------|---------|
| Field View (default) | Concept 3 | Structural overview, divergence detection, gas topology |
| Instrument Panel | Concept 2 | Per-step machine state, disassembly, trace log |
| Flow Overlay | Concept 1 | Optional gas-pressure visualization in the field view |
| Brand / Chrome | All three | Minimal warm-grey chassis, monospace-first, functional color only |

### Design system foundation:

- **Palette:** Near-black void (#020203). Warm grey chassis (#0E0F10 → #161718). Data text as warm white (#D8D4D0). Functional color ONLY (amber = state, blue = flow, red = fracture, green = memory). No branding color in the workspace.
- **Typography:** JetBrains Mono as the data font (or IBM Plex Mono). Inter as the chrome/label font. Two weights only: regular and semibold.
- **Motion:** Minimal. Cursor transitions (100ms). No decorative animation. The only persistent motion is the pulse of a "live" indicator.
- **Interaction:** Keyboard-first. Scrubber for temporal navigation. Click-to-inspect on bands. Cmd+K palette for commands.
- **Information architecture:** Overview first, detail on demand. The field view should make the *shape* of execution visible before you read any text.

---

## Files Delivered

```
C:\projects\Schlieren\design-concepts\
├── concept-1-flow-field.html      ← Thermal flow field (gas as fluid)
├── concept-2-protocol-lab.html    ← Dense professional workstation
├── concept-3-interference.html    ← Interferogram / field visualization
└── DESIGN_DIRECTION.md            ← This document
```

All three are working HTML prototypes viewable in any browser. They render at 1440×900 to match the current Schlieren window size.

---

**Next step:** You choose a direction (or a hybrid), and I build the production design system + component library.
