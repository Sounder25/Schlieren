import { useState, useCallback } from 'react';
import { useAppStore } from '../../engine/store';
import type { JournalFrameTreeNode, JournalSecurityFinding } from '../../engine/store';
import './FrameTree.css';

const CALL_TYPE_CLASS: Record<string, string> = {
  CALL: 'ct-call',
  ROOT: 'ct-call',
  DELEGATECALL: 'ct-delegate',
  CALLCODE: 'ct-callcode',
  STATICCALL: 'ct-static',
  CREATE: 'ct-create',
  CREATE2: 'ct-create',
};

function normalizeCallType(callType: string): string {
  return callType.replace(/[^A-Za-z0-9]/g, '').toUpperCase();
}

function shortCallType(callType: string): string {
  const key = normalizeCallType(callType);
  if (key === 'DELEGATECALL') return 'DELG';
  if (key === 'STATICCALL') return 'STAT';
  if (key === 'CALLCODE') return 'CCOD';
  if (key === 'CREATE2') return 'NEW2';
  if (key === 'CREATE') return 'NEW';
  if (key === 'ROOT') return 'ROOT';
  return key.slice(0, 4) || 'CALL';
}

function shortAddr(addr: string): string {
  if (!addr || addr.length < 10) return addr;
  return addr.slice(0, 6) + '…' + addr.slice(-4);
}

function isDelegating(callType: string): boolean {
  const key = normalizeCallType(callType);
  return key === 'DELEGATECALL' || key === 'CALLCODE';
}

function ChainNode({
  node,
  findings,
  depth,
  isLast,
}: {
  node: JournalFrameTreeNode;
  findings: JournalSecurityFinding[];
  depth: number;
  isLast: boolean;
}) {
  const [expanded, setExpanded] = useState(depth < 2);
  const selectedFrameId = useAppStore((s) => s.selectedFrameId);
  const setSelectedFrameId = useAppStore((s) => s.setSelectedFrameId);
  const setCurrentStep = useAppStore((s) => s.setCurrentStep);
  const steps = useAppStore((s) => s.result?.steps);

  const frame = node.frame;
  const survived = frame.success !== false;
  const codeAddress = frame.codeAddress ?? frame.contractAddress;
  const dissociated = isDelegating(frame.callType)
    && frame.contractAddress.toLowerCase() !== codeAddress.toLowerCase();
  const hasChildren = node.children.length > 0;
  const hasFindings = findings.some(
    (f) => f.primaryFrameId === frame.id || f.frameAncestry.includes(frame.id),
  );
  const nodeFindings = findings.filter((f) => f.primaryFrameId === frame.id);
  const selected = selectedFrameId === frame.id;
  const callKey = normalizeCallType(frame.callType);

  const handleTap = useCallback(() => {
    setSelectedFrameId(selected ? null : frame.id);
    const index = steps?.findIndex((step) => step.frameId === frame.id) ?? -1;
    if (index >= 0) setCurrentStep(index);
    if (hasChildren) setExpanded((e) => !e);
  }, [selected, hasChildren, frame.id, steps, setSelectedFrameId, setCurrentStep]);

  return (
    <div className={`cn-wrapper ${isLast ? 'cn-last' : ''}`}>
      {depth > 0 && <div className="cn-line-v" />}

      <div
        className={[
          'cn-node',
          selected ? 'cn-selected' : '',
          !survived ? 'cn-reverted' : '',
          hasFindings ? 'cn-flagged' : '',
          dissociated ? 'cn-dissociated' : '',
        ].filter(Boolean).join(' ')}
        onClick={handleTap}
        role="button"
        tabIndex={0}
        onKeyDown={(e) => e.key === 'Enter' && handleTap()}
      >
        <div className="cn-top">
          <span className={`cn-calltype ${CALL_TYPE_CLASS[callKey] ?? 'ct-call'}`}>
            {shortCallType(frame.callType)}
          </span>

          <span className="cn-addr">
            {dissociated ? (
              <>
                <span className="cn-storage-addr" title={`storage: ${frame.contractAddress}`}>
                  {shortAddr(frame.contractAddress)}
                </span>
                <span className="cn-dissoc-arrow">←</span>
                <span className="cn-code-addr" title={`code: ${codeAddress}`}>
                  {shortAddr(codeAddress)}
                </span>
              </>
            ) : (
              <span title={frame.contractAddress}>{shortAddr(frame.contractAddress)}</span>
            )}
          </span>

          <div className="cn-indicators">
            {hasFindings && <span className="cn-finding-dot" title="Security finding" />}
            {!survived && <span className="cn-rev-tag">REV</span>}
            {node.stateEffectIds.length > 0 && (
              <span className="cn-effects-tag">{node.stateEffectIds.length}fx</span>
            )}
            {hasChildren && (
              <span className={`cn-chevron ${expanded ? 'cn-chevron-open' : ''}`}>›</span>
            )}
          </div>
        </div>

        {selected && (
          <div className="cn-detail">
            <div className="cn-detail-row">
              <span className="cn-detail-label">frame</span>
              <span className="cn-detail-val">#{frame.id}</span>
            </div>
            {dissociated && (
              <>
                <div className="cn-detail-row">
                  <span className="cn-detail-label">storage</span>
                  <span className="cn-detail-val cn-mono">{frame.contractAddress}</span>
                </div>
                <div className="cn-detail-row">
                  <span className="cn-detail-label">code</span>
                  <span className="cn-detail-val cn-mono">{codeAddress}</span>
                </div>
              </>
            )}
            <div className="cn-detail-row">
              <span className="cn-detail-label">result</span>
              <span className={`cn-detail-val ${survived ? 'cn-ok' : 'cn-rev'}`}>
                {survived ? 'success' : 'reverted'}
              </span>
            </div>
            {node.ancestorIds.length > 0 && (
              <div className="cn-detail-row">
                <span className="cn-detail-label">ancestors</span>
                <span className="cn-detail-val">
                  {node.ancestorIds.map((id) => `#${id}`).join(' › ')}
                </span>
              </div>
            )}
            {nodeFindings.length > 0 && (
              <div className="cn-findings-list">
                {nodeFindings.map((f) => (
                  <div key={f.id} className={`cn-finding sev-${f.severity.toLowerCase()}`}>
                    <span className="cn-finding-sev">{f.severity}</span>
                    <span className="cn-finding-text">{f.summary}</span>
                  </div>
                ))}
              </div>
            )}
          </div>
        )}
      </div>

      {hasChildren && expanded && (
        <div className="cn-children">
          <div className="cn-line-h" />
          <div className="cn-children-nodes">
            {node.children.map((child, i) => (
              <ChainNode
                key={child.frame.id}
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

export function FrameTree() {
  const result = useAppStore((s) => s.result);
  const frameTree = result?.frameTree ?? null;
  const findings = result?.securityFindings ?? [];
  const survivedFindings = findings.filter((f) => f.executionDisposition === 'survived');

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
            Tap any frame to jump to its first step.
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
