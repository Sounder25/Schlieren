import { buildFrameRows, getConservationState } from '../../engine/journal-view';
import { useAppStore, type JournalGasNode } from '../../engine/store';
import './Flow.css';

function GasNode({ node, depth = 0 }: { node: JournalGasNode; depth?: number }) {
  return <li className={`gas-node effect-${node.effect}`}><div className="gas-node-row" style={{ paddingLeft: `${12 + depth * 18}px` }}><span className="gas-node-mark" /><span className="gas-node-label">{node.label}</span>{node.frameId !== null && <span className="gas-frame">F{node.frameId}</span>}<span className="gas-semantics">{node.semantics}</span><span className="gas-amount">{node.effect === 'credit' ? '−' : node.effect === 'charge' ? '+' : '≈'}{node.amount.toLocaleString()}</span><span className="gas-total">Σ {node.totalGas.toLocaleString()}</span></div>{node.children.length > 0 && <ul>{node.children.map((child) => <GasNode key={child.id} node={child} depth={depth + 1} />)}</ul>}</li>;
}

export function Flow() {
  const result = useAppStore((state) => state.result);
  if (!result) return <div className="flow-view flow-empty">Execute a trace to inspect exclusive gas ownership.</div>;
  const conservation = getConservationState(result.conservation);
  const frames = buildFrameRows(result.frameTree);
  return <div className="flow-view"><header className="flow-header"><div><span className="flow-kicker">JOURNAL ACCOUNTING</span><h2>Gas ownership topology</h2></div><div className={`flow-conservation ${conservation.tone}`}><strong>{conservation.label}</strong><span>{result.conservation.derivedGas.toLocaleString()} derived / {result.conservation.settledGas.toLocaleString()} settled</span></div></header><div className="flow-grid"><section className="flow-panel frame-panel"><h3>Frame interferogram</h3><p>Explicit execution ownership. Child opcodes stay in their child frame.</p><div className="frame-list">{frames.map((frame) => <div className="frame-row" key={frame.id} style={{ marginLeft: frame.indent * 18 }}><span className="frame-pulse" style={{ ['--frame-hue' as string]: `${205 + (frame.id * 47) % 110}` }} /><strong>F{frame.id}</strong><span>{frame.callType}</span><code>{frame.contractAddress}</code><b>{frame.gasUsed?.toLocaleString() ?? '—'} gas</b></div>)}</div></section><section className="flow-panel gas-panel"><h3>Exclusive gas tree</h3><p>Charges add, credits subtract, forwarded gas remains non-additive evidence.</p><ul className="gas-tree"><GasNode node={result.gasTree} /></ul></section></div></div>;
}
