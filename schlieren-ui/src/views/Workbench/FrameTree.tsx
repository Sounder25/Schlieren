import { useState, useCallback } from 'react';
import { useAppStore } from '../../engine/store';
import type { JournalFrameNode, SecurityFinding } from '../../engine/store';
import './FrameTree.css';

// ─── Constants ────────────────────────────────────────────────────────────────

const CALL_TYPE_SHORT: Record<string, string> = {
  Call:         'CALL',
  DelegateCall: 'DELG',
  CallCode:     'CCOD',
  StaticCall:   'STAT',
  Create:       'NEW',
  Create2:      'NEW2',
};

const CALL_TYPE_CLASS: Record<string, string> = {
  Call:         'ct-call',
  DelegateCall: 'ct-delegate',
  CallCode:     'ct-callcode',
  StaticCall:   'ct-static',
  Create:       'ct-create',
  Create2:      'ct-create',
};

function shortAddr(addr: string): string {
  if (!addr || addr.length < 10) return addr;
  return addr.slice(0, 6) + '…' + addr.slice(-4);
}

// ─── Chain node ───────────────────────────────────────────────────────────────

function ChainNode({
  node,
  findings,
  depth,
  isLast,
}: {
  node: JournalFrameNode;
  findings: SecurityFinding[];
  depth: number;
  isLast: boolean;
}) {
  const [expanded, setExpanded] = useState(depth < 2);
  const selectedFrameId    = useAppStore((s) => s.selectedFrameId);
  const setSelectedFrameId = useAppStore((s) => s.setSelectedFrameId);

  const survived    = node.executionDisposition === 'Survived';
  const delegating  = node.callType === 'DelegateCall' || node.callType === 'CallCode';
  const dissociated = delegating &&
    node.contractAddress.toLowerCase() !== node.codeAddress.toLowerCase();
  const hasChildren = node.children.length > 0;
  const hasFindings = findings.some(
    (f) => f.primaryFrameId === node.frameId || f.frameAncestry.includes(node.frameId)
  );
  const nodeFindings = findings.filter((f) => f.primaryFrameId === node.frameId);
  const selected = selectedFrameId === node.frameId;

  const handleTap = useCallback(() => {
    setSelectedFrameId(selected ? null : node.frameId);
    if (hasChildren) setExpanded((e) => !e);
  }, [selected, hasChildren, node.frameId, setSelectedFrameId]);

  return (
    <div className={`cn-wrapper ${isLast ? 'cn-last' : ''}`}>
      {/* Connector line from parent */}
      {depth > 0 && <div className="cn-line-v" />}

      {/* The node itself */}
      <div
        className={[
          'cn-node',
          selected    ? 'cn-selected'  : '',
          !survived   ? 'cn-reverted'  : '',
          hasFindings ? 'cn-flagged'   : '',
          dissociated ? 'cn-dissociated' : '',
        ].filter(Boolean).join(' ')}
        onClick={handleTap}
        role="button"
        tabIndex={0}
        onKeyDown={(e) => e.key === 'Enter' && handleTap()}
      >
        {/* Top row: call type + address */}
        <div className="cn-top">
          <span className={`cn-calltype ${CALL_TYPE_CLASS[node.callType] ?? 'ct-call'}`}>
            {CALL_TYPE_SHORT[node.callType] ?? node.callType}
          </span>

          <span className="cn-addr">
            {dissociated ? (
              <>
                <span className="cn-storage-addr" title={`storage: ${node.contractAddress}`}>
                  {shortAddr(node.contractAddress)}
                </span>
                <span className="cn-dissoc-arrow">←</span>
                <span className="cn-code-addr" title={`code: ${node.codeAddress}`}>
                  {shortAddr(node.codeAddress)}
                </span>
              </>
            ) : (
              <span title={node.contractAddress}>{shortAddr(node.contractAddress)}</span>
            )}
          </span>

          {/* indicators row */}
          <div className="cn-indicators">
            {hasFindings && <span className="cn-finding-dot" title="Security finding" />}
            {!survived && (
              <span className="cn-rev-tag">REV</span>
            )}
            {node.stateEffectIds.length > 0 && (
              <span className="cn-effects-tag">{node.stateEffectIds.length}fx</span>
            )}
            {hasChildren && (
              <span className={`cn-chevron ${expanded ? 'cn-chevron-open' : ''}`}>
                ›
              </span>
            )}
          </div>
        </div>

        {/* Expanded detail */}
        {selected && (
          <div className="cn-detail">
            <div className="cn-detail-row">
              <span className="cn-detail-label">frame</span>
              <span className="cn-detail-val">#{node.frameId}</span>
            </div>
            {dissociated && (
              <>
                <div className="cn-detail-row">
                  <span className="cn-detail-label">storage</span>
                  <span className="cn-detail-val cn-mono">{node.contractAddress}</span>
                </div>
                <div className="cn-detail-row">
                  <span className="cn-detail-label">code</span>
                  <span className="cn-detail-val cn-mono">{node.codeAddress}</span>
                </div>
              </>
            )}
            <div className="cn-detail-row">
              <span className="cn-detail-label">disposition</span>
              <span className={`cn-detail-val ${survived ? 'cn-ok' : 'cn-rev'}`}>
                {node.executionDisposition}
              </span>
            </div>
            <div className="cn-detail-row">
              <span className="cn-detail-label">persistence</span>
              <span className="cn-detail-val">{node.persistenceDisposition}</span>
            </div>
            {node.revertedByFrameId != null && (
              <div className="cn-detail-row">
                <span className="cn-detail-label">reverted by</span>
                <span className="cn-detail-val cn-rev">frame #{node.revertedByFrameId}</span>
              </div>
            )}
            {node.ancestorIds.length > 0 && (
              <div className="cn-detail-row">
                <span className="cn-detail-label">ancestors</span>
                <span className="cn-detail-val">
                  {node.ancestorIds.map((id) => `#${id}`).join(' › ')}
                </span>
              </div>
            )}
            {/* findings in this frame */}
            {nodeFindings.length > 0 && (
              <div className="cn-findings-list">
                {nodeFindings.map((f) => (
                  <div key={f.findingId} className={`cn-finding sev-${f.severity.toLowerCase()}`}>
                    <span className="cn-finding-sev">{f.severity}</span>
                    <span className="cn-finding-text">{f.summary}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </div>

      {/* Children chain */}
      {hasChildren && expanded && (
        <div className="cn-children">
          <div className="cn-line-h" />
          <div className="cn-children-nodes">
            {node.children.map((child, i) => (
              <ChainNode
                key={child.frameId}
                node={child}
                findings={findings}
                depth={depth + 1}
                isLast={i === node.children.length - 1}
              />
            ))}
          </div>
        </div>
      )}
    </div>
  );
}

// ─── FrameTree panel ──────────────────────────────────────────────────────────

export function FrameTree() {
  const result   = useAppStore((s) => s.result);
  const frameTree = result?.frameTree ?? null;
  const findings  = result?.securityFindings ?? [];

  const survivedFindings = findings.filter((f) => f.executionDisposition === 'Survived');

  if (!result || !frameTree) {
    return (
      <div className="ft-pane">
        <div className="ft-header">
          <span className="ft-header-title">Frame Chain</span>
        </div>
        <div className="ft-empty">
          <div className="ft-empty-glyph">⬡</div>
          <p className="ft-empty-text">
            Execute to see the call chain.
            Tap any frame to expand its evidence.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="ft-pane">
      <div className="ft-header">
        <span className="ft-header-title">Frame Chain</span>
        {survivedFindings.length > 0 && (
          <span className="ft-header-findings">
            {survivedFindings.length} finding{survivedFindings.length !== 1 ? 's' : ''}
          </span>
        )}
      </div>

      <div className="ft-scroll">
        <ChainNode
          node={frameTree}
          findings={findings}
          depth={0}
          isLast
        />
      </div>
    </div>
  );
}
