import { useDeferredValue, useEffect, useMemo, useRef, useState, useTransition } from 'react';
import type {
	DiffDocument,
	DiffLine,
	LabelCount,
	PairSummary,
	PatternCluster,
	ReportBootstrap,
	ReportDetailChunk,
	ReportIndex,
} from './types';

const REVIEW_CATEGORIES = ['Unreviewed', 'Needs Review', 'Accepted Difference', 'Bug Identified'] as const;
const PRIMARY_TRIAGE_CATEGORIES = ['Needs Review', 'Accepted Difference', 'Bug Identified'] as const;
const PAGE_SIZE_OPTIONS = [25, 50, 100, 250];
const COLLAPSED_CONTEXT_LINES = 6;

type ReviewCategory = (typeof REVIEW_CATEGORIES)[number];
type PrimaryTriageCategory = (typeof PRIMARY_TRIAGE_CATEGORIES)[number];
type StatusFilter = 'all' | 'different' | 'equal' | 'error';
type SortKey = 'index' | 'differenceCount' | 'file1' | 'status';
type DiffViewMode = 'split' | 'unified';

interface AppProps {
	bootstrap: ReportBootstrap | null;
	loadError: string | null;
}

interface DiffSection {
	id: string;
	kind: 'changed' | 'context';
	lines: DiffLine[];
}

interface PatternInfo {
	key: string;
	label: string;
	kind: string;
	description?: string | null;
}

export default function App({ bootstrap, loadError }: AppProps) {
	if (bootstrap == null) {
		return (
			<div className="content empty-panel">
				<strong>Unable to load the comparison report</strong>
				<p className="muted">{loadError ?? 'The embedded report payload was missing.'}</p>
			</div>
		);
	}

	return <ReportView bootstrap={bootstrap} />;
}

function ReportView({ bootstrap }: { bootstrap: ReportBootstrap }) {
	const report = bootstrap.report;
	const [isFilteringPending, startFilteringTransition] = useTransition();
	const [index, setIndex] = useState<ReportIndex | null>(bootstrap.index ?? null);
	const [indexError, setIndexError] = useState<string | null>(null);
	const [indexLoading, setIndexLoading] = useState<boolean>(bootstrap.index == null);
	const [statusFilter, setStatusFilter] = useState<StatusFilter>(
		report.summary.differentCount > 0 || report.summary.errorCount > 0 ? 'different' : 'all',
	);
	const [reviewFilter, setReviewFilter] = useState<'All triage states' | ReviewCategory>('All triage states');
	const [comparisonKindFilter, setComparisonKindFilter] = useState<string>('all');
	const [searchText, setSearchText] = useState('');
	const deferredSearchText = useDeferredValue(searchText);
	const [fieldFilter, setFieldFilter] = useState<string | null>(null);
	const [patternFilter, setPatternFilter] = useState<string | null>(null);
	const [sortKey, setSortKey] = useState<SortKey>('differenceCount');
	const [pageSize, setPageSize] = useState<number>(normalizePageSize(bootstrap.defaultPageSize));
	const [currentPage, setCurrentPage] = useState(1);
	const [selectedPairId, setSelectedPairId] = useState<string | null>(null);
	const [diffViewMode, setDiffViewMode] = useState<DiffViewMode>('split');
	const [activeDiffBlockIndex, setActiveDiffBlockIndex] = useState(0);
	const [expandedSections, setExpandedSections] = useState<Record<string, boolean>>({});
	const [detailError, setDetailError] = useState<string | null>(null);
	const [loadingChunks, setLoadingChunks] = useState<Record<string, boolean>>({});
	const [condensedMode, setCondensedMode] = useState(true);
	const searchInputRef = useRef<HTMLInputElement | null>(null);
	const diffSectionRefs = useRef<Record<string, HTMLDivElement | null>>({});
	const legacyStorageKey = useMemo(() => `comparison-tool-report:${report.reportId}:review-categories`, [report.reportId]);
	const storageKey = useMemo(() => `comparison-tool-report:${report.reportId}:triage-categories:v2`, [report.reportId]);
	const [reviewCategories, setReviewCategories] = useState<Record<string, ReviewCategory>>(() => readCategories(storageKey, legacyStorageKey));
	const inlineChunkCache = useMemo(() => {
		const entries = (bootstrap.detailChunks ?? []).map((chunk) => [chunk.chunkId, chunk] as const);
		return Object.fromEntries(entries) as Record<string, ReportDetailChunk>;
	}, [bootstrap.detailChunks]);
	const [chunkCache, setChunkCache] = useState<Record<string, ReportDetailChunk>>(inlineChunkCache);
	const chunkCacheRef = useRef<Record<string, ReportDetailChunk>>(inlineChunkCache);
	const inflightChunksRef = useRef<Record<string, Promise<void>>>({});

	useEffect(() => {
		setChunkCache((current) => ({ ...current, ...inlineChunkCache }));
	}, [inlineChunkCache]);

	useEffect(() => {
		chunkCacheRef.current = chunkCache;
	}, [chunkCache]);

	useEffect(() => {
		let cancelled = false;

		if (bootstrap.index != null) {
			setIndex(bootstrap.index);
			setIndexLoading(false);
			return undefined;
		}

		if (bootstrap.indexPath == null || bootstrap.indexPath.trim() === '') {
			setIndexError('No index path was embedded for this static-site report.');
			setIndexLoading(false);
			return undefined;
		}

		const staticSiteFileProtocolError = getStaticSiteFileProtocolError(bootstrap.mode, bootstrap.indexPath);
		if (staticSiteFileProtocolError != null) {
			setIndexError(staticSiteFileProtocolError);
			setIndexLoading(false);
			return undefined;
		}

		setIndexLoading(true);
		setIndexError(null);

		void (async () => {
			try {
				const response = await fetch(bootstrap.indexPath as string, { cache: 'no-cache' });
				if (!response.ok) {
					throw new Error(`Failed to load report index (${response.status} ${response.statusText}).`);
				}

				const data = (await response.json()) as ReportIndex;
				if (!cancelled) {
					setIndex(data);
					setIndexLoading(false);
				}
			} catch (error) {
				if (!cancelled) {
					setIndexError(error instanceof Error ? error.message : 'Unknown report index load failure.');
					setIndexLoading(false);
				}
			}
		})();

		return () => {
			cancelled = true;
		};
	}, [bootstrap.index, bootstrap.indexPath, bootstrap.mode]);

	useEffect(() => {
		try {
			localStorage.setItem(storageKey, JSON.stringify(reviewCategories));
		} catch {
			// Ignore storage failures for static artifacts.
		}
	}, [reviewCategories, storageKey]);

	const allPairs = index?.pairs ?? [];
	const prioritizedPatterns = useMemo(() => prioritizePatterns(index?.patterns ?? []), [index]);
	const patternInfoLookup = useMemo(() => {
		const map = new Map<string, PatternInfo>();
		for (const pattern of index?.patterns ?? []) {
			map.set(pattern.key, {
				key: pattern.key,
				label: pattern.label,
				kind: pattern.kind,
				description: pattern.description,
			});
		}

		for (const [key, metadata] of Object.entries(index?.patternMetadata ?? {})) {
			if (!map.has(key)) {
				map.set(key, {
					key,
					label: metadata.label,
					kind: metadata.kind,
					description: metadata.description,
				});
			}
		}

		return map;
	}, [index]);
	const patternLabelLookup = useMemo(
		() => new Map([...patternInfoLookup.values()].map((pattern) => [pattern.key, pattern.label] as const)),
		[patternInfoLookup],
	);

	const filteredPairs = useMemo(() => {
		const search = deferredSearchText.trim().toLowerCase();

		return allPairs.filter((pair) => {
			const pairStatus = getPairStatus(pair);
			const reviewCategory = reviewCategories[pair.pairId] ?? 'Unreviewed';
			const matchesStatus =
				statusFilter === 'all'
					? true
					: statusFilter === 'different'
						? pairStatus === 'different' || pairStatus === 'error'
						: pairStatus === statusFilter;
			const matchesReview = reviewFilter === 'All triage states' ? true : reviewCategory === reviewFilter;
			const matchesKind = comparisonKindFilter === 'all' ? true : pair.comparisonKind === comparisonKindFilter;
			const matchesField = fieldFilter == null ? true : pair.affectedFields.includes(fieldFilter);
			const matchesPattern = patternFilter == null ? true : pair.patternKeys.includes(patternFilter);
			const matchesSearch = search.length === 0 ? true : pair.searchText.includes(search);

			return matchesStatus && matchesReview && matchesKind && matchesField && matchesPattern && matchesSearch;
		});
	}, [allPairs, comparisonKindFilter, deferredSearchText, fieldFilter, patternFilter, reviewCategories, reviewFilter, statusFilter]);

	const sortedPairs = useMemo(() => {
		const pairs = [...filteredPairs];
		pairs.sort((left, right) => comparePairs(left, right, sortKey));
		return pairs;
	}, [filteredPairs, sortKey]);
	const filteredPatternPairsByKey = useMemo(() => buildPatternPairMap(sortedPairs), [sortedPairs]);

	const pageCount = Math.max(1, Math.ceil(sortedPairs.length / pageSize));

	useEffect(() => {
		if (currentPage > pageCount) {
			setCurrentPage(pageCount);
		}
	}, [currentPage, pageCount]);

	useEffect(() => {
		if (sortedPairs.length === 0) {
			setSelectedPairId(null);
			return;
		}

		if (selectedPairId == null || !sortedPairs.some((pair) => pair.pairId === selectedPairId)) {
			setSelectedPairId(sortedPairs[0].pairId);
		}
	}, [selectedPairId, sortedPairs]);

	const selectedPair = selectedPairId == null ? null : sortedPairs.find((pair) => pair.pairId === selectedPairId) ?? null;
	const selectedPairIndex = selectedPair == null ? -1 : sortedPairs.findIndex((pair) => pair.pairId === selectedPair.pairId);

	useEffect(() => {
		if (selectedPairIndex < 0) {
			return;
		}

		const desiredPage = Math.floor(selectedPairIndex / pageSize) + 1;
		if (desiredPage !== currentPage) {
			setCurrentPage(desiredPage);
		}
	}, [currentPage, pageSize, selectedPairIndex]);

	const visiblePairs = useMemo(() => {
		const startIndex = (currentPage - 1) * pageSize;
		return sortedPairs.slice(startIndex, startIndex + pageSize);
	}, [currentPage, pageSize, sortedPairs]);

	async function ensureChunkLoaded(pair: PairSummary) {
		if (chunkCacheRef.current[pair.detailChunkId] != null) {
			return;
		}

		if (pair.detailChunkPath == null || pair.detailChunkPath.trim() === '') {
			return;
		}

		if (inflightChunksRef.current[pair.detailChunkId] != null) {
			await inflightChunksRef.current[pair.detailChunkId];
			return;
		}

		setLoadingChunks((current) => ({ ...current, [pair.detailChunkId]: true }));
		setDetailError(null);

		const loadPromise = (async () => {
			try {
				const staticSiteFileProtocolError = getStaticSiteFileProtocolError(bootstrap.mode, pair.detailChunkPath);
				if (staticSiteFileProtocolError != null) {
					throw new Error(staticSiteFileProtocolError);
				}

				const response = await fetch(pair.detailChunkPath as string, { cache: 'no-cache' });
				if (!response.ok) {
					throw new Error(`Failed to load detail chunk (${response.status} ${response.statusText}).`);
				}

				const data = (await response.json()) as ReportDetailChunk;
				setChunkCache((current) => ({ ...current, [data.chunkId]: data }));
			} catch (error) {
				setDetailError(error instanceof Error ? error.message : 'Unknown detail chunk load failure.');
			} finally {
				setLoadingChunks((current) => {
					const next = { ...current };
					delete next[pair.detailChunkId];
					return next;
				});
				delete inflightChunksRef.current[pair.detailChunkId];
			}
		})();

		inflightChunksRef.current[pair.detailChunkId] = loadPromise;
		await loadPromise;
	}

	useEffect(() => {
		if (selectedPair == null) {
			return;
		}

		void ensureChunkLoaded(selectedPair);
	}, [selectedPair]);

	useEffect(() => {
		const uniquePairs = new Map<string, PairSummary>();
		for (const pair of visiblePairs) {
			if (!uniquePairs.has(pair.detailChunkId)) {
				uniquePairs.set(pair.detailChunkId, pair);
			}
		}

		void Promise.all([...uniquePairs.values()].map((pair) => ensureChunkLoaded(pair)));
	}, [visiblePairs]);

	const selectedDetail = useMemo(() => {
		if (selectedPair == null) {
			return null;
		}

		const chunk = chunkCache[selectedPair.detailChunkId];
		return chunk?.pairs.find((pair) => pair.pairId === selectedPair.pairId) ?? null;
	}, [chunkCache, selectedPair]);

	const selectedChunkLoading = selectedPair == null ? false : loadingChunks[selectedPair.detailChunkId] === true;
	const diffSections = useMemo(() => buildDiffSections(selectedDetail?.diffDocument?.lines ?? []), [selectedDetail]);
	const changedSections = useMemo(() => diffSections.filter((section) => section.kind === 'changed'), [diffSections]);
	const selectedPattern = useMemo(() => (patternFilter == null ? null : patternInfoLookup.get(patternFilter) ?? null), [patternFilter, patternInfoLookup]);
	const selectedPatternBulkEligible = selectedPattern != null && isBulkTriagePattern(selectedPattern);
	const activePatternPairs = useMemo(
		() => (patternFilter == null ? [] : filteredPatternPairsByKey.get(patternFilter) ?? []),
		[filteredPatternPairsByKey, patternFilter],
	);

	useEffect(() => {
		setActiveDiffBlockIndex(0);
		setExpandedSections({});
	}, [selectedPairId]);

	useEffect(() => {
		if (changedSections.length === 0) {
			return;
		}

		const boundedIndex = Math.min(activeDiffBlockIndex, changedSections.length - 1);
		if (boundedIndex !== activeDiffBlockIndex) {
			setActiveDiffBlockIndex(boundedIndex);
			return;
		}

		const activeSection = changedSections[boundedIndex];
		diffSectionRefs.current[activeSection.id]?.scrollIntoView({ behavior: 'smooth', block: 'center' });
	}, [activeDiffBlockIndex, changedSections]);

	const reviewCounts = useMemo(() => buildReviewCounts(allPairs, reviewCategories), [allPairs, reviewCategories]);
	const reviewedCount = useMemo(
		() => allPairs.filter((pair) => (reviewCategories[pair.pairId] ?? 'Unreviewed') !== 'Unreviewed').length,
		[allPairs, reviewCategories],
	);
	const actionableCount = report.summary.differentCount + report.summary.errorCount;
	const reviewedActionableCount = useMemo(
		() =>
			allPairs.filter(
				(pair) =>
					(getPairStatus(pair) === 'different' || getPairStatus(pair) === 'error') &&
					(reviewCategories[pair.pairId] ?? 'Unreviewed') !== 'Unreviewed',
			).length,
		[allPairs, reviewCategories],
	);
	const progressPercent = actionableCount === 0 ? 100 : Math.round((reviewedActionableCount / actionableCount) * 100);

	function updateReviewCategory(pairId: string, category: ReviewCategory, options?: { advance?: boolean }) {
		setReviewCategories((current) => ({ ...current, [pairId]: category }));
		if (options?.advance === true) {
			selectRelativePair(1);
		}
	}

	function clearReviewCategory(pairId: string) {
		setReviewCategories((current) => ({ ...current, [pairId]: 'Unreviewed' }));
	}

	function bulkApplyReviewCategory(pairIds: string[], category: ReviewCategory) {
		setReviewCategories((current) => {
			const next = { ...current };
			for (const pairId of pairIds) {
				next[pairId] = category;
			}
			return next;
		});
	}

	function selectRelativePair(offset: number) {
		if (selectedPairIndex < 0) {
			return;
		}

		const nextPair = sortedPairs[selectedPairIndex + offset];
		if (nextPair != null) {
			setSelectedPairId(nextPair.pairId);
		}
	}

	useEffect(() => {
		function handleKeyDown(event: KeyboardEvent) {
			const target = event.target as HTMLElement | null;
			if (target != null && ['INPUT', 'TEXTAREA', 'SELECT'].includes(target.tagName)) {
				return;
			}

			if (event.key === '/') {
				event.preventDefault();
				searchInputRef.current?.focus();
				return;
			}

			if (event.key === 'u' || event.key === 'U') {
				event.preventDefault();
				setDiffViewMode((current) => (current === 'split' ? 'unified' : 'split'));
				return;
			}

			if (event.key === 'n' || event.key === 'N' || event.key === ']') {
				event.preventDefault();
				setActiveDiffBlockIndex((current) => Math.min(current + 1, Math.max(changedSections.length - 1, 0)));
				return;
			}

			if (event.key === 'p' || event.key === 'P' || event.key === '[') {
				event.preventDefault();
				setActiveDiffBlockIndex((current) => Math.max(current - 1, 0));
				return;
			}

			if (event.key === 'j' || event.key === 'ArrowDown') {
				event.preventDefault();
				selectRelativePair(1);
				return;
			}

			if (event.key === 'k' || event.key === 'ArrowUp') {
				event.preventDefault();
				selectRelativePair(-1);
				return;
			}

			if (selectedPair != null && event.key === '1') {
				event.preventDefault();
				updateReviewCategory(selectedPair.pairId, 'Bug Identified', { advance: true });
				return;
			}

			if (selectedPair != null && event.key === '2') {
				event.preventDefault();
				updateReviewCategory(selectedPair.pairId, 'Accepted Difference', { advance: true });
				return;
			}

			if (selectedPair != null && event.key === '3') {
				event.preventDefault();
				updateReviewCategory(selectedPair.pairId, 'Needs Review', { advance: true });
				return;
			}

			if (event.key === 'c' || event.key === 'C') {
				event.preventDefault();
				setCondensedMode((current) => !current);
			}
		}

		window.addEventListener('keydown', handleKeyDown);
		return () => window.removeEventListener('keydown', handleKeyDown);
	}, [changedSections.length, selectedPair, selectedPairIndex, sortedPairs]);

	if (indexLoading && index == null) {
		return (
			<div className="content empty-panel">
				<strong>Preparing the audit queue</strong>
				<p className="muted">The index is loading so the queue and pattern board can stay responsive.</p>
			</div>
		);
	}

	if (index == null) {
		return (
			<div className="content empty-panel">
				<strong>Unable to load the comparison index</strong>
				<p className="muted">{indexError ?? 'The report index was unavailable.'}</p>
			</div>
		);
	}

	return (
		<div className="audit-shell">
			<header className="audit-header panel panel-hero">
				<div className="audit-header-left">
					<p className="eyebrow">High-velocity auditing</p>
					<h1 className="title">Production-line review workspace</h1>
					<p className="subtitle">
						{formatCommandLabel(report.command)} • {index.totalPairs} pairs • Generated {formatDateTime(report.generatedAt)}
					</p>
					<div className="chip-row top-gap-tight">
						<span className="chip">Mode: {humanizeLabel(bootstrap.mode)}</span>
						<span className="chip">{report.elapsedSeconds.toFixed(2)}s</span>
						{report.model ? <span className="chip">Model: {report.model}</span> : null}
					</div>
				</div>
				<div className="audit-header-right">
					<div className="triage-metric-strip">
						<MetricCard label="Different" value={report.summary.differentCount} tone="warn" />
						<MetricCard label="Equal" value={report.summary.equalCount} tone="ok" />
						<MetricCard label="Errors" value={report.summary.errorCount} tone="danger" />
						<MetricCard label="Reviewed" value={`${reviewedCount}/${index.totalPairs}`} tone="neutral" />
					</div>
					<div className="progress-card compact-progress-card">
						<div className="progress-meta">
							<strong>{progressPercent}%</strong>
							<span className="muted">Actionable queue processed</span>
						</div>
						<div className="progress-track">
							<div className="progress-value" style={{ width: `${progressPercent}%` }} />
						</div>
					</div>
				</div>
			</header>

			<div className="audit-controls panel">
				<div className="audit-controls-grid">
					<div className="filter-row">
						{(['different', 'all', 'equal', 'error'] as const).map((filter) => (
							<button
								key={filter}
								className={`filter-button ${statusFilter === filter ? 'active' : ''}`}
								onClick={() =>
									startFilteringTransition(() => {
										setStatusFilter(filter);
										setCurrentPage(1);
									})
								}
								type="button"
							>
								{formatFilterLabel(filter)}
							</button>
						))}
					</div>
					<input
						ref={searchInputRef}
						className="search-input"
						placeholder="Search files, change previews, fields, or outcomes"
						value={searchText}
						onChange={(event) =>
							startFilteringTransition(() => {
								setSearchText(event.target.value);
								setCurrentPage(1);
							})
						}
					/>
					<select
						className="select-input"
						value={reviewFilter}
						onChange={(event) =>
							startFilteringTransition(() => {
								setReviewFilter(event.target.value as 'All triage states' | ReviewCategory);
								setCurrentPage(1);
							})
						}
					>
						<option>All triage states</option>
						{REVIEW_CATEGORIES.map((category) => (
							<option key={category}>{category}</option>
						))}
					</select>
					<select
						className="select-input"
						value={comparisonKindFilter}
						onChange={(event) =>
							startFilteringTransition(() => {
								setComparisonKindFilter(event.target.value);
								setCurrentPage(1);
							})
						}
					>
						<option value="all">All comparison kinds</option>
						{index.comparisonKindCounts.map((item) => (
							<option key={item.label} value={item.label}>
								{humanizeLabel(item.label)} ({item.count})
							</option>
						))}
					</select>
					<select className="select-input" value={sortKey} onChange={(event) => setSortKey(event.target.value as SortKey)}>
						<option value="differenceCount">Sort by difference count</option>
						<option value="index">Sort by input order</option>
						<option value="status">Sort by status</option>
						<option value="file1">Sort by file name</option>
					</select>
					<select
						className="select-input"
						value={pageSize}
						onChange={(event) => {
							setPageSize(Number(event.target.value));
							setCurrentPage(1);
						}}
					>
						{PAGE_SIZE_OPTIONS.map((option) => (
							<option key={option} value={option}>
								{option} queue rows
							</option>
						))}
					</select>
					<button className={`filter-button ${condensedMode ? 'active' : ''}`} onClick={() => setCondensedMode((current) => !current)} type="button">
						{condensedMode ? 'Condensed mode on' : 'Expanded mode'}
					</button>
					<span className={`state-pill ${isFilteringPending ? 'active' : ''}`}>{isFilteringPending ? 'Filtering' : 'Ready'}</span>
				</div>
				{fieldFilter != null || patternFilter != null ? (
					<div className="chip-row audit-active-filters">
						{fieldFilter != null ? (
							<button className="chip chip-button active" onClick={() => setFieldFilter(null)} type="button">
								Field: {fieldFilter} ×
							</button>
						) : null}
						{patternFilter != null ? (
							<button className="chip chip-button active" onClick={() => setPatternFilter(null)} type="button">
								Pattern: {findPatternLabel(patternLabelLookup, patternFilter)} ×
							</button>
						) : null}
					</div>
				) : null}
			</div>

			<div className="audit-body">
				<section className="queue-column">
					<section className="panel triage-command-panel">
						<div className="panel-header compact-header">
							<div>
								<h2 className="section-title">Common patterns</h2>
								<p className="muted">Start by bulk-triaging recurring exact changes and high-frequency diff clusters.</p>
							</div>
						</div>
						<div className="panel-body section-stack">
							{selectedPattern != null ? (
								<div className="bulk-pattern-bar">
									<div>
										<p className="eyebrow">Bulk triage</p>
										<h3 className="section-title">{selectedPattern.label}</h3>
										{selectedPatternBulkEligible ? <span className="badge top-gap-tight">Cluster {formatPatternSignature(selectedPattern.key)}</span> : null}
										<p className="muted">
											{selectedPattern.description ?? 'Recurring difference cluster'} • {activePatternPairs.length} matching pairs in the current queue
										</p>
										{selectedPatternBulkEligible ? null : (
											<p className="muted">This pattern is useful for filtering, but bulk triage is limited to exact recurring changes and raw diff duplicates.</p>
										)}
									</div>
									<div className="bulk-pattern-actions">
										{selectedPatternBulkEligible ? (
											<TriageButtons
												activeCategory={null}
												onApply={(category) => bulkApplyReviewCategory(activePatternPairs.map((pair) => pair.pairId), category)}
											/>
										) : null}
										<button
											className="toolbar-button"
											disabled={activePatternPairs.length === 0}
											onClick={() => setSelectedPairId(activePatternPairs[0]?.pairId ?? null)}
											type="button"
										>
											Open first match
										</button>
										<button className="toolbar-button" onClick={() => setPatternFilter(null)} type="button">
											Clear pattern
										</button>
									</div>
								</div>
							) : null}
							<div className="pattern-grid triage-pattern-grid">
								{prioritizedPatterns.slice(0, 18).map((pattern) => {
									const matchingPairs = filteredPatternPairsByKey.get(pattern.key) ?? [];
									const bulkEligible = isBulkTriagePattern(pattern);
									return (
										<button
											key={pattern.key}
											className={`pattern-card ${patternFilter === pattern.key ? 'active' : ''}`}
											onClick={() => {
												setPatternFilter((current) => (current === pattern.key ? null : pattern.key));
												setCurrentPage(1);
											}}
											type="button"
										>
											<span className="pattern-kind">{humanizeLabel(pattern.kind)}</span>
											<strong>{pattern.label}</strong>
											<p className="pattern-description">{pattern.description ?? 'Recurring difference cluster'}</p>
											<div className="pattern-card-footer">
												<span className="badge">{pattern.count} pairs</span>
												{bulkEligible ? <span className="badge">Cluster {formatPatternSignature(pattern.key)}</span> : <span className="badge">filter only</span>}
												{bulkEligible ? <span className="badge">{matchingPairs.length} in queue</span> : null}
											</div>
										</button>
									);
								})}
							</div>
						</div>
					</section>

					{index.topFields.length > 0 ? (
						<section className="panel field-hotspot-panel">
							<div className="panel-header compact-header">
								<div>
									<h2 className="section-title">Field hotspots</h2>
									<p className="muted">Jump straight into the most frequently affected fields across the full report.</p>
								</div>
							</div>
							<div className="panel-body">
								<div className="chip-row">
									{index.topFields.slice(0, 16).map((field) => (
										<button
											className={`chip chip-button ${fieldFilter === field.label ? 'active' : ''}`}
											key={field.label}
											onClick={() => {
												setFieldFilter((current) => (current === field.label ? null : field.label));
												setCurrentPage(1);
											}}
											type="button"
										>
											{field.label} ({field.count})
										</button>
									))}
								</div>
							</div>
						</section>
					) : null}

					<section className="panel queue-panel panel-fill">
						<div className="panel-header compact-header">
							<div>
								<h2 className="section-title">Pairs queue</h2>
								<p className="muted">
									Showing {(currentPage - 1) * pageSize + (sortedPairs.length === 0 ? 0 : 1)}-
									{Math.min(currentPage * pageSize, sortedPairs.length)} of {sortedPairs.length}
								</p>
							</div>
							<div className="pager-row">
								<button className="toolbar-button" disabled={currentPage <= 1} onClick={() => setCurrentPage((page) => page - 1)} type="button">
									Previous page
								</button>
								<span className="state-pill">{currentPage}/{pageCount}</span>
								<button className="toolbar-button" disabled={currentPage >= pageCount} onClick={() => setCurrentPage((page) => page + 1)} type="button">
									Next page
								</button>
							</div>
						</div>
						<div className="panel-body pair-list triage-pair-list">
							{sortedPairs.length === 0 ? (
								<div className="empty-state">
									<strong>No pairs match the current queue filters</strong>
									<p className="muted">Clear the active pattern, field, or search filters to reopen the queue.</p>
								</div>
							) : (
								visiblePairs.map((pair) => {
									const triageState = reviewCategories[pair.pairId] ?? 'Unreviewed';
									const chunkReady = chunkCache[pair.detailChunkId] != null;
									return (
										<article className={`queue-item ${pair.pairId === selectedPair?.pairId ? 'selected' : ''}`} key={pair.pairId}>
											<button className="queue-item-focus" onClick={() => setSelectedPairId(pair.pairId)} type="button">
												<div className="queue-item-head">
													<div>
														<div className="queue-item-title">#{pair.index} {pair.file1}</div>
														<div className="queue-item-meta">{pair.requestRelativePath ?? pair.file2}</div>
													</div>
													<div className="queue-item-badges">
														<PairStatusBadge pair={pair} />
														<span className={`badge review-${slugifyReviewCategory(triageState)}`}>{triageState}</span>
													</div>
												</div>
												<div className="queue-item-preview">
													<div className="queue-item-preview-label">{getPairPreviewLabel(pair)}</div>
													<div className="queue-item-preview-change">{getPairPreviewChange(pair)}</div>
												</div>
												{condensedMode ? null : (
													<div className="queue-item-detail-row">
														<div className="badge-row">
															<span className="badge">{pair.differenceCount} diffs</span>
															<span className={`badge ${chunkReady ? 'status-ready' : 'status-lazy'}`}>{chunkReady ? 'detail ready' : 'lazy chunk'}</span>
															{pair.patternKeys.slice(0, 2).map((patternKey) => (
																<span className="chip compact-chip" key={patternKey}>{findPatternLabel(patternLabelLookup, patternKey)}</span>
															))}
														</div>
													</div>
												)}
											</button>
											<div className="queue-item-actions">
												<TriageButtons activeCategory={triageState} onApply={(category) => updateReviewCategory(pair.pairId, category)} compact />
												<button className="toolbar-button subtle-button" onClick={() => clearReviewCategory(pair.pairId)} type="button">
													Clear
												</button>
											</div>
										</article>
									);
								})
							)}
						</div>
					</section>

					<section className="panel compact-summary-panel">
						<div className="panel-body compact-summary-grid">
							{reviewCounts.map((item) => (
								<div className="mini-stat" key={item.label}>
									<strong>{item.count}</strong>
									<span className="muted">{item.label}</span>
								</div>
							))}
							<button className="export-button" onClick={() => exportCategories(report, index.pairs, reviewCategories)} type="button">
								Export triage JSON
							</button>
						</div>
					</section>
				</section>

				<section className="detail-column">
					{selectedPair == null ? (
						<section className="panel empty-panel">
							<strong>Select a queue item</strong>
							<p className="muted">The investigator lane opens the selected pair without taking you back through the queue.</p>
						</section>
					) : (
						<>
							<section className="panel sticky-toolbar investigator-toolbar">
								<div className="panel-body investigator-toolbar-grid">
									<div>
										<p className="eyebrow">Investigator lane</p>
										<h2 className="detail-title">{selectedPair.file1}</h2>
										<p className="muted">{selectedPair.requestRelativePath ?? selectedPair.file2}</p>
									</div>
									<div className="investigator-action-cluster">
										<div className="toolbar-cluster">
											<button className="toolbar-button" disabled={selectedPairIndex <= 0} onClick={() => selectRelativePair(-1)} type="button">
												Previous pair
											</button>
											<button className="toolbar-button" disabled={selectedPairIndex >= sortedPairs.length - 1} onClick={() => selectRelativePair(1)} type="button">
												Next pair
											</button>
										</div>
										<TriageButtons
											activeCategory={reviewCategories[selectedPair.pairId] ?? 'Unreviewed'}
											onApply={(category) => updateReviewCategory(selectedPair.pairId, category, { advance: true })}
										/>
										<button className="toolbar-button subtle-button" onClick={() => clearReviewCategory(selectedPair.pairId)} type="button">
											Clear
										</button>
									</div>
								</div>
								<div className="panel-body investigator-summary-row">
									<DetailCard label="Current triage" value={reviewCategories[selectedPair.pairId] ?? 'Unreviewed'} />
									<DetailCard label="Preview" value={getPairPreviewLabel(selectedPair)} />
									<DetailCard label="Change" value={getPairPreviewChange(selectedPair)} />
									<DetailCard label="Differences" value={selectedPair.differenceCount} />
									{selectedPair.pairOutcome ? <DetailCard label="Request outcome" value={humanizeLabel(selectedPair.pairOutcome)} /> : null}
								</div>
							</section>

							<section className="panel quick-facts-panel">
								<div className="panel-body section-stack">
									<div className="detail-grid">
										<DetailCard label="Status" value={formatPairStatus(getPairStatus(selectedPair))} />
										<DetailCard label="Comparison kind" value={humanizeLabel(selectedPair.comparisonKind)} />
										<DetailCard label="Chunk" value={selectedPair.detailChunkId} />
										{selectedPair.httpStatusA != null || selectedPair.httpStatusB != null ? (
											<DetailCard label="HTTP status" value={`A: ${selectedPair.httpStatusA ?? '—'} | B: ${selectedPair.httpStatusB ?? '—'}`} />
										) : null}
									</div>
									<div className="chip-row">
										{selectedPair.patternKeys.map((patternKey) => (
											<button
												className={`chip chip-button ${patternFilter === patternKey ? 'active' : ''}`}
												key={patternKey}
												onClick={() => setPatternFilter((current) => (current === patternKey ? null : patternKey))}
												type="button"
											>
												{findPatternLabel(patternLabelLookup, patternKey)}
											</button>
										))}
									</div>
									{selectedPair.affectedFields.length > 0 ? (
										<div className="chip-row">
											{selectedPair.affectedFields.map((field) => (
												<button
													className={`chip chip-button ${fieldFilter === field ? 'active' : ''}`}
													key={field}
													onClick={() => setFieldFilter((current) => (current === field ? null : field))}
													type="button"
												>
													{field}
												</button>
											))}
										</div>
									) : null}
									<div className="shortcut-row">
										<span className="shortcut-pill">1</span>
										<span className="muted">bug + next</span>
										<span className="shortcut-pill">2</span>
										<span className="muted">accepted + next</span>
										<span className="shortcut-pill">3</span>
										<span className="muted">needs review + next</span>
										<span className="shortcut-pill">j / k</span>
										<span className="muted">next or previous pair</span>
										<span className="shortcut-pill">n / p</span>
										<span className="muted">next or previous diff block</span>
									</div>
								</div>
							</section>

							{selectedChunkLoading && selectedDetail == null ? (
								<section className="panel empty-panel">
									<strong>Loading pair detail</strong>
									<p className="muted">Fetching the detail chunk for this pair.</p>
								</section>
							) : null}

							{detailError ? (
								<section className="panel">
									<div className="panel-body error-callout">
										<h3 className="section-title">Detail load error</h3>
										<p className="value-block">{detailError}</p>
									</div>
								</section>
							) : null}

							{selectedDetail?.hasError ? (
								<section className="panel">
									<div className="panel-body error-callout">
										<h3 className="section-title">Comparison error</h3>
										<p className="value-block">{selectedDetail.errorMessage ?? 'Unknown comparison error.'}</p>
										{selectedDetail.errorType ? <p className="muted">Type: {selectedDetail.errorType}</p> : null}
									</div>
								</section>
							) : null}

							{selectedDetail != null && selectedDetail.diffDocument != null ? (
								<section className="panel diff-stage-panel">
									<div className="panel-header compact-header">
										<div>
											<h3 className="section-title">Changed line view</h3>
											<p className="muted">
												{selectedDetail.diffDocument.format.toUpperCase()} • {selectedDetail.diffDocument.changedLineCount} changed lines
											</p>
										</div>
										<div className="toolbar-cluster">
											<button className={`toolbar-button ${diffViewMode === 'split' ? 'active' : ''}`} onClick={() => setDiffViewMode('split')} type="button">
												Split view
											</button>
											<button className={`toolbar-button ${diffViewMode === 'unified' ? 'active' : ''}`} onClick={() => setDiffViewMode('unified')} type="button">
												Unified view
											</button>
											<button className="toolbar-button" disabled={changedSections.length === 0 || activeDiffBlockIndex <= 0} onClick={() => setActiveDiffBlockIndex((current) => Math.max(current - 1, 0))} type="button">
												Previous diff
											</button>
											<span className="state-pill">{changedSections.length === 0 ? '0/0' : `${activeDiffBlockIndex + 1}/${changedSections.length}`}</span>
											<button className="toolbar-button" disabled={changedSections.length === 0 || activeDiffBlockIndex >= changedSections.length - 1} onClick={() => setActiveDiffBlockIndex((current) => Math.min(current + 1, Math.max(changedSections.length - 1, 0)))} type="button">
												Next diff
											</button>
										</div>
									</div>
									<div className="panel-body diff-viewer-shell">
										<DiffViewer
											document={selectedDetail.diffDocument}
											mode={diffViewMode}
											sections={diffSections}
											activeSectionId={changedSections[activeDiffBlockIndex]?.id ?? null}
											expandedSections={expandedSections}
											onToggleSection={(sectionId) => setExpandedSections((current) => ({ ...current, [sectionId]: !current[sectionId] }))}
											onRegisterSection={(sectionId, element) => {
												diffSectionRefs.current[sectionId] = element;
											}}
										/>
									</div>
								</section>
							) : null}

							{selectedPair.categoryCounts.length > 0 || selectedPair.rootObjectCounts.length > 0 ? (
								<section className="panel">
									<div className="panel-body detail-grid-grid">
										{selectedPair.categoryCounts.length > 0 ? (
											<div className="detail-subpanel">
												<h3 className="section-title">Difference categories</h3>
												<div className="chip-row top-gap">
													{selectedPair.categoryCounts.map((item) => (
														<span className="chip" key={item.label}>{humanizeLabel(item.label)}: {item.count}</span>
													))}
												</div>
											</div>
										) : null}
										{selectedPair.rootObjectCounts.length > 0 ? (
											<div className="detail-subpanel">
												<h3 className="section-title">Root objects</h3>
												<div className="chip-row top-gap">
													{selectedPair.rootObjectCounts.map((item) => (
														<span className="chip" key={item.label}>{item.label}: {item.count}</span>
													))}
												</div>
											</div>
										) : null}
									</div>
								</section>
							) : null}

							{selectedDetail != null && selectedDetail.differences.length > 0 ? (
								<section className="panel">
									<div className="panel-header compact-header">
										<div>
											<h3 className="section-title">Structured validation</h3>
											<p className="muted">Property-level differences for final verification.</p>
										</div>
									</div>
									<div className="panel-body table-scroll">
										<table className="diff-table">
											<thead>
												<tr>
													<th>Property</th>
													<th>Expected</th>
													<th>Actual</th>
												</tr>
											</thead>
											<tbody>
												{selectedDetail.differences.map((difference, indexValue) => (
													<tr key={`${difference.propertyName}-${indexValue}`}>
														<td className="mono">{difference.propertyName}</td>
														<td><pre className="value-block">{formatValue(difference.expected)}</pre></td>
														<td><pre className="value-block">{formatValue(difference.actual)}</pre></td>
													</tr>
												))}
											</tbody>
										</table>
									</div>
								</section>
							) : null}

							{selectedDetail != null && selectedDetail.rawTextDifferences.length > 0 ? (
								<section className="panel">
									<div className="panel-header compact-header">
										<div>
											<h3 className="section-title">Raw validation</h3>
											<p className="muted">Fallback text-level differences for non-model comparisons.</p>
										</div>
									</div>
									<div className="panel-body diff-card-list">
										{selectedDetail.rawTextDifferences.map((difference, indexValue) => (
											<article className="diff-card" key={`${difference.type}-${indexValue}`}>
												<div className="diff-card-header">
													<strong>{humanizeLabel(difference.type)}</strong>
													<span className="badge">A:{difference.lineNumberA ?? '—'} • B:{difference.lineNumberB ?? '—'}</span>
												</div>
												<p className="diff-card-text">{difference.description}</p>
												{difference.textA ? (
													<div>
														<p className="eyebrow">Text A</p>
														<pre className="value-block">{difference.textA}</pre>
													</div>
												) : null}
												{difference.textB ? (
													<div>
														<p className="eyebrow">Text B</p>
														<pre className="value-block">{difference.textB}</pre>
													</div>
												) : null}
											</article>
										))}
									</div>
								</section>
							) : null}
						</>
					)}
				</section>
			</div>
		</div>
	);
}

function TriageButtons({
	activeCategory,
	onApply,
	compact = false,
}: {
	activeCategory: ReviewCategory | null;
	onApply: (category: PrimaryTriageCategory) => void;
	compact?: boolean;
}) {
	return (
		<div className={`triage-button-row ${compact ? 'compact' : ''}`}>
			{PRIMARY_TRIAGE_CATEGORIES.map((category) => (
				<button
					className={`triage-button ${slugifyReviewCategory(category)} ${activeCategory === category ? 'active' : ''}`}
					key={category}
					onClick={() => onApply(category)}
					type="button"
				>
					{category}
				</button>
			))}
		</div>
	);
}

function DiffViewer({
	document,
	mode,
	sections,
	activeSectionId,
	expandedSections,
	onToggleSection,
	onRegisterSection,
}: {
	document: DiffDocument;
	mode: DiffViewMode;
	sections: DiffSection[];
	activeSectionId: string | null;
	expandedSections: Record<string, boolean>;
	onToggleSection: (sectionId: string) => void;
	onRegisterSection: (sectionId: string, element: HTMLDivElement | null) => void;
}) {
	return (
		<div className="diff-viewer">
			<div className={`diff-header ${mode === 'unified' ? 'unified' : ''}`}>
				<div className="diff-file-label">{document.leftLabel}</div>
				<div className="diff-file-label">{mode === 'split' ? document.rightLabel : 'Unified view'}</div>
			</div>
			<div className="diff-body">
				{sections.map((section) => {
					const isActive = section.id === activeSectionId;
					const isExpandable = section.kind === 'context' && section.lines.length > COLLAPSED_CONTEXT_LINES;
					const isExpanded = expandedSections[section.id] === true;
					const leadingLines = isExpandable && !isExpanded ? section.lines.slice(0, 2) : section.lines;
					const trailingLines = isExpandable && !isExpanded ? section.lines.slice(-2) : [];
					const hiddenCount = isExpandable && !isExpanded ? section.lines.length - 4 : 0;

					return (
						<div className={`diff-section ${section.kind} ${isActive ? 'active' : ''}`} key={section.id} ref={(element) => onRegisterSection(section.id, element)}>
							{renderDiffRows(leadingLines, mode)}
							{hiddenCount > 0 ? (
								<button className="context-toggle" onClick={() => onToggleSection(section.id)} type="button">
									Show {hiddenCount} unchanged lines
								</button>
							) : null}
							{hiddenCount > 0 ? renderDiffRows(trailingLines, mode) : null}
						</div>
					);
				})}
			</div>
		</div>
	);
}

function renderDiffRows(lines: DiffLine[], mode: DiffViewMode) {
	if (mode === 'unified') {
		return lines.flatMap((line) => renderUnifiedLine(line));
	}

	return lines.map((line) => (
		<div className={`diff-row ${line.changeType}`} key={`split-${line.rowNumber}-${line.leftLineNumber ?? 'x'}-${line.rightLineNumber ?? 'y'}`}>
			<div className="diff-cell line-number">{line.leftLineNumber ?? ''}</div>
			<pre className="diff-cell diff-text">{line.leftText ?? ''}</pre>
			<div className="diff-cell line-number">{line.rightLineNumber ?? ''}</div>
			<pre className="diff-cell diff-text">{line.rightText ?? ''}</pre>
		</div>
	));
}

function renderUnifiedLine(line: DiffLine) {
	const baseKey = `unified-${line.rowNumber}-${line.leftLineNumber ?? 'x'}-${line.rightLineNumber ?? 'y'}`;

	if (line.changeType === 'modified') {
		return [
			<div className="diff-row deleted unified-row" key={`${baseKey}-old`}>
				<div className="diff-cell line-number">{line.leftLineNumber ?? ''}</div>
				<div className="diff-cell line-number">-</div>
				<pre className="diff-cell diff-text wide">{line.leftText ?? ''}</pre>
			</div>,
			<div className="diff-row inserted unified-row" key={`${baseKey}-new`}>
				<div className="diff-cell line-number">{line.rightLineNumber ?? ''}</div>
				<div className="diff-cell line-number">+</div>
				<pre className="diff-cell diff-text wide">{line.rightText ?? ''}</pre>
			</div>,
		];
	}

	if (line.changeType === 'deleted') {
		return [
			<div className="diff-row deleted unified-row" key={baseKey}>
				<div className="diff-cell line-number">{line.leftLineNumber ?? ''}</div>
				<div className="diff-cell line-number">-</div>
				<pre className="diff-cell diff-text wide">{line.leftText ?? ''}</pre>
			</div>,
		];
	}

	if (line.changeType === 'inserted') {
		return [
			<div className="diff-row inserted unified-row" key={baseKey}>
				<div className="diff-cell line-number">{line.rightLineNumber ?? ''}</div>
				<div className="diff-cell line-number">+</div>
				<pre className="diff-cell diff-text wide">{line.rightText ?? ''}</pre>
			</div>,
		];
	}

	return [
		<div className="diff-row unchanged unified-row" key={baseKey}>
			<div className="diff-cell line-number">{line.leftLineNumber ?? line.rightLineNumber ?? ''}</div>
			<div className="diff-cell line-number"> </div>
			<pre className="diff-cell diff-text wide">{line.leftText ?? line.rightText ?? ''}</pre>
		</div>,
	];
}

function MetricCard({ label, value, tone }: { label: string; value: number | string; tone: 'ok' | 'warn' | 'danger' | 'neutral' }) {
	return (
		<div className={`metric-card ${tone}`}>
			<strong>{value}</strong>
			<span className="muted">{label}</span>
		</div>
	);
}

function DetailCard({ label, value }: { label: string; value: number | string }) {
	return (
		<div className="detail-card">
			<div className="detail-card-label">{label}</div>
			<div className="detail-card-value">{value}</div>
		</div>
	);
}

function PairStatusBadge({ pair }: { pair: PairSummary }) {
	const status = getPairStatus(pair);
	return <span className={`badge status-${status === 'different' ? pair.comparisonKind : status}`}>{formatPairStatus(status)}</span>;
}

function buildDiffSections(lines: DiffLine[]): DiffSection[] {
	if (lines.length === 0) {
		return [];
	}

	const sections: DiffSection[] = [];
	let currentLines: DiffLine[] = [];
	let currentKind: DiffSection['kind'] = lines[0].changeType === 'unchanged' ? 'context' : 'changed';
	let blockIndex = 1;

	for (const line of lines) {
		const lineKind: DiffSection['kind'] = line.changeType === 'unchanged' ? 'context' : 'changed';
		if (currentLines.length > 0 && lineKind !== currentKind) {
			sections.push({ id: `${currentKind}-${blockIndex}`, kind: currentKind, lines: currentLines });
			blockIndex += 1;
			currentLines = [];
			currentKind = lineKind;
		}

		currentLines.push(line);
	}

	if (currentLines.length > 0) {
		sections.push({ id: `${currentKind}-${blockIndex}`, kind: currentKind, lines: currentLines });
	}

	return sections;
}

function getPairStatus(pair: PairSummary): 'equal' | 'different' | 'error' {
	if (pair.hasError) {
		return 'error';
	}

	return pair.areEqual ? 'equal' : 'different';
}

function comparePairs(left: PairSummary, right: PairSummary, sortKey: SortKey): number {
	if (sortKey === 'differenceCount') {
		return right.differenceCount - left.differenceCount || left.index - right.index;
	}

	if (sortKey === 'file1') {
		return left.file1.localeCompare(right.file1) || left.index - right.index;
	}

	if (sortKey === 'status') {
		return statusRank(getPairStatus(left)) - statusRank(getPairStatus(right)) || right.differenceCount - left.differenceCount || left.index - right.index;
	}

	return left.index - right.index;
}

function statusRank(status: 'equal' | 'different' | 'error'): number {
	switch (status) {
		case 'error':
			return 0;
		case 'different':
			return 1;
		default:
			return 2;
	}
}

function formatPairStatus(status: 'equal' | 'different' | 'error'): string {
	switch (status) {
		case 'equal':
			return 'Equal';
		case 'error':
			return 'Error';
		default:
			return 'Different';
	}
}

function formatFilterLabel(filter: StatusFilter): string {
	switch (filter) {
		case 'all':
			return 'All';
		case 'equal':
			return 'Equal';
		case 'error':
			return 'Errors';
		default:
			return 'Differences';
	}
}

function formatCommandLabel(command: string): string {
	return command === 'request' ? 'Request comparison' : 'Folder comparison';
}

function formatDateTime(value: string): string {
	const date = new Date(value);
	return Number.isNaN(date.getTime()) ? value : date.toLocaleString();
}

function formatValue(value: unknown): string {
	if (value == null) {
		return 'null';
	}

	if (typeof value === 'string') {
		return value;
	}

	if (typeof value === 'number' || typeof value === 'boolean') {
		return String(value);
	}

	try {
		return JSON.stringify(value, null, 2);
	} catch {
		return String(value);
	}
}

function humanizeLabel(label: string): string {
	return label
		.replace(/([a-z])([A-Z])/g, '$1 $2')
		.replace(/[-_]/g, ' ')
		.replace(/\s+/g, ' ')
		.trim();
}

function buildReviewCounts(pairs: PairSummary[], categories: Record<string, ReviewCategory>): LabelCount[] {
	return REVIEW_CATEGORIES.map((category) => ({
		label: category,
		count: pairs.filter((pair) => (categories[pair.pairId] ?? 'Unreviewed') === category).length,
	}));
}

function buildPatternPairMap(pairs: PairSummary[]): Map<string, PairSummary[]> {
	const map = new Map<string, PairSummary[]>();
	for (const pair of pairs) {
		for (const key of pair.patternKeys) {
			const bucket = map.get(key);
			if (bucket == null) {
				map.set(key, [pair]);
			} else {
				bucket.push(pair);
			}
		}
	}

	return map;
}

function prioritizePatterns(patterns: PatternCluster[]): PatternCluster[] {
	const exactPatterns = patterns.filter((pattern) => isBulkTriagePattern(pattern));
	const groupingPatterns = patterns.filter((pattern) => !isBulkTriagePattern(pattern));
	const sortByPriority = (left: PatternCluster, right: PatternCluster) => right.count - left.count || left.label.localeCompare(right.label);

	return [...exactPatterns.sort(sortByPriority), ...groupingPatterns.sort(sortByPriority)];
}

function isBulkTriagePattern(pattern: PatternCluster | PatternInfo | string): boolean {
	if (typeof pattern === 'string') {
		return pattern.startsWith('change:') || pattern.startsWith('raw:');
	}

	return pattern.kind === 'change' || pattern.kind === 'raw' || pattern.key.startsWith('change:') || pattern.key.startsWith('raw:');
}

function readCategories(storageKey: string, legacyStorageKey?: string): Record<string, ReviewCategory> {
	try {
		const value = localStorage.getItem(storageKey) ?? (legacyStorageKey == null ? null : localStorage.getItem(legacyStorageKey));
		if (value == null || value.trim() === '') {
			return {};
		}

		const parsed = JSON.parse(value) as Record<string, unknown>;
		return Object.fromEntries(
			Object.entries(parsed)
				.map(([pairId, category]) => [pairId, migrateReviewCategory(category)] as const)
				.filter((entry): entry is [string, ReviewCategory] => entry[1] != null),
		);
	} catch {
		return {};
	}
}

function exportCategories(report: ReportBootstrap['report'], pairs: PairSummary[], categories: Record<string, ReviewCategory>) {
	const payload = {
		reportId: report.reportId,
		generatedAt: report.generatedAt,
		exportedAt: new Date().toISOString(),
		reviews: pairs.map((pair) => ({
			pairId: pair.pairId,
			file1: pair.file1,
			file2: pair.file2,
			requestRelativePath: pair.requestRelativePath,
			category: categories[pair.pairId] ?? 'Unreviewed',
			differenceCount: pair.differenceCount,
			pairOutcome: pair.pairOutcome,
			previewLabel: getPairPreviewLabel(pair),
			previewChange: getPairPreviewChange(pair),
		})),
	};
	const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json;charset=utf-8' });
	const url = URL.createObjectURL(blob);
	const anchor = document.createElement('a');
	anchor.href = url;
	anchor.download = `comparison-review-${sanitizeFileName(report.command)}-${sanitizeFileName(report.reportId)}.json`;
	anchor.click();
	URL.revokeObjectURL(url);
}

function sanitizeFileName(value: string): string {
	return value.replace(/[^a-z0-9-_]+/gi, '-').replace(/-+/g, '-').replace(/^-|-$/g, '');
}

function normalizePageSize(value: number): number {
	if (!Number.isFinite(value) || value < 25) {
		return 100;
	}

	return PAGE_SIZE_OPTIONS.includes(value) ? value : 100;
}

function findPatternLabel(patternLookup: Map<string, string>, patternKey: string): string {
	return patternLookup.get(patternKey) ?? patternKey;
}

function getPairPreviewLabel(pair: PairSummary): string {
	if (pair.previewLabel != null && pair.previewLabel.trim() !== '') {
		return pair.previewLabel;
	}

	if (pair.hasError) {
		return pair.errorType ?? 'Comparison error';
	}

	if (pair.areEqual) {
		return 'No differences';
	}

	return pair.comparisonKind === 'raw-text' ? 'Raw difference' : 'Change summary';
}

function getPairPreviewChange(pair: PairSummary): string {
	if (pair.previewChange != null && pair.previewChange.trim() !== '') {
		return pair.previewChange;
	}

	if (pair.hasError) {
		return pair.errorMessage ?? 'An error interrupted this comparison.';
	}

	if (pair.areEqual) {
		return 'Accepted by the active comparison rules.';
	}

	return `${pair.differenceCount} detected difference${pair.differenceCount === 1 ? '' : 's'}.`;
}

function getStaticSiteFileProtocolError(mode: string, relativePath?: string | null): string | null {
	if (mode !== 'static-site' || window.location.protocol !== 'file:') {
		return null;
	}

	const pathSuffix = relativePath != null && relativePath.trim() !== '' ? ` (${relativePath})` : '';
	return `This StaticSite report cannot be opened directly from disk because browsers block file:// fetch requests${pathSuffix}. Serve the report directory over http(s), or regenerate the report with --html-mode SingleFile for local file opening.`;
}

function slugifyReviewCategory(category: ReviewCategory): string {
	return category.toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-|-$/g, '');
}

function isReviewCategory(value: unknown): value is ReviewCategory {
	return typeof value === 'string' && REVIEW_CATEGORIES.includes(value as ReviewCategory);
}

function migrateReviewCategory(value: unknown): ReviewCategory | null {
	if (isReviewCategory(value)) {
		return value;
	}

	if (typeof value !== 'string') {
		return null;
	}

	switch (value) {
		case 'Expected difference':
		case 'False positive':
		case 'Done':
			return 'Accepted Difference';
		case 'Unexpected difference':
			return 'Bug Identified';
		case 'Needs follow-up':
		case 'Blocked':
			return 'Needs Review';
		default:
			return null;
	}
}

function formatPatternSignature(patternKey: string): string {
	const hash = patternKey.split(':')[1] ?? patternKey;
	return hash.slice(0, 8).toUpperCase();
}