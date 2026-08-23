import { useAppStore, type ViewId } from '../engine/store';
import { motion } from 'framer-motion';
import './TopNav.css';

const views: { id: ViewId; label: string }[] = [
  { id: 'workbench', label: 'Workbench' },
  { id: 'interference', label: 'Interference' },
  { id: 'flow', label: 'Flow' },
  { id: 'conformance', label: 'Conformance' },
  { id: 'harvest', label: 'Harvest' },
];

export function TopNav() {
  const activeView = useAppStore((s) => s.activeView);
  const setActiveView = useAppStore((s) => s.setActiveView);
  const isRunning = useAppStore((s) => s.isRunning);

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
        <select className="nav-fork-select chrome">
          <option>Osaka</option>
          <option>Prague</option>
          <option>Cancun</option>
          <option>Shanghai</option>
          <option>Paris</option>
          <option>London</option>
          <option>Berlin</option>
        </select>
        <button className="nav-run-btn chrome" disabled={isRunning}>
          {isRunning ? 'EXECUTING…' : 'RUN'}
        </button>
      </div>
    </nav>
  );
}
