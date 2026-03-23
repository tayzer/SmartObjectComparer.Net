import { useDeferredValue, useEffect, useMemo, useRef, useState } from 'react';
import type {
	DiffDocument,
	PairDetail,
	PairSummary,
	RawTextDifference,
	ReportBootstrap,
	ReportDetailChunk,
	ReportIndex,
	StructuredDifference,
} from './types';

type PairStatus = 'equal' | 'different' | 'error';
type SelectionKind = 'group' | 'field' | 'pattern';
type GroupSelectionId = 'structured' | 'change' | 'raw';

interface AppProps {
	bootstrap: ReportBootstrap | null;
	loadError: string | null;
}

interface PatternInfo {
	key: string;
	label: string;
	kind: string;
	description?: string | null;
}

interface NavigatorSelection {
	kind: SelectionKind;
	label: string;
	description?: string;
	groupId?: GroupSelectionId;
	path?: string;
	exact?: boolean;
	patternKey?: string;
	patternKind?: string;
}

interface NavigatorNode {
	key: string;
	label: string;
	description?: string;
	count: number;
	selection: NavigatorSelection;
	children: NavigatorNode[];
	depth: number;
	icon: 'group' | 'branch' | 'leaf' | 'pattern' | 'raw';
}

interface EvidenceMatch {
	key: string;
	kind: 'structured' | 'raw';
	label: string;
	description: string;
	structured?: StructuredDifference;
	raw?: RawTextDifference;
}

type ReviewStatus = 'unreviewed' | 'needs-review' | 'accepted-difference' | 'bug-identified';

interface ReviewState {
	status: ReviewStatus;
	updatedAt: string;
}

interface PatternCardItem {
	key: string;
	label: string;
	description?: string;
	count: number;
	selection: NavigatorSelection;
	groupLabel: string;
	tone: 'change' | 'raw';
}

type OutcomeFocusKind = 'all' | 'non-success' | 'comparison-error' | 'http-non-success' | 'status-mismatch' | 'equal-non-success' | 'pair-outcome';
type ContextTone = 'neutral' | 'warn' | 'danger' | 'ok';

interface OutcomeFocusOption {
	key: string;
	label: string;
	description: string;
	count: number;
	kind: OutcomeFocusKind;
	pairOutcome?: string;
	tone: ContextTone;
}

interface ContextChipItem {
	key: string;
	label: string;
	tone: ContextTone;
	title?: string;
}

interface PropertyPathDisplay {
	shortLabel: string;
	fullPath?: string;
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

	return <ReportWorkspace bootstrap={bootstrap} />;
}

function ReportWorkspace({ bootstrap }: { bootstrap: ReportBootstrap }) {
	const report = bootstrap.report;
	const [index, setIndex] = useState<ReportIndex | null>(bootstrap.index ?? null);
	const [indexLoading, setIndexLoading] = useState(bootstrap.index == null);
	const [indexError, setIndexError] = useState<string | null>(null);
	const [treeSearch, setTreeSearch] = useState('');
	const [pairSearch, setPairSearch] = useState('');
	const deferredTreeSearch = useDeferredValue(treeSearch);
	const deferredPairSearch = useDeferredValue(pairSearch);
	const [selectedNodeKey, setSelectedNodeKey] = useState<string | null>(null);
	const [selectedPatternKey, setSelectedPatternKey] = useState<string | null>(null);
	const [selectedOutcomeKey, setSelectedOutcomeKey] = useState<string>('outcome/all');
	const [selectedPairId, setSelectedPairId] = useState<string | null>(null);
	const [expandedTreeKeys, setExpandedTreeKeys] = useState<Record<string, boolean>>({});
	const [showFullDiff, setShowFullDiff] = useState(false);
	const [detailError, setDetailError] = useState<string | null>(null);
	const [loadingChunks, setLoadingChunks] = useState<Record<string, boolean>>({});
	const [reviewStates, setReviewStates] = useState<Record<string, ReviewState>>(() => loadReviewStates(report.reportId));
	const [chunkCache, setChunkCache] = useState<Record<string, ReportDetailChunk>>(() => {
		const inlineChunks = (bootstrap.detailChunks ?? []).map((chunk) => [chunk.chunkId, chunk] as const);
		return Object.fromEntries(inlineChunks) as Record<string, ReportDetailChunk>;
	});
	const chunkCacheRef = useRef(chunkCache);
	const inflightChunksRef = useRef<Record<string, Promise<void>>>({});

	useEffect(() => {
		chunkCacheRef.current = chunkCache;
	}, [chunkCache]);

	useEffect(() => {
		setReviewStates(loadReviewStates(report.reportId));
	}, [report.reportId]);

	useEffect(() => {
		persistReviewStates(report.reportId, reviewStates);
	}, [report.reportId, reviewStates]);

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

		const protocolError = getStaticSiteFileProtocolError(bootstrap.mode, bootstrap.indexPath);
		if (protocolError != null) {
			setIndexError(protocolError);
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

	const allPairs = index?.pairs ?? [];
	const patternInfoLookup = useMemo(() => buildPatternInfoLookup(index), [index]);
	const navigatorRoots = useMemo(() => buildNavigatorRoots(allPairs, patternInfoLookup), [allPairs, patternInfoLookup]);
	const outcomeFocusOptions = useMemo(() => buildOutcomeFocusOptions(allPairs), [allPairs]);
	const outcomeFocusLookup = useMemo(() => new Map(outcomeFocusOptions.map((option) => [option.key, option] as const)), [outcomeFocusOptions]);
	const structuredRoot = useMemo(() => navigatorRoots.find((node) => node.key === 'root/structured') ?? null, [navigatorRoots]);
	const fieldNavigatorRoots = structuredRoot?.children ?? [];
	const filteredFieldNavigatorRoots = useMemo(
		() => filterNavigatorRoots(fieldNavigatorRoots, deferredTreeSearch),
		[deferredTreeSearch, fieldNavigatorRoots],
	);
	const visibleFieldNavigatorRoots = deferredTreeSearch.trim().length > 0 ? filteredFieldNavigatorRoots : fieldNavigatorRoots;
	const patternCards = useMemo(() => buildPatternCards(navigatorRoots), [navigatorRoots]);
	const primaryOutcomeOptions = useMemo(
		() => outcomeFocusOptions.filter((option) => option.kind !== 'pair-outcome'),
		[outcomeFocusOptions],
	);
	const pairOutcomeOptions = useMemo(
		() => outcomeFocusOptions.filter((option) => option.kind === 'pair-outcome'),
		[outcomeFocusOptions],
	);
	const patternCardLookup = useMemo(() => new Map(patternCards.map((card) => [card.key, card] as const)), [patternCards]);
	const selectedFieldNode = useMemo(() => findSelectedNode(fieldNavigatorRoots, selectedNodeKey), [fieldNavigatorRoots, selectedNodeKey]);
	const visibleSelectedFieldNode = useMemo(
		() => findSelectedNode(visibleFieldNavigatorRoots, selectedNodeKey),
		[visibleFieldNavigatorRoots, selectedNodeKey],
	);
	const selectedOutcomeFocus = useMemo(
		() => outcomeFocusLookup.get(selectedOutcomeKey) ?? outcomeFocusLookup.get('outcome/all') ?? null,
		[outcomeFocusLookup, selectedOutcomeKey],
	);
	const outcomeFocusActive = selectedOutcomeFocus != null && selectedOutcomeFocus.kind !== 'all';
	const selectedPatternNode = useMemo(
		() => (selectedPatternKey == null ? null : patternCardLookup.get(selectedPatternKey) ?? null),
		[patternCardLookup, selectedPatternKey],
	);
	const activeSelectionNode = outcomeFocusActive ? null : selectedPatternNode ?? selectedFieldNode;
	const activeFieldSelection = activeSelectionNode?.selection.kind === 'field' ? activeSelectionNode.selection : null;
	const activeFieldDisplay = useMemo(
		() => (activeFieldSelection?.path != null ? getPropertyPathDisplay(activeFieldSelection.path, 'Structured validation') : null),
		[activeFieldSelection],
	);

	useEffect(() => {
		if (outcomeFocusLookup.has(selectedOutcomeKey)) {
			return;
		}

		setSelectedOutcomeKey('outcome/all');
	}, [outcomeFocusLookup, selectedOutcomeKey]);

	useEffect(() => {
		if (selectedPatternNode != null || visibleSelectedFieldNode != null) {
			return;
		}

		const defaultNode = pickDefaultNode(visibleFieldNavigatorRoots);
		if (defaultNode != null) {
			setSelectedNodeKey(defaultNode.key);
			setExpandedTreeKeys((current) => ({ ...current, ...expandPathKeys(defaultNode.key) }));
			return;
		}

		setSelectedNodeKey(null);
	}, [selectedPatternNode, visibleFieldNavigatorRoots, visibleSelectedFieldNode]);

	const filteredPairs = useMemo(() => {
		const search = deferredPairSearch.trim().toLowerCase();
		if (search.length === 0 && selectedPatternNode == null && !outcomeFocusActive && deferredTreeSearch.trim().length > 0 && visibleFieldNavigatorRoots.length === 0) {
			return [];
		}

		let base = allPairs;

		if (search.length > 0) {
			base = base.filter((pair) => pairMatchesGlobalPairSearch(pair, search));
		} else if (outcomeFocusActive) {
			base = allPairs;
		} else {
			if (activeSelectionNode != null) {
				base = base.filter((pair) => pairMatchesSelection(pair, activeSelectionNode.selection, patternInfoLookup));
			} else if (!outcomeFocusActive) {
				base = base.filter((pair) => getPairStatus(pair) !== 'equal');
			}
		}

		if (selectedOutcomeFocus != null && selectedOutcomeFocus.kind !== 'all') {
			base = base.filter((pair) => matchesOutcomeFocus(pair, selectedOutcomeFocus));
		}

		return base
			.sort((left, right) => comparePairs(left, right));
	}, [activeSelectionNode, allPairs, deferredPairSearch, deferredTreeSearch, outcomeFocusActive, patternInfoLookup, selectedOutcomeFocus, selectedPatternNode, visibleFieldNavigatorRoots.length]);

	useEffect(() => {
		if (filteredPairs.length === 0) {
			setSelectedPairId(null);
			return;
		}

		if (selectedPairId == null || !filteredPairs.some((pair) => pair.pairId === selectedPairId)) {
			setSelectedPairId(filteredPairs[0].pairId);
		}
	}, [filteredPairs, selectedPairId]);

	const selectedPair = useMemo(
		() => (selectedPairId == null ? null : filteredPairs.find((pair) => pair.pairId === selectedPairId) ?? null),
		[filteredPairs, selectedPairId],
	);

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
				const protocolError = getStaticSiteFileProtocolError(bootstrap.mode, pair.detailChunkPath);
				if (protocolError != null) {
					throw new Error(protocolError);
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
		setShowFullDiff(false);
	}, [selectedOutcomeKey, selectedPairId, selectedNodeKey, selectedPatternKey]);

	const selectedDetail = useMemo(() => {
		if (selectedPair == null) {
			return null;
		}

		const chunk = chunkCache[selectedPair.detailChunkId];
		return chunk?.pairs.find((pair) => pair.pairId === selectedPair.pairId) ?? null;
	}, [chunkCache, selectedPair]);

	const matchedEvidence = useMemo(
		() => buildMatchedEvidence(selectedPair, selectedDetail, activeSelectionNode?.selection ?? null),
		[activeSelectionNode, selectedDetail, selectedPair],
	);

	if (indexLoading && index == null) {
		return (
			<div className="content empty-panel">
				<strong>Preparing the structured validation view</strong>
				<p className="muted">The report index is loading so the tree and pair list can be built.</p>
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

	const hasFieldFilterNoResults = deferredTreeSearch.trim().length > 0 && visibleFieldNavigatorRoots.length === 0;
	const hasGlobalPairSearch = deferredPairSearch.trim().length > 0;
	const selectedChunkLoading = selectedPair == null ? false : loadingChunks[selectedPair.detailChunkId] === true;
	const reviewSummary = buildReviewSummary(allPairs, reviewStates);
	const selectedReviewStatus = selectedPair == null ? 'unreviewed' : getReviewStatus(reviewStates, selectedPair.pairId);
	const displayMatchedEvidence = matchedEvidence.length > 0;
	const selectedPairContextItems = selectedPair == null ? [] : getPairResponseContextItems(selectedPair);

	function updatePairReviewStatus(pairId: string, status: Exclude<ReviewStatus, 'unreviewed'>) {
		setReviewStates((current) => {
			const existing = getReviewStatus(current, pairId);
			if (existing === status) {
				const next = { ...current };
				delete next[pairId];
				return next;
			}

			return {
				...current,
				[pairId]: {
					status,
					updatedAt: new Date().toISOString(),
				},
			};
		});
	}

	return (
		<div className="parity-shell">
			<header className="panel panel-hero parity-header">
				<div>
					<p className="eyebrow">Comparison report</p>
					<h1 className="title">Review structured differences first</h1>
					<p className="subtitle">
						{formatCommandLabel(report.command)} report • {index.totalPairs} pairs • Generated {formatDateTime(report.generatedAt)}
					</p>
				</div>
				<div className="parity-header-metrics">
					<MetricCard label="Different" value={report.summary.differentCount} tone="warn" />
					<MetricCard label="Equal" value={report.summary.equalCount} tone="ok" />
					<MetricCard label="Errors" value={report.summary.errorCount} tone="danger" />
					<MetricCard label="Structured fields" value={index.topFields.length} tone="neutral" />
				</div>
			</header>

			<section className="panel parity-row-panel">
				<div className="panel-body parity-row-body">
					<div className="focus-control-grid">
						<section className="focus-section focus-section-primary">
							<div className="focus-section-header">
								<h3 className="section-title">Field path navigator</h3>
							</div>
							<div className="focus-section-content focus-section-content-navigator">
								<div className="navigator-summary-row">
									<input
										className="search-input parity-row-search"
										placeholder="Filter fields and properties"
										value={treeSearch}
										onChange={(event) => setTreeSearch(event.target.value)}
									/>
									<span className="state-pill">{countLeafNodes(fieldNavigatorRoots)} field/property leaves</span>
									<div className="toolbar-cluster">
										<button className="toolbar-button" onClick={() => setExpandedTreeKeys(expandAllNodes(visibleFieldNavigatorRoots))} type="button">
											Expand all
										</button>
										<button className="toolbar-button" onClick={() => setExpandedTreeKeys(collapseAllNodes(visibleFieldNavigatorRoots))} type="button">
											Collapse all
										</button>
									</div>
								</div>
								<div className="navigator-tree parity-row-scroll navigator-tree-fill">
									{visibleFieldNavigatorRoots.length === 0 ? (
										<div className="empty-state compact-empty-state">
											<strong>No field or property nodes match the current filter</strong>
											<p className="muted">Clear the field filter to restore the full navigator.</p>
										</div>
									) : (
										visibleFieldNavigatorRoots.map((node) => (
											<NavigatorTreeNode
												key={node.key}
												node={node}
												expandedKeys={expandedTreeKeys}
												onToggle={(key) => setExpandedTreeKeys((current) => ({ ...current, [key]: !current[key] }))}
												onSelect={(key) => {
													setSelectedOutcomeKey('outcome/all');
													setSelectedPatternKey(null);
													setSelectedNodeKey(key);
													setExpandedTreeKeys((current) => ({ ...current, ...expandPathKeys(key) }));
												}}
												selectedKey={selectedPatternKey == null && !outcomeFocusActive ? selectedNodeKey : null}
											/>
										))
									)}
								</div>
							</div>
						</section>

						<div className="focus-section-stack">
							<section className="focus-section focus-section-outcomes">
								<div className="focus-section-header">
									<h3 className="section-title">Response outcomes</h3>
									{selectedOutcomeFocus != null && selectedOutcomeFocus.kind !== 'all' ? (
										<button className="toolbar-button" onClick={() => setSelectedOutcomeKey('outcome/all')} type="button">
											Clear outcome focus
										</button>
									) : null}
								</div>
								<div className="focus-section-content focus-section-content-outcomes">
									<div className="outcome-focus-grid outcome-focus-grid-fill">
										{primaryOutcomeOptions.map((option) => {
											const active = selectedOutcomeKey === option.key;
											const disabled = option.kind !== 'all' && option.count === 0;
											return (
												<button
													className={`outcome-focus-card ${active ? 'active' : ''}`}
													disabled={disabled}
													key={option.key}
													onClick={() => {
														setSelectedPatternKey(null);
														setSelectedOutcomeKey(option.key);
													}}
													type="button"
												>
													<div className="outcome-focus-card-header">
														<span className={`badge context-chip tone-${option.tone}`}>{option.label}</span>
														<strong>{option.count}</strong>
													</div>
													<p className="muted">{option.description}</p>
												</button>
											);
										})}
									</div>
									{pairOutcomeOptions.length > 0 ? (
										<div className="outcome-chip-block">
											<div className="focus-section-subheader">
												<strong>Pair outcome labels</strong>
												<p className="muted">Exact labels already emitted into the report data.</p>
											</div>
											<div className="chip-row outcome-chip-row">
												{pairOutcomeOptions.map((option) => (
													<button
														className={`chip-button outcome-chip-button ${selectedOutcomeKey === option.key ? 'active' : ''}`}
														key={option.key}
														onClick={() => {
															setSelectedPatternKey(null);
															setSelectedOutcomeKey(option.key);
														}}
														title={option.description}
														type="button"
													>
														<span>{option.label}</span>
														<span className="badge">{option.count}</span>
													</button>
												))}
											</div>
										</div>
									) : null}
								</div>
							</section>

							{patternCards.length > 0 ? (
								<section className="focus-section focus-section-patterns">
									<div className="focus-section-header">
										<h3 className="section-title">Recurring difference patterns</h3>
										{selectedPatternNode != null ? (
											<button className="toolbar-button" onClick={() => setSelectedPatternKey(null)} type="button">
												Clear pattern focus
											</button>
										) : null}
									</div>
									<div className="focus-section-content focus-section-content-patterns">
										<div className="branch-list common-pattern-list pattern-branch-list fill-scroll-region">
											{patternCards.map((card) => (
												<button
													className={`branch-row ${selectedPatternKey === card.key ? 'active' : ''}`}
													key={card.key}
													onClick={() => {
														setSelectedOutcomeKey('outcome/all');
														setSelectedPatternKey(card.key);
													}}
													type="button"
												>
													<div className="branch-row-header">
														<div>
															<div className="branch-row-title">{card.label}</div>
															<div className="branch-row-path">{card.groupLabel}</div>
														</div>
														<div className="branch-row-statuses">
															<span className="badge">{card.count} pairs</span>
															<span className="state-pill">Focus results</span>
														</div>
													</div>
													<div className="branch-row-preview">
														<div className="branch-row-preview-change">{card.description ?? 'Jump directly to the matching file pairs.'}</div>
													</div>
												</button>
											))}
										</div>
									</div>
								</section>
							) : null}
						</div>
					</div>
				</div>
			</section>

			<div className="parity-main-grid">
				<section className="panel parity-branch-panel">
						<div className="panel-header compact-header">
							<div>
								<p className="eyebrow">Matching pairs</p>
								<h2 className="section-title">Results</h2>
								<p className="muted">Use the focus controls above to narrow the list, then select a pair to inspect its detail on the right.</p>
							</div>
							<div className="toolbar-cluster">
								<span className="state-pill">{filteredPairs.length} matching pairs</span>
								{selectedPatternNode != null ? (
									<button className="toolbar-button" onClick={() => setSelectedPatternKey(null)} type="button">
										Clear pattern focus
									</button>
								) : null}
							</div>
						</div>
						<div className="panel-body parity-branch-body">
							<div className="badge-row focus-summary-row">
								{hasGlobalPairSearch ? <span className="state-pill">Search: {deferredPairSearch.trim()}</span> : null}
								{!hasGlobalPairSearch && activeFieldDisplay != null ? (
									<span className="chip" title={activeFieldDisplay.fullPath ?? undefined}>Field: {activeFieldDisplay.shortLabel}</span>
								) : null}
								{!hasGlobalPairSearch && selectedPatternNode != null ? (
									<span className="chip" title={selectedPatternNode.description ?? undefined}>Pattern: {selectedPatternNode.label}</span>
								) : null}
								{selectedOutcomeFocus != null && selectedOutcomeFocus.kind !== 'all' ? (
									<span className={`chip context-chip tone-${selectedOutcomeFocus.tone}`} title={selectedOutcomeFocus.description}>Outcome: {selectedOutcomeFocus.label}</span>
								) : null}
								{!hasGlobalPairSearch && activeFieldDisplay == null && selectedPatternNode == null && (selectedOutcomeFocus == null || selectedOutcomeFocus.kind === 'all') ? (
									<span className="muted">Showing pairs that currently need attention.</span>
								) : null}
								{hasFieldFilterNoResults && !hasGlobalPairSearch && selectedPatternNode == null ? (
									<span className="chip">Field filter returned no matches</span>
								) : null}
							</div>
							<div className="parity-branch-toolbar">
								<input
									className="search-input"
									placeholder="Search matching pairs by file, request path, or preview text"
									value={pairSearch}
									onChange={(event) => setPairSearch(event.target.value)}
								/>
								<p className="muted pair-search-note">Name search spans the entire report and overrides field/pattern focus so you can jump straight to a known pair. Response outcome focus still applies.</p>
							</div>
							<div className="review-summary-row">
								<ReviewBadge status="unreviewed" count={reviewSummary.unreviewed} />
								<ReviewBadge status="needs-review" count={reviewSummary['needs-review']} />
								<ReviewBadge status="accepted-difference" count={reviewSummary['accepted-difference']} />
								<ReviewBadge status="bug-identified" count={reviewSummary['bug-identified']} />
							</div>
							<div className="branch-list results-branch-list">
								{filteredPairs.length === 0 ? (
									<div className="empty-state compact-empty-state">
										<strong>No file pairs match the current focus</strong>
										<p className="muted">Adjust the field path, recurring pattern, response outcome, or file-pair search to broaden the result set.</p>
									</div>
								) : (
									filteredPairs.map((pair) => {
										const active = pair.pairId === selectedPairId;
										const reviewStatus = getReviewStatus(reviewStates, pair.pairId);
										const responseContextItems = getPairResponseContextItems(pair);
										return (
											<button
												className={`branch-row ${active ? 'active' : ''}`}
												key={pair.pairId}
												onClick={() => setSelectedPairId(pair.pairId)}
												type="button"
											>
												<div className="branch-row-header">
													<div>
														<div className="branch-row-title">#{pair.index} {pair.file1}</div>
														<div className="branch-row-path">{pair.requestRelativePath ?? pair.file2}</div>
													</div>
													<div className="branch-row-statuses">
														<PairStatusBadge pair={pair} />
														<ReviewBadge status={reviewStatus} />
													</div>
												</div>
												<div className="branch-row-preview">
													<div className="branch-row-preview-label">{getPairPreviewLabel(pair)}</div>
													<div className="branch-row-preview-change">{getPairPreviewChange(pair)}</div>
												</div>
												<div className="badge-row top-gap-tight branch-row-context">
													<span className="badge">{pair.differenceCount} issues</span>
													{responseContextItems.map((item) => (
														<span className={`chip compact-chip context-chip tone-${item.tone}`} key={item.key} title={item.title}>
															{item.label}
														</span>
													))}
													{pair.affectedFields.slice(0, 2).map((field) => (
														<PathChip key={field} path={field} />
													))}
												</div>
											</button>
										);
									})
								)}
							</div>
						</div>
					</section>

					<section className="panel parity-leaf-panel">
						<div className="panel-header compact-header">
							<div>
								<p className="eyebrow">Selected pair detail</p>
								<h2 className="section-title">{selectedPair != null ? `#${selectedPair.index} ${selectedPair.file1}` : 'Select a pair'}</h2>
								<p className="muted">{selectedPair != null ? selectedPair.requestRelativePath ?? selectedPair.file2 : 'Select a pair from the results list to inspect its detail.'}</p>
								{selectedPair != null && selectedPairContextItems.length > 0 ? (
									<div className="badge-row top-gap-tight">
										{selectedPairContextItems.map((item) => (
											<span className={`chip compact-chip context-chip tone-${item.tone}`} key={item.key} title={item.title}>
												{item.label}
											</span>
										))}
									</div>
								) : null}
							</div>
							{selectedPair != null ? (
								<div className="toolbar-cluster">
									<ReviewActionBar selectedStatus={selectedReviewStatus} onSelect={(status) => updatePairReviewStatus(selectedPair.pairId, status)} />
									<button className="toolbar-button" onClick={() => setShowFullDiff((current) => !current)} type="button">
										{showFullDiff ? 'Hide Full XML' : 'Expand/Compare Full XML'}
									</button>
								</div>
							) : null}
						</div>
						<div className="panel-body parity-leaf-body">
							{selectedPair == null ? (
								<div className="empty-state compact-empty-state">
									<strong>Select a file pair</strong>
									<p className="muted">Choose a branch item to open the structured validation detail.</p>
								</div>
							) : selectedChunkLoading && selectedDetail == null ? (
								<div className="empty-state compact-empty-state">
									<strong>Loading pair detail</strong>
									<p className="muted">Fetching the selected file pair for deep dive analysis.</p>
								</div>
							) : (
								<>
									<div className="detail-topline">
										<PairStatusBadge pair={selectedPair} />
										<ReviewBadge status={selectedReviewStatus} />
										<span className="badge">{selectedPair.differenceCount} detected differences</span>
										{selectedPairContextItems.map((item) => (
											<span className={`chip context-chip tone-${item.tone}`} key={item.key} title={item.title}>
												{item.label}
											</span>
										))}
										{selectedPair.requestRelativePath ? <span className="chip">{selectedPair.requestRelativePath}</span> : null}
									</div>

									{!showFullDiff ? (
										<section className="structured-panel">
											<div className="structured-panel-header">
												<div>
													<h3 className="section-title">Structured validation</h3>
													<p className="muted">This stays open by default so testers start with the failed data points, not the raw XML.</p>
												</div>
												{matchedEvidence.length > 0 ? <span className="state-pill">Auto-focused {matchedEvidence.length} matching item(s)</span> : null}
											</div>

											{detailError != null ? (
												<div className="error-callout">
													<strong>Detail load error</strong>
													<p className="value-block">{detailError}</p>
												</div>
											) : null}

											{selectedDetail?.hasError ? (
												<div className="error-callout">
													<strong>Comparison error</strong>
													<p className="value-block">{selectedDetail.errorMessage ?? 'Unknown comparison error.'}</p>
												</div>
											) : null}

											{selectedDetail != null && displayMatchedEvidence ? (
												<div className="structured-list">
													{matchedEvidence.map((match) => {
														if (match.kind === 'structured' && match.structured) {
															const difference = match.structured;
															return (
																<div className="structured-item active" key={match.key}>
																	<div className="structured-item-header">
																		<PropertyPathHeading path={difference.propertyName} />
																		<span className="state-pill active">Focused from tree</span>
																	</div>
																	<div className="structured-diff-grid">
																		<div className="structured-value expected">
																			<p className="eyebrow">Expected</p>
																			<pre className="value-block">{formatValue(difference.expected)}</pre>
																		</div>
																		<div className="structured-value actual">
																			<p className="eyebrow">Actual</p>
																			<pre className="value-block">{formatValue(difference.actual)}</pre>
																		</div>
																	</div>
																</div>
															);
														}

														if (match.kind === 'raw' && match.raw) {
															const difference = match.raw;
															return (
																<div className="structured-item active" key={match.key}>
																	<div className="structured-item-header">
																		<strong>{humanizeLabel(difference.type)}</strong>
																		<span className="state-pill active">Focused from tree</span>
																	</div>
																	<p className="value-block">{difference.description}</p>
																	<div className="structured-diff-grid">
																		<div className="structured-value expected">
																			<p className="eyebrow">Text A</p>
																			<pre className="value-block">{difference.textA ?? '—'}</pre>
																		</div>
																		<div className="structured-value actual">
																			<p className="eyebrow">Text B</p>
																			<pre className="value-block">{difference.textB ?? '—'}</pre>
																		</div>
																	</div>
																</div>
															);
														}
														return null;
													})}
													{selectedDetail.differences.length + selectedDetail.rawTextDifferences.length > matchedEvidence.length && (
														<div className="empty-state compact-empty-state">
															<strong>Showing filtered differences</strong>
															<p className="muted">Clear the field/pattern filter to see all differences for this file pair.</p>
														</div>
													)}
												</div>
											) : selectedDetail != null && selectedDetail.differences.length > 0 ? (
												<div className="structured-list">
													{selectedDetail.differences.map((difference, indexValue) => {
														const key = buildStructuredEvidenceKey(difference, indexValue);
														return (
															<div
																className="structured-item"
																key={key}
															>
																<div className="structured-item-header">
																	<PropertyPathHeading path={difference.propertyName} />
																</div>
																<div className="structured-diff-grid">
																	<div className="structured-value expected">
																		<p className="eyebrow">Expected</p>
																		<pre className="value-block">{formatValue(difference.expected)}</pre>
																	</div>
																	<div className="structured-value actual">
																		<p className="eyebrow">Actual</p>
																		<pre className="value-block">{formatValue(difference.actual)}</pre>
																	</div>
																</div>
															</div>
														);
													})}
												</div>
											) : selectedDetail != null && selectedDetail.rawTextDifferences.length > 0 ? (
												<div className="structured-list">
													{selectedDetail.rawTextDifferences.map((difference, indexValue) => {
														const key = buildRawEvidenceKey(difference, indexValue);
														return (
															<div
																className="structured-item"
																key={key}
															>
																<div className="structured-item-header">
																	<strong>{humanizeLabel(difference.type)}</strong>
																</div>
																<p className="value-block">{difference.description}</p>
																<div className="structured-diff-grid">
																	<div className="structured-value expected">
																		<p className="eyebrow">Text A</p>
																		<pre className="value-block">{difference.textA ?? '—'}</pre>
																	</div>
																	<div className="structured-value actual">
																		<p className="eyebrow">Text B</p>
																		<pre className="value-block">{difference.textB ?? '—'}</pre>
																	</div>
																</div>
															</div>
														);
													})}
												</div>
											) : (
												<div className="empty-state compact-empty-state">
													<strong>No structured differences were emitted for this pair</strong>
													<p className="muted">Use the full XML toggle if you need the raw side-by-side document.</p>
												</div>
											)}
										</section>
									) : (
										<section className="full-diff-panel">
											<div className="structured-panel-header">
												<div>
													<h3 className="section-title">Full XML comparison</h3>
													<p className="muted">Collapsed by default to keep the structured summary primary.</p>
												</div>
											</div>
											{selectedDetail?.diffDocument != null ? (
												<FullXmlDiffViewer document={selectedDetail.diffDocument} />
											) : (
												<div className="empty-state compact-empty-state">
													<strong>No XML diff was available for this pair</strong>
													<p className="muted">The structured validation remains the authoritative view.</p>
												</div>
											)}
										</section>
									)}
								</>
							)}
						</div>
					</section>
				</div>
		</div>
	);
}

function ReviewActionBar({
	selectedStatus,
	onSelect,
}: {
	selectedStatus: ReviewStatus;
	onSelect: (status: Exclude<ReviewStatus, 'unreviewed'>) => void;
}) {
	return (
		<div className="triage-button-row compact">
			<button className={`triage-button needs-review ${selectedStatus === 'needs-review' ? 'active' : ''}`} onClick={() => onSelect('needs-review')} type="button">
				Needs Review
			</button>
			<button className={`triage-button accepted-difference ${selectedStatus === 'accepted-difference' ? 'active' : ''}`} onClick={() => onSelect('accepted-difference')} type="button">
				Accepted
			</button>
			<button className={`triage-button bug-identified ${selectedStatus === 'bug-identified' ? 'active' : ''}`} onClick={() => onSelect('bug-identified')} type="button">
				Bug Identified
			</button>
		</div>
	);
}

function ReviewBadge({ status, count }: { status: ReviewStatus; count?: number }) {
	const label = formatReviewStatus(status);
	const className = status === 'unreviewed' ? 'badge review-unreviewed' : `badge review-${status}`;
	return <span className={className}>{count == null ? label : `${label} ${count}`}</span>;
}

function PathChip({ path }: { path: string }) {
	const display = getPropertyPathDisplay(path, path);
	return (
		<span className="chip compact-chip property-chip" title={display.fullPath ?? undefined}>
			{display.shortLabel}
		</span>
	);
}

function PropertyPathHeading({ path }: { path?: string | null }) {
	const display = getPropertyPathDisplay(path, 'Changed value');
	return (
		<span className="property-path-heading" title={display.fullPath ?? undefined}>
			<strong>{display.shortLabel}</strong>
			{display.fullPath != null && display.fullPath !== display.shortLabel ? (
				<span className="muted property-path-secondary">{display.fullPath}</span>
			) : null}
		</span>
	);
}

function NavigatorTreeNode({
	node,
	expandedKeys,
	onToggle,
	onSelect,
	selectedKey,
}: {
	node: NavigatorNode;
	expandedKeys: Record<string, boolean>;
	onToggle: (key: string) => void;
	onSelect: (key: string) => void;
	selectedKey: string | null;
}) {
	const isExpanded = expandedKeys[node.key] ?? node.depth < 1;
	const isSelected = selectedKey === node.key;
	const hasChildren = node.children.length > 0;

	return (
		<div className="navigator-node-wrap">
			<div className={`navigator-node ${isSelected ? 'selected' : ''}`} style={{ paddingLeft: `${12 + node.depth * 18}px` }}>
				{hasChildren ? (
					<button
						aria-label={`${isExpanded ? 'Collapse' : 'Expand'} ${node.label}`}
						className="navigator-expander"
						onClick={() => onToggle(node.key)}
						type="button"
					>
						<span className="navigator-expander-icon">{isExpanded ? '▾' : '▸'}</span>
					</button>
				) : (
					<span aria-hidden="true" className="navigator-expander-spacer" />
				)}
				<button className="navigator-node-main" onClick={() => onSelect(node.key)} type="button">
					<span className={`navigator-icon ${node.icon}`}>{renderNodeIcon(node.icon, hasChildren)}</span>
					<span className="navigator-node-copy">
						<strong>{node.label}</strong>
						{node.description ? <span className="muted navigator-node-description">{node.description}</span> : null}
					</span>
				</button>
				<span className="badge navigator-count">{node.count}</span>
			</div>
			{hasChildren && isExpanded ? node.children.map((child) => (
				<NavigatorTreeNode
					key={child.key}
					node={child}
					expandedKeys={expandedKeys}
					onToggle={onToggle}
					onSelect={onSelect}
					selectedKey={selectedKey}
				/>
			)) : null}
		</div>
	);
}

function FullXmlDiffViewer({ document }: { document: DiffDocument }) {
	return (
		<div className="xml-diff-viewer">
			<div className="xml-diff-header">
				<div>{document.leftLabel}</div>
				<div>{document.rightLabel}</div>
			</div>
			<div className="xml-diff-body">
				{document.lines.map((line) => (
					<div className={`xml-diff-row ${line.changeType}`} key={`diff-${line.rowNumber}-${line.leftLineNumber ?? 'x'}-${line.rightLineNumber ?? 'y'}`}>
						<div className="xml-diff-cell line-no">{line.leftLineNumber ?? ''}</div>
						<pre className="xml-diff-cell xml-text">{line.leftText ?? ''}</pre>
						<div className="xml-diff-cell line-no">{line.rightLineNumber ?? ''}</div>
						<pre className="xml-diff-cell xml-text">{line.rightText ?? ''}</pre>
					</div>
				))}
			</div>
		</div>
	);
}

function MetricCard({ label, value, tone }: { label: string; value: number | string; tone: 'ok' | 'warn' | 'danger' | 'neutral' }) {
	return (
		<div className={`metric-card ${tone}`}>
			<span className="metric-card-label">{label}</span>
			<strong>{value}</strong>
		</div>
	);
}

function PairStatusBadge({ pair }: { pair: PairSummary }) {
	const status = getPairStatus(pair);
	return <span className={`badge status-${status === 'different' ? pair.comparisonKind : status}`}>{formatPairStatus(status)}</span>;
}

function buildPatternInfoLookup(index: ReportIndex | null): Map<string, PatternInfo> {
	const map = new Map<string, PatternInfo>();
	for (const pattern of index?.patterns ?? []) {
		map.set(pattern.key, { key: pattern.key, label: pattern.label, kind: pattern.kind, description: pattern.description });
	}

	for (const [key, metadata] of Object.entries(index?.patternMetadata ?? {})) {
		if (!map.has(key)) {
			map.set(key, { key, label: metadata.label, kind: metadata.kind, description: metadata.description });
		}
	}

	return map;
}

function buildNavigatorRoots(pairs: PairSummary[], patternInfoLookup: Map<string, PatternInfo>): NavigatorNode[] {
	const structuredPairs = pairs.filter((pair) => pair.comparisonKind === 'structured' && pair.differenceCount > 0 && !pair.hasError);
	const changePatterns = buildPatternNodes(pairs, patternInfoLookup, 'change', 0);
	const rawPatterns = buildPatternNodes(pairs, patternInfoLookup, 'raw', 0);
	const fieldTree = buildFieldTree(structuredPairs);

	const roots: NavigatorNode[] = [];
	if (fieldTree.length > 0) {
		roots.push({
			key: 'root/structured',
			label: 'Structured validation',
			description: 'Hierarchy of XML tags and attributes with value-level differences.',
			count: structuredPairs.length,
			selection: { kind: 'group', groupId: 'structured', label: 'Structured validation', description: 'Pairs with structured value mismatches.' },
			children: fieldTree,
			depth: 0,
			icon: 'group',
		});
	}

	if (changePatterns.length > 0) {
		roots.push({
			key: 'root/change',
			label: 'Common exact changes',
			description: 'Recurring exact value changes grouped for high-volume triage.',
			count: changePatterns.reduce((sum, node) => sum + node.count, 0),
			selection: { kind: 'group', groupId: 'change', label: 'Common exact changes', description: 'Pairs grouped by recurring exact structured changes.' },
			children: changePatterns,
			depth: 0,
			icon: 'pattern',
		});
	}

	if (rawPatterns.length > 0) {
		roots.push({
			key: 'root/raw',
			label: 'Raw diff clusters',
			description: 'Recurring text-level differences when structured comparison is unavailable.',
			count: rawPatterns.reduce((sum, node) => sum + node.count, 0),
			selection: { kind: 'group', groupId: 'raw', label: 'Raw diff clusters', description: 'Pairs grouped by recurring raw-text differences.' },
			children: rawPatterns,
			depth: 0,
			icon: 'raw',
		});
	}

	return roots;
}

function buildPatternCards(navigatorRoots: NavigatorNode[]): PatternCardItem[] {
	return navigatorRoots
		.filter((root) => root.selection.groupId === 'change' || root.selection.groupId === 'raw')
		.flatMap((root) => {
			const tone = root.selection.groupId === 'raw' ? 'raw' : 'change';
			return root.children.map((node) => ({
				key: node.key,
				label: node.label,
				description: node.description,
				count: node.count,
				selection: node.selection,
				groupLabel: root.label,
				tone,
			}));
		});
}

function buildPatternNodes(pairs: PairSummary[], patternInfoLookup: Map<string, PatternInfo>, targetKind: 'change' | 'raw', depth: number): NavigatorNode[] {
	const counts = new Map<string, number>();
	for (const pair of pairs) {
		for (const key of pair.patternKeys) {
			const info = patternInfoLookup.get(key);
			if ((info?.kind ?? getPatternKindFromKey(key)) !== targetKind) {
				continue;
			}

			counts.set(key, (counts.get(key) ?? 0) + 1);
		}
	}

	return [...counts.entries()]
		.sort((left, right) => right[1] - left[1] || left[0].localeCompare(right[0]))
		.map(([key, count]) => {
			const info = patternInfoLookup.get(key);
			return {
				key,
				label: info?.label ?? key,
				description: info?.description ?? undefined,
				count,
				selection: {
					kind: 'pattern',
					label: info?.label ?? key,
					description: info?.description ?? undefined,
					patternKey: key,
					patternKind: info?.kind ?? targetKind,
				},
				children: [],
				depth: depth + 1,
				icon: targetKind === 'raw' ? 'raw' : 'pattern',
			} satisfies NavigatorNode;
		});
}

function buildFieldTree(pairs: PairSummary[]): NavigatorNode[] {
	interface MutableNode {
		key: string;
		label: string;
		count: number;
		children: Map<string, MutableNode>;
		depth: number;
		path: string;
	}

	const rootMap = new Map<string, MutableNode>();

	for (const pair of pairs) {
		const uniqueFields = [...new Set(pair.affectedFields.filter((field) => field.trim() !== ''))];
		for (const field of uniqueFields) {
			const segments = splitPropertyPath(field);
			let currentMap = rootMap;
			let currentPath = '';
			let currentKey = 'field';
			for (let index = 0; index < segments.length; index++) {
				const segment = segments[index];
				currentPath = currentPath === '' ? segment : `${currentPath}.${segment}`;
				currentKey = `${currentKey}/${segment}`;
				let node = currentMap.get(segment);
				if (node == null) {
					node = {
						key: currentKey,
						label: segment,
						count: 0,
						children: new Map<string, MutableNode>(),
						depth: index + 1,
						path: currentPath,
					};
					currentMap.set(segment, node);
				}
				node.count += 1;
				currentMap = node.children;
			}
		}
	}

	return [...rootMap.values()]
		.sort((left, right) => right.count - left.count || left.label.localeCompare(right.label))
		.map((node) => convertFieldNode(node));
}

function convertFieldNode(node: { key: string; label: string; count: number; children: Map<string, any>; depth: number; path: string }): NavigatorNode {
	const children = [...node.children.values()]
		.sort((left, right) => right.count - left.count || left.label.localeCompare(right.label))
		.map((child) => convertFieldNode(child));

	return {
		key: node.key,
		label: node.label,
		description: node.path,
		count: node.count,
		selection: {
			kind: 'field',
			label: node.path,
			description: node.path,
			path: node.path,
			exact: children.length === 0,
		},
		children,
		depth: node.depth,
		icon: children.length === 0 ? 'leaf' : 'branch',
	};
}

function filterNavigatorRoots(nodes: NavigatorNode[], searchText: string): NavigatorNode[] {
	const search = searchText.trim().toLowerCase();
	if (search.length === 0) {
		return nodes;
	}

	const filterNode = (node: NavigatorNode): NavigatorNode | null => {
		const filteredChildren = node.children
			.map((child) => filterNode(child))
			.filter((child): child is NavigatorNode => child != null);
		const matches = `${node.label} ${node.description ?? ''}`.toLowerCase().includes(search);
		if (!matches && filteredChildren.length === 0) {
			return null;
		}

		return { ...node, children: filteredChildren };
	};

	return nodes.map((node) => filterNode(node)).filter((node): node is NavigatorNode => node != null);
}

function findSelectedNode(nodes: NavigatorNode[], key: string | null): NavigatorNode | null {
	if (key == null) {
		return null;
	}

	for (const node of nodes) {
		if (node.key === key) {
			return node;
		}

		const child = findSelectedNode(node.children, key);
		if (child != null) {
			return child;
		}
	}

	return null;
}

function pickDefaultNode(nodes: NavigatorNode[]): NavigatorNode | null {
	for (const node of nodes) {
		if (node.children.length > 0) {
			return pickDefaultNode(node.children) ?? node;
		}
		return node;
	}

	return null;
}

function pairMatchesSelection(pair: PairSummary, selection: NavigatorSelection | null, patternInfoLookup: Map<string, PatternInfo>): boolean {
	if (selection == null) {
		return getPairStatus(pair) !== 'equal';
	}

	if (selection.kind === 'group') {
		switch (selection.groupId) {
			case 'structured':
				return pair.comparisonKind === 'structured' && pair.differenceCount > 0 && !pair.hasError;
			case 'change':
				return pair.patternKeys.some((key) => (patternInfoLookup.get(key)?.kind ?? getPatternKindFromKey(key)) === 'change');
			case 'raw':
				return pair.patternKeys.some((key) => (patternInfoLookup.get(key)?.kind ?? getPatternKindFromKey(key)) === 'raw');
			default:
				return getPairStatus(pair) !== 'equal';
		}
	}

	if (selection.kind === 'pattern') {
		return selection.patternKey != null && pair.patternKeys.includes(selection.patternKey);
	}

	if (selection.kind === 'field') {
		if (selection.path == null) {
			return false;
		}

		return pair.affectedFields.some((field) => matchesPropertyPath(field, selection.path as string, selection.exact === true));
	}

	return true;
}

function pairMatchesGlobalPairSearch(pair: PairSummary, search: string): boolean {
	const preview = `${getPairPreviewLabel(pair)} ${getPairPreviewChange(pair)}`.toLowerCase();
	return [pair.file1, pair.file2, pair.requestRelativePath, pair.searchText, preview]
		.filter((value): value is string => value != null)
		.some((value) => value.toLowerCase().includes(search));
}

function buildMatchedEvidence(
	selectedPair: PairSummary | null,
	detail: PairDetail | null,
	selection: NavigatorSelection | null,
): EvidenceMatch[] {
	if (selectedPair == null || detail == null || selection == null) {
		return [];
	}

	if (selection.kind === 'field' && selection.path != null) {
		return detail.differences
			.map((difference, indexValue) => ({ difference, indexValue }))
			.filter(({ difference }) => matchesPropertyPath(difference.propertyName, selection.path as string, selection.exact === true))
			.map(({ difference, indexValue }) => ({
				key: buildStructuredEvidenceKey(difference, indexValue),
				kind: 'structured' as const,
				label: difference.propertyName || 'Changed value',
				description: `${formatValue(difference.expected)} -> ${formatValue(difference.actual)}`,
				structured: difference,
			}));
	}

	if (selection.kind === 'pattern') {
		if (selection.patternKind === 'raw') {
			const matches = detail.rawTextDifferences
				.map((difference, indexValue) => ({ difference, indexValue }))
				.filter(({ difference }) => {
					if (selection.description != null && selection.description.trim() !== '') {
						return buildRawPreview(difference) === selection.description;
					}

					return buildRawPatternLabel(difference) === selection.label;
				});

			const rawSource = matches.length > 0 ? matches : detail.rawTextDifferences.slice(0, 1).map((difference, indexValue) => ({ difference, indexValue }));
			return rawSource.map(({ difference, indexValue }) => ({
				key: buildRawEvidenceKey(difference, indexValue),
				kind: 'raw' as const,
				label: humanizeLabel(difference.type),
				description: difference.description,
				raw: difference,
			}));
		}

		return detail.differences
			.map((difference, indexValue) => ({ difference, indexValue }))
			.filter(({ difference }) => {
				if (selection.description != null && selection.description.trim() !== '') {
					return buildExactPatternDescription(difference) === selection.description;
				}

				if (selection.label === difference.propertyName) {
					return true;
				}

				return difference.propertyName === '' && selection.label === 'Changed value';
			})
			.map(({ difference, indexValue }) => ({
				key: buildStructuredEvidenceKey(difference, indexValue),
				kind: 'structured' as const,
				label: difference.propertyName || 'Changed value',
				description: `${formatValue(difference.expected)} -> ${formatValue(difference.actual)}`,
				structured: difference,
			}));
	}

	if (selection.kind === 'group' && selection.groupId === 'raw') {
		return detail.rawTextDifferences.slice(0, 1).map((difference, indexValue) => ({
			key: buildRawEvidenceKey(difference, indexValue),
			kind: 'raw' as const,
			label: humanizeLabel(difference.type),
			description: difference.description,
			raw: difference,
		}));
	}

	return detail.differences.slice(0, 1).map((difference, indexValue) => ({
		key: buildStructuredEvidenceKey(difference, indexValue),
		kind: 'structured' as const,
		label: difference.propertyName || 'Changed value',
		description: `${formatValue(difference.expected)} -> ${formatValue(difference.actual)}`,
		structured: difference,
	}));
}

function splitPropertyPath(path: string): string[] {
	return path.split('.').filter((segment) => segment.trim() !== '');
}

function matchesPropertyPath(candidate: string, path: string, exact: boolean): boolean {
	if (exact) {
		return candidate === path;
	}

	return candidate === path || candidate.startsWith(`${path}.`) || candidate.startsWith(`${path}[`);
}

function comparePairs(left: PairSummary, right: PairSummary): number {
	const leftStatus = getPairStatus(left);
	const rightStatus = getPairStatus(right);
	const statusDiff = getStatusRank(leftStatus) - getStatusRank(rightStatus);
	if (statusDiff !== 0) {
		return statusDiff;
	}

	return right.differenceCount - left.differenceCount || left.index - right.index;
}

function getPairStatus(pair: PairSummary): PairStatus {
	if (pair.hasError) {
		return 'error';
	}

	return pair.areEqual ? 'equal' : 'different';
}

function getStatusRank(status: PairStatus): number {
	switch (status) {
		case 'error':
			return 0;
		case 'different':
			return 1;
		default:
			return 2;
	}
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

function buildReviewSummary(pairs: PairSummary[], reviewStates: Record<string, ReviewState>): Record<ReviewStatus, number> {
	const summary: Record<ReviewStatus, number> = {
		unreviewed: 0,
		'needs-review': 0,
		'accepted-difference': 0,
		'bug-identified': 0,
	};

	for (const pair of pairs) {
		const status = getReviewStatus(reviewStates, pair.pairId);
		summary[status] += 1;
	}

	return summary;
}

function getReviewStatus(reviewStates: Record<string, ReviewState>, pairId: string): ReviewStatus {
	return reviewStates[pairId]?.status ?? 'unreviewed';
}

function buildStructuredEvidenceKey(difference: StructuredDifference, indexValue: number): string {
	return `structured:${difference.propertyName}:${indexValue}`;
}

function buildRawEvidenceKey(difference: RawTextDifference, indexValue: number): string {
	return `raw:${difference.type}:${difference.lineNumberA ?? 'x'}:${difference.lineNumberB ?? 'y'}:${indexValue}`;
}

function countLeafNodes(nodes: NavigatorNode[]): number {
	return nodes.reduce((sum, node) => sum + (node.children.length === 0 ? 1 : countLeafNodes(node.children)), 0);
}

function expandAllNodes(nodes: NavigatorNode[]): Record<string, boolean> {
	const expanded: Record<string, boolean> = {};
	const visit = (node: NavigatorNode) => {
		expanded[node.key] = true;
		node.children.forEach(visit);
	};
	nodes.forEach(visit);
	return expanded;
}

function collapseAllNodes(nodes: NavigatorNode[]): Record<string, boolean> {
	const collapsed: Record<string, boolean> = {};
	for (const node of nodes) {
		collapsed[node.key] = node.depth < 1;
	}
	return collapsed;
}

function expandPathKeys(key: string): Record<string, boolean> {
	const parts = key.split('/');
	const expanded: Record<string, boolean> = {};
	for (let index = 1; index <= parts.length; index++) {
		expanded[parts.slice(0, index).join('/')] = true;
	}
	return expanded;
}

function renderNodeIcon(icon: NavigatorNode['icon'], hasChildren: boolean): string {
	if (icon === 'pattern') {
		return '#';
	}

	if (icon === 'raw') {
		return '~';
	}

	if (icon === 'group') {
		return '≡';
	}

	if (hasChildren) {
		return '◦';
	}

	return '•';
}

function buildOutcomeFocusOptions(pairs: PairSummary[]): OutcomeFocusOption[] {
	const pairOutcomeCounts = new Map<string, number>();
	for (const pair of pairs) {
		const pairOutcome = pair.pairOutcome?.trim();
		if (pairOutcome != null && pairOutcome !== '') {
			pairOutcomeCounts.set(pairOutcome, (pairOutcomeCounts.get(pairOutcome) ?? 0) + 1);
		}
	}

	const primaryOptions = [
		{
			key: 'outcome/all',
			label: 'All pairs',
			description: 'Reset response outcome focus and keep the standard results behavior.',
			count: pairs.length,
			kind: 'all',
			tone: 'neutral',
		},
		{
			key: 'outcome/non-success',
			label: 'Non-success / failed',
			description: 'Comparison errors, non-2xx statuses, or explicit failed outcome labels.',
			count: pairs.filter((pair) => isPairNonSuccess(pair)).length,
			kind: 'non-success',
			tone: 'warn',
		},
		{
			key: 'outcome/comparison-error',
			label: 'Comparison errors',
			description: 'Pairs where the comparison itself failed before review could start.',
			count: pairs.filter((pair) => pair.hasError).length,
			kind: 'comparison-error',
			tone: 'danger',
		},
		{
			key: 'outcome/http-non-success',
			label: 'HTTP non-success',
			description: 'Any response pair with a non-2xx status in A or B.',
			count: pairs.filter((pair) => hasHttpNonSuccess(pair)).length,
			kind: 'http-non-success',
			tone: 'warn',
		},
		{
			key: 'outcome/status-mismatch',
			label: 'Status mismatch',
			description: 'A and B returned different HTTP status codes.',
			count: pairs.filter((pair) => hasStatusMismatch(pair)).length,
			kind: 'status-mismatch',
			tone: 'warn',
		},
		{
			key: 'outcome/equal-non-success',
			label: 'Equal but non-success',
			description: 'Pairs marked equal by comparison rules but still carrying non-success outcome context.',
			count: pairs.filter((pair) => pair.areEqual && isPairNonSuccess(pair)).length,
			kind: 'equal-non-success',
			tone: 'ok',
		},
	] satisfies OutcomeFocusOption[];

	const pairOutcomeOptions = [...pairOutcomeCounts.entries()]
		.sort((left, right) => right[1] - left[1] || left[0].localeCompare(right[0]))
		.map<OutcomeFocusOption>(([pairOutcome, count]) => ({
			key: `outcome/pair/${pairOutcome}`,
			label: pairOutcome,
			description: `Focus file pairs whose pairOutcome is exactly "${pairOutcome}".`,
			count,
			kind: 'pair-outcome' as const,
			pairOutcome,
			tone: isNonSuccessOutcomeLabel(pairOutcome) ? 'warn' : 'neutral',
		}));

	return [...primaryOptions.filter((option) => option.kind === 'all' || option.count > 0), ...pairOutcomeOptions];
}

function matchesOutcomeFocus(pair: PairSummary, option: OutcomeFocusOption): boolean {
	switch (option.kind) {
		case 'all':
			return true;
		case 'non-success':
			return isPairNonSuccess(pair);
		case 'comparison-error':
			return pair.hasError;
		case 'http-non-success':
			return hasHttpNonSuccess(pair);
		case 'status-mismatch':
			return hasStatusMismatch(pair);
		case 'equal-non-success':
			return pair.areEqual && isPairNonSuccess(pair);
		case 'pair-outcome':
			return (pair.pairOutcome?.trim() ?? '') === (option.pairOutcome ?? '');
		default:
			return true;
	}
}

function getPairResponseContextItems(pair: PairSummary): ContextChipItem[] {
	const items: ContextChipItem[] = [];
	const pairOutcome = pair.pairOutcome?.trim();
	if (pairOutcome != null && pairOutcome !== '') {
		items.push({
			key: `outcome:${pairOutcome}`,
			label: `Outcome: ${pairOutcome}`,
			tone: isNonSuccessOutcomeLabel(pairOutcome) ? 'warn' : 'neutral',
			title: `pairOutcome: ${pairOutcome}`,
		});
	}

	if (pair.httpStatusA != null) {
		items.push({
			key: `http-status-a:${pair.httpStatusA}`,
			label: `A ${pair.httpStatusA}`,
			tone: isHttpSuccessStatus(pair.httpStatusA) ? 'ok' : 'warn',
			title: `Response A HTTP status ${pair.httpStatusA}`,
		});
	}

	if (pair.httpStatusB != null) {
		items.push({
			key: `http-status-b:${pair.httpStatusB}`,
			label: `B ${pair.httpStatusB}`,
			tone: isHttpSuccessStatus(pair.httpStatusB) ? 'ok' : 'warn',
			title: `Response B HTTP status ${pair.httpStatusB}`,
		});
	}

	if (hasStatusMismatch(pair)) {
		items.push({
			key: 'status-mismatch',
			label: 'Status mismatch',
			tone: 'warn',
			title: `A returned ${pair.httpStatusA}, B returned ${pair.httpStatusB}`,
		});
	}

	if (pair.hasError) {
		items.push({
			key: 'comparison-error',
			label: 'Comparison error',
			tone: 'danger',
			title: pair.errorMessage ?? 'The comparison engine reported an error.',
		});
	}

	return items;
}

function getPropertyPathDisplay(path: string | null | undefined, fallback: string): PropertyPathDisplay {
	const fullPath = path?.trim();
	if (fullPath == null || fullPath === '') {
		return { shortLabel: fallback };
	}

	const segments = splitPropertyPath(fullPath);
	if (segments.length <= 2) {
		return { shortLabel: fullPath, fullPath };
	}

	return {
		shortLabel: segments.slice(-2).join('.'),
		fullPath,
	};
}

function hasHttpNonSuccess(pair: PairSummary): boolean {
	return isNonSuccessHttpStatus(pair.httpStatusA) || isNonSuccessHttpStatus(pair.httpStatusB);
}

function hasStatusMismatch(pair: PairSummary): boolean {
	return pair.httpStatusA != null && pair.httpStatusB != null && pair.httpStatusA !== pair.httpStatusB;
}

function isPairNonSuccess(pair: PairSummary): boolean {
	return pair.hasError || hasHttpNonSuccess(pair) || isNonSuccessOutcomeLabel(pair.pairOutcome);
}

function isHttpSuccessStatus(status: number | null | undefined): boolean {
	return status != null && status >= 200 && status < 300;
}

function isNonSuccessHttpStatus(status: number | null | undefined): boolean {
	return status != null && !isHttpSuccessStatus(status);
}

function isNonSuccessOutcomeLabel(value: string | null | undefined): boolean {
	const normalized = value?.trim().toLowerCase();
	if (normalized == null || normalized === '') {
		return false;
	}

	if (/(error|fail|failed|timeout|timed out|abort|aborted|cancel|cancelled|exception|invalid|mismatch|non-?success|unsuccess|not found|unauthorized|forbidden)/.test(normalized)) {
		return true;
	}

	const statusCodes = normalized.match(/\b\d{3}\b/g);
	if (statusCodes?.some((valueMatch) => !isHttpSuccessStatus(Number(valueMatch)))) {
		return true;
	}

	if (/(^|\b)(success|successful|ok|passed|pass|equal|matched|same)(\b|$)/.test(normalized)) {
		return false;
	}

	return false;
}

function getPatternKindFromKey(key: string): string {
	const separatorIndex = key.indexOf(':');
	return separatorIndex < 0 ? 'general' : key.slice(0, separatorIndex);
}

function formatPairStatus(status: PairStatus): string {
	switch (status) {
		case 'equal':
			return 'Equal';
		case 'error':
			return 'Error';
		default:
			return 'Different';
	}
}

function formatReviewStatus(status: ReviewStatus): string {
	switch (status) {
		case 'needs-review':
			return 'Needs Review';
		case 'accepted-difference':
			return 'Accepted';
		case 'bug-identified':
			return 'Bug Identified';
		default:
			return 'Unreviewed';
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

function formatPreviewValue(value: unknown): string {
	return trimPreview(formatValue(value));
}

function buildExactPatternDescription(difference: StructuredDifference): string {
	const label = difference.propertyName || 'Changed value';
	return `${label}: ${formatPreviewValue(difference.expected)} -> ${formatPreviewValue(difference.actual)}`;
}

function buildRawPatternLabel(difference: RawTextDifference): string {
	return `Raw ${difference.type}`;
}

function buildRawPreview(difference: RawTextDifference): string {
	if (difference.description != null && difference.description.trim() !== '') {
		return trimPreview(difference.description);
	}

	const rawPreview = difference.textB != null && difference.textB.trim() !== '' ? difference.textB : difference.textA;
	return trimPreview(rawPreview ?? humanizeLabel(difference.type));
}

function trimPreview(value: string): string {
	const normalized = value.replace(/\r/g, ' ').replace(/\n/g, ' ').trim();
	return normalized.length <= 120 ? normalized : `${normalized.slice(0, 119)}...`;
}

function humanizeLabel(label: string): string {
	return label
		.replace(/([a-z])([A-Z])/g, '$1 $2')
		.replace(/[-_]/g, ' ')
		.replace(/\s+/g, ' ')
		.trim();
}

function getStaticSiteFileProtocolError(mode: string, relativePath?: string | null): string | null {
	if (mode !== 'static-site' || window.location.protocol !== 'file:') {
		return null;
	}

	const pathSuffix = relativePath != null && relativePath.trim() !== '' ? ` (${relativePath})` : '';
	return `This StaticSite report cannot be opened directly from disk because browsers block file:// fetch requests${pathSuffix}. Serve the report directory over http(s), or regenerate the report with --html-mode SingleFile for local file opening.`;
}

function getReviewStorageKey(reportId: string): string {
	return `comparisontool-report-review-v1:${reportId}`;
}

function getLegacyReviewStorageKey(reportId: string): string {
	return `comparison-tool-report:${reportId}:review-categories`;
}

function normalizeStoredReviewStates(input: unknown): Record<string, ReviewState> {
	if (input == null || typeof input !== 'object') {
		return {};
	}

	const normalized: Record<string, ReviewState> = {};
	for (const [pairId, value] of Object.entries(input as Record<string, unknown>)) {
		const mappedStatus = mapLegacyReviewStatus(value);
		if (mappedStatus === 'unreviewed') {
			continue;
		}

		normalized[pairId] = {
			status: mappedStatus,
			updatedAt:
				typeof value === 'object' && value != null && typeof (value as { updatedAt?: unknown }).updatedAt === 'string'
					? (value as { updatedAt: string }).updatedAt
					: '',
		};
	}

	return normalized;
}

function mapLegacyReviewStatus(value: unknown): ReviewStatus {
	if (typeof value === 'string') {
		switch (value) {
			case 'Needs Review':
			case 'needs-review':
				return 'needs-review';
			case 'Accepted Difference':
			case 'accepted-difference':
			case 'Expected difference':
			case 'False positive':
				return 'accepted-difference';
			case 'Bug Identified':
			case 'bug-identified':
			case 'Unexpected difference':
				return 'bug-identified';
			default:
				return 'unreviewed';
		}
	}

	if (typeof value === 'object' && value != null && 'status' in value) {
		return mapLegacyReviewStatus((value as { status?: unknown }).status);
	}

	return 'unreviewed';
}

function loadReviewStates(reportId: string): Record<string, ReviewState> {
	if (typeof window === 'undefined') {
		return {};
	}

	try {
		const raw = window.localStorage.getItem(getReviewStorageKey(reportId));
		if (raw == null || raw.trim() === '') {
			const legacyRaw = window.localStorage.getItem(getLegacyReviewStorageKey(reportId));
			if (legacyRaw == null || legacyRaw.trim() === '') {
				return {};
			}

			return normalizeStoredReviewStates(JSON.parse(legacyRaw));
		}

		return normalizeStoredReviewStates(JSON.parse(raw));
	} catch {
		return {};
	}
}

function persistReviewStates(reportId: string, reviewStates: Record<string, ReviewState>): void {
	if (typeof window === 'undefined') {
		return;
	}

	try {
		if (Object.keys(reviewStates).length === 0) {
			window.localStorage.removeItem(getReviewStorageKey(reportId));
			return;
		}

		window.localStorage.setItem(getReviewStorageKey(reportId), JSON.stringify(reviewStates));
	} catch {
		// Ignore persistence failures in the static report runtime.
	}
}