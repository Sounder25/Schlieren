import { useEffect, useState } from 'react';
import { FORKS } from '../../engine/forks';
import { loadFixture } from '../../engine/fixture-adapter';
import {
  cancelConformance,
  pollConformance,
  prepareConformance,
  readConformanceFixture,
  startConformance,
  type ConformanceCluster,
  type ConformanceFailure,
  type ConformanceSnapshot,
} from '../../engine/conformance-rpc';
import { alignEelsTrace, type EelsAlignment } from '../../engine/eels';
import { useAppStore } from '../../engine/store';
import './Conformance.css';

const EMPTY_STEPS: never[] = [];
const DEFAULT_FIXTURES = 'C:\\projects\\Schlieren\\fixtures\\state_tests';

export function Conformance() {
  const setActiveView = useAppStore((s) => s.setActiveView);
  const applyLoadedFixture = useAppStore((s) => s.applyLoadedFixture);
  const result = useAppStore((s) => s.result);
  const steps = result?.steps ?? EMPTY_STEPS;

  const [fork, setFork] = useState('Osaka');
  const [fixturesBasePath, setFixturesBasePath] = useState(DEFAULT_FIXTURES);
  const [excludePortedStatic, setExcludePortedStatic] = useState(true);
  const [prep, setPrep] = useState<{ valid: boolean; resolvedRoot: string; fileCount: number } | null>(null);
  const [runId, setRunId] = useState<string | null>(null);
  const [running, setRunning] = useState(false);
  const [snapshot, setSnapshot] = useState<ConformanceSnapshot | null>(null);
  const [selected, setSelected] = useState<ConformanceFailure | null>(null);
  const [clusterFilter, setClusterFilter] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [reference, setReference] = useState('');
  const [alignment, setAlignment] = useState<EelsAlignment | null>(null);
  const [alignError, setAlignError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    prepareConformance({ fork, fixturesBasePath, excludePortedStatic })
      .then((p) => { if (!cancelled) setPrep(p); })
      .catch((e) => { if (!cancelled) setPrep({ valid: false, resolvedRoot: '', fileCount: 0 }); setError(String(e.message ?? e)); });
    return () => { cancelled = true; };
  }, [fork, fixturesBasePath, excludePortedStatic]);

  useEffect(() => {
    if (!running || !runId) return;
    let stop = false;
    const tick = async () => {
      try {
        const snap = await pollConformance(runId);
        if (stop) return;
        setSnapshot(snap);
        if (snap.done) setRunning(false);
      } catch (e) {
        if (!stop) {
          setError(e instanceof Error ? e.message : String(e));
          setRunning(false);
        }
      }
    };
    void tick();
    const id = setInterval(() => void tick(), 750);
    return () => { stop = true; clearInterval(id); };
  }, [running, runId]);

  const run = async () => {
    setError(null);
    setSelected(null);
    setClusterFilter(null);
    try {
      const id = await startConformance({ fork, fixturesBasePath, excludePortedStatic });
      setRunId(id);
      setRunning(true);
      setSnapshot(null);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const stop = async () => {
    await cancelConformance(runId ?? undefined);
  };

  const reset = () => {
    void cancelConformance(runId ?? undefined);
    setRunning(false);
    setRunId(null);
    setSnapshot(null);
    setSelected(null);
    setClusterFilter(null);
    setError(null);
  };

  const openInWorkbench = async (failure: ConformanceFailure) => {
    try {
      const file = await readConformanceFixture(failure.fixturePath);
      const loaded = loadFixture(file.text, {
        path: file.path,
        preferredFork: fork,
        caseId: failure.caseId,
      });
      applyLoadedFixture(loaded);
      setActiveView('workbench');
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  };

  const failures = (snapshot?.failures ?? []).filter(
    (f) => !clusterFilter || f.clusterKey === clusterFilter,
  );
  const clusters: ConformanceCluster[] = snapshot?.clusters ?? [];
  const total = snapshot?.total ?? 0;
  const passed = snapshot?.passed ?? 0;
  const failed = snapshot?.failed ?? 0;
  const rate = total > 0 ? Math.round((passed / total) * 1000) / 10 : null;

  return (
    <div className="conformance-view">
      <header>
        <span>EELS STATE TESTS</span>
        <h2>Conformance suite</h2>
        <p>
          Live fork sweep against disk fixtures. Failures open in Workbench as a LoadedFixture
          — same normalized journal request, expected post-state kept off the wire.
        </p>
      </header>

      <div className="conf-toolbar">
        <label>
          FORK
          <select value={fork} onChange={(e) => setFork(e.target.value)}>
            {FORKS.map((name) => <option key={name} value={name}>{name}</option>)}
          </select>
        </label>
        <label className="conf-path">
          FIXTURES BASE PATH
          <input value={fixturesBasePath} onChange={(e) => setFixturesBasePath(e.target.value)} />
        </label>
        <label className="conf-check">
          <input type="checkbox" checked={excludePortedStatic} onChange={(e) => setExcludePortedStatic(e.target.checked)} />
          exclude ported_static
        </label>
        <button className="conf-run" onClick={() => void run()} disabled={running || prep?.valid === false}>RUN</button>
        <button className="conf-stop" onClick={() => void stop()} disabled={!running}>STOP</button>
        <button className="conf-reset" onClick={reset}>RESET</button>
      </div>

      <div className="conf-status">
        {error && <span className="conf-error">{error}</span>}
        {!error && (
          <span>
            {prep?.valid
              ? `${fork} · ${prep.fileCount.toLocaleString()} files · ${prep.resolvedRoot}`
              : prep ? 'Fixture folder not found' : 'Resolving…'}
            {snapshot ? ` — ${snapshot.status}` : ''}
          </span>
        )}
      </div>

      <div className="conf-stats">
        <Stat label="PASSED" value={passed.toLocaleString()} tone="agree" />
        <Stat label="FAILED" value={failed.toLocaleString()} tone="fracture" />
        <Stat label="TOTAL" value={total.toLocaleString()} />
        <Stat label="PASS RATE" value={rate == null ? '—' : `${rate}%`} tone={rate === 100 ? 'agree' : rate != null && rate < 100 ? 'fracture' : undefined} />
      </div>

      <div className="conf-progress">
        <div className="conf-progress-bar">
          <div style={{ width: total ? `${(passed + failed) / total * 100}%` : '0%' }} />
        </div>
        <span>{snapshot?.currentCase || '—'}</span>
      </div>

      <div className="conf-body">
        <section className="conf-failures">
          <header>
            FAILURES
            <span>{failures.length} shown</span>
            {clusterFilter && <button type="button" onClick={() => setClusterFilter(null)}>Clear filter</button>}
          </header>
          {failures.length === 0 && <p className="conf-empty">No failures yet — live results stream here.</p>}
          {failures.map((f) => (
            <button
              key={f.caseId + f.fixturePath}
              type="button"
              className={`conf-fail-row ${selected?.caseId === f.caseId ? 'active' : ''}`}
              onClick={() => setSelected(f)}
            >
              <strong>{f.caseId}</strong>
              <span>{f.summary}</span>
              <em>{f.clusterKey}</em>
            </button>
          ))}
        </section>

        <aside className="conf-inspector">
          <div className="conf-clusters">
            <header>FAILURE CLUSTERS</header>
            {clusters.length === 0 && <p className="conf-empty">Clusters appear as failures stream.</p>}
            {clusters.map((c) => (
              <button key={c.key} type="button" onClick={() => setClusterFilter(c.key)}>
                <b>{c.count}</b>
                <span>{c.key}</span>
              </button>
            ))}
          </div>
          <div className="conf-detail">
            <header>
              CASE INSPECTOR
              {selected && (
                <button type="button" onClick={() => void openInWorkbench(selected)}>
                  OPEN IN WORKBENCH
                </button>
              )}
            </header>
            {!selected && <p className="conf-empty">Select a failure row.</p>}
            {selected && (
              <div className="conf-detail-body">
                <h3>{selected.caseId}</h3>
                <p className="conf-l1">{selected.layer1Headline || selected.primaryCategory}</p>
                <pre>{selected.layer1Body || selected.mismatches.join('\n')}</pre>
                <p className="conf-meta">{selected.fixturePath}</p>
              </div>
            )}
          </div>
        </aside>
      </div>

      <details className="conf-align">
        <summary>EIP-3155 alignment (journal ↔ pasted EELS structLogs)</summary>
        <div className="conformance-grid">
          <section className="reference-panel">
            <div className="conf-panel-head">
              <strong>REFERENCE STRUCTLOGS</strong>
              <span>{steps.length.toLocaleString()} journal steps loaded</span>
            </div>
            <textarea
              value={reference}
              onChange={(event) => setReference(event.target.value)}
              placeholder={'{"structLogs":[{"pc":0,"op":"PUSH1","gas":100000,"gasCost":3,"depth":0}]}'}
              spellCheck={false}
            />
            <button
              onClick={() => {
                try {
                  setAlignment(alignEelsTrace(steps, reference));
                  setAlignError(null);
                } catch (cause) {
                  setAlignment(null);
                  setAlignError(cause instanceof Error ? cause.message : String(cause));
                }
              }}
              disabled={!reference || steps.length === 0}
            >
              COMPARE TRACE
            </button>
          </section>
          <section className={`alignment-panel ${alignment?.isAligned ? 'agree' : alignment ? 'fracture' : ''}`}>
            {!alignment && !alignError && <div className="alignment-idle"><b>NO COMPARISON</b><span>Run bytecode, then paste EELS EIP-3155 output.</span></div>}
            {alignError && <div className="alignment-error"><b>INVALID REFERENCE</b><span>{alignError}</span></div>}
            {alignment?.isAligned && <div className="alignment-result"><b>ALIGNED</b><strong>{alignment.comparedSteps.toLocaleString()}</strong><span>steps agree across PC, opcode, gas, gas cost, and depth</span></div>}
            {alignment?.firstDivergence && (
              <div className="alignment-result">
                <b>FIRST FRACTURE</b>
                <strong>STEP {alignment.firstDivergence.index}</strong>
                <dl>
                  <dt>FIELD</dt><dd>{alignment.firstDivergence.field}</dd>
                  <dt>EELS</dt><dd>{alignment.firstDivergence.expected}</dd>
                  <dt>SCHLIEREN</dt><dd>{alignment.firstDivergence.actual}</dd>
                  <dt>FRAME</dt><dd>F{alignment.firstDivergence.frameId ?? '—'}</dd>
                  <dt>LOCATION</dt><dd>pc {alignment.firstDivergence.pc} · {alignment.firstDivergence.op}</dd>
                </dl>
              </div>
            )}
          </section>
        </div>
      </details>
    </div>
  );
}

function Stat({ label, value, tone }: { label: string; value: string; tone?: 'agree' | 'fracture' }) {
  return (
    <div className={`conf-stat ${tone ?? ''}`}>
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  );
}
