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

const REVIEW_CATEGORIES = [
	'Unreviewed',
	'Expected difference',
	'Unexpected difference',
	'False positive',
	'Needs follow-up',
	'Blocked',
	'Done',
] as const;

const PAGE_SIZE_OPTIONS = [25, 50, 100, 250];
const COLLAPSED_CONTEXT_LINES = 6;

type ReviewCategory = (typeof REVIEW_CATEGORIES)[number];
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
	const [reviewFilter, setReviewFilter] = useState<'All review categories' | ReviewCategory>('All review categories');
	const [comparisonKindFilter, setComparisonKindFilter] = useState<string>('all');
	const [searchText, setSearchText] = useState('');
	const deferredSearchText = useDeferredValue(searchText);
	const [fieldFilter, setFieldFilter] = useState<string | null>(null);
	const [patternFilter, setPatternFilter] = useState<string | null>(null);
	const [sortKey, setSortKey] = useState<SortKey>('index');
	const [pageSize, setPageSize] = useState<number>(normalizePageSize(bootstrap.defaultPageSize));
	const [currentPage, setCurrentPage] = useState(1);
	const [selectedPairId, setSelectedPairId] = useState<string | null>(null);
	const [diffViewMode, setDiffViewMode] = useState<DiffViewMode>('split');
	const [activeDiffBlockIndex, setActiveDiffBlockIndex] = useState(0);
	const [expandedSections, setExpandedSections] = useState<Record<string, boolean>>({});
	const [detailError, setDetailError] = useState<string | null>(null);
	const [loadingChunks, setLoadingChunks] = useState<Record<string, boolean>>({});
	const searchInputRef = useRef<HTMLInputElement | null>(null);
	const diffSectionRefs = useRef<Record<string, HTMLDivElement | null>>({});
	const storageKey = useMemo(() => `comparison-tool-report:${report.reportId}:review-categories`, [report.reportId]);
	const [reviewCategories, setReviewCategories] = useState<Record<string, ReviewCategory>>(() => readCategories(storageKey));
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
	}, [bootstrap.index, bootstrap.indexPath]);

	useEffect(() => {
		try {
			localStorage.setItem(storageKey, JSON.stringify(reviewCategories));
		} catch {
			// Ignore storage failures for static artifacts.
		}
	}, [reviewCategories, storageKey]);

	const allPairs = index?.pairs ?? [];
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
			const matchesReview = reviewFilter === 'All review categories' ? true : reviewCategory === reviewFilter;
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
			}
		}

		window.addEventListener('keydown', handleKeyDown);
		return () => window.removeEventListener('keydown', handleKeyDown);
	}, [changedSections.length, selectedPairIndex, sortedPairs]);

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

	function selectRelativePair(offset: number) {
		if (selectedPairIndex < 0) {
			return;
		}

		const nextPair = sortedPairs[selectedPairIndex + offset];
		if (nextPair != null) {
			setSelectedPairId(nextPair.pairId);
		}
	}

	function updateReviewCategory(pairId: string, category: ReviewCategory) {
		setReviewCategories((current) => ({ ...current, [pairId]: category }));
	}

	if (indexLoading && index == null) {
		return (
			<div className="app-shell">
				<aside className="sidebar">
					<section className="panel panel-hero">
						<div className="panel-body">
							<p className="eyebrow">Comparison artifact</p>
							<h1 className="title">Loading report index</h1>
							<p className="muted">The HTML shell loaded. The pair index is being fetched from the static artifact.</p>
						</div>
					</section>
				</aside>
				<main className="content">
					<section className="panel empty-panel">
						<strong>Preparing the large-report index</strong>
						<p className="muted">This static site only fetches the pair index and detail chunks on demand.</p>
					</section>
				</main>
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
		<div className="app-shell">
			<aside className="sidebar">
				<section className="panel panel-hero">
					<div className="panel-body">
						<p className="eyebrow">Static comparison artifact</p>
						<h1 className="title">Manual triage workspace</h1>
						<p className="subtitle">
							{formatCommandLabel(report.command)} • Generated {formatDateTime(report.generatedAt)}
						</p>
						<div className="chip-row top-gap">
							<span className="chip">Mode: {humanizeLabel(bootstrap.mode)}</span>
							<span className="chip">{report.summary.totalPairs} pairs</span>
							<span className="chip">{report.elapsedSeconds.toFixed(2)}s</span>
							{report.model ? <span className="chip">Model: {report.model}</span> : null}
						</div>
					</div>
				</section>

				<section className="panel">
					<div className="panel-body">
						<div className="metric-grid">
							<MetricCard label="Different" value={report.summary.differentCount} tone="warn" />
							<MetricCard label="Equal" value={report.summary.equalCount} tone="ok" />
							<MetricCard label="Errors" value={report.summary.errorCount} tone="danger" />
							<MetricCard label="Reviewed" value={`${reviewedCount}/${index.totalPairs}`} tone="neutral" />
						</div>
						<div className="progress-card top-gap">
							<div className="progress-meta">
								<strong>{progressPercent}%</strong>
								<span className="muted">Actionable pairs reviewed</span>
							</div>
							<div className="progress-track">
								<div className="progress-value" style={{ width: `${progressPercent}%` }} />
							</div>
							<p className="muted">{reviewedActionableCount} of {actionableCount} different or error pairs have a review label.</p>
						</div>
					</div>
				</section>

				<section className="panel">
					<div className="panel-header compact-header">
						<div>
							<h2 className="section-title">Filters</h2>
							<p className="muted">Client-side filtering runs against the pre-built pair index.</p>
						</div>
						<span className={`state-pill ${isFilteringPending ? 'active' : ''}`}>{isFilteringPending ? 'Filtering' : 'Ready'}</span>
					</div>
					<div className="panel-body section-stack">
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
							placeholder="Search file names, request paths, outcomes, or affected fields"
							value={searchText}
							onChange={(event) =>
								startFilteringTransition(() => {
									setSearchText(event.target.value);
									setCurrentPage(1);
								})
							}
						/>
						<div className="control-grid">
							<select
								className="select-input"
								value={reviewFilter}
								onChange={(event) =>
									startFilteringTransition(() => {
										setReviewFilter(event.target.value as 'All review categories' | ReviewCategory);
										setCurrentPage(1);
									})
								}
							>
								<option>All review categories</option>
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
						</div>
						<div className="control-grid">
							<select className="select-input" value={sortKey} onChange={(event) => setSortKey(event.target.value as SortKey)}>
								<option value="index">Sort by input order</option>
								<option value="differenceCount">Sort by difference count</option>
								<option value="file1">Sort by file name</option>
								<option value="status">Sort by status</option>
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
										{option} rows per page
									</option>
								))}
							</select>
						</div>
						{fieldFilter != null || patternFilter != null ? (
							<div className="chip-row">
								{fieldFilter != null ? (
									<button className="chip chip-button active" onClick={() => setFieldFilter(null)} type="button">
										Field: {fieldFilter} ×
									</button>
								) : null}
								{patternFilter != null ? (
									<button className="chip chip-button active" onClick={() => setPatternFilter(null)} type="button">
										Pattern: {findPatternLabel(index.patterns, patternFilter)} ×
									</button>
								) : null}
							</div>
						) : null}
					</div>
				</section>

				<section className="panel">
					<div className="panel-header compact-header">
						<div>
							<h2 className="section-title">Common patterns</h2>
							<p className="muted">Recurring fields, outcomes, and semantic groups for pattern-based triage.</p>
						</div>
					</div>
					<div className="panel-body pattern-grid">
						{index.patterns.slice(0, 12).map((pattern) => (
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
								<span className="muted">{pattern.count} pairs</span>
								{pattern.description ? <span className="muted small-text">{pattern.description}</span> : null}
							</button>
						))}
					</div>
				</section>

				{report.mostAffectedFields.fields.length > 0 ? (
					<section className="panel">
						<div className="panel-body">
							<h2 className="section-title">Most affected fields</h2>
							<div className="chip-row top-gap">
								{report.mostAffectedFields.fields.map((field) => (
									<button
										key={field.fieldPath}
										className={`chip chip-button ${fieldFilter === field.fieldPath ? 'active' : ''}`}
										onClick={() => {
											setFieldFilter((current) => (current === field.fieldPath ? null : field.fieldPath));
											setCurrentPage(1);
										}}
										type="button"
									>
										{field.fieldPath} ({field.affectedPairCount})
									</button>
								))}
							</div>
						</div>
					</section>
				) : null}

				<section className="panel panel-fill">
					<div className="panel-header compact-header">
						<div>
							<h2 className="section-title">Pairs</h2>
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
					<div className="panel-body pair-list">
						{sortedPairs.length === 0 ? (
							<div className="empty-state">
								<strong>No pairs match the current filters</strong>
								<p className="muted">Clear the search or active pattern and field filters to broaden the result set.</p>
							</div>
						) : (
							visiblePairs.map((pair) => {
								const category = reviewCategories[pair.pairId] ?? 'Unreviewed';
								const chunkReady = chunkCache[pair.detailChunkId] != null;

								return (
									<button
										className={`pair-list-item ${pair.pairId === selectedPair?.pairId ? 'active' : ''}`}
										key={pair.pairId}
										onClick={() => setSelectedPairId(pair.pairId)}
										type="button"
									>
										<div className="pair-list-header">
											<div>
												<div className="pair-list-name">{pair.file1}</div>
												<div className="pair-list-path">{pair.requestRelativePath ?? pair.file2}</div>
											</div>
											<PairStatusBadge pair={pair} />
										</div>
										<div className="badge-row top-gap-tight">
											<span className="badge">#{pair.index}</span>
											<span className="badge">{pair.differenceCount} diffs</span>
											<span className={`badge ${chunkReady ? 'status-ready' : 'status-lazy'}`}>
												{chunkReady ? 'Detail ready' : 'Lazy chunk'}
											</span>
										</div>
										{pair.patternKeys.length > 0 ? (
											<div className="chip-row top-gap-tight">
												{pair.patternKeys.slice(0, 2).map((patternKey) => (
													<span className="chip compact-chip" key={patternKey}>
														{findPatternLabel(index.patterns, patternKey)}
													</span>
												))}
											</div>
										) : null}
										<div className="pair-review-row top-gap">
											<span className="muted">Review</span>
											<select
												className="pair-review-select"
												value={category}
												onChange={(event) => {
													event.stopPropagation();
													updateReviewCategory(pair.pairId, event.target.value as ReviewCategory);
												}}
												onClick={(event) => event.stopPropagation()}
											>
												{REVIEW_CATEGORIES.map((option) => (
													<option key={option}>{option}</option>
												))}
											</select>
										</div>
									</button>
								);
							})
						)}
					</div>
				</section>

				<section className="panel">
					<div className="panel-header compact-header">
						<div>
							<h2 className="section-title">Review export</h2>
							<p className="muted">Persisted in local browser storage per report ID.</p>
						</div>
						<button className="export-button" onClick={() => exportCategories(report, index.pairs, reviewCategories)} type="button">
							Export JSON
						</button>
					</div>
					<div className="panel-body review-grid">
						{reviewCounts.map((item) => (
							<div className="mini-stat" key={item.label}>
								<strong>{item.count}</strong>
								<span className="muted">{item.label}</span>
							</div>
						))}
					</div>
				</section>
			</aside>

			<main className="content">
				{selectedPair == null ? (
					<section className="panel empty-panel">
						<strong>Select a file pair</strong>
						<p className="muted">Choose a pair from the left to inspect the lazy-loaded diff view and structured details.</p>
					</section>
				) : (
					<>
						<section className="panel sticky-toolbar">
							<div className="panel-body toolbar-grid">
								<div>
									<p className="eyebrow">Pair {selectedPair.index}</p>
									<h2 className="detail-title">{selectedPair.file1}</h2>
									<p className="muted">{selectedPair.requestRelativePath ?? selectedPair.file2}</p>
								</div>
								<div className="toolbar-cluster">
									<div className="toolbar-cluster">
										<button className="toolbar-button" disabled={selectedPairIndex <= 0} onClick={() => selectRelativePair(-1)} type="button">
											Previous pair
										</button>
										<button className="toolbar-button" disabled={selectedPairIndex >= sortedPairs.length - 1} onClick={() => selectRelativePair(1)} type="button">
											Next pair
										</button>
									</div>
									<div className="toolbar-cluster">
										<button
											className={`toolbar-button ${diffViewMode === 'split' ? 'active' : ''}`}
											onClick={() => setDiffViewMode('split')}
											type="button"
										>
											Split view
										</button>
										<button
											className={`toolbar-button ${diffViewMode === 'unified' ? 'active' : ''}`}
											onClick={() => setDiffViewMode('unified')}
											type="button"
										>
											Unified view
										</button>
									</div>
									<select
										className="inline-select"
										value={reviewCategories[selectedPair.pairId] ?? 'Unreviewed'}
										onChange={(event) => updateReviewCategory(selectedPair.pairId, event.target.value as ReviewCategory)}
									>
										{REVIEW_CATEGORIES.map((option) => (
											<option key={option}>{option}</option>
										))}
									</select>
								</div>
							</div>
						</section>

						<section className="panel">
							<div className="panel-body section-stack">
								<div className="detail-grid">
									<DetailCard label="Status" value={formatPairStatus(getPairStatus(selectedPair))} />
									<DetailCard label="Comparison kind" value={humanizeLabel(selectedPair.comparisonKind)} />
									<DetailCard label="Differences" value={selectedPair.differenceCount} />
									<DetailCard label="Chunk" value={selectedPair.detailChunkId} />
									{selectedPair.pairOutcome ? <DetailCard label="Request outcome" value={humanizeLabel(selectedPair.pairOutcome)} /> : null}
									{selectedPair.httpStatusA != null || selectedPair.httpStatusB != null ? (
										<DetailCard label="HTTP status" value={`A: ${selectedPair.httpStatusA ?? '—'} | B: ${selectedPair.httpStatusB ?? '—'}`} />
									) : null}
								</div>
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
								{selectedPair.patternKeys.length > 0 ? (
									<div className="chip-row">
										{selectedPair.patternKeys.map((patternKey) => (
											<button
												className={`chip chip-button ${patternFilter === patternKey ? 'active' : ''}`}
												key={patternKey}
												onClick={() => setPatternFilter((current) => (current === patternKey ? null : patternKey))}
												type="button"
											>
												{findPatternLabel(index.patterns, patternKey)}
											</button>
										))}
									</div>
								) : null}
								<div className="shortcut-row">
									<span className="shortcut-pill">j / k</span>
									<span className="muted">next or previous pair</span>
									<span className="shortcut-pill">n / p</span>
									<span className="muted">next or previous diff block</span>
									<span className="shortcut-pill">u</span>
									<span className="muted">toggle diff mode</span>
									<span className="shortcut-pill">/</span>
									<span className="muted">focus search</span>
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

						{selectedDetail != null && selectedDetail.diffDocument != null ? (
							<section className="panel">
								<div className="panel-header compact-header">
									<div>
										<h3 className="section-title">Diff viewer</h3>
										<p className="muted">
											{selectedDetail.diffDocument.format.toUpperCase()} • {selectedDetail.diffDocument.changedLineCount} changed lines • collapsed unchanged regions
										</p>
									</div>
									<div className="toolbar-cluster">
										<button
											className="toolbar-button"
											disabled={changedSections.length === 0 || activeDiffBlockIndex <= 0}
											onClick={() => setActiveDiffBlockIndex((current) => Math.max(current - 1, 0))}
											type="button"
										>
											Previous diff
										</button>
										<span className="state-pill">{changedSections.length === 0 ? '0/0' : `${activeDiffBlockIndex + 1}/${changedSections.length}`}</span>
										<button
											className="toolbar-button"
											disabled={changedSections.length === 0 || activeDiffBlockIndex >= changedSections.length - 1}
											onClick={() =>
												setActiveDiffBlockIndex((current) => Math.min(current + 1, Math.max(changedSections.length - 1, 0)))
											}
											type="button"
										>
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
										onToggleSection={(sectionId) =>
											setExpandedSections((current) => ({ ...current, [sectionId]: !current[sectionId] }))
										}
										onRegisterSection={(sectionId, element) => {
											diffSectionRefs.current[sectionId] = element;
										}}
									/>
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

						{selectedPair.categoryCounts.length > 0 ? (
							<section className="panel">
								<div className="panel-body">
									<h3 className="section-title">Difference categories</h3>
									<div className="chip-row top-gap">
										{selectedPair.categoryCounts.map((item) => (
											<span className="chip" key={item.label}>
												{humanizeLabel(item.label)}: {item.count}
											</span>
										))}
									</div>
								</div>
							</section>
						) : null}

						{selectedPair.rootObjectCounts.length > 0 ? (
							<section className="panel">
								<div className="panel-body">
									<h3 className="section-title">Root objects</h3>
									<div className="chip-row top-gap">
										{selectedPair.rootObjectCounts.map((item) => (
											<span className="chip" key={item.label}>
												{item.label}: {item.count}
											</span>
										))}
									</div>
								</div>
							</section>
						) : null}

						{selectedDetail != null && selectedDetail.differences.length > 0 ? (
							<section className="panel">
								<div className="panel-header compact-header">
									<div>
										<h3 className="section-title">Structured differences</h3>
										<p className="muted">Precomputed property-level changes for fast manual review.</p>
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
													<td>
														<pre className="value-block">{formatValue(difference.expected)}</pre>
													</td>
													<td>
														<pre className="value-block">{formatValue(difference.actual)}</pre>
													</td>
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
										<h3 className="section-title">Raw text differences</h3>
										<p className="muted">Fallback differences for response-body or text comparisons.</p>
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

						{selectedDetail != null &&
							!selectedDetail.hasError &&
							selectedDetail.differences.length === 0 &&
							selectedDetail.rawTextDifferences.length === 0 &&
							selectedDetail.diffDocument == null ? (
							<section className="panel empty-panel">
								<strong>No manual inspection needed</strong>
								<p className="muted">This pair is equal under the active comparison rules.</p>
							</section>
						) : null}
					</>
				)}
			</main>
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
						<div
							className={`diff-section ${section.kind} ${isActive ? 'active' : ''}`}
							key={section.id}
							ref={(element) => onRegisterSection(section.id, element)}
						>
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
			sections.push({
				id: `${currentKind}-${blockIndex}`,
				kind: currentKind,
				lines: currentLines,
			});
			blockIndex += 1;
			currentLines = [];
			currentKind = lineKind;
		}

		currentLines.push(line);
	}

	if (currentLines.length > 0) {
		sections.push({
			id: `${currentKind}-${blockIndex}`,
			kind: currentKind,
			lines: currentLines,
		});
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

function readCategories(storageKey: string): Record<string, ReviewCategory> {
	try {
		const raw = localStorage.getItem(storageKey);
		if (raw == null || raw.trim() === '') {
			return {};
		}

		return JSON.parse(raw) as Record<string, ReviewCategory>;
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

function findPatternLabel(patterns: PatternCluster[], patternKey: string): string {
	return patterns.find((pattern) => pattern.key === patternKey)?.label ?? patternKey;
}