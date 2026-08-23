import { useState } from 'react';
import { useHarvestStore, harvest } from './harvest-store';
import { useAppStore } from '../../engine/store';
import './Harvest.css';

export function Harvest() {
  const targetAddress = useHarvestStore((s) => s.targetAddress);
  const setTargetAddress = useHarvestStore((s) => s.setTargetAddress);
  const providerUrl = useHarvestStore((s) => s.providerUrl);
  const setProviderUrl = useHarvestStore((s) => s.setProviderUrl);
  const contracts = useHarvestStore((s) => s.contracts);
  const removeContract = useHarvestStore((s) => s.removeContract);
  const isHarvesting = useHarvestStore((s) => s.isHarvesting);
  const error = useHarvestStore((s) => s.error);

  const setConfig = useAppStore((s) => s.setConfig);
  const setActiveView = useAppStore((s) => s.setActiveView);

  const [showProvider, setShowProvider] = useState(false);

  const handleHarvest = async () => {
    try {
      await harvest(targetAddress);
      setTargetAddress('');
    } catch {}
  };

  const handleKeyDown = (e: React.KeyboardEvent) => {
    if (e.key === 'Enter') handleHarvest();
  };

  const loadIntoWorkbench = (bytecode: string, address: string) => {
    setConfig({ bytecode: bytecode.startsWith('0x') ? bytecode.slice(2) : bytecode, to: address });
    setActiveView('workbench');
  };

  const formatSize = (bytes: number) => {
    if (bytes < 1024) return `${bytes} B`;
    return `${(bytes / 1024).toFixed(1)} KB`;
  };

  const formatTime = (ts: number) => {
    const d = new Date(ts);
    return d.toLocaleDateString() + ' ' + d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  };

  return (
    <div className="harvest-view">
      {/* Input section */}
      <div className="harvest-input-section">
        <div className="harvest-input-row">
          <span className="harvest-input-label">CONTRACT ADDRESS</span>
          <input
            className="harvest-address-input"
            type="text"
            placeholder="0x… — paste any deployed contract address"
            value={targetAddress}
            onChange={(e) => setTargetAddress(e.target.value)}
            onKeyDown={handleKeyDown}
            spellCheck={false}
            autoComplete="off"
          />
          <button
            className="harvest-btn chrome"
            onClick={handleHarvest}
            disabled={isHarvesting || !targetAddress}
          >
            {isHarvesting ? '⟳ HARVESTING…' : '⊛ HARVEST'}
          </button>
        </div>

        <div className="harvest-meta-row">
          <button
            className="harvest-provider-toggle chrome"
            onClick={() => setShowProvider(!showProvider)}
          >
            provider: {providerUrl.replace('https://', '').split('/')[0]}
          </button>
          {error && <span className="harvest-error">{error}</span>}
        </div>

        {showProvider && (
          <div className="harvest-provider-row">
            <span className="harvest-input-label">RPC PROVIDER</span>
            <input
              className="harvest-provider-input"
              type="text"
              value={providerUrl}
              onChange={(e) => setProviderUrl(e.target.value)}
              spellCheck={false}
            />
          </div>
        )}
      </div>

      {/* Collection */}
      <div className="harvest-collection">
        <div className="harvest-collection-header">
          <span className="harvest-collection-title">
            HARVESTED ({contracts.length})
          </span>
        </div>

        {contracts.length === 0 ? (
          <div className="harvest-empty">
            <p className="harvest-empty-text">
              No contracts harvested yet. Paste an address above to pull 
              its bytecode from mainnet and load it into the Workbench for analysis.
            </p>
          </div>
        ) : (
          <div className="harvest-list">
            {contracts.map((c) => (
              <div key={c.address} className="harvest-card">
                <div className="harvest-card-main">
                  <span className="harvest-card-address">{c.address}</span>
                  <div className="harvest-card-meta">
                    <span className="harvest-card-size">{formatSize(c.sizeBytes)}</span>
                    <span className="harvest-card-sep">·</span>
                    <span className="harvest-card-network">{c.network}</span>
                    <span className="harvest-card-sep">·</span>
                    <span className="harvest-card-time">{formatTime(c.harvestedAt)}</span>
                  </div>
                </div>
                <div className="harvest-card-actions">
                  <button
                    className="harvest-action-btn load chrome"
                    onClick={() => loadIntoWorkbench(c.bytecode, c.address)}
                  >
                    ▶ ANALYZE
                  </button>
                  <button
                    className="harvest-action-btn remove chrome"
                    onClick={() => removeContract(c.address)}
                  >
                    ✕
                  </button>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
