import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
import './app.css';
import type { ComparisonReport } from './types';

function readReportData(): { report: ComparisonReport | null; loadError: string | null } {
	const element = document.getElementById('report-data');

	if (element == null || element.textContent == null || element.textContent.trim() === '') {
		return {
			report: null,
			loadError: 'No report payload was embedded into this HTML artifact.',
		};
	}

	try {
		return {
			report: JSON.parse(element.textContent) as ComparisonReport,
			loadError: null,
		};
	} catch (error) {
		return {
			report: null,
			loadError: error instanceof Error ? error.message : 'Unknown JSON parsing error.',
		};
	}
}

const { report, loadError } = readReportData();

ReactDOM.createRoot(document.getElementById('root')!).render(
	<React.StrictMode>
		<App report={report} loadError={loadError} />
	</React.StrictMode>,
);
