export interface ComparisonReport {
  schemaVersion: number;
  reportId: string;
  generatedAt: string;
  command: string;
  model?: string | null;
  directory1?: string | null;
  directory2?: string | null;
  endpointA?: string | null;
  endpointB?: string | null;
  jobId?: string | null;
  elapsedSeconds: number;
  summary: ComparisonSummary;
  mostAffectedFields: MostAffectedFields;
  pairOutcomeCounts: LabelCount[];
  filePairs: ComparisonFilePair[];
  metadata?: Record<string, unknown> | null;
}

export interface ComparisonSummary {
  totalPairs: number;
  allEqual: boolean;
  equalCount: number;
  differentCount: number;
  errorCount: number;
}

export interface MostAffectedFields {
  top: number;
  structuredPairCount: number;
  excludedRawTextPairCount: number;
  fields: MostAffectedField[];
}

export interface MostAffectedField {
  fieldPath: string;
  affectedPairCount: number;
  occurrenceCount: number;
}

export interface LabelCount {
  label: string;
  count: number;
}

export interface ComparisonFilePair {
  pairId: string;
  index: number;
  file1: string;
  file2: string;
  file1Path?: string | null;
  file2Path?: string | null;
  requestRelativePath?: string | null;
  areEqual: boolean;
  hasError: boolean;
  errorMessage?: string | null;
  errorType?: string | null;
  pairOutcome?: string | null;
  httpStatusA?: number | null;
  httpStatusB?: number | null;
  differenceCount: number;
  comparisonKind: 'equal' | 'structured' | 'raw-text' | 'error' | string;
  affectedFields: string[];
  categoryCounts: LabelCount[];
  rootObjectCounts: LabelCount[];
  differences: StructuredDifference[];
  rawTextDifferences: RawTextDifference[];
}

export interface StructuredDifference {
  propertyName: string;
  expected: unknown;
  actual: unknown;
}

export interface RawTextDifference {
  type: string;
  lineNumberA?: number | null;
  lineNumberB?: number | null;
  textA?: string | null;
  textB?: string | null;
  description: string;
}