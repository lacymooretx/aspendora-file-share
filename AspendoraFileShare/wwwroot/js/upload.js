// File Upload functionality for Aspendora File Share
// Handles chunked uploads up to 50GB

window.fileUploadInterop = {
    CHUNK_SIZE: 50 * 1024 * 1024, // 50MB chunks
    MAX_FILE_SIZE: 50 * 1024 * 1024 * 1024, // 50GB max per file

    selectedFiles: [],
    dotNetRef: null,

    // Initialize the upload component
    initialize: function (dotNetReference, dropZoneId, fileInputId) {
        console.log('FileUpload: Initializing with', dropZoneId, fileInputId);
        this.dotNetRef = dotNetReference;
        this.selectedFiles = [];

        const dropZone = document.getElementById(dropZoneId);
        const fileInput = document.getElementById(fileInputId);

        console.log('FileUpload: Found elements', { dropZone: !!dropZone, fileInput: !!fileInput });

        if (!dropZone || !fileInput) {
            console.error('Drop zone or file input not found:', dropZoneId, fileInputId);
            return;
        }

        console.log('FileUpload: Setting up event listeners');

        // Store reference to this for event handlers
        const self = this;

        // Drag and drop handlers
        dropZone.addEventListener('dragover', (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropZone.classList.add('drag-over');
            console.log('FileUpload: dragover');
        });

        dropZone.addEventListener('dragleave', (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropZone.classList.remove('drag-over');
            console.log('FileUpload: dragleave');
        });

        dropZone.addEventListener('drop', (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropZone.classList.remove('drag-over');
            console.log('FileUpload: drop', e.dataTransfer.files);

            const files = Array.from(e.dataTransfer.files);
            self.addFiles(files);
        });

        // Click to browse
        dropZone.addEventListener('click', () => {
            console.log('FileUpload: click');
            fileInput.click();
        });

        // File input change
        fileInput.addEventListener('change', (e) => {
            console.log('FileUpload: file input changed', e.target.files);
            const files = Array.from(e.target.files);
            self.addFiles(files);
            fileInput.value = ''; // Reset input
        });

        console.log('FileUpload: Initialization complete');
    },

    // Add files to the selection
    addFiles: function (files) {
        console.log('FileUpload: addFiles called with', files.length, 'files');
        for (const file of files) {
            console.log('FileUpload: Processing file:', file.name, file.size);
            // Check if file already added
            if (this.selectedFiles.some(f => f.name === file.name && f.size === file.size)) {
                console.log('FileUpload: File already added, skipping');
                continue;
            }

            // Validate file size
            if (file.size > this.MAX_FILE_SIZE) {
                console.log('FileUpload: File too large');
                window.clipboardInterop?.showToast(`${file.name} exceeds 50GB limit`, 'error');
                continue;
            }

            this.selectedFiles.push(file);
            console.log('FileUpload: File added, total:', this.selectedFiles.length);
        }

        this.updateFileList();
    },

    // Remove a file from selection
    removeFile: function (index) {
        this.selectedFiles.splice(index, 1);
        this.updateFileList();
    },

    // Update the file list in Blazor
    updateFileList: function () {
        console.log('FileUpload: updateFileList called');
        const fileData = this.selectedFiles.map((f, i) => ({
            index: i,
            name: f.name,
            size: f.size,
            type: f.type || 'application/octet-stream'
        }));

        console.log('FileUpload: Calling Blazor with fileData', fileData);
        if (this.dotNetRef) {
            this.dotNetRef.invokeMethodAsync('OnFilesSelected', fileData)
                .then(() => console.log('FileUpload: Blazor callback succeeded'))
                .catch(err => console.error('FileUpload: Blazor callback failed', err));
        } else {
            console.error('FileUpload: dotNetRef is null!');
        }
    },

    // Clear all files
    clearFiles: function () {
        this.selectedFiles = [];
        this.updateFileList();
    },

    // Get total size of selected files
    getTotalSize: function () {
        return this.selectedFiles.reduce((sum, f) => sum + f.size, 0);
    },

    // Upload all files with chunking
    uploadFiles: async function () {
        if (this.selectedFiles.length === 0) {
            return { success: false, error: 'No files selected' };
        }

        try {
            // Step 1: Initiate upload
            const fileMetadata = this.selectedFiles.map(f => {
                console.log(`FileUpload: Preparing file ${f.name}, size=${f.size} bytes (${f.size / 1024 / 1024 / 1024} GB), expected parts=${Math.ceil(f.size / this.CHUNK_SIZE)}`);
                return {
                    fileName: f.name,
                    fileSize: f.size,
                    mimeType: f.type || 'application/octet-stream'
                };
            });

            console.log('FileUpload: Sending initiate request with:', JSON.stringify(fileMetadata));

            const initiateResponse = await fetch('/api/upload/initiate', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({ files: fileMetadata })
            });

            if (!initiateResponse.ok) {
                const error = await initiateResponse.json();
                throw new Error(error.error || 'Failed to initiate upload');
            }

            const initData = await initiateResponse.json();
            console.log('FileUpload: Server returned:', JSON.stringify(initData.uploads.map(u => ({
                fileName: u.fileName,
                fileSize: u.fileSize,
                totalParts: u.totalParts,
                presignedUrlCount: u.presignedUrls?.length
            }))));
            const { shareId, shareLinkId, uploads } = initData;

            // Step 2: Upload chunks (direct to S3 with presigned URLs, or via server as fallback)
            const uploadResults = [];
            const totalChunks = this.selectedFiles.reduce((sum, f) =>
                sum + Math.ceil(f.size / this.CHUNK_SIZE), 0);
            let completedChunks = 0;
            const CONCURRENT_UPLOADS = 10; // 10 parallel uploads for speed

            for (let i = 0; i < this.selectedFiles.length; i++) {
                const file = this.selectedFiles[i];
                const uploadSession = uploads[i];
                const totalParts = uploadSession.totalParts || Math.ceil(file.size / this.CHUNK_SIZE);
                const parts = new Array(totalParts);
                const presignedUrls = uploadSession.presignedUrls;
                const useDirectUpload = presignedUrls && presignedUrls.length > 0;

                console.log(`FileUpload: Using ${useDirectUpload ? 'direct S3' : 'server proxy'} upload for ${file.name}`);

                // Create all chunk upload tasks
                const chunkTasks = [];
                for (let partNumber = 1; partNumber <= totalParts; partNumber++) {
                    chunkTasks.push({
                        partNumber,
                        file,
                        uploadSession,
                        presignedUrl: useDirectUpload ? presignedUrls[partNumber - 1] : null
                    });
                }

                // Process chunks in parallel batches
                for (let batch = 0; batch < chunkTasks.length; batch += CONCURRENT_UPLOADS) {
                    const batchTasks = chunkTasks.slice(batch, batch + CONCURRENT_UPLOADS);

                    const batchPromises = batchTasks.map(async (task) => {
                        const start = (task.partNumber - 1) * this.CHUNK_SIZE;
                        const end = Math.min(start + this.CHUNK_SIZE, task.file.size);
                        const chunk = task.file.slice(start, end);

                        let etag;

                        if (task.presignedUrl) {
                            // Direct upload to S3 using presigned URL with retry
                            const MAX_RETRIES = 3;
                            let lastError = null;

                            for (let retry = 0; retry < MAX_RETRIES; retry++) {
                                if (retry > 0) {
                                    console.log(`FileUpload: Retrying chunk ${task.partNumber} (attempt ${retry + 1})`);
                                    await new Promise(r => setTimeout(r, 1000 * retry)); // Exponential backoff
                                }

                                try {
                                    const chunkResponse = await fetch(task.presignedUrl, {
                                        method: 'PUT',
                                        body: chunk
                                    });

                                    if (!chunkResponse.ok) {
                                        const errorText = await chunkResponse.text();
                                        console.error(`FileUpload: Chunk ${task.partNumber} failed:`, chunkResponse.status, errorText);
                                        lastError = new Error(`Failed to upload chunk ${task.partNumber} of ${task.file.name}: ${chunkResponse.status}`);
                                        continue; // Retry
                                    }

                                    // Get ETag from S3 response headers (try both cases)
                                    etag = chunkResponse.headers.get('ETag') || chunkResponse.headers.get('etag');

                                    // Debug: log all response headers
                                    console.log(`FileUpload: Chunk ${task.partNumber} headers:`, [...chunkResponse.headers.entries()]);

                                    if (!etag) {
                                        console.warn(`FileUpload: No ETag for chunk ${task.partNumber}, retrying...`);
                                        lastError = new Error(`No ETag received for chunk ${task.partNumber}`);
                                        continue; // Retry
                                    }

                                    etag = etag.replace(/"/g, '');
                                    console.log(`FileUpload: Chunk ${task.partNumber} completed with ETag: ${etag}`);
                                    lastError = null;
                                    break; // Success, exit retry loop
                                } catch (fetchError) {
                                    console.error(`FileUpload: Chunk ${task.partNumber} fetch error:`, fetchError);
                                    lastError = fetchError;
                                }
                            }

                            if (lastError) {
                                throw lastError;
                            }
                        } else {
                            // Fallback: Upload via server
                            const formData = new FormData();
                            formData.append('chunk', chunk);
                            formData.append('key', task.uploadSession.key);
                            formData.append('uploadId', task.uploadSession.uploadId);
                            formData.append('partNumber', task.partNumber.toString());

                            const chunkResponse = await fetch('/api/upload/chunk', {
                                method: 'POST',
                                body: formData
                            });

                            if (!chunkResponse.ok) {
                                const error = await chunkResponse.json();
                                throw new Error(error.error || `Failed to upload chunk ${task.partNumber} of ${task.file.name}`);
                            }

                            const chunkData = await chunkResponse.json();
                            etag = chunkData.etag;
                        }

                        parts[task.partNumber - 1] = {
                            partNumber: task.partNumber,
                            etag: etag
                        };

                        // Update progress
                        completedChunks++;
                        const totalProgress = Math.round((completedChunks / totalChunks) * 100);

                        if (this.dotNetRef) {
                            this.dotNetRef.invokeMethodAsync('OnUploadProgress', totalProgress, i, task.file.name);
                        }

                        return { etag };
                    });

                    // Wait for this batch to complete before starting next
                    await Promise.all(batchPromises);
                }

                uploadResults.push({
                    key: uploadSession.key,
                    uploadId: uploadSession.uploadId,
                    parts: parts,
                    fileName: file.name,
                    fileSize: file.size,
                    mimeType: file.type || 'application/octet-stream'
                });
            }

            // Step 3: Complete upload
            console.log('FileUpload: Completing upload with results:', JSON.stringify(uploadResults, null, 2));

            // Verify all parts have ETags
            for (const upload of uploadResults) {
                const missingParts = upload.parts.filter(p => !p || !p.etag);
                if (missingParts.length > 0) {
                    console.error('FileUpload: Missing ETags for parts:', missingParts);
                    throw new Error(`Missing ETags for ${missingParts.length} parts in ${upload.fileName}`);
                }
            }

            const completeResponse = await fetch('/api/upload/complete', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    shareLinkId: shareLinkId,
                    uploads: uploadResults
                })
            });

            if (!completeResponse.ok) {
                const error = await completeResponse.json();
                throw new Error(error.error || 'Failed to complete upload');
            }

            // Clear files after successful upload
            this.selectedFiles = [];

            return {
                success: true,
                shareId: shareId,
                shortId: initData.shortId
            };

        } catch (error) {
            console.error('Upload error:', error);
            return {
                success: false,
                error: error.message
            };
        }
    },

    // Format bytes for display
    formatBytes: function (bytes) {
        if (bytes === 0) return '0 Bytes';
        const k = 1024;
        const sizes = ['Bytes', 'KB', 'MB', 'GB', 'TB'];
        const i = Math.floor(Math.log(bytes) / Math.log(k));
        return parseFloat((bytes / Math.pow(k, i)).toFixed(2)) + ' ' + sizes[i];
    }
};
