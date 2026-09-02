import type { ExecutionResult, RunConfig } from './store';
import { executionStatusLabel, executionStatus } from './store';

export function buildTraceExport(result: ExecutionResult, config: RunConfig) {
  // For exceptional halts, storage writes in the trace are attempted but rolled back.
  // Make this explicit so consumers don't have to infer it from success:false.
  const isExceptionalHalt = executionStatus(result) === 'FAULT';
  const mutationDisposition = result.success
    ? 'committed'
    : isExceptionalHalt
      ? 'rolled-back: exceptional-halt'
      : 'rolled-back: revert';

  return {
    format: 'schlieren-structLog-v1',
    // EIP-3155 structLog convention: stack[] is pre-op (received by opcode);
    // memory[] and storage{} are post-op (cumulative state after opcode ran).
    // Use stateEffects[] for authoritative mutation disposition and rollback evidence.
    structLogSemantics: 'stack=pre-op, memory=post-op, storage=cumulative-post-op',
    forkLabel: result.fork || config.fork,
    success: result.success,
    returnData: result.returnData,
    error: result.error ?? result.execution.error,
    gasUsed: result.gasUsed,
    mutationDisposition,
    // Reproduction inputs — sufficient to replay this execution
    reproduction: {
      bytecode: config.bytecode || null,
      calldata: config.calldata || null,
      caller: config.from,
      target: config.to,
      gasLimit: config.gasLimit,
      value: config.value,
      fork: result.fork || config.fork,
    },
    conservation: result.conservation,
    stateEffects: result.stateEffects.map(e => ({
      kind: e.kind,
      pc: e.pc,
      opcode: e.opcode,
      frameId: e.frameId,
      executionDisposition: e.executionDisposition,
      persistenceDisposition: e.persistenceDisposition,
      revertedByFrameId: e.revertedByFrameId,
      data: e.data,
    })),
    steps: result.steps.map((s, i) => ({
      step: i,
      sequence: s.sequence,
      pc: s.pc,
      op: s.op,
      gas: s.gasBefore,
      gasCost: s.gasCost,
      depth: s.depth,
      frameId: s.frameId,
      stack: s.stack,
      memory: s.memory,
      storage: s.storage,
      callType: s.callType ?? null,
      contract: s.contractAddress ?? null,
      caller: s.callerAddress ?? null,
    })),
  };
}

export function buildAuditReport(result: ExecutionResult, config: RunConfig): string {
  const generated = new Date().toISOString().replace('T', ' ').replace(/\.\d+Z$/, ' UTC');
  const lines: string[] = [
    '# SCHLIEREN — Smart Contract Security & Gas Audit Report',
    '',
    '*.NET 8 Ethereum Execution & Verification Engine*',
    '',
    `- **Target**              : \`${config.to}\``,
    `- **EVM Hard Fork**       : \`${result.fork || config.fork}\``,
    `- **Total Execution Steps**: \`${result.steps.length.toLocaleString()}\``,
    `- **Total Gas Used**      : \`${result.gasUsed.toLocaleString()}\``,
    `- **Outcome**             : \`${executionStatusLabel(result)}\``,
    `- **Conservation**        : \`${result.conservation.isConserved ? 'conserved' : `drift ${result.conservation.delta}`}\``,
    `- **Report Generated**    : \`${generated}\``,
    '',
    '## Security Vulnerabilities & Findings',
    '',
  ];

  if (result.securityFindings.length === 0) {
    lines.push('No journal-backed security findings on this path.');
  } else {
    lines.push('| Severity | Rule | Frame | Summary | Disposition |');
    lines.push('| :------- | :--- | :---- | :------ | :---------- |');
    for (const f of result.securityFindings) {
      lines.push(
        `| ${escapeCell(f.severity)} | \`${escapeCell(f.ruleId)}\` | F${f.primaryFrameId} | ${escapeCell(f.summary)} | ${escapeCell(f.executionDisposition)} / ${escapeCell(f.persistenceDisposition)} |`,
      );
    }
  }

  lines.push('', '## Execution', '');
  lines.push(`Return data: \`${result.returnData || '0x'}\``);

  if (!result.success) {
    const status = executionStatus(result);
    const faultingStep = result.steps[result.steps.length - 1];
    lines.push('');
    lines.push('### Fault Chain');
    lines.push('');
    if (faultingStep) {
      lines.push(`| Field | Value |`);
      lines.push(`| :---- | :---- |`);
      lines.push(`| Faulting opcode | \`${faultingStep.op}\` @ PC \`0x${faultingStep.pc.toString(16).padStart(2,'0')}\` |`);
      lines.push(`| Error | \`${result.error}\` |`);
      lines.push(`| Classification | ${status === 'FAULT' ? 'Exceptional halt — remaining gas burned' : 'Explicit REVERT'} |`);
      lines.push(`| Mutation disposition | ${status === 'FAULT' ? 'All storage writes rolled back' : 'All storage writes rolled back'} |`);
      lines.push(`| Gas at fault | \`${faultingStep.gas.toLocaleString()}\` remaining before instruction |`);
      if (faultingStep.stack && faultingStep.stack.length > 0) {
        lines.push(`| Stack at fault | \`[${faultingStep.stack.slice(0,4).join(', ')}${faultingStep.stack.length > 4 ? ', …' : ''}]\` |`);
      }
      // Show storage effects from journal stateEffects (authoritative, includes rollback disposition)
      const storageWrites = result.stateEffects.filter(e => e.kind === 'storageWrite');
      if (storageWrites.length > 0) {
        lines.push('');
        lines.push('### Storage Effects');
        lines.push('');
        lines.push('| PC | Slot | Value | Disposition |');
        lines.push('| :- | :--- | :---- | :---------- |');
        for (const e of storageWrites) {
          const pc = e.pc != null ? `0x${e.pc.toString(16)}` : '—';
          const slot = String(e.data?.slot ?? '—').slice(0, 20);
          const val  = String(e.data?.value ?? '—').slice(0, 20);
          const disp = e.persistenceDisposition === 'committedToState'
            ? '✓ committed'
            : e.persistenceDisposition === 'simulationDiscarded'
              ? '✗ simulation discarded'
              : `✗ rolled back (frame ${e.revertedByFrameId ?? '?'})`;
          lines.push(`| \`${pc}\` | \`${slot}…\` | \`${val}…\` | ${disp} |`);
        }
      }
    }
  }

  lines.push('');
  return lines.join('\n');
}

export function downloadText(filename: string, text: string, mime: string) {
  const blob = new Blob([text], { type: mime });
  const url = URL.createObjectURL(blob);
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = filename;
  anchor.click();
  URL.revokeObjectURL(url);
}

function escapeCell(value: string): string {
  return value.replace(/\|/g, '\\|').replace(/\n/g, ' ');
}
