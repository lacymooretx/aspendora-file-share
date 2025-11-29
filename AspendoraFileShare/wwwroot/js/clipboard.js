// Clipboard functionality for Aspendora File Share

window.clipboardInterop = {
    copyToClipboard: async function (text) {
        try {
            await navigator.clipboard.writeText(text);
            return true;
        } catch (err) {
            // Fallback for older browsers
            const textArea = document.createElement('textarea');
            textArea.value = text;
            textArea.style.position = 'fixed';
            textArea.style.left = '-9999px';
            document.body.appendChild(textArea);
            textArea.select();
            try {
                document.execCommand('copy');
                return true;
            } catch (e) {
                console.error('Failed to copy:', e);
                return false;
            } finally {
                document.body.removeChild(textArea);
            }
        }
    },

    showToast: function (message, type = 'success') {
        // Remove existing toasts
        const existingToast = document.querySelector('.toast-notification');
        if (existingToast) {
            existingToast.remove();
        }

        // Create toast element
        const toast = document.createElement('div');
        toast.className = 'toast-notification fixed bottom-4 right-4 px-6 py-3 rounded-lg shadow-lg text-white z-50';
        toast.style.backgroundColor = type === 'success' ? '#660000' : '#ef4444';
        toast.innerHTML = `
            <div class="flex items-center gap-2">
                ${type === 'success'
                    ? '<svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 13l4 4L19 7"></path></svg>'
                    : '<svg class="w-5 h-5" fill="none" stroke="currentColor" viewBox="0 0 24 24"><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M6 18L18 6M6 6l12 12"></path></svg>'
                }
                <span>${message}</span>
            </div>
        `;
        toast.style.animation = 'slideInRight 0.3s ease';

        document.body.appendChild(toast);

        // Auto remove after 3 seconds
        setTimeout(() => {
            toast.style.animation = 'fadeOut 0.3s ease';
            setTimeout(() => toast.remove(), 300);
        }, 3000);
    }
};

// Add fadeOut keyframes if not present
if (!document.querySelector('#toast-styles')) {
    const style = document.createElement('style');
    style.id = 'toast-styles';
    style.textContent = `
        @keyframes fadeOut {
            from { opacity: 1; transform: translateX(0); }
            to { opacity: 0; transform: translateX(100%); }
        }
    `;
    document.head.appendChild(style);
}

// API helper for authenticated requests (uses browser cookies)
window.apiInterop = {
    sendShareEmail: async function (shareLinkId, recipientEmail, recipientName, message) {
        try {
            const response = await fetch('/api/share/email', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                credentials: 'include', // Include cookies for authentication
                body: JSON.stringify({
                    shareLinkId: shareLinkId,
                    recipientEmail: recipientEmail,
                    recipientName: recipientName,
                    message: message
                })
            });

            const data = await response.json();

            if (response.ok) {
                return { success: true };
            } else {
                return { success: false, error: data.error || 'Failed to send email' };
            }
        } catch (err) {
            console.error('Error sending email:', err);
            return { success: false, error: err.message || 'Network error' };
        }
    }
};
