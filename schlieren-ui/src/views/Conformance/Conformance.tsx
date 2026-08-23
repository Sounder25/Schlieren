import { useState } from 'react';
import { alignEelsTrace, type EelsAlignment } from '../../engine/eels';
import { useAppStore } from '../../engine/store';
import './Conformance.css';

const EMPTY_STEPS: never[] = [];

export function Conformance() {
  const result = useAppStore((state) => state.result);
  const steps = result?.steps ?? EMPTY_STEPS;
  const [reference, setReference] = useState('');
  const [alignment, setAlignment] = useState<EelsAlignment | null>(null);
  const [error, setError] = useState<string | null>(null);
  const compare = () => {
    try { setAlignment(alignEelsTrace(steps, reference)); setError(null); }
    catch (cause) { setAlignment(null); setError(cause instanceof Error ? cause.message : String(cause)); }
  };
  return (
    <div className="conformance-view">
      <header><span>EIP-3155 ALIGNMENT GATE</span><h2>Journal ↔ EELS</h2><p>Paste a Python EELS trace. Schlieren reports the first semantic divergence and anchors it to the owning journal frame.</p></header>
      <main className="conformance-grid">
        <section className="reference-panel"><div className="conf-panel-head"><strong>REFERENCE STRUCTLOGS</strong><span>{steps.length.toLocaleString()} journal steps loaded</span></div><textarea value={reference} onChange={(event) => setReference(event.target.value)} placeholder={'{"structLogs":[{"pc":0,"op":"PUSH1","gas":100000,"gasCost":3,"depth":0}]}'} spellCheck={false} /><button onClick={compare} disabled={!reference || steps.length === 0}>COMPARE TRACE</button></section>
        <section className={`alignment-panel ${alignment?.isAligned ? 'agree' : alignment ? 'fracture' : ''}`}>
          {!alignment && !error && <div className="alignment-idle"><b>NO COMPARISON</b><span>Run bytecode, then paste EELS EIP-3155 output.</span></div>}
          {error && <div className="alignment-error"><b>INVALID REFERENCE</b><span>{error}</span></div>}
          {alignment?.isAligned && <div className="alignment-result"><b>ALIGNED</b><strong>{alignment.comparedSteps.toLocaleString()}</strong><span>steps agree across PC, opcode, gas, gas cost, and depth</span></div>}
          {alignment?.firstDivergence && <div className="alignment-result"><b>FIRST FRACTURE</b><strong>STEP {alignment.firstDivergence.index}</strong><dl><dt>FIELD</dt><dd>{alignment.firstDivergence.field}</dd><dt>EELS</dt><dd>{alignment.firstDivergence.expected}</dd><dt>SCHLIEREN</dt><dd>{alignment.firstDivergence.actual}</dd><dt>FRAME</dt><dd>F{alignment.firstDivergence.frameId ?? '—'}</dd><dt>LOCATION</dt><dd>pc {alignment.firstDivergence.pc} · {alignment.firstDivergence.op}</dd></dl></div>}
        </section>
      </main>
    </div>
  );
}
