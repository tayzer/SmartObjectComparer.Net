function saveAsFile(filename, contentType, content) {
    const blob = new Blob([content], { type: contentType });
    const url = URL.createObjectURL(blob);

    const a = document.createElement('a');
    a.href = url;
    a.download = filename;

    document.body.appendChild(a);

    a.click();

    setTimeout(() => {
        document.body.removeChild(a);
        URL.revokeObjectURL(url);
    }, 0);
}

function renderPieChart(canvasId, labels, data) {
    const ctx = document.getElementById(canvasId).getContext('2d');
    const colors = generateColors(labels.length);

    new Chart(ctx, {
        type: 'pie',
        data: {
            labels: labels,
            datasets: [{
                data: data,
                backgroundColor: colors,
                borderWidth: 1
            }]
        },
        options: {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    position: 'right',
                }
            }
        }
    });
}

function generateColors(count) {
    const colors = [];
    const baseColors = [
        '#4e73df', '#1cc88a', '#36b9cc', '#f6c23e', '#e74a3b',
        '#fd7e14', '#6f42c1', '#20c9a6', '#27a844', '#e83e8c'
    ];

    for (let i = 0; i < count; i++) {
        colors.push(baseColors[i % baseColors.length]);
    }

    return colors;
}

function processFilesInBatches(files, batchSize, callback) {
    return new Promise((resolve) => {
        const totalFiles = files.length;
        let processed = 0;

        function processNextBatch() {
            const batch = files.slice(processed, processed + batchSize);

            if (batch.length === 0) {
                resolve();
                return;
            }

            Promise.all(batch.map(callback))
                .then(() => {
                    processed += batch.length;
                    setTimeout(processNextBatch, 0); // Allow UI to refresh
                });
        }

        processNextBatch();
    });
}

function optimizedFileRead(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result);
        reader.onerror = reject;
        reader.readAsArrayBuffer(file);
    });
}

// Check if browser supports folder upload
function checkFolderUploadSupport() {
    const input = document.createElement('input');
    input.type = 'file';
    return 'webkitdirectory' in input || 'directory' in input;
}

// Show processing indicator while loading files
function showProcessingIndicator(message) {
    // Create or get the processing indicator
    let indicator = document.getElementById('file-processing-indicator');
    if (!indicator) {
        indicator = document.createElement('div');
        indicator.id = 'file-processing-indicator';
        indicator.style.position = 'fixed';
        indicator.style.top = '50%';
        indicator.style.left = '50%';
        indicator.style.transform = 'translate(-50%, -50%)';
        indicator.style.padding = '20px';
        indicator.style.backgroundColor = 'rgba(0, 0, 0, 0.8)';
        indicator.style.color = 'white';
        indicator.style.borderRadius = '5px';
        indicator.style.zIndex = '9999';
        indicator.innerHTML = `
            <div class="text-center">
                <div class="spinner-border text-light mb-2" role="status">
                    <span class="visually-hidden">Loading...</span>
                </div>
                <div id="processing-message">${message}</div>
                <div class="progress mt-2" style="height: 10px; width: 250px;">
                    <div id="processing-progress-bar" class="progress-bar progress-bar-striped progress-bar-animated" style="width: 0%"></div>
                </div>
                <div id="processing-count" class="mt-1 small">0 / 0 files</div>
            </div>
        `;
        document.body.appendChild(indicator);
    } else {
        document.getElementById('processing-message').textContent = message;
        document.getElementById('processing-progress-bar').style.width = '0%';
        document.getElementById('processing-count').textContent = '0 / 0 files';
        indicator.style.display = 'block';
    }
}

// Update progress while processing files
function updateProcessingProgress(percentage, current, total) {
    const progressBar = document.getElementById('processing-progress-bar');
    const countText = document.getElementById('processing-count');

    if (progressBar) {
        progressBar.style.width = `${percentage}%`;
    }

    if (countText) {
        countText.textContent = `${current} / ${total} files`;
    }
}

// Hide processing indicator when done
function hideProcessingIndicator() {
    const indicator = document.getElementById('file-processing-indicator');
    if (indicator) {
        indicator.style.display = 'none';
    }
}

// Process files in batches to avoid UI freezing
function processFilesInBatches(files, batchSize, callback) {
    return new Promise((resolve) => {
        const totalFiles = files.length;
        let processed = 0;

        function processNextBatch() {
            const batch = files.slice(processed, processed + batchSize);

            if (batch.length === 0) {
                resolve();
                return;
            }

            Promise.all(batch.map(callback))
                .then(() => {
                    processed += batch.length;
                    updateProcessingProgress((processed / totalFiles) * 100, processed, totalFiles);
                    setTimeout(processNextBatch, 0); // Allow UI to refresh
                });
        }

        processNextBatch();
    });
}

// Add these functions to your app.js file

// Helper function to click a hidden element
function clickElement(selector) {
    const element = document.querySelector(selector);
    if (element) {
        element.click();
    } else {
        console.error(`Element not found: ${selector}`);
    }
}

// Check if browser supports folder upload
function checkFolderUploadSupport() {
    const input = document.createElement('input');
    input.type = 'file';
    return 'webkitdirectory' in input || 'directory' in input;
}

// Process files in batches to avoid UI freezing
async function processFilesInBatches(files, batchSize, processor) {
    const totalFiles = files.length;
    let processed = 0;

    for (let i = 0; i < totalFiles; i += batchSize) {
        const batch = files.slice(i, i + batchSize);

        // Process batch
        await Promise.all(batch.map(processor));

        processed += batch.length;

        // Report progress
        const progress = Math.round((processed / totalFiles) * 100);
        console.log(`Processed ${processed}/${totalFiles} files (${progress}%)`);

        // Allow UI to refresh
        await new Promise(resolve => setTimeout(resolve, 0));
    }
}

// Memory management helper
function releaseMemory() {
    // In JavaScript, we can suggest garbage collection by:
    // 1. Removing references to large objects
    // 2. Setting them to null
    // 3. Create some memory pressure

    // This is just a hint to browsers that might be struggling
    if (window.gc) {
        // Available in some browsers when launched with specific flags
        window.gc();
    } else {
        // Try to create memory pressure to trigger GC
        const pressure = [];
        try {
            // Create some temporary memory pressure
            for (let i = 0; i < 10000; i++) {
                pressure.push(new ArrayBuffer(1024 * 10)); // 10KB each
            }
        } catch (e) {
            // Ignore - we're just trying to trigger GC
        }
        // Clear the array to release the memory
        pressure.length = 0;
    }
}

// Filter function to check if a file is supported (XML or JSON)
function isSupportedFile(file) {
    const name = file.name.toLowerCase();
    return name.endsWith('.xml') || name.endsWith('.json');
}

// Legacy function name for backward compatibility
function isXmlFile(file) {
    return isSupportedFile(file);
}

// Read a file from disk as an ArrayBuffer (works better for binary files)
function readFileAsArrayBuffer(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result);
        reader.onerror = reject;
        reader.readAsArrayBuffer(file);
    });
}

// Read a file as text (better for XML files)
function readFileAsText(file) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(reader.result);
        reader.onerror = reject;
        reader.readAsText(file);
    });
}

// Helper to extract folder structure information
function extractFolderInfo(files) {
    const folders = {};

    // Group files by their folder
    for (const file of files) {
        const path = file.webkitRelativePath || file.name;
        const folderPath = path.split('/').slice(0, -1).join('/');

        if (!folders[folderPath]) {
            folders[folderPath] = [];
        }

        folders[folderPath].push(file);
    }

    return folders;
}

// Calculate folder statistics
function getFolderStats(files) {
    const folderInfo = extractFolderInfo(files);
    const stats = {
        totalFolders: Object.keys(folderInfo).length,
        totalFiles: files.length,
        folderBreakdown: []
    };

    // Generate breakdown by folder
    for (const [folder, folderFiles] of Object.entries(folderInfo)) {
        stats.folderBreakdown.push({
            path: folder || 'Root',
            fileCount: folderFiles.length,
            xmlCount: folderFiles.filter(f => isXmlFile(f)).length,
            totalSize: folderFiles.reduce((sum, f) => sum + f.size, 0)
        });
    }

    return stats;
}

// Handle memory issues by compressing large XML files
async function compressXmlIfNeeded(xmlContent, threshold = 1024 * 1024) {
    // If the XML is large, we can strip whitespace to reduce size
    if (xmlContent.length > threshold) {
        try {
            const parser = new DOMParser();
            const xmlDoc = parser.parseFromString(xmlContent, "text/xml");

            // Remove whitespace text nodes
            const removeWhitespaceNodes = (node) => {
                if (node.nodeType === Node.TEXT_NODE) {
                    if (node.nodeValue.trim() === '') {
                        node.nodeValue = '';
                    }
                }

                const children = node.childNodes;
                for (let i = 0; i < children.length; i++) {
                    removeWhitespaceNodes(children[i]);
                }
            };

            removeWhitespaceNodes(xmlDoc);

            // Serialize back to string
            const serializer = new XMLSerializer();
            return serializer.serializeToString(xmlDoc);
        } catch (e) {
            console.warn('XML compression failed, using original', e);
            return xmlContent;
        }
    }

    return xmlContent;
}

// Add these functions to your app.js file

/**
 * Open a native folder picker dialog and return the selected path
 * Note: This requires server-side implementation to work properly
 */
async function browseFolder(title) {
    try {
        // First, check if we're in a desktop environment where we can select folders
        const isDesktopEnvironment = await window.isDesktopEnvironment?.();

        if (isDesktopEnvironment) {
            // Call C# method through JS interop to open native folder dialog
            return await DotNet.invokeMethodAsync('ComparisonTool.Web', 'BrowseFolderAsync', title);
        } else {
            // In web environment, create a clearer message
            alert("Folder selection is only available in desktop mode.\n\nPlease manually enter the folder path or use file upload mode.");
            return null;
        }
    } catch (e) {
        console.error("Error browsing for folder:", e);
        return null;
    }
}

/**
 * Shows a modal dialog for entering a folder path
 */
function showFolderPathDialog(title, defaultPath) {
    return new Promise((resolve) => {
        // Create modal if it doesn't exist
        let modal = document.getElementById('folderPathModal');
        if (!modal) {
            modal = document.createElement('div');
            modal.id = 'folderPathModal';
            modal.className = 'modal fade';
            modal.setAttribute('tabindex', '-1');
            modal.innerHTML = `
                <div class="modal-dialog">
                    <div class="modal-content">
                        <div class="modal-header">
                            <h5 class="modal-title"></h5>
                            <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
                        </div>
                        <div class="modal-body">
                            <div class="form-group">
                                <label>Enter folder path:</label>
                                <input type="text" id="folderPathInput" class="form-control" />
                            </div>
                        </div>
                        <div class="modal-footer">
                            <button type="button" class="btn btn-secondary" data-bs-dismiss="modal">Cancel</button>
                            <button type="button" class="btn btn-primary" id="confirmFolderPath">Confirm</button>
                        </div>
                    </div>
                </div>
            `;
            document.body.appendChild(modal);

            // Initialize Bootstrap modal
            modal = new bootstrap.Modal(modal);

            // Handle confirm button
            document.getElementById('confirmFolderPath').addEventListener('click', () => {
                const path = document.getElementById('folderPathInput').value;
                modal.hide();
                resolve(path);
            });

            // Handle dismiss
            document.getElementById('folderPathModal').addEventListener('hidden.bs.modal', () => {
                resolve(null);
            });
        }

        // Set title and default path
        document.querySelector('#folderPathModal .modal-title').textContent = title || 'Enter Folder Path';
        document.getElementById('folderPathInput').value = defaultPath || '';

        // Show modal
        modal.show();
    });
}

/**
 * Process folder contents for batched comparison
 */
async function processFolderContents(files, processor, batchSize = 50) {
    // Group files by folder
    const folders = {};
    for (const file of files) {
        const path = file.webkitRelativePath || file.name;
        const folderPath = path.split('/').slice(0, -1).join('/') || 'root';

        if (!folders[folderPath]) {
            folders[folderPath] = [];
        }

        folders[folderPath].push(file);
    }

    // Process each folder
    const folderNames = Object.keys(folders);
    for (let i = 0; i < folderNames.length; i++) {
        const folderName = folderNames[i];
        const folderFiles = folders[folderName];

        // Process this folder's files in batches
        for (let j = 0; j < folderFiles.length; j += batchSize) {
            const batch = folderFiles.slice(j, j + batchSize);
            await Promise.all(batch.map(processor));

            // Allow UI to refresh
            await new Promise(resolve => setTimeout(resolve, 0));
        }

        // Report progress
        console.log(`Processed folder ${i + 1}/${folderNames.length}: ${folderName} with ${folderFiles.length} files`);
    }
}

/**
 * Estimate memory usage for a set of files
 */
function estimateMemoryUsage(files) {
    let totalSize = 0;
    let supportedFileCount = 0;

    for (const file of files) {
        if (isSupportedFile(file)) {
            totalSize += file.size;
            supportedFileCount++;
        }
    }

    // Add some overhead for processing
    const estimatedMemoryMB = (totalSize * 2.5) / (1024 * 1024);

    return {
        totalSizeMB: totalSize / (1024 * 1024),
        estimatedMemoryMB: estimatedMemoryMB,
        xmlCount: xmlCount,
        isLarge: estimatedMemoryMB > 500 // Flag if more than 500MB estimated
    };
}

// Batch upload folder files to backend using fetch and notify Blazor of progress/errors via DotNetObjectRef
window.uploadFolderInBatches = async function (inputId, batchSize, dotNetRef) {
    console.log('uploadFolderInBatches called with', inputId, batchSize, dotNetRef);
    const input = document.getElementById(inputId);
    console.log('Resolved input:', input);
    if (!input || !input.files) {
        alert('Could not find folder input element.');
        return;
    }
    
    // Filter for supported files (XML and JSON)
    const files = Array.from(input.files).filter(f => isSupportedFile(f));
    const totalFiles = files.length;
    let uploaded = 0;
    let uploadedFileNames = [];
    
    // Use smaller batch sizes for very large folder uploads to avoid memory issues
    const adjustedBatchSize = totalFiles > 500 ? Math.min(batchSize, 10) : batchSize;
    
    // Process in batches to avoid overloading the server
    for (let i = 0; i < totalFiles; i += adjustedBatchSize) {
        const batch = files.slice(i, i + adjustedBatchSize);
        const form = new FormData();
        
        for (const file of batch) {
            // Use webkitRelativePath to preserve folder structure
            form.append('files', file, file.webkitRelativePath || file.name);
        }
        
        try {
            const response = await fetch('/api/upload/batch', {
                method: 'POST',
                body: form
            });
            
            if (!response.ok) {
                const err = await response.text();
                console.log('Batch upload error:', err);
                if (dotNetRef) dotNetRef.invokeMethodAsync('OnBatchUploadError', err);
                break;
            }
            
            // Parse backend response
            const result = await response.json();
            
            // Handle large file sets with the new approach
            if (result.batchId) {
                // For large file sets, the server returns a batch ID instead of the file list
                // Store the batch ID for later reference
                if (!uploadedFileNames.includes(result.batchId)) {
                    uploadedFileNames.push(result.batchId);
                }
            } else if (result && result.files) {
                // For smaller sets, collect the files directly
                uploadedFileNames = uploadedFileNames.concat(result.files);
            }
            
            // Add a small delay between batches to prevent server overload
            await new Promise(resolve => setTimeout(resolve, 50));
        } catch (e) {
            console.log('Batch upload exception:', e);
            if (dotNetRef) dotNetRef.invokeMethodAsync('OnBatchUploadError', e.toString());
            break;
        }
        
        uploaded += batch.length;
        console.log('Batch uploaded:', uploaded, '/', totalFiles);
        if (dotNetRef) dotNetRef.invokeMethodAsync('OnBatchUploadProgress', uploaded, totalFiles);
    }
    
    // For large uploads, we might need to fetch the file list in a separate request
    // to avoid memory issues in the initial response
    if (uploadedFileNames.length === 1 && uploadedFileNames[0].length === 8) {
        // This looks like a batch ID, not a file path
        const batchId = uploadedFileNames[0];
        try {
            const response = await fetch(`/api/upload/batch/${batchId}`);
            if (response.ok) {
                const result = await response.json();
                if (result && result.files) {
                    uploadedFileNames = result.files;
                }
            }
        } catch (e) {
            console.error('Error fetching file list:', e);
        }
    }
    
    // Send result back to Blazor
    const uploadResult = JSON.stringify({ 
        uploaded: uploadedFileNames.length, 
        files: uploadedFileNames
    });
    
    console.log('Upload complete:', uploaded, '/', totalFiles);
    if (dotNetRef) dotNetRef.invokeMethodAsync('OnBatchUploadComplete', uploadResult);
};

// Helper to trigger hidden input click and handle upload after file selection
window.triggerFolderInput = function(inputId, batchSize, dotNetRef) {
    const input = document.getElementById(inputId);
    if (!input) return;
    // Remove any previous event handler to avoid duplicate uploads
    input.onchange = null;
    input.value = '';
    input.onchange = function() {
        window.uploadFolderInBatches(inputId, batchSize, dotNetRef);
    };
    input.click();
};

window.scrollToElement = function(elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        // Use 'start' to align the top of the element with the top of the scrollable ancestor
        element.scrollIntoView({ behavior: 'smooth', block: 'start' });
    }
};