import { useRef, useCallback, useState } from 'react';
import DockLayout from 'rc-dock';
import type { LayoutData, TabData } from 'rc-dock';
import 'rc-dock/dist/rc-dock-dark.css';
import { useAppStore } from '../../engine/store';
import { executeTrace } from '../../engine/rpc';
import { loadFixture } from '../../engine/fixture-adapter';
import { Disassembly } from './Disassembly';
import { MachineState } from './MachineState';
import { TracePanel } from './TracePanel';
import { Diagnostics } from './Diagnostics';
import { FrameTree } from './FrameTree';
import { TxDrawer } from './TxDrawer';
import './Workbench.css';

const panels: Record<string, { title: string; component: React.FC }> = {
  disassembly: { title: 'Disassembly', component: Disassembly },
  'machine-state': { title: 'Machine State', component: MachineState },
  trace: { title: 'Trace Field', component: TracePanel },
  diagnostics: { title: 'Diagnostics', component: Diagnostics },
  'frame-tree': { title: 'Frame Chain', component: FrameTree },
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

const LAYOUT_STORAGE_KEY = 'schlieren-dock-layout-v2';

function saveLayout(layout: LayoutData) {
  try {
    const serializable = JSON.stringify(layout, (key, value) => {
      if (key === 'content' || key === 'title') return undefined;
      return value;
    });
    localStorage.setItem(LAYOUT_STORAGE_KEY, serializable);
  } catch {
    // layout save is best-effort
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

const defaultLayout: LayoutData = {
  dockbox: {
    mode: 'horizontal',
    children: [
      {
        mode: 'vertical',
        size: 650,
        children: [
          {
            size: 450,
            tabs: [createTab('disassembly')],
          },
          {
            size: 250,
            tabs: [createTab('trace'), createTab('machine-state'), createTab('frame-tree')],
          },
        ],
      },
      {
        size: 340,
        tabs: [createTab('diagnostics')],
      },
    ],
  },
};

export function Workbench() {
  const config = useAppStore((s) => s.config);
  const setConfig = useAppStore((s) => s.setConfig);
  const isRunning = useAppStore((s) => s.isRunning);
  const resetWorkbench = useAppStore((s) => s.resetWorkbench);
  const applyLoadedFixture = useAppStore((s) => s.applyLoadedFixture);
  const loadedFixture = useAppStore((s) => s.loadedFixture);
  const setLastError = useAppStore((s) => s.setLastError);
  const dockRef = useRef<DockLayout>(null);
  const fileRef = useRef<HTMLInputElement>(null);
  const savedLayout = useRef<LayoutData | null>(loadSavedLayout());
  const [txOpen, setTxOpen] = useState(false);

  const handleLayoutChange = useCallback((newLayout: LayoutData) => {
    saveLayout(newLayout);
  }, []);

  const handleRun = async () => {
    try {
      await executeTrace();
    } catch {
      /* lastError is set in executeTrace */
    }
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) {
      handleRun();
    }
  };

  const handleFile = async (file: File) => {
    if (file.size > 10 * 1024 * 1024) {
      setLastError('File exceeds 10 MB limit.');
      return;
    }
    try {
      const text = await file.text();
      applyLoadedFixture(loadFixture(text, { path: file.name, preferredFork: config.fork }));
    } catch (err) {
      setLastError(err instanceof Error ? err.message : String(err));
    }
  };

  return (
    <div className="workbench">
      <div className="wb-input-bar">
        <span className="wb-input-label">BYTECODE</span>
        <input
          className="wb-bytecode-input"
          type="text"
          placeholder="Paste hex bytecode (0x optional) — Ctrl+Enter to execute"
          value={config.bytecode}
          onChange={(e) => setConfig({ bytecode: e.target.value })}
          onKeyDown={handleKeyDown}
          spellCheck={false}
          autoComplete="off"
        />
        <div className="wb-input-actions">
          <input
            ref={fileRef}
            type="file"
            accept=".json,.hex,.txt,.bin"
            hidden
            onChange={(e) => {
              const file = e.target.files?.[0];
              e.target.value = '';
              if (file) void handleFile(file);
            }}
          />
          <button
            className="wb-calldata-btn chrome"
            title="Load state-test, pre-state, or hex"
            onClick={() => fileRef.current?.click()}
          >
            LOAD
          </button>
          <button
            className="wb-calldata-btn chrome"
            title="Configure transaction parameters"
            onClick={() => setTxOpen(true)}
          >
            TX
          </button>
          <button
            className="wb-reset-btn chrome"
            title="Reset workbench (keep fork and addresses)"
            onClick={resetWorkbench}
          >
            RESET
          </button>
          <button
            className="wb-run-btn chrome"
            onClick={handleRun}
            disabled={isRunning || (!config.bytecode && !loadedFixture)}
          >
            {isRunning ? (
              <span className="run-spinner">⟳</span>
            ) : (
              '▶ EXECUTE'
            )}
          </button>
        </div>
      </div>

      <div className="wb-dock-container">
        <DockLayout
          ref={dockRef}
          defaultLayout={savedLayout.current || defaultLayout}
          loadTab={loadTab}
          onLayoutChange={handleLayoutChange}
          style={{ position: 'absolute', inset: 0 }}
        />
      </div>

      <TxDrawer open={txOpen} onClose={() => setTxOpen(false)} />
    </div>
  );
}
