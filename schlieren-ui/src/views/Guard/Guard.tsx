import { useState, useRef } from 'react';
import { useAppStore, type GuardReport, type GuardOutcomeKind } from '../../engine/store';
import { executeGuard } from '../../engine/guard-rpc';
import type { LoadedFixture, TracePreStateAccount, TraceBlockContext } from '../../engine/journal-request';
import './Guard.css';

// ─── Verdict badge ────────────────────────────────────────────────────────────

const VERDICT_LABEL: Record<GuardOutcomeKind, string> = {
  SellSuccessful: 'SELL SUCCESSFUL',
  SellBlocked: 'SELL BLOCKED',
  SellDelayed: 'SELL DELAYED',
  BuyFailed: 'BUY FAILED',
  Inconclusive: 'INCONCLUSIVE',
};

const VERDICT_CLASS: Record<GuardOutcomeKind, string> = {
  SellSuccessful: 'verdict-ok',
  SellBlocked: 'verdict-blocked',
  SellDelayed: 'verdict-delayed',
  BuyFailed: 'verdict-failed',
  Inconclusive: 'verdict-inconclusive',
};

function VerdictBadge({ kind }: { kind: GuardOutcomeKind }) {
  return (
    <span className={`guard-verdict-badge ${VERDICT_CLASS[kind]}`}>
      {VERDICT_LABEL[kind]}
    </span>
  );
}

// ─── Step row ─────────────────────────────────────────────────────────────────

function StepRow({ step, index }: { step: GuardReport['steps'][0]; index: number }) {
  return (
    <div className={`guard-step-row ${step.success ? 'step-ok' : 'step-fail'}`}>
      <span className="guard-step-num">{index + 1}</span>
      <span className={`guard-step-status ${step.success ? 'ok' : 'fail'}`}>
        {step.success ? '✓' : '✗'}
      </span>
      <span className="guard-step-name">{step.name.toUpperCase()}</span>
      <span className="guard-step-meta">
        {Number(step.gasUsed).toLocaleString()} gas
      </span>
      {!step.success && step.error && step.error !== 'Success' && (
        <span className="guard-step-error">{step.error}</span>
      )}
      <span className="guard-step-deltas">
        ETH {step.ethBefore} → {step.ethAfter}
        {step.tokenBefore !== '0' || step.tokenAfter !== '0'
          ? ` · TKN ${step.tokenBefore} → ${step.tokenAfter}`
          : ''}
      </span>
    </div>
  );
}

// ─── Causal frame callout ─────────────────────────────────────────────────────

function CausalFrameCallout({ report, onOpenWorkbench }: {
  report: GuardReport;
  onOpenWorkbench: () => void;
}) {
  const v = report.verdict;
  if (!v.causalFrameId) return null;
  return (
    <div className="guard-causal-callout">
      <div className="guard-causal-header">
        <span className="guard-causal-label">CAUSAL FRAME</span>
        <span className="guard-causal-id">#{v.causalFrameId}</span>
        <span className="guard-causal-depth">depth {v.causalDepth}</span>
      </div>
      <div className="guard-causal-contract">{v.causalContract}</div>
      <button
        className="guard-replay-btn chrome"
        type="button"
        onClick={onOpenWorkbench}
        title="Load the causal transaction into Workbench for frame-level inspection"
      >
        OPEN IN WORKBENCH →
      </button>
    </div>
  );
}

// ─── Report panel ─────────────────────────────────────────────────────────────

function ReportPanel({
  report,
  onOpenWorkbench,
}: {
  report: GuardReport;
  onOpenWorkbench: () => void;
}) {
  const v = report.verdict;
  return (
    <div className="guard-report">
      {/* Headline */}
      <div className="guard-report-header">
        <VerdictBadge kind={v.kind} />
        {v.effectiveLossPercent != null && v.effectiveLossPercent !== undefined && (
          <span className="guard-loss-pct">
            {v.effectiveLossPercent.toFixed(2)}% round-trip loss
          </span>
        )}
      </div>

      {/* Detail narrative */}
      <p className="guard-detail-text">{v.detail}</p>

      {/* Causal frame */}
      <CausalFrameCallout report={report} onOpenWorkbench={onOpenWorkbench} />

      {/* Pin info */}
      <div className="guard-pin-row">
        <span className="guard-pin-label">PIN</span>
        <span className="guard-pin-val">
          block {report.pin.blockNumber.toLocaleString()} · {report.pin.fork} · chain {report.pin.chainId}
        </span>
      </div>
      <div className="guard-pin-row">
        <span className="guard-pin-label">TOKEN</span>
        <span className="guard-pin-val guard-addr">{report.token}</span>
      </div>
      <div className="guard-pin-row">
        <span className="guard-pin-label">BUYER</span>
        <span className="guard-pin-val guard-addr">{report.buyer}</span>
      </div>

      {/* Steps */}
      <section className="guard-steps-section">
        <header className="guard-steps-header">EXECUTION STEPS</header>
        <div className="guard-steps-list">
          {report.steps.map((step, i) => (
            <StepRow key={step.name + i} step={step} index={i} />
          ))}
        </div>
      </section>
    </div>
  );
}

// ─── Main view ────────────────────────────────────────────────────────────────

export function Guard() {
  const connected = useAppStore((s) => s.connected);
  const endpoint = useAppStore((s) => s.endpoint);
  const guardReport = useAppStore((s) => s.guardReport);
  const guardError = useAppStore((s) => s.guardError);
  const guardRunning = useAppStore((s) => s.guardRunning);
  const setGuardReport = useAppStore((s) => s.setGuardReport);
  const setGuardError = useAppStore((s) => s.setGuardError);
  const setActiveView = useAppStore((s) => s.setActiveView);
  const setResult = useAppStore((s) => s.setResult);

  const [token, setToken] = useState('');
  const [rpcUrl, setRpcUrl] = useState('');
  const [block, setBlock] = useState('');
  const tokenRef = useRef<HTMLInputElement>(null);

  const handleRun = async () => {
    const t = token.trim();
    if (!t) {
      tokenRef.current?.focus();
      return;
    }
    const rpc = rpcUrl.trim() || endpoint;
    const blockArg = block.trim()
      ? Number(block.trim()) || ('latest' as const)
      : undefined;

    try {
      await executeGuard({ token: t, rpc, block: blockArg });
    } catch {
      // error already stored in guardError via executeGuard
    }
  };

  const handleOpenWorkbench = () => {
    if (!guardReport?.workbench) return;
    const params = guardReport.workbench.params[0] as Record<string, unknown>;
    const tx = params.transaction as Record<string, string>;

    const fixture: LoadedFixture = {
      identity: {
        path: `guard:${guardReport.token}`,
        name: `Guard — ${guardReport.verdict.headline}`,
        caseId: `guard-${guardReport.token}`,
        fork: (params.fork as string) ?? 'Osaka',
        kind: 'prestate',
      },
      source: JSON.stringify(guardReport.workbench),
      request: {
        fork: (params.fork as string) ?? 'Osaka',
        transaction: {
          from: tx.from ?? '',
          to: tx.to ?? '',
          data: tx.data ?? '0x',
          value: tx.value ?? '0x0',
          gasLimit: tx.gasLimit ?? '0x1E8480',
          nonce: tx.nonce ?? '0x0',
          gasPrice: tx.gasPrice ?? '0x0',
        },
        preState: (params.preState as TracePreStateAccount[]) ?? [],
        blockContext: params.blockContext as TraceBlockContext | undefined,
        options: params.options as { disableStack?: boolean; disableMemory?: boolean; disableStorage?: boolean } | undefined,
      },
      expected: {
        success: true,
        exception: null,
        postHash: null,
        postState: [],
      },
    };

    setResult(null);
    useAppStore.getState().applyLoadedFixture(fixture);
    setActiveView('workbench');
  };

  const handleClear = () => {
    setGuardReport(null);
    setGuardError(null);
  };

  return (
    <div className="guard-view">
      {/* ── Input bar ── */}
      <div className="guard-input-bar">
        <span className="guard-input-label">TOKEN</span>
        <input
          ref={tokenRef}
          className="guard-token-input chrome"
          type="text"
          placeholder="0x token address"
          value={token}
          onChange={(e) => setToken(e.target.value)}
          onKeyDown={(e) => { if (e.key === 'Enter') void handleRun(); }}
          spellCheck={false}
          autoComplete="off"
        />
        <span className="guard-input-label">RPC</span>
        <input
          className="guard-rpc-input chrome"
          type="text"
          placeholder={endpoint}
          value={rpcUrl}
          onChange={(e) => setRpcUrl(e.target.value)}
          spellCheck={false}
          autoComplete="off"
        />
        <span className="guard-input-label">BLOCK</span>
        <input
          className="guard-block-input chrome"
          type="text"
          placeholder="latest"
          value={block}
          onChange={(e) => setBlock(e.target.value)}
          spellCheck={false}
        />
        <div className="guard-input-actions">
          <button
            className="guard-clear-btn chrome"
            type="button"
            onClick={handleClear}
            disabled={!guardReport && !guardError}
          >
            CLEAR
          </button>
          <button
            className="guard-run-btn chrome"
            type="button"
            onClick={() => void handleRun()}
            disabled={guardRunning || !token.trim()}
          >
            {guardRunning ? (
              <span className="run-spinner">⟳</span>
            ) : (
              '▶ EVALUATE'
            )}
          </button>
        </div>
      </div>

      {/* ── Content area ── */}
      <div className="guard-content">
        {!connected && !guardRunning && !guardReport && !guardError && (
          <div className="guard-idle-pane">
            <div className="guard-idle-icon">⬡</div>
            <p className="guard-idle-heading">Guard — Token Risk Scanner</p>
            <p className="guard-idle-body">
              Paste a token address above and provide a live mainnet RPC endpoint.
              Guard will pin the current block, simulate a full buy → approve → sell
              loop, and return frame-level proof of the outcome.
            </p>
            <div className="guard-case-table">
              <div className="guard-case-row">
                <span className="verdict-ok guard-case-verdict">SELL SUCCESSFUL</span>
                <span className="guard-case-desc">Clean round-trip, loss measured precisely</span>
              </div>
              <div className="guard-case-row">
                <span className="verdict-blocked guard-case-verdict">SELL BLOCKED</span>
                <span className="guard-case-desc">Causal revert frame identified, Workbench replay ready</span>
              </div>
              <div className="guard-case-row">
                <span className="verdict-delayed guard-case-verdict">SELL DELAYED</span>
                <span className="guard-case-desc">Cooldown — NOT a honeypot, second sell at block+12 committed</span>
              </div>
              <div className="guard-case-row">
                <span className="verdict-failed guard-case-verdict">BUY FAILED</span>
                <span className="guard-case-desc">No liquidity or pre-buy restriction</span>
              </div>
            </div>
          </div>
        )}

        {guardRunning && (
          <div className="guard-running-pane">
            <span className="guard-spinner-large">⟳</span>
            <p>Pinning block and executing trade simulation…</p>
            <p className="guard-running-note">
              Fetching state on-demand from the RPC endpoint. This takes 5–30 seconds
              depending on how many contracts are in the call graph.
            </p>
          </div>
        )}

        {guardError && !guardRunning && (
          <div className="guard-error-pane">
            <span className="guard-error-label">ERROR</span>
            <p className="guard-error-text">{guardError}</p>
            <button
              className="guard-clear-btn chrome"
              type="button"
              onClick={handleClear}
            >
              DISMISS
            </button>
          </div>
        )}

        {guardReport && !guardRunning && (
          <div className="guard-report-scroll">
            <ReportPanel
              report={guardReport}
              onOpenWorkbench={handleOpenWorkbench}
            />
          </div>
        )}
      </div>
    </div>
  );
}
