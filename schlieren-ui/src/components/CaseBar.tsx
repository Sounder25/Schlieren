import { useAppStore, executionStatusLabel, executionStatus } from '../engine/store';
import './CaseBar.css';

export function CaseBar() {
  const result = useAppStore((s) => s.result);
  const currentStep = useAppStore((s) => s.currentStep);
  const config = useAppStore((s) => s.config);

  const totalSteps = result?.steps.length ?? 0;
  const step = result?.steps[currentStep];

  return (
    <div className="case-bar">
      <div className="case-field">
        <span className="case-label">TARGET</span>
        <span className="case-value">{config.to}</span>
      </div>
      <div className="case-separator" />
      <div className="case-field">
        <span className="case-label">FORK</span>
        <span className="case-value highlight">{config.fork}</span>
      </div>
      <div className="case-separator" />
      <div className="case-field">
        <span className="case-label">STEP</span>
        <span className="case-value highlight">
          {totalSteps > 0 ? `${currentStep + 1} / ${totalSteps} · F${step?.frameId}` : '— / —'}
        </span>
      </div>
      <div className="case-separator" />
      <div className="case-field">
        <span className="case-label">STEP GAS</span>
        <span className="case-value">
          {step ? `${step.gasBefore.toLocaleString()} → ${step.gasAfter.toLocaleString()} (−${step.gasCost})` : '—'}
        </span>
      </div>
      <div className="case-separator" />
      <div className="case-field">
        <span className="case-label">TX GAS</span>
        <span className="case-value">
          {result ? `${result.gasUsed.toLocaleString()} total` : '—'}
        </span>
      </div>

      {result && (
        <>
          <div className="case-separator" />
          <div className={`case-oracle-badge ${result.success ? 'agree' : executionStatus(result).toLowerCase()}`}>
            <div className={`oracle-dot ${result.success ? 'agree' : executionStatus(result).toLowerCase()}`} />
            <span>{executionStatusLabel(result)}</span>
          </div>
        </>
      )}
    </div>
  );
}
