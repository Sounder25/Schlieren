import { useEffect } from 'react';
import { motion, AnimatePresence } from 'framer-motion';
import type { Variants } from 'framer-motion';
import { useAppStore, type ViewId } from './engine/store';
import { checkConnection } from './engine/rpc';
import { TopNav } from './components/TopNav';
import { CaseBar } from './components/CaseBar';
import { StatusBar } from './components/StatusBar';
import { Workbench } from './views/Workbench/Workbench';
import { Interference } from './views/Interference/Interference';
import { Flow } from './views/Flow/Flow';
import { Conformance } from './views/Conformance/Conformance';
import { Harvest } from './views/Harvest/Harvest';
import './design/tokens.css';
import './design/global.css';
import './design/ambient.css';
import './design/dock-overrides.css';
import './App.css';

const viewComponents: Record<ViewId, React.FC> = {
  workbench: Workbench,
  interference: Interference,
  flow: Flow,
  conformance: Conformance,
  harvest: Harvest,
};

const viewVariants: Variants = {
  initial: { opacity: 0, y: 6 },
  animate: { opacity: 1, y: 0, transition: { duration: 0.22, ease: [0.16, 1, 0.3, 1] } },
  exit: { opacity: 0, y: -4, transition: { duration: 0.14, ease: [0.65, 0, 0.35, 1] } },
};

export default function App() {
  const activeView = useAppStore((s) => s.activeView);
  const ViewComponent = viewComponents[activeView];

  // Check RPC connection on mount and every 30s
  useEffect(() => {
    checkConnection();
    const interval = setInterval(checkConnection, 30000);
    return () => clearInterval(interval);
  }, []);

  return (
    <div className="app-shell">
      <TopNav />
      <CaseBar />
      <main className="app-workspace">
        <AnimatePresence mode="wait">
          <motion.div
            key={activeView}
            className="view-container"
            variants={viewVariants}
            initial="initial"
            animate="animate"
            exit="exit"
          >
            <ViewComponent />
          </motion.div>
        </AnimatePresence>
      </main>
      <StatusBar />
    </div>
  );
}
