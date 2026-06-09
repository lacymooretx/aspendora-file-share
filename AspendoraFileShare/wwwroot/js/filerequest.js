// Anonymous upload flow for Aspendora File Share "Request Files" pages.
// Mirrors upload.js but posts to /api/filerequest/{shortId}/* and carries the
// submitter's name/email. Used by the public /r/{shortId} upload page.

window.fileRequestInterop = {
    CHUNK_SIZE: 50 * 1024 * 1024, // 50MB chunks - must match FileRequestController
    MAX_FILE_SIZE: 50 * 1024 * 1024 * 1024, // 50GB max per file

    selectedFiles: [],
    dotNetRef: null,
    shortId: null,

    initialize: function (dotNetReference, dropZoneId, fileInputId, shortId) {
        this.dotNetRef = dotNetReference;
        this.selectedFiles = [];
        this.shortId = shortId;

        const dropZone = document.getElementById(dropZoneId);
        const fileInput = document.getElementById(fileInputId);

        if (!dropZone || !fileInput) {
            console.error('FileRequest: drop zone or file input not found:', dropZoneId, fileInputId);
            return;
        }

        const self = this;

        dropZone.addEventListener('dragover', (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropZone.classList.add('drag-over');
        });

        dropZone.addEventListener('dragleave', (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropZone.classList.remove('drag-over');
        });

        dropZone.addEventListener('drop', (e) => {
            e.preventDefault();
            e.stopPropagation();
            dropZone.classList.remove('drag-over');
            self.addFiles(Array.from(e.dataTransfer.files));
        });

        dropZone.addEventListener('click', () => fileInput.click());

        fileInput.addEventListener('change', (e) => {
            self.addFiles(Array.from(e.target.files));
            fileInput.value = '';
        });
    },

    addFiles: function (files) {
        for (const file of files) {
            if (this.selectedFiles.some(f => f.name === file.name && f.size === file.size)) {
                continue;
            }
            if (file.size > this.MAX_FILE_SIZE) {
                window.clipboardInterop?.showToast(`${file.name} exceeds 50GB limit`, 'error');
                continue;
            }
            this.selectedFiles.push(file);
        }
        this.updateFileList();
    },

    removeFile: function (index) {
        this.selectedFiles.splice(index, 1);
        this.updateFileList();
    },

    updateFileList: function () {
        const fileData = this.selectedFiles.map((f, i) => ({
            index: i,
            name: f.name,
            size: f.size,
            type: f.type || 'application/octet-stream'
        }));
        if (this.dotNetRef) {
            this.dotNetRef.invokeMethodAsync('OnFilesSelected', fileData)
                .catch(err => console.error('FileRequest: Blazor callback failed', err));
        }
    },

    clearFiles: function () {
        this.selectedFiles = [];
        this.updateFileList();
    },

    uploadFiles: async function (submitterName, submitterEmail) {
        if (this.selectedFiles.length === 0) {
            return { success: false, error: 'No files selected' };
        }

        try {
            const fileMetadata = this.selectedFiles.map(f => ({
                fileName: f.name,
                fileSize: f.size,
                mimeType: f.type || 'application/octet-stream'
            }));

            const initiateResponse = await fetch(`/api/filerequest/${this.shortId}/initiate`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    submitterName: submitterName || null,
                    submitterEmail: submitterEmail || null,
                    files: fileMetadata
                })
            });

            if (!initiateResponse.ok) {
                const error = await initiateResponse.json().catch(() => ({}));
                throw new Error(error.error || 'Failed to start upload');
            }

            const initData = await initiateResponse.json();
            const { submissionShortId, uploads } = initData;

            const uploadResults = [];
            const totalChunks = this.selectedFiles.reduce((sum, f) =>
                sum + Math.ceil(f.size / this.CHUNK_SIZE), 0);
            let completedChunks = 0;
            const CONCURRENT_UPLOADS = 10;

            for (let i = 0; i < this.selectedFiles.length; i++) {
                const file = this.selectedFiles[i];
                const uploadSession = uploads[i];
                const totalParts = uploadSession.totalParts || Math.ceil(file.size / this.CHUNK_SIZE);
                const parts = new Array(totalParts);
                const presignedUrls = uploadSession.presignedUrls;
                const useDirectUpload = presignedUrls && presignedUrls.length > 0;

                const chunkTasks = [];
                for (let partNumber = 1; partNumber <= totalParts; partNumber++) {
                    chunkTasks.push({
                        partNumber,
                        file,
                        uploadSession,
                        presignedUrl: useDirectUpload ? presignedUrls[partNumber - 1] : null
                    });
                }

                for (let batch = 0; batch < chunkTasks.length; batch += CONCURRENT_UPLOADS) {
                    const batchTasks = chunkTasks.slice(batch, batch + CONCURRENT_UPLOADS);

                    const batchPromises = batchTasks.map(async (task) => {
                        const start = (task.partNumber - 1) * this.CHUNK_SIZE;
                        const end = Math.min(start + this.CHUNK_SIZE, task.file.size);
                        const chunk = task.file.slice(start, end);

                        let etag;

                        if (task.presignedUrl) {
                            const MAX_RETRIES = 3;
                            let lastError = null;

                            for (let retry = 0; retry < MAX_RETRIES; retry++) {
                                if (retry > 0) {
                                    await new Promise(r => setTimeout(r, 1000 * retry));
                                }
                                try {
                                    const chunkResponse = await fetch(task.presignedUrl, {
                                        method: 'PUT',
                                        body: chunk
                                    });
                                    if (!chunkResponse.ok) {
                                        lastError = new Error(`Failed to upload chunk ${task.partNumber}: ${chunkResponse.status}`);
                                        continue;
                                    }
                                    etag = chunkResponse.headers.get('ETag') || chunkResponse.headers.get('etag');
                                    if (!etag) {
                                        lastError = new Error(`No ETag received for chunk ${task.partNumber}`);
                                        continue;
                                    }
                                    etag = etag.replace(/"/g, '');
                                    lastError = null;
                                    break;
                                } catch (fetchError) {
                                    lastError = fetchError;
                                }
                            }
                            if (lastError) {
                                throw lastError;
                            }
                        } else {
                            const formData = new FormData();
                            formData.append('chunk', chunk);
                            formData.append('key', task.uploadSession.key);
                            formData.append('uploadId', task.uploadSession.uploadId);
                            formData.append('partNumber', task.partNumber.toString());

                            const chunkResponse = await fetch('/api/filerequest/chunk', {
                                method: 'POST',
                                body: formData
                            });
                            if (!chunkResponse.ok) {
                                const error = await chunkResponse.json().catch(() => ({}));
                                throw new Error(error.error || `Failed to upload chunk ${task.partNumber}`);
                            }
                            const chunkData = await chunkResponse.json();
                            etag = chunkData.etag;
                        }

                        parts[task.partNumber - 1] = { partNumber: task.partNumber, etag };

                        completedChunks++;
                        const totalProgress = Math.round((completedChunks / totalChunks) * 100);
                        if (this.dotNetRef) {
                            this.dotNetRef.invokeMethodAsync('OnUploadProgress', totalProgress, i, task.file.name);
                        }
                        return { etag };
                    });

                    await Promise.all(batchPromises);
                }

                uploadResults.push({
                    key: uploadSession.key,
                    uploadId: uploadSession.uploadId,
                    parts,
                    fileName: file.name,
                    fileSize: file.size,
                    mimeType: file.type || 'application/octet-stream'
                });
            }

            for (const upload of uploadResults) {
                const missingParts = upload.parts.filter(p => !p || !p.etag);
                if (missingParts.length > 0) {
                    throw new Error(`Missing ETags for ${missingParts.length} parts in ${upload.fileName}`);
                }
            }

            const completeResponse = await fetch(`/api/filerequest/${this.shortId}/complete`, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    submissionShortId: submissionShortId,
                    uploads: uploadResults
                })
            });

            if (!completeResponse.ok) {
                const error = await completeResponse.json().catch(() => ({}));
                throw new Error(error.error || 'Failed to finalize upload');
            }

            this.selectedFiles = [];
            return { success: true, submissionShortId: submissionShortId };
        } catch (error) {
            console.error('FileRequest upload error:', error);
            return { success: false, error: error.message };
        }
    }
};

// Authenticated helpers for the owner-side "Request Files" modal (uses cookies).
window.apiInterop = window.apiInterop || {};

window.apiInterop.createFileRequest = async function (title, message, recipientEmail, recipientName) {
    try {
        const response = await fetch('/api/filerequest/create', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include',
            body: JSON.stringify({ title, message, recipientEmail, recipientName })
        });
        const data = await response.json().catch(() => ({}));
        if (response.ok) {
            return { success: true, shortId: data.shortId };
        }
        return { success: false, error: data.error || 'Failed to create request' };
    } catch (err) {
        console.error('Error creating file request:', err);
        return { success: false, error: err.message || 'Network error' };
    }
};

window.apiInterop.sendFileRequestInvite = async function (requestShortId, recipientEmail, recipientName, message) {
    try {
        const response = await fetch('/api/filerequest/email', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            credentials: 'include',
            body: JSON.stringify({ requestShortId, recipientEmail, recipientName, message })
        });
        const data = await response.json().catch(() => ({}));
        if (response.ok) {
            return { success: true };
        }
        return { success: false, error: data.error || 'Failed to send invite' };
    } catch (err) {
        console.error('Error sending file request invite:', err);
        return { success: false, error: err.message || 'Network error' };
    }
};
