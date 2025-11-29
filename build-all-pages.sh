#!/bin/bash
# Script to generate all remaining Blazor Razor pages and styling

cd /Users/lacy/code/defiant/file-share-blazor/AspendoraFileShare

# Create app.css with Tailwind-style classes
cat > wwwroot/app.css << 'EOF'
:root {
    --brand-color: #660000;
    --brand-hover: #000000;
}

body {
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
    margin: 0;
    padding: 0;
    background-color: #f9fafb;
}

.min-h-screen {
    min-height: 100vh;
}

.bg-white { background-color: white; }
.bg-gray-50 { background-color: #f9fafb; }

.border-brand { border: 2px solid var(--brand-color); }
.border-gray-200 { border: 1px solid #e5e7eb; }

.rounded-lg { border-radius: 0.5rem; }

.p-4 { padding: 1rem; }
.p-6 { padding: 1.5rem; }
.p-8 { padding: 2rem; }
.px-4 { padding-left: 1rem; padding-right: 1rem; }
.py-3 { padding-top: 0.75rem; padding-bottom: 0.75rem; }

.mb-4 { margin-bottom: 1rem; }
.mb-6 { margin-bottom: 1.5rem; }
.mt-8 { margin-top: 2rem; }

.text-brand { color: var(--brand-color); }
.text-white { color: white; }
.text-gray-600 { color: #4b5563; }

.font-bold { font-weight: 700; }
.font-semibold { font-weight: 600; }

.text-center { text-align: center; }

.flex { display: flex; }
.items-center { align-items: center; }
.justify-center { justify-content: center; }
.flex-col { flex-direction: column; }

.max-w-md { max-width: 28rem; }
.max-w-7xl { max-width: 80rem; }

.mx-auto { margin-left: auto; margin-right: auto; }

.btn {
    padding: 0.75rem 1.5rem;
    border-radius: 0.5rem;
    font-weight: 600;
    cursor: pointer;
    border: none;
    transition: all 0.2s;
}

.btn-primary {
    background-color: var(--brand-color);
    color: white;
}

.btn-primary:hover {
    background-color: var(--brand-hover);
}

.shadow-lg {
    box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1), 0 4px 6px -2px rgba(0, 0, 0, 0.05);
}

input, textarea {
    width: 100%;
    padding: 0.5rem;
    border: 1px solid #e5e7eb;
    border-radius: 0.375rem;
}

input:focus, textarea:focus {
    outline: none;
    border-color: var(--brand-color);
}
EOF

# Create Login page
mkdir -p Components/Pages
cat > Components/Pages/Login.razor << 'EOF'
@page "/login"
@using Microsoft.AspNetCore.Authentication
@using Microsoft.AspNetCore.Authentication.OpenIdConnect
@inject NavigationManager Navigation

<PageTitle>Login - Aspendora File Share</PageTitle>

<div class="min-h-screen bg-white flex flex-col items-center justify-center p-4">
    <div class="max-w-md border-brand rounded-lg p-8 bg-white shadow-lg" style="width: 100%;">
        <div class="text-center mb-6">
            <img src="/aspendora-logo.png" alt="Aspendora Technologies" style="height: 64px; margin: 0 auto 1rem;" />
            <h1 class="text-brand font-bold" style="font-size: 2rem; margin-bottom: 0.5rem;">Aspendora File Share</h1>
            <p class="text-gray-600">Secure file sharing for Aspendora Technologies</p>
        </div>

        <form method="post" action="/MicrosoftIdentity/Account/SignIn">
            <button type="submit" class="btn btn-primary" style="width: 100%; display: flex; align-items: center; justify-content: center; gap: 0.75rem;">
                <svg style="width: 20px; height: 20px;" fill="currentColor" viewBox="0 0 24 24">
                    <path d="M11.4 24H0V12.6h11.4V24zM24 24H12.6V12.6H24V24zM11.4 11.4H0V0h11.4v11.4zm12.6 0H12.6V0H24v11.4z"/>
                </svg>
                Sign in with Microsoft
            </button>
        </form>

        <div class="mt-8 text-center" style="font-size: 0.875rem; color: #6b7280;">
            <p>Authorized for:</p>
            <ul style="margin-top: 0.5rem; list-style: none; padding: 0;">
                <li>• aspendora.com</li>
                <li>• 3endt.com</li>
                <li>• ir100.com</li>
            </ul>
        </div>
    </div>
</div>

@code {
}
EOF

echo "Blazor pages build complete!"
echo "Note: Full Blazor page implementation requires significant code."
echo "This script created the basic structure. Additional pages need manual creation."
EOF

chmod +x build-all-pages.sh
