import { useMemo } from 'react';
import { motion, AnimatePresence, type Variants } from 'framer-motion';
import { useAppStore } from '../../engine/store';
import './MachineState.css';

// ─── Stack diff logic ─────────────────────────────────────────────────────────

interface StackDiff {
  index: number;
  value: string;
  truncated: string;
  state: 'new' | 'stable' | 'consumed';
}

function truncateHex(val: string): string {
  if (val.length <= 18) return val;
  return val.slice(0, 8) + '···' + val.slice(-6);
}

function diffStacks(current: string[], previous: string[] | undefined): StackDiff[] {
  const reversed = [...current].reverse();
  const prevReversed = previous ? [...previous].reverse() : [];

  return reversed.map((val, i) => {
    const realIndex = current.length - 1 - i;
    const isNew = !previous || i >= prevReversed.length || prevReversed[i] !== val;
    return {
      index: realIndex,
      value: val,
      truncated: truncateHex(val),
      state: isNew ? 'new' : 'stable',
    };
  });
}

// ─── Animation variants ───────────────────────────────────────────────────────

const stackItemVariants: Variants = {
  initial: { opacity: 0, y: 8, scale: 0.97 },
  animate: { 
    opacity: 1, y: 0, scale: 1,
    transition: { duration: 0.18, ease: [0.16, 1, 0.3, 1] }
  },
  exit: { 
    opacity: 0, y: -6, scale: 0.98,
    transition: { duration: 0.12, ease: [0.65, 0, 0.35, 1] }
  },
};

// ─── Component ────────────────────────────────────────────────────────────────

export function MachineState() {
  const result = useAppStore((s) => s.result);
  const currentStep = useAppStore((s) => s.currentStep);
  const setCurrentStep = useAppStore((s) => s.setCurrentStep);

  const step = result?.steps[currentStep];
  const prevStep = currentStep > 0 ? result?.steps[currentStep - 1] : undefined;
  const totalSteps = result?.steps.length ?? 0;

  const stackDiff = useMemo(() => {
    if (!step) return [];
    return diffStacks(step.stack ?? [], prevStep?.stack);
  }, [step, prevStep]);

  return (
    <div className="machine-pane">
      <div className="pane-toolbar">
        <span className="pane-title">Machine State</span>
        {step && (
          <span className="pane-meta">
            frame {step.frameId} · pc 0x{step.pc.toString(16)} · {step.op} · gas {step.gasBefore.toLocaleString()}
          </span>
        )}
      </div>

      <div className="machine-scroll">
        {/* ─── Step Controls ─── */}
        <div className="step-controls">
          <button
            className="step-btn"
            onClick={() => setCurrentStep(0)}
            disabled={!result}
            title="First step (Home)"
          >
            ⏮
          </button>
          <button
            className="step-btn"
            onClick={() => setCurrentStep(Math.max(0, currentStep - 1))}
            disabled={!result}
            title="Step back (F11 / ↑)"
          >
            ◀
          </button>

          <div className="step-scrubber">
            <input
              type="range"
              min={0}
              max={Math.max(0, totalSteps - 1)}
              value={currentStep}
              onChange={(e) => setCurrentStep(Number(e.target.value))}
              className="step-slider"
              disabled={!result}
            />
            <div className="step-progress-label chrome">
              {totalSteps > 0
                ? `${currentStep + 1} / ${totalSteps.toLocaleString()}`
                : '—'}
            </div>
          </div>

          <button
            className="step-btn"
            onClick={() => setCurrentStep(Math.min(totalSteps - 1, currentStep + 1))}
            disabled={!result}
            title="Step forward (F10 / ↓)"
          >
            ▶
          </button>
          <button
            className="step-btn"
            onClick={() => setCurrentStep(totalSteps - 1)}
            disabled={!result}
            title="Last step (End)"
          >
            ⏭
          </button>
        </div>

        {!step ? (
          <div className="machine-empty">
            <span>Execute to inspect machine state at each step</span>
          </div>
        ) : (
          <>
            {/* ─── Context registers ─── */}
            <section className="state-section">
              <header className="state-header">
                <span className="state-title">Context</span>
              </header>
              <div className="state-body">
                <div className="reg-grid">
                  <span className="reg-k">PC</span>
                  <span className="reg-v bright">
                    0x{step.pc.toString(16).padStart(4, '0')}
                  </span>
                  <span className="reg-k">OP</span>
                  <span className="reg-v bright">{step.op}</span>
                  <span className="reg-k">GAS LEFT</span>
                  <span className="reg-v">{step.gasBefore.toLocaleString()}</span>
                  <span className="reg-k">COST</span>
                  <span className="reg-v" style={{ color: step.gasCost >= 5000 ? 'var(--sig-thermal-high)' : undefined }}>
                    {step.gasCost.toLocaleString()}
                  </span>
                  <span className="reg-k">DEPTH</span>
                  <span className="reg-v">{step.depth}</span>
                  <span className="reg-k">FRAME</span>
                  <span className="reg-v">#{step.frameId}</span>
                </div>
              </div>
            </section>

            {/* ─── Stack — values breathe ─── */}
            <section className="state-section">
              <header className="state-header">
                <span className="state-title">Stack</span>
                <span className="state-meta">depth {step.stack?.length ?? 0}</span>
              </header>
              <div className="state-body stack-body">
                {!step.stack || step.stack.length === 0 ? (
                  <span className="state-empty-note">(empty)</span>
                ) : (
                  <div className="stack-list">
                    <AnimatePresence mode="popLayout">
                      {stackDiff.map((entry) => (
                        <motion.div
                          key={`${entry.index}-${entry.value}`}
                          className={`stack-entry ${entry.state}`}
                          variants={stackItemVariants}
                          initial="initial"
                          animate="animate"
                          exit="exit"
                          layout
                        >
                          <span className="se-idx">{entry.index}</span>
                          <span className="se-val" title={entry.value}>
                            {entry.truncated}
                          </span>
                        </motion.div>
                      ))}
                    </AnimatePresence>
                  </div>
                )}
              </div>
            </section>

            {/* ─── Storage ─── */}
            <section className="state-section">
              <header className="state-header">
                <span className="state-title">Storage</span>
                <span className="state-meta">
                  {Object.keys(step.storage ?? {}).length} slot
                  {Object.keys(step.storage ?? {}).length !== 1 ? 's' : ''}
                </span>
              </header>
              <div className="state-body">
                {!step.storage || Object.keys(step.storage).length === 0 ? (
                  <span className="state-empty-note">(no mutations observed)</span>
                ) : (
                  <div className="storage-grid">
                    {Object.entries(step.storage ?? {}).map(([slot, val]) => (
                      <div key={slot} className="storage-entry">
                        <span className="storage-slot">{truncateHex(slot)}</span>
                        <span className="storage-arrow">→</span>
                        <span className="storage-val">{truncateHex(val)}</span>
                      </div>
                    ))}
                  </div>
                )}
              </div>
            </section>

            {/* ─── Memory (show only if non-empty) ─── */}
            {step.memory && step.memory.length > 0 && (
              <section className="state-section">
                <header className="state-header">
                  <span className="state-title">Memory</span>
                  <span className="state-meta">
                    {Math.ceil(step.memory.length / 2)} bytes
                  </span>
                </header>
                <div className="state-body">
                  <div className="memory-hex">
                    {step.memory.slice(0, 8).join(' ')}
                    {step.memory.length > 8 && (
                      <span className="memory-ellipsis"> ···({step.memory.length - 8} more words)</span>
                    )}
                  </div>
                </div>
              </section>
            )}

            {/* ─── Execution Result ─── */}
            {result && (
              <section className="state-section">
                <header className="state-header">
                  <span className="state-title">Result</span>
                  <span className={`result-badge ${result.success ? 'success' : 'revert'}`}>
                    {result.success ? 'SUCCESS' : 'REVERT'}
                  </span>
                </header>
                <div className="state-body">
                  <div className="reg-grid">
                    <span className="reg-k">GAS USED</span>
                    <span className="reg-v">{result.gasUsed.toLocaleString()}</span>
                    <span className="reg-k">RETURN</span>
                    <span className="reg-v return-data">
                      {result.returnData.length > 66
                        ? result.returnData.slice(0, 34) + '…' + result.returnData.slice(-32)
                        : result.returnData || '0x'}
                    </span>
                  </div>
                </div>
              </section>
            )}
          </>
        )}
      </div>
    </div>
  );
}
