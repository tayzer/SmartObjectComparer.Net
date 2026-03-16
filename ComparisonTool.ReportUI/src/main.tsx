import React from 'react';
import ReactDOM from 'react-dom/client';
import App from './App';
import './app.css';
import type { ReportBootstrap } from './types';

function readReportData(): { bootstrap: ReportBootstrap | null; loadError: string | null } {
	const element = document.getElementById('report-data');

	if (element == null || element.textContent == null || element.textContent.trim() === '') {
		return {
			bootstrap: null,
			loadError: 'No report payload was embedded into this HTML artifact.',
		};
	}

	try {
		return {
			bootstrap: JSON.parse(element.textContent) as ReportBootstrap,
			loadError: null,
		};
	} catch (error) {
		return {
			bootstrap: null,
			loadError: error instanceof Error ? error.message : 'Unknown JSON parsing error.',
		};
	}
}

const { bootstrap, loadError } = readReportData();

ReactDOM.createRoot(document.getElementById('root')!).render(
	<React.StrictMode>
		<App bootstrap={bootstrap} loadError={loadError} />
	</React.StrictMode>,
);
