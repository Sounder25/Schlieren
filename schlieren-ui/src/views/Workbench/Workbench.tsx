import { useRef, useCallback } from 'react';
import DockLayout from 'rc-dock';
import type { LayoutData, TabData } from 'rc-dock';
import 'rc-dock/dist/rc-dock-dark.css';
import { useAppStore } from '../../engine/store';
import { executeTrace } from '../../engine/rpc';
import { loadGuardEvidence } from '../../engine/guard-evidence';
import { Disassembly } from './Disassembly';
import { MachineState } from './MachineState';
import { TracePanel } from './TracePanel';
import { Diagnostics } from './Diagnostics';
import './Workbench.css';

// ─── Panel definitions ───────────────────────────────────────────────────────

const panels: Record<string, { title: string; component: React.FC }> = {
  disassembly: { title: 'Disassembly', component: Disassembly },
  'machine-state': { title: 'Machine State', component: MachineState },
  trace: { title: 'Trace Field', component: TracePanel },
  diagnostics: { title: 'Diagnostics', component: Diagnostics },
};

function loadTab(tab: TabData): TabData {
  const panel = panels[tab.id as string];
  if (!panel) return tab;
  const Component = panel.component;
  return {
    ...tab,
    title: panel.title,
    content: <Component />,
    closable: false,
  };
}

function createTab(id: string): TabData {
  const panel = panels[id];
  const Component = panel.component;
  return {
    id,
    title: panel.title,
    content: <Component />,
    closable: false,
  };
}

// ─── Layout persistence ──────────────────────────────────────────────────────

const LAYOUT_STORAGE_KEY = 'schlieren-dock-layout';

function saveLayout(layout: LayoutData) {
  try {
    // rc-dock layouts contain React elements in tabs — strip content before saving
    const serializable = JSON.stringify(layout, (key, value) => {
      if (key === 'content' || key === 'title') return undefined;
      return value;
    });
    localStorage.setItem(LAYOUT_STORAGE_KEY, serializable);
  } catch {
    // Silently fail — layout save is best-effort
  }
}

function loadSavedLayout(): LayoutData | null {
  try {
    const saved = localStorage.getItem(LAYOUT_STORAGE_KEY);
    if (!saved) return null;
    return JSON.parse(saved) as LayoutData;
  } catch {
    return null;
  }
}

// ─── Default layout ──────────────────────────────────────────────────────────

const defaultLayout: LayoutData = {
  dockbox: {
    mode: 'horizontal',
    children: [
      {
        // Left column: Disassembly on top, Trace + Machine State as tabs below
        mode: 'vertical',
        size: 650,
        children: [
          {
            size: 450,
            tabs: [createTab('disassembly')],
          },
          {
            size: 250,
            tabs: [createTab('trace'), createTab('machine-state')],
          },
        ],
      },
      {
        // Right column: Diagnostics full height
        size: 340,
        tabs: [createTab('diagnostics')],
      },
    ],
  },
};

// ─── Workbench ───────────────────────────────────────────────────────────────

export function Workbench() {
  const config = useAppStore((s) => s.config);
  const setConfig = useAppStore((s) => s.setConfig);
  const isRunning = useAppStore((s) => s.isRunning);
  const guardReplay = useAppStore((s) => s.guardReplay);
  const setGuardReplay = useAppStore((s) => s.setGuardReplay);
  const dockRef = useRef<DockLayout>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const savedLayout = useRef<LayoutData | null>(loadSavedLayout());

  const handleLayoutChange = useCallback((newLayout: LayoutData) => {
    saveLayout(newLayout);
  }, []);

  const handleRun = async () => {
    try {
      await executeTrace();
    } catch (err) {
      console.error('Execution failed:', err);
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) {
      handleRun();
    }
  };

  const handleFile = async (file: File) => {
    const text = await file.text();
    try {
      const loaded = loadGuardEvidence(text);
      setGuardReplay(loaded.replay);
      setConfig(loaded.config);
    } catch (err) {
      console.error('Guard evidence load failed:', err);
    }
  };

  return (
    <div className="workbench">
      {/* Input bar */}
      <div className="wb-input-bar">
        <span className="wb-input-label">BYTECODE</span>
        <input
          className="wb-bytecode-input"
          type="text"
          placeholder="Paste hex bytecode (0x optional) — Ctrl+Enter to execute"
          value={config.bytecode}
          onChange={(e) => {
            setGuardReplay(null);
            setConfig({ bytecode: e.target.value });
          }}
          onKeyDown={handleKeyDown}
          spellCheck={false}
          autoComplete="off"
        />
        <div className="wb-input-actions">
          <input
            ref={fileRef}
            type="file"
            accept=".json"
            hidden
            onChange={(e) => {
              const file = e.target.files?.[0];
              if (file) void handleFile(file);
              e.target.value = '';
            }}
          />
          <button
            className="wb-calldata-btn chrome"
            title="Open Guard evidence JSON"
            onClick={() => fileRef.current?.click()}
          >
            EVIDENCE
          </button>
          <button
            className="wb-calldata-btn chrome"
            title="Configure transaction parameters"
          >
            TX
          </button>
          <button
            className="wb-run-btn chrome"
            onClick={handleRun}
            disabled={isRunning || (!config.bytecode && !guardReplay)}
          >
            {isRunning ? (
              <span className="run-spinner">⟳</span>
            ) : (
              '▶ EXECUTE'
            )}
          </button>
        </div>
      </div>

      {/* Dockable workspace */}
      <div className="wb-dock-container">
        <DockLayout
          ref={dockRef}
          defaultLayout={savedLayout.current || defaultLayout}
          loadTab={loadTab}
          onLayoutChange={handleLayoutChange}
          style={{ position: 'absolute', inset: 0 }}
        />
      </div>
    </div>
  );
}
