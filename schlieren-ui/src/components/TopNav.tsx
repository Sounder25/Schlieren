import { useAppStore, type ViewId } from '../engine/store';
import { FORKS } from '../engine/forks';
import { cancelTrace, executeTrace, setOpSec } from '../engine/rpc';
import { motion } from 'framer-motion';
import './TopNav.css';

const views: { id: ViewId; label: string }[] = [
  { id: 'workbench', label: 'Workbench' },
  { id: 'interference', label: 'Interference' },
  { id: 'flow', label: 'Flow' },
  { id: 'conformance', label: 'Conformance' },
  { id: 'harvest', label: 'Harvest' },
  { id: 'guard', label: 'Guard' },
];

export function TopNav() {
  const activeView = useAppStore((s) => s.activeView);
  const setActiveView = useAppStore((s) => s.setActiveView);
  const isRunning = useAppStore((s) => s.isRunning);
  const fork = useAppStore((s) => s.config.fork);
  const setConfig = useAppStore((s) => s.setConfig);
  const opSecEnabled = useAppStore((s) => s.opSecEnabled);

  const handleRun = async () => {
    try {
      await executeTrace();
    } catch {
      /* lastError is set in executeTrace */
    }
  };

  return (
    <nav className="top-nav">
      <span className="nav-brand">SCHLIEREN</span>

      <div className="nav-views">
        {views.map((v) => (
          <button
            key={v.id}
            className={`nav-view-tab ${activeView === v.id ? 'active' : ''}`}
            onClick={() => setActiveView(v.id)}
          >
            {v.label}
            {activeView === v.id && (
              <motion.div
                className="tab-indicator"
                layoutId="tab-indicator"
                transition={{ type: 'spring', stiffness: 500, damping: 35 }}
              />
            )}
          </button>
        ))}
      </div>

      <div className="nav-separator" />

      <div className="nav-right">
        <button
          className={`nav-opsec-btn chrome ${opSecEnabled ? 'on' : ''}`}
          onClick={() => void setOpSec(!opSecEnabled)}
          title="Server-side network lockout"
        >
          {opSecEnabled ? 'OPSEC: ON' : 'OPSEC: OFF'}
        </button>
        <select
          className="nav-fork-select chrome"
          value={fork}
          onChange={(e) => setConfig({ fork: e.target.value })}
          aria-label="Fork"
        >
          {FORKS.map((name) => (
            <option key={name} value={name}>{name}</option>
          ))}
        </select>
        <button
          className="nav-run-btn chrome"
          onClick={handleRun}
          disabled={isRunning}
        >
          {isRunning ? 'EXECUTING…' : 'RUN'}
        </button>
        <button
          className="nav-stop-btn chrome"
          onClick={() => cancelTrace()}
          disabled={!isRunning}
        >
          STOP
        </button>
      </div>
    </nav>
  );
}
