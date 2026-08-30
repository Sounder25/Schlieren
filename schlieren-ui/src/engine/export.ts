import type { ExecutionResult, RunConfig } from './store';

export function buildTraceExport(result: ExecutionResult, config: RunConfig) {
  return {
    format: 'schlieren-structLog-v1',
    forkLabel: result.fork || config.fork,
    success: result.success,
    returnData: result.returnData,
    error: result.error ?? result.execution.error,
    gasUsed: result.gasUsed,
    caller: config.from,
    contract: config.to,
    conservation: result.conservation,
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
    `- **Outcome**             : \`${result.success ? 'SUCCESS' : 'REVERT'}\``,
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
  if (result.error) lines.push(`Error: ${result.error}`);
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
