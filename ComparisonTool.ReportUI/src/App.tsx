import { useEffect, useMemo, useState } from 'react';
import type { ComparisonFilePair, ComparisonReport, LabelCount } from './types';

const REVIEW_CATEGORIES = [
	'Unreviewed',
	'Expected difference',
	'Unexpected difference',
	'False positive',
	'Needs follow-up',
	'Blocked',
	'Done',
] as const;

type ReviewCategory = (typeof REVIEW_CATEGORIES)[number];
type StatusFilter = 'all' | 'different' | 'equal' | 'error';

interface AppProps {
	report: ComparisonReport | null;
	loadError: string | null;
}

export default function App({ report, loadError }: AppProps) {
	if (report == null) {
		return (
			<div className="content empty-panel">
				<strong>Unable to load the comparison report</strong>
				<p className="muted">{loadError ?? 'The embedded report payload was missing.'}</p>
			</div>
		);
	}

	return <ReportView report={report} />;
}

function ReportView({ report }: { report: ComparisonReport }) {
	const storageKey = useMemo(() => `comparison-tool-report:${report.reportId}:review-categories`, [report.reportId]);

	const defaultStatusFilter: StatusFilter = report.summary.differentCount > 0 || report.summary.errorCount > 0
		? 'different'
		: 'all';

	const [statusFilter, setStatusFilter] = useState<StatusFilter>(defaultStatusFilter);
	const [categoryFilter, setCategoryFilter] = useState<string>('All categories');
	const [searchText, setSearchText] = useState('');
	const [fieldFilter, setFieldFilter] = useState<string | null>(null);
	const [reviewCategories, setReviewCategories] = useState<Record<string, ReviewCategory>>(() =>
		readCategories(storageKey),
	);

	const filteredPairs = useMemo(() => {
		const search = searchText.trim().toLowerCase();

		return report.filePairs.filter((pair) => {
			const pairStatus = getPairStatus(pair);
			const reviewCategory = reviewCategories[pair.pairId] ?? 'Unreviewed';
			const searchTarget = [
				pair.file1,
				pair.file2,
				pair.requestRelativePath,
				pair.pairOutcome,
				pair.errorMessage,
				...pair.affectedFields,
			]
				.filter(Boolean)
				.join(' ')
				.toLowerCase();

			const matchesStatus =
				statusFilter === 'all'
					? true
					: statusFilter === 'different'
						? pairStatus === 'different' || pairStatus === 'error'
						: pairStatus === statusFilter;

			const matchesCategory =
				categoryFilter === 'All categories' ? true : reviewCategory === categoryFilter;

			const matchesField = fieldFilter == null ? true : pair.affectedFields.includes(fieldFilter);
			const matchesSearch = search.length === 0 ? true : searchTarget.includes(search);

			return matchesStatus && matchesCategory && matchesField && matchesSearch;
		});
	}, [categoryFilter, fieldFilter, report.filePairs, reviewCategories, searchText, statusFilter]);

	const [selectedPairId, setSelectedPairId] = useState<string | null>(filteredPairs[0]?.pairId ?? null);

	useEffect(() => {
		try {
			localStorage.setItem(storageKey, JSON.stringify(reviewCategories));
		} catch {
			// Ignore storage failures for static artifact usage.
		}
	}, [reviewCategories, storageKey]);

	useEffect(() => {
		if (filteredPairs.length === 0) {
			setSelectedPairId(null);
			return;
		}

		if (selectedPairId == null || !filteredPairs.some((pair) => pair.pairId === selectedPairId)) {
			setSelectedPairId(filteredPairs[0].pairId);
		}
	}, [filteredPairs, selectedPairId]);

	const selectedPair = filteredPairs.find((pair) => pair.pairId === selectedPairId) ?? filteredPairs[0] ?? null;
	const selectedIndex = selectedPair == null ? -1 : filteredPairs.findIndex((pair) => pair.pairId === selectedPair.pairId);
	const reviewCounts = buildReviewCounts(report.filePairs, reviewCategories);

	return (
		<div className="app-shell">
			<aside className="sidebar">
				<section className="panel">
					<div className="panel-body">
						<p className="eyebrow">Static comparison artifact</p>
						<h1 className="title">Comparison report</h1>
						<p className="subtitle">
							{formatCommandLabel(report.command)} • Generated {formatDateTime(report.generatedAt)}
						</p>
						<div className="chip-row" style={{ marginTop: '0.9rem' }}>
							<span className="chip">Schema v{report.schemaVersion}</span>
							<span className="chip">{report.summary.totalPairs} pairs</span>
							<span className="chip">{report.elapsedSeconds.toFixed(2)}s</span>
							{report.model ? <span className="chip">Model: {report.model}</span> : null}
						</div>
					</div>
				</section>

				<section className="panel">
					<div className="panel-body">
						<div className="metric-grid">
							<MetricCard label="Different" value={report.summary.differentCount} />
							<MetricCard label="Equal" value={report.summary.equalCount} />
							<MetricCard label="Errors" value={report.summary.errorCount} />
							<MetricCard label="All equal" value={report.summary.allEqual ? 'Yes' : 'No'} />
						</div>
					</div>
				</section>

				<section className="panel">
					<div className="panel-body">
						<div className="filter-row" style={{ marginBottom: '0.75rem' }}>
							{(['different', 'all', 'equal', 'error'] as const).map((filter) => (
								<button
									key={filter}
									className={`filter-button ${statusFilter === filter ? 'active' : ''}`}
									onClick={() => setStatusFilter(filter)}
									type="button"
								>
									{formatFilterLabel(filter)}
								</button>
							))}
						</div>
						<input
							className="search-input"
							placeholder="Search files, outcomes, or fields"
							value={searchText}
							onChange={(event) => setSearchText(event.target.value)}
						/>
						<div style={{ marginTop: '0.75rem' }}>
							<select
								className="select-input"
								value={categoryFilter}
								onChange={(event) => setCategoryFilter(event.target.value)}
							>
								<option>All categories</option>
								{REVIEW_CATEGORIES.map((category) => (
									<option key={category}>{category}</option>
								))}
							</select>
						</div>
						{fieldFilter ? (
							<div className="chip-row" style={{ marginTop: '0.75rem' }}>
								<button className="chip chip-button active" onClick={() => setFieldFilter(null)} type="button">
									Field filter: {fieldFilter} ×
								</button>
							</div>
						) : null}
					</div>
				</section>

				<section className="panel">
					<div className="panel-header">
						<div>
							<h2 className="section-title">Review categories</h2>
							<p className="muted">Stored in local browser storage for this report ID.</p>
						</div>
						<button className="export-button" onClick={() => exportCategories(report, reviewCategories)} type="button">
							Export JSON
						</button>
					</div>
					<div className="panel-body">
						<div className="review-grid">
							{reviewCounts.map((item) => (
								<div className="mini-stat" key={item.label}>
									<strong>{item.count}</strong>
									<span className="muted">{item.label}</span>
								</div>
							))}
						</div>
					</div>
				</section>

				{report.pairOutcomeCounts.length > 0 ? (
					<section className="panel">
						<div className="panel-body">
							<h2 className="section-title">Request outcomes</h2>
							<div className="chip-row" style={{ marginTop: '0.75rem' }}>
								{report.pairOutcomeCounts.map((item) => (
									<span className="chip" key={item.label}>
										{humanizeLabel(item.label)}: {item.count}
									</span>
								))}
							</div>
						</div>
					</section>
				) : null}

				{report.mostAffectedFields.fields.length > 0 ? (
					<section className="panel">
						<div className="panel-body">
							<h2 className="section-title">Most affected fields</h2>
							<div className="chip-row" style={{ marginTop: '0.75rem' }}>
								{report.mostAffectedFields.fields.map((field) => (
									<button
										key={field.fieldPath}
										className={`chip chip-button ${fieldFilter === field.fieldPath ? 'active' : ''}`}
										onClick={() => setFieldFilter((current) => (current === field.fieldPath ? null : field.fieldPath))}
										type="button"
									>
										{field.fieldPath} ({field.affectedPairCount})
									</button>
								))}
							</div>
						</div>
					</section>
				) : null}

				<section className="panel" style={{ minHeight: 0, flex: 1 }}>
					<div className="panel-header">
						<div>
							<h2 className="section-title">Pairs</h2>
							<p className="muted">{filteredPairs.length} visible of {report.filePairs.length}</p>
						</div>
					</div>
					<div className="panel-body pair-list">
						{filteredPairs.length === 0 ? (
							<div className="empty-state">
								<strong>No pairs match the current filters</strong>
								<p className="muted">Clear the search, category, or field filter to broaden the result set.</p>
							</div>
						) : (
							filteredPairs.map((pair) => {
								const category = reviewCategories[pair.pairId] ?? 'Unreviewed';

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
										<div className="pair-list-subheader" style={{ marginTop: '0.75rem' }}>
											<div className="badge-row">
												<span className="pair-stat badge">#{pair.index}</span>
												<span className="pair-stat badge">{pair.differenceCount} diffs</span>
												{pair.pairOutcome ? <span className="pair-stat badge">{humanizeLabel(pair.pairOutcome)}</span> : null}
											</div>
										</div>
										<div className="pair-review-row" style={{ marginTop: '0.85rem' }}>
											<span className="muted">Review</span>
											<select
												className="pair-review-select"
												value={category}
												onChange={(event) => {
													event.stopPropagation();
													setReviewCategories((current) => ({
														...current,
														[pair.pairId]: event.target.value as ReviewCategory,
													}));
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
			</aside>

			<main className="content">
				{selectedPair == null ? (
					<section className="panel empty-panel">
						<strong>Select a file pair</strong>
						<p className="muted">Choose a pair from the left to inspect structured or raw-text differences.</p>
					</section>
				) : (
					<>
						<section className="panel sticky-toolbar">
							<div className="panel-body detail-toolbar">
								<div>
									<p className="eyebrow">Pair {selectedPair.index}</p>
									<h2 className="detail-title">{selectedPair.file1}</h2>
									<p className="muted">{selectedPair.requestRelativePath ?? selectedPair.file2}</p>
								</div>
								<div className="detail-toolbar">
									<button
										className="toolbar-button"
										disabled={selectedIndex <= 0}
										onClick={() => setSelectedPairId(filteredPairs[selectedIndex - 1]?.pairId ?? null)}
										type="button"
									>
										Previous
									</button>
									<button
										className="toolbar-button"
										disabled={selectedIndex < 0 || selectedIndex >= filteredPairs.length - 1}
										onClick={() => setSelectedPairId(filteredPairs[selectedIndex + 1]?.pairId ?? null)}
										type="button"
									>
										Next
									</button>
									<select
										className="inline-select"
										value={reviewCategories[selectedPair.pairId] ?? 'Unreviewed'}
										onChange={(event) =>
											setReviewCategories((current) => ({
												...current,
												[selectedPair.pairId]: event.target.value as ReviewCategory,
											}))
										}
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
									<DetailCard label="Review" value={reviewCategories[selectedPair.pairId] ?? 'Unreviewed'} />
									{selectedPair.pairOutcome ? (
										<DetailCard label="Request outcome" value={humanizeLabel(selectedPair.pairOutcome)} />
									) : null}
									{selectedPair.httpStatusA != null || selectedPair.httpStatusB != null ? (
										<DetailCard
											label="HTTP status"
											value={`A: ${selectedPair.httpStatusA ?? '—'} | B: ${selectedPair.httpStatusB ?? '—'}`}
										/>
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

								{selectedPair.categoryCounts.length > 0 ? (
									<div>
										<h3 className="section-title">Difference categories</h3>
										<div className="chip-row" style={{ marginTop: '0.75rem' }}>
											{selectedPair.categoryCounts.map((item) => (
												<span className="chip" key={item.label}>
													{humanizeLabel(item.label)}: {item.count}
												</span>
											))}
										</div>
									</div>
								) : null}

								{selectedPair.rootObjectCounts.length > 0 ? (
									<div>
										<h3 className="section-title">Root objects</h3>
										<div className="chip-row" style={{ marginTop: '0.75rem' }}>
											{selectedPair.rootObjectCounts.map((item) => (
												<span className="chip" key={item.label}>
													{item.label}: {item.count}
												</span>
											))}
										</div>
									</div>
								) : null}
							</div>
						</section>

						{selectedPair.hasError ? (
							<section className="panel">
								<div className="panel-body">
									<div className="error-callout">
										<h3 className="section-title">Comparison error</h3>
										<p className="value-block">{selectedPair.errorMessage ?? 'Unknown comparison error.'}</p>
										{selectedPair.errorType ? <p className="muted">Type: {selectedPair.errorType}</p> : null}
									</div>
								</div>
							</section>
						) : null}

						{selectedPair.differences.length > 0 ? (
							<section className="panel">
								<div className="panel-header">
									<div>
										<h3 className="section-title">Structured differences</h3>
										<p className="muted">Compare expected and actual values side by side.</p>
									</div>
								</div>
								<div className="panel-body" style={{ overflowX: 'auto' }}>
									<table className="diff-table">
										<thead>
											<tr>
												<th>Property</th>
												<th>Expected</th>
												<th>Actual</th>
											</tr>
										</thead>
										<tbody>
											{selectedPair.differences.map((difference, index) => (
												<tr key={`${difference.propertyName}-${index}`}>
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

						{selectedPair.rawTextDifferences.length > 0 ? (
							<section className="panel">
								<div className="panel-header">
									<div>
										<h3 className="section-title">Raw text differences</h3>
										<p className="muted">Useful for non-success HTTP responses or non-model comparisons.</p>
									</div>
								</div>
								<div className="panel-body diff-card-list">
									{selectedPair.rawTextDifferences.map((difference, index) => (
										<article className="diff-card panel" key={`${difference.type}-${index}`}>
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

						{!selectedPair.hasError &&
						selectedPair.differences.length === 0 &&
						selectedPair.rawTextDifferences.length === 0 ? (
							<section className="panel empty-panel">
								<strong>No manual inspection needed</strong>
								<p className="muted">This pair is currently equal under the active comparison rules.</p>
							</section>
						) : null}
					</>
				)}
			</main>
		</div>
	);
}

function MetricCard({ label, value }: { label: string; value: number | string }) {
	return (
		<div className="metric-card">
			<strong>{value}</strong>
			<span className="muted">{label}</span>
		</div>
	);
}

function DetailCard({ label, value }: { label: string; value: number | string }) {
	return (
		<div className="detail-card panel">
			<div className="detail-card-label">{label}</div>
			<div className="detail-card-value">{value}</div>
		</div>
	);
}

function PairStatusBadge({ pair }: { pair: ComparisonFilePair }) {
	const status = getPairStatus(pair);

	return (
		<span className={`badge status-${status === 'different' ? pair.comparisonKind : status}`}>
			{formatPairStatus(status)}
		</span>
	);
}

function getPairStatus(pair: ComparisonFilePair): 'equal' | 'different' | 'error' {
	if (pair.hasError) {
		return 'error';
	}

	return pair.areEqual ? 'equal' : 'different';
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

function buildReviewCounts(
	pairs: ComparisonFilePair[],
	categories: Record<string, ReviewCategory>,
): LabelCount[] {
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

function exportCategories(report: ComparisonReport, categories: Record<string, ReviewCategory>) {
	const payload = {
		reportId: report.reportId,
		generatedAt: report.generatedAt,
		exportedAt: new Date().toISOString(),
		reviews: report.filePairs.map((pair) => ({
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
