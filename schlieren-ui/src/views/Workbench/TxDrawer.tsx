import { useCallback } from 'react';
import { useAppStore } from '../../engine/store';
import { FORKS } from '../../engine/forks';
import './TxDrawer.css';

// ─── Props ────────────────────────────────────────────────────────────────────

interface TxDrawerProps {
  open: boolean;
  onClose: () => void;
}

// ─── Helpers ──────────────────────────────────────────────────────────────────

/** Strip trailing zeroes and normalise a hex value string. */
function normaliseHex(raw: string): string {
  const s = raw.trim();
  if (!s.startsWith('0x') && !s.startsWith('0X')) return s;
  return s;
}

// ─── TxDrawer ─────────────────────────────────────────────────────────────────

export function TxDrawer({ open, onClose }: TxDrawerProps) {
  const config = useAppStore((s) => s.config);
  const setConfig = useAppStore((s) => s.setConfig);

  const handleFrom = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setConfig({ from: e.target.value }),
    [setConfig]
  );

  const handleTo = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setConfig({ to: e.target.value }),
    [setConfig]
  );

  const handleValue = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setConfig({ value: e.target.value }),
    [setConfig]
  );

  const handleValueBlur = useCallback(
    (e: React.FocusEvent<HTMLInputElement>) =>
      setConfig({ value: normaliseHex(e.target.value) }),
    [setConfig]
  );

  const handleGasLimit = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) => {
      const n = parseInt(e.target.value.replace(/[^0-9]/g, ''), 10);
      if (!isNaN(n)) setConfig({ gasLimit: n });
    },
    [setConfig]
  );

  const handleCalldata = useCallback(
    (e: React.ChangeEvent<HTMLInputElement>) =>
      setConfig({ calldata: e.target.value }),
    [setConfig]
  );

  const handleFork = useCallback(
    (e: React.ChangeEvent<HTMLSelectElement>) =>
      setConfig({ fork: e.target.value }),
    [setConfig]
  );

  const handleBackdropClick = useCallback(
    (e: React.MouseEvent<HTMLDivElement>) => {
      if (e.target === e.currentTarget) onClose();
    },
    [onClose]
  );

  return (
    <div
      className={`tx-drawer-backdrop ${open ? 'tx-drawer-backdrop--open' : ''}`}
      onClick={handleBackdropClick}
      aria-hidden={!open}
    >
      <aside
        className={`tx-drawer ${open ? 'tx-drawer--open' : ''}`}
        role="dialog"
        aria-label="Transaction parameters"
      >
        {/* ── Header ── */}
        <header className="tx-drawer-header">
          <div className="tx-drawer-header-left">
            <span className="tx-drawer-dot" />
            <span className="tx-drawer-title">TX PARAMS</span>
          </div>
          <button
            className="tx-drawer-close"
            onClick={onClose}
            aria-label="Close TX drawer"
          >
            ✕
          </button>
        </header>

        {/* ── Body ── */}
        <div className="tx-drawer-body">

          {/* ── Fork selector (top — most consequential) ── */}
          <div className="tx-field tx-field--fork">
            <label className="tx-label" htmlFor="tx-fork">FORK</label>
            <div className="tx-select-wrap">
              <select
                id="tx-fork"
                className="tx-select"
                value={config.fork}
                onChange={handleFork}
              >
                {FORKS.map((f) => (
                  <option key={f} value={f}>{f}</option>
                ))}
              </select>
              <span className="tx-select-arrow">▾</span>
            </div>
          </div>

          <div className="tx-divider" />

          {/* ── Address pair ── */}
          <div className="tx-field">
            <label className="tx-label" htmlFor="tx-from">FROM</label>
            <input
              id="tx-from"
              className="tx-input tx-input--addr"
              type="text"
              spellCheck={false}
              autoComplete="off"
              placeholder="0x0000…0001"
              value={config.from}
              onChange={handleFrom}
            />
          </div>

          <div className="tx-field">
            <label className="tx-label" htmlFor="tx-to">TO</label>
            <input
              id="tx-to"
              className="tx-input tx-input--addr"
              type="text"
              spellCheck={false}
              autoComplete="off"
              placeholder="0x0000…00aa"
              value={config.to}
              onChange={handleTo}
            />
          </div>

          <div className="tx-divider" />

          {/* ── Value + Gas on same row ── */}
          <div className="tx-row">
            <div className="tx-field tx-field--half">
              <label className="tx-label" htmlFor="tx-value">VALUE</label>
              <input
                id="tx-value"
                className="tx-input tx-input--hex"
                type="text"
                spellCheck={false}
                autoComplete="off"
                placeholder="0x0"
                value={config.value}
                onChange={handleValue}
                onBlur={handleValueBlur}
              />
              <span className="tx-hint">hex wei</span>
            </div>

            <div className="tx-field tx-field--half">
              <label className="tx-label" htmlFor="tx-gas">GAS LIMIT</label>
              <input
                id="tx-gas"
                className="tx-input tx-input--num"
                type="text"
                inputMode="numeric"
                spellCheck={false}
                autoComplete="off"
                placeholder="10000000"
                value={config.gasLimit}
                onChange={handleGasLimit}
              />
              <span className="tx-hint">decimal</span>
            </div>
          </div>

          <div className="tx-divider" />

          {/* ── Calldata ── */}
          <div className="tx-field">
            <label className="tx-label" htmlFor="tx-calldata">CALLDATA</label>
            <input
              id="tx-calldata"
              className="tx-input tx-input--hex tx-input--calldata"
              type="text"
              spellCheck={false}
              autoComplete="off"
              placeholder="0x"
              value={config.calldata}
              onChange={handleCalldata}
            />
            <span className="tx-hint">
              {config.calldata.length > 2
                ? `${Math.floor((config.calldata.replace(/^0x/i, '').length) / 2)} bytes`
                : 'empty'}
            </span>
          </div>

        </div>

        {/* ── Footer ── */}
        <footer className="tx-drawer-footer">
          <span className="tx-footer-note">
            Changes apply to the next execution
          </span>
        </footer>
      </aside>
    </div>
  );
}
