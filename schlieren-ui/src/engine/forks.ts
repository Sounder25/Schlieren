/** Canonical fork labels accepted by ForkRulesFactory.For (not Prague-fallback aliases). */
export const FORKS = [
  'Frontier',
  'Homestead',
  'TangerineWhistle',
  'SpuriousDragon',
  'Byzantium',
  'Constantinople',
  'Istanbul',
  'Berlin',
  'London',
  'Paris',
  'Shanghai',
  'Cancun',
  'Prague',
  'Osaka',
] as const;

export type ForkName = (typeof FORKS)[number];
