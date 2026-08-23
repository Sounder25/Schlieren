import type { JournalConservation, JournalFrame, JournalFrameTreeNode } from './store';

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
