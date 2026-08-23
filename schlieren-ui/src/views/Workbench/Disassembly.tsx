import { useEffect, useRef } from 'react';
import { useAppStore } from '../../engine/store';
import './Disassembly.css';

// ─── Signal classification ───────────────────────────────────────────────────

const FLOW_OPS = new Set([
  'JUMP', 'JUMPI', 'JUMPDEST', 'CALL', 'STATICCALL', 'DELEGATECALL',
  'CALLCODE', 'CREATE', 'CREATE2', 'RETURN', 'REVERT', 'STOP',
  'SELFDESTRUCT', 'INVALID',
]);

const MEM_OPS = new Set([
  'MLOAD', 'MSTORE', 'MSTORE8', 'MCOPY', 'CALLDATACOPY',
  'CODECOPY', 'EXTCODECOPY', 'RETURNDATACOPY',
]);

const STATE_OPS = new Set(['SLOAD', 'SSTORE', 'TLOAD', 'TSTORE']);

function signalClass(op: string): string {
  if (FLOW_OPS.has(op)) return 'sig-flow';
  if (MEM_OPS.has(op)) return 'sig-mem';
  if (STATE_OPS.has(op)) return 'sig-state';
  return '';
}

function thermalIntensity(cost: number): number {
  if (cost <= 3) return 0;
  if (cost >= 20000) return 1;
  return (Math.log2(cost) - 1.58) / (14.3 - 1.58);
}

function thermalColor(intensity: number): string {
  if (intensity <= 0) return 'var(--t-tertiary)';
  if (intensity < 0.3) return 'var(--sig-thermal-low)';
  if (intensity < 0.55) return 'var(--sig-thermal-mid)';
  if (intensity < 0.8) return 'var(--sig-thermal-high)';
  return 'var(--sig-thermal-peak)';
}

// ─── Component ───────────────────────────────────────────────────────────────

export function Disassembly() {
  const result = useAppStore((s) => s.result);
  const currentStep = useAppStore((s) => s.currentStep);
  const setCurrentStep = useAppStore((s) => s.setCurrentStep);
  const scrollRef = useRef<HTMLDivElement>(null);
  const rowRefs = useRef<Map<number, HTMLTableRowElement>>(new Map());

  // Smooth scroll cursor into view
  useEffect(() => {
    const row = rowRefs.current.get(currentStep);
    if (row && scrollRef.current) {
      const container = scrollRef.current;
      const rowRect = row.getBoundingClientRect();
      const containerRect = container.getBoundingClientRect();
      const rowCenter = rowRect.top + rowRect.height / 2;
      const containerCenter = containerRect.top + containerRect.height / 2;
      const offset = rowCenter - containerCenter;
      container.scrollBy({ top: offset, behavior: 'smooth' });
    }
  }, [currentStep]);

  // Keyboard navigation with acceleration on hold
  useEffect(() => {
    let held = false;
    let interval: ReturnType<typeof setInterval> | null = null;

    const keydown = (e: KeyboardEvent) => {
      if (!result) return;
      const max = result.steps.length - 1;
      if (e.key === 'ArrowDown' || e.key === 'F10') {
        e.preventDefault();
        if (!held) {
          setCurrentStep(Math.min(currentStep + 1, max));
          held = true;
          interval = setInterval(() => {
            useAppStore.setState((s) => ({
              currentStep: Math.min(s.currentStep + 1, max),
            }));
          }, 60);
        }
      } else if (e.key === 'ArrowUp' || e.key === 'F11') {
        e.preventDefault();
        if (!held) {
          setCurrentStep(Math.max(currentStep - 1, 0));
          held = true;
          interval = setInterval(() => {
            useAppStore.setState((s) => ({
              currentStep: Math.max(s.currentStep - 1, 0),
            }));
          }, 60);
        }
      } else if (e.key === 'Home') {
        e.preventDefault();
        setCurrentStep(0);
      } else if (e.key === 'End') {
        e.preventDefault();
        setCurrentStep(max);
      }
    };

    const keyup = (e: KeyboardEvent) => {
      if (['ArrowDown', 'ArrowUp', 'F10', 'F11'].includes(e.key)) {
        held = false;
        if (interval) { clearInterval(interval); interval = null; }
      }
    };

    window.addEventListener('keydown', keydown);
    window.addEventListener('keyup', keyup);
    return () => {
      window.removeEventListener('keydown', keydown);
      window.removeEventListener('keyup', keyup);
      if (interval) clearInterval(interval);
    };
  }, [result, currentStep, setCurrentStep]);

  // ─── Empty state ───
  if (!result || result.steps.length === 0) {
    return (
      <div className="disasm-pane">
        <div className="disasm-header">
          <div className="disasm-col-labels">
            <span className="col-label col-step">#</span>
            <span className="col-label col-pc">PC</span>
            <span className="col-label col-op">OPCODE</span>
            <span className="col-label col-gas">GAS</span>
            <span className="col-label col-depth">DEPTH</span>
          </div>
        </div>
        <div className="disasm-empty">
          <div className="disasm-empty-inner">
            <div className="disasm-empty-glyph">⟁</div>
            <p className="disasm-empty-text">
              Execute bytecode to populate the instruction trace
            </p>
            <div className="disasm-empty-shortcuts">
              <kbd>F10</kbd> step forward
              <span className="shortcut-sep">·</span>
              <kbd>F11</kbd> step back
              <span className="shortcut-sep">·</span>
              <kbd>Home</kbd> / <kbd>End</kbd> boundaries
            </div>
          </div>
        </div>
      </div>
    );
  }

  // ─── Populated state ───
  return (
    <div className="disasm-pane">
      <div className="disasm-header">
        <div className="disasm-col-labels">
          <span className="col-label col-step">#</span>
          <span className="col-label col-pc">PC</span>
          <span className="col-label col-op">OPCODE</span>
          <span className="col-label col-gas">GAS</span>
          <span className="col-label col-depth">DEPTH</span>
        </div>
        <span className="disasm-summary">
          {result.steps.length.toLocaleString()} steps ·{' '}
          {result.steps.filter((s) => STATE_OPS.has(s.op)).length} mutations ·{' '}
          {result.steps.filter((s) => s.op === 'CALL' || s.op === 'STATICCALL' || s.op === 'DELEGATECALL').length} calls
        </span>
      </div>
      <div className="disasm-scroll" ref={scrollRef}>
        <table className="instr-table">
          <tbody>
            {result.steps.map((step, i) => {
              const isCursor = i === currentStep;
              const sig = signalClass(step.op);
              const thermal = thermalIntensity(step.gasCost);

              return (
                <tr
                  key={i}
                  ref={(el) => {
                    if (el) rowRefs.current.set(i, el);
                    else rowRefs.current.delete(i);
                  }}
                  className={`instr-row ${isCursor ? 'cursor-row' : ''}`}
                  onClick={() => setCurrentStep(i)}
                >
                  <td className="i-step">{i}</td>
                  <td className="i-pc">
                    {step.pc.toString(16).padStart(4, '0')}
                  </td>
                  <td className={`i-op ${sig}`}>{step.op}</td>
                  <td className="i-gas" style={{ color: thermalColor(thermal) }}>
                    {step.gasCost >= 1000
                      ? step.gasCost.toLocaleString()
                      : step.gasCost}
                  </td>
                  <td className="i-depth">
                    {step.depth > 1 && (
                      <span className="depth-indicator">
                        {'│'.repeat(step.depth - 1)}
                      </span>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
