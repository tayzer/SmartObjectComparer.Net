export interface ReportBootstrap {
  schemaVersion: number;
  mode: 'single-file' | 'static-site' | string;
  report: ReportHeader;
  defaultPageSize: number;
  detailChunkSize: number;
  indexPath?: string | null;
  index?: ReportIndex | null;
  detailChunks?: ReportDetailChunk[] | null;
}

export interface ReportHeader {
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
  metadataKeys: string[];
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

export interface ReportIndex {
  totalPairs: number;
  pairs: PairSummary[];
  patterns: PatternCluster[];
  patternMetadata?: Record<string, PatternMetadata>;
  statusCounts: LabelCount[];
  comparisonKindCounts: LabelCount[];
  topFields: LabelCount[];
}

export interface PairSummary {
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
  previewLabel?: string | null;
  previewChange?: string | null;
  affectedFields: string[];
  categoryCounts: LabelCount[];
  rootObjectCounts: LabelCount[];
  patternKeys: string[];
  searchText: string;
  detailChunkId: string;
  detailChunkPath?: string | null;
}

export interface PatternCluster {
  key: string;
  label: string;
  kind: string;
  count: number;
  description?: string | null;
  samplePairIds: string[];
}

export interface PatternMetadata {
  label: string;
  kind: string;
  description?: string | null;
}

export interface ReportDetailChunk {
  chunkId: string;
  pairIds: string[];
  pairs: PairDetail[];
}

export interface PairDetail {
  pairId: string;
  index: number;
  areEqual: boolean;
  hasError: boolean;
  errorMessage?: string | null;
  errorType?: string | null;
  differences: StructuredDifference[];
  rawTextDifferences: RawTextDifference[];
  diffDocument?: DiffDocument | null;
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

export interface DiffDocument {
  format: 'xml' | 'json' | 'text' | string;
  leftLabel: string;
  rightLabel: string;
  totalLines: number;
  changedLineCount: number;
  lines: DiffLine[];
}

export interface DiffLine {
  rowNumber: number;
  changeType: 'unchanged' | 'modified' | 'inserted' | 'deleted' | string;
  leftLineNumber?: number | null;
  rightLineNumber?: number | null;
  leftText?: string | null;
  rightText?: string | null;
}