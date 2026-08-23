import type { JournalConservation, JournalEvent, JournalFrame, JournalFrameTreeNode, JournalSecurityFinding, TraceStep } from './store';

export interface FrameRow extends JournalFrame {
  indent: number;
  ancestorIds: number[];
  stateEffectIds: number[];
  securityFindingIds: string[];
}

export function buildFrameRows(frameTree: JournalFrameTreeNode | null): FrameRow[] {
  const rows: FrameRow[] = [];
  const visit = (node: JournalFrameTreeNode, indent: number) => {
    rows.push({
      ...node.frame,
      indent,
      ancestorIds: node.ancestorIds,
      stateEffectIds: node.stateEffectIds,
      securityFindingIds: node.securityFindingIds,
    });
    for (const child of node.children) visit(child, indent + 1);
  };
  if (frameTree) visit(frameTree, 0);
  return rows;
}

export interface SecurityRow extends JournalSecurityFinding {
  frameId: number;
  framePath: number[];
}

export function buildSecurityRows(
  frameTree: JournalFrameTreeNode | null,
  findings: JournalSecurityFinding[],
): SecurityRow[] {
  if (!frameTree) return [];
  const findingsById = new Map(findings.map((finding) => [finding.id, finding]));
  return buildFrameRows(frameTree).flatMap((frame) => frame.securityFindingIds.flatMap((id) => {
    const finding = findingsById.get(id);
    return finding ? [{ ...finding, frameId: frame.id, framePath: [...finding.frameAncestry, frame.id] }] : [];
  }));
}

export function findSecurityFindingStepIndex(
  finding: JournalSecurityFinding,
  events: JournalEvent[],
  steps: TraceStep[],
): number | null {
  if (finding.primaryInstructionId !== null) {
    const opcodeEvent = events.find((event) =>
      event.kind === 'opcodeGas' && event.instructionId === finding.primaryInstructionId);
    if (opcodeEvent) {
      const linked = steps.findIndex((step) => step.sequence === opcodeEvent.sequence);
      if (linked >= 0) return linked;
    }
  }
  const firstInFrame = steps.findIndex((step) => step.frameId === finding.primaryFrameId);
  return firstInFrame >= 0 ? firstInFrame : null;
}

export function gasEffect(event: Pick<{ semantics: string; amount: number | null }, 'semantics' | 'amount'>): 'charge' | 'credit' | 'evidence' {
  const semantics = event.semantics.toLowerCase();
  if (semantics === 'credit' || semantics.includes('return')) return 'credit';
  if (semantics === 'exclusive' || semantics.includes('burn')) return 'charge';
  return 'evidence';
}

export function getConservationState(conservation: JournalConservation): { tone: 'agree' | 'fracture'; label: string } {
  return conservation.isConserved
    ? { tone: 'agree', label: 'CONSERVED' }
    : { tone: 'fracture', label: `DRIFT ${conservation.delta} GAS` };
}
