import { useRef, useEffect, useCallback } from 'react';
import { useAppStore } from '../../engine/store';
import { getConservationState } from '../../engine/journal-view';
import './TracePanel.css';

/**
 * The Trace Panel is a horizontal seismograph of the execution.
 * Each step is rendered as a thin vertical band whose color encodes
 * the operation class and whose brightness encodes gas cost.
 * 
 * This uses a Canvas element for performance — we may have 10,000+ steps
 * and the DOM can't render that at 60fps. The canvas redraws only when
 * the result or cursor changes.
 */

const OP_COLORS: Record<string, string> = {
  flow: '#5B8EC9',
  mem: '#4BA89A',
  state: '#C8943C',
  halt: '#B85A32',
  default: '#454B54',
};

const FLOW_OPS = new Set([
  'JUMP', 'JUMPI', 'JUMPDEST', 'CALL', 'STATICCALL', 'DELEGATECALL',
  'CALLCODE', 'CREATE', 'CREATE2', 'RETURN', 'REVERT', 'STOP', 'SELFDESTRUCT',
]);
const MEM_OPS = new Set([
  'MLOAD', 'MSTORE', 'MSTORE8', 'MCOPY', 'CALLDATACOPY', 'CODECOPY',
  'EXTCODECOPY', 'RETURNDATACOPY',
]);
const STATE_OPS = new Set(['SLOAD', 'SSTORE', 'TLOAD', 'TSTORE']);
const HALT_OPS = new Set(['REVERT', 'INVALID', 'SELFDESTRUCT']);

function getOpCategory(op: string): string {
  if (HALT_OPS.has(op)) return 'halt';
  if (FLOW_OPS.has(op)) return 'flow';
  if (MEM_OPS.has(op)) return 'mem';
  if (STATE_OPS.has(op)) return 'state';
  return 'default';
}

export function TracePanel() {
  const result = useAppStore((s) => s.result);
  const currentStep = useAppStore((s) => s.currentStep);
  const setCurrentStep = useAppStore((s) => s.setCurrentStep);
  const canvasRef = useRef<HTMLCanvasElement>(null);
  const containerRef = useRef<HTMLDivElement>(null);
  const conservation = result ? getConservationState(result.conservation) : null;

  // Draw the trace field
  const draw = useCallback(() => {
    const canvas = canvasRef.current;
    const container = containerRef.current;
    if (!canvas || !container || !result || result.steps.length === 0) return;

    const rect = container.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    const width = rect.width;
    const height = rect.height;

    canvas.width = width * dpr;
    canvas.height = height * dpr;
    canvas.style.width = `${width}px`;
    canvas.style.height = `${height}px`;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    ctx.scale(dpr, dpr);
    ctx.clearRect(0, 0, width, height);

    const steps = result.steps;
    const totalSteps = steps.length;
    const bandWidth = Math.max(1, width / totalSteps);
    
    // Find max gas cost for normalization
    const maxGas = Math.max(...steps.map((s) => s.gasCost), 1);
    const maxDepth = Math.max(...steps.map((s) => s.depth), 0);

    // Draw each step as a band
    for (let i = 0; i < totalSteps; i++) {
      const step = steps[i];
      const x = (i / totalSteps) * width;
      const category = getOpCategory(step.op);
      const baseColor = OP_COLORS[category] || OP_COLORS.default;

      // Height encodes gas cost (logarithmic)
      const intensity = step.gasCost <= 3 ? 0.15 : 
        0.15 + 0.85 * (Math.log2(step.gasCost) / Math.log2(maxGas));
      const barHeight = height * Math.min(1, intensity);

      ctx.fillStyle = baseColor;
      ctx.globalAlpha = 0.4 + 0.5 * intensity;
      ctx.fillRect(x, height - barHeight, Math.max(bandWidth, 1.2), barHeight);

      const railHeight = Math.max(2, Math.min(6, height / (maxDepth + 5)));
      ctx.globalAlpha = 0.9;
      ctx.fillStyle = `hsl(${205 + (step.frameId * 47) % 110} 42% 55%)`;
      ctx.fillRect(x, step.depth * railHeight, Math.max(bandWidth, 1.2), railHeight);
    }

    // Draw cursor line
    ctx.globalAlpha = 1;
    const cursorX = (currentStep / totalSteps) * width;
    ctx.strokeStyle = 'rgba(255, 255, 255, 0.9)';
    ctx.lineWidth = 1.5;
    ctx.beginPath();
    ctx.moveTo(cursorX, 0);
    ctx.lineTo(cursorX, height);
    ctx.stroke();

    // Cursor glow
    const gradient = ctx.createLinearGradient(cursorX - 8, 0, cursorX + 8, 0);
    gradient.addColorStop(0, 'rgba(91, 142, 201, 0)');
    gradient.addColorStop(0.5, 'rgba(91, 142, 201, 0.15)');
    gradient.addColorStop(1, 'rgba(91, 142, 201, 0)');
    ctx.fillStyle = gradient;
    ctx.fillRect(cursorX - 8, 0, 16, height);
  }, [result, currentStep]);

  // Redraw on changes
  useEffect(() => {
    draw();
  }, [draw]);

  // Redraw on resize
  useEffect(() => {
    const observer = new ResizeObserver(() => draw());
    if (containerRef.current) observer.observe(containerRef.current);
    return () => observer.disconnect();
  }, [draw]);

  // Click to seek
  const handleClick = (e: React.MouseEvent<HTMLCanvasElement>) => {
    if (!result || result.steps.length === 0) return;
    const rect = canvasRef.current!.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const ratio = x / rect.width;
    const step = Math.round(ratio * (result.steps.length - 1));
    setCurrentStep(Math.max(0, Math.min(step, result.steps.length - 1)));
  };

  return (
    <div className="trace-panel">
      <div className="pane-toolbar">
        <span className="pane-title">Trace Field</span>
        {result && (
          <>
            <span className={`conservation-chip ${conservation?.tone}`}>{conservation?.label}</span>
            <span className="pane-meta">
              {result.frames.length} frames · {result.steps.length.toLocaleString()} steps · click to seek
            </span>
          </>
        )}
      </div>
      <div className="trace-canvas-container" ref={containerRef}>
        {result && result.steps.length > 0 ? (
          <canvas
            ref={canvasRef}
            className="trace-canvas"
            onClick={handleClick}
          />
        ) : (
          <div className="trace-empty">
            <span className="trace-empty-text">
              The trace field renders execution as a continuous spectrum — 
              gas pressure, control flow, and state mutations visible at a glance
            </span>
          </div>
        )}
      </div>
    </div>
  );
}
