import { useAppStore } from '../engine/store';
import './StatusBar.css';

export function StatusBar() {
  const connected = useAppStore((s) => s.connected);
  const result = useAppStore((s) => s.result);
  const config = useAppStore((s) => s.config);
  const lastError = useAppStore((s) => s.lastError);

  return (
    <div className="status-bar">
      <div className="status-item">
        <div className={`status-dot ${connected ? 'green' : 'dim'}`} />
        <span>{connected ? 'Schlieren RPC' : 'Disconnected'}</span>
      </div>
      <div className="status-sep" />
      {lastError && (
        <>
          <span style={{ color: 'var(--sig-fracture)' }}>{lastError}</span>
          <div className="status-sep" />
        </>
      )}
      {result && (
        <>
          <span>
            {result.steps.length.toLocaleString()} steps · {' '}
            {result.success ? 'SUCCESS' : 'REVERT'} · {' '}
            {result.gasUsed.toLocaleString()} gas
          </span>
          <div className="status-sep" />
        </>
      )}
      <span style={{ marginLeft: 'auto', color: 'var(--t-ghost)' }}>
        Schlieren 1.0.0 · .NET 8 · {config.fork.toLowerCase()}
      </span>
    </div>
  );
}
