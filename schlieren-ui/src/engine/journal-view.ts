import type { JournalConservation, JournalFrame } from './store';

export interface FrameRow extends JournalFrame {
  indent: number;
}

export function buildFrameRows(frames: JournalFrame[]): FrameRow[] {
  const children = new Map<number | null, JournalFrame[]>();
  for (const frame of frames) {
    const group = children.get(frame.parentId) ?? [];
    group.push(frame);
    children.set(frame.parentId, group);
  }

  const rows: FrameRow[] = [];
  const visit = (frame: JournalFrame, indent: number) => {
    rows.push({ ...frame, indent });
    for (const child of children.get(frame.id) ?? []) visit(child, indent + 1);
  };
  for (const root of children.get(null) ?? []) visit(root, 0);
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
