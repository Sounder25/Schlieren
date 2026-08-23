import { buildSecurityRows, findSecurityFindingStepIndex } from '../../engine/journal-view';
import { useAppStore, type ExecutionResult, type JournalSecurityFinding } from '../../engine/store';
import './Diagnostics.css';

export function SecurityFindings({
  result,
  onFocus,
}: {
  result: ExecutionResult | null;
  onFocus: (finding: JournalSecurityFinding) => void;
}) {
  if (!result) {
    return (
      <p className="diag-idle-note">
        Security analysis runs automatically on execution and links each finding to journal evidence.
      </p>
    );
  }

  const findings = buildSecurityRows(result.frameTree, result.securityFindings);
  if (findings.length === 0)
    return <span className="diag-empty-note">No findings in this execution</span>;

  return (
    <div className="diag-findings">
      {findings.map((finding) => (
        <button
          className={`diag-finding severity-${finding.severity.toLowerCase()}`}
          key={finding.id}
          onClick={() => onFocus(finding)}
          type="button"
        >
          <span className="diag-finding-header">
            <span className="diag-finding-rule">{finding.ruleId.split('.').pop()}</span>
            <span className="diag-finding-severity">{finding.severity.toUpperCase()}</span>
          </span>
          <span className="diag-finding-summary">{finding.summary}</span>
          <span className="diag-finding-evidence">
            Frame {finding.primaryFrameId} · {finding.executionDisposition} · {finding.persistenceDisposition}
          </span>
          {finding.addresses.length > 0 && (
            <span className="diag-finding-evidence">Addresses: {finding.addresses.join(', ')}</span>
          )}
          {finding.storageSlots.length > 0 && (
            <span className="diag-finding-evidence">Slots: {finding.storageSlots.join(', ')}</span>
          )}
          <span className="diag-finding-evidence">
            {finding.supportingEventSequences.length} evidence event{finding.supportingEventSequences.length === 1 ? '' : 's'}
          </span>
          <span className="diag-finding-limitation">{finding.limitation}</span>
        </button>
      ))}
    </div>
  );
}

export function Diagnostics() {
  const result = useAppStore((s) => s.result);
  const connected = useAppStore((s) => s.connected);
  const setCurrentStep = useAppStore((s) => s.setCurrentStep);

  const focusFinding = (finding: JournalSecurityFinding) => {
    if (!result) return;
    const index = findSecurityFindingStepIndex(finding, result.events, result.steps);
    if (index !== null) setCurrentStep(index);
  };

  return (
    <div className="diagnostics-pane">
      <div className="diag-scroll">
        {/* Execution Diagnosis */}
        <section className="diag-section">
          <header className="diag-section-header">
            <div className="diag-icon diagnosis" />
            <span className="diag-section-title">Execution Diagnosis</span>
            {result && (
              <span className={`diag-confidence ${result.success ? 'high' : 'fracture'}`}>
                {result.success ? 'CLEAN' : 'HALTED'}
              </span>
            )}
          </header>
          <div className="diag-section-body">
            {!result ? (
              <p className="diag-idle-note">
                Execute bytecode to generate causal analysis.
                The diagnosis engine classifies failures by gas rule,
                EIP subsystem, and oracle agreement.
              </p>
            ) : result.success ? (
              <div className="diag-card ok">
                <span className="diag-headline">No divergence detected</span>
                <span className="diag-detail">
                  {result.steps.length.toLocaleString()} steps ·{' '}
                  {result.gasUsed.toLocaleString()} gas · fork {result.fork}
                </span>
              </div>
            ) : (
              <div className="diag-card fracture">
                <span className="diag-headline">Execution halted — REVERT</span>
                <span className="diag-detail">
                  {result.error || 'Reverted without reason string. Inspect trace for the halt point.'}
                </span>
              </div>
            )}
          </div>
        </section>

        {/* Security Findings */}
        <section className="diag-section">
          <header className="diag-section-header">
            <div className="diag-icon security" />
            <span className="diag-section-title">Security Findings</span>
          </header>
          <div className="diag-section-body">
            <SecurityFindings result={result} onFocus={focusFinding} />
          </div>
        </section>

        {/* Oracle Comparison */}
        <section className="diag-section">
          <header className="diag-section-header">
            <div className="diag-icon oracle" />
            <span className="diag-section-title">Oracle Comparison</span>
          </header>
          <div className="diag-section-body">
            <p className="diag-idle-note">
              Differential analysis against EELS and REVM oracles.
              Fractures appear here when gas accounting diverges.
            </p>
          </div>
        </section>

        {/* Evidence Chain */}
        <section className="diag-section">
          <header className="diag-section-header">
            <div className="diag-icon evidence" />
            <span className="diag-section-title">Evidence Chain</span>
          </header>
          <div className="diag-section-body">
            <p className="diag-idle-note">
              Linked evidence from diagnosis → security → oracle.
              Each finding traces back to a specific step, frame, and gas rule.
            </p>
          </div>
        </section>

        {/* Connection status */}
        <section className="diag-section diag-connection">
          <header className="diag-section-header">
            <div className={`diag-icon ${connected ? 'connected' : 'disconnected'}`} />
            <span className="diag-section-title">Engine</span>
          </header>
          <div className="diag-section-body">
            <div className="diag-connection-status">
              <span className={`connection-label ${connected ? 'on' : 'off'}`}>
                {connected ? 'Schlieren RPC connected' : 'Not connected'}
              </span>
              <span className="connection-endpoint">
                {useAppStore.getState().endpoint}
              </span>
            </div>
          </div>
        </section>
      </div>
    </div>
  );
}
