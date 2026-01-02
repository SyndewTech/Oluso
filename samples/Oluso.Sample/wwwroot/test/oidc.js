// OIDC Test Page JavaScript

var config = {
    issuer: 'http://localhost:5050',
    clientId: 'test-client',
    redirectUri: 'http://localhost:5050/test/oidc/callback'
};

document.addEventListener('DOMContentLoaded', function() {
    loadConfig();
    checkForCallback();
});

async function loadConfig() {
    try {
        var response = await fetch('/test/oidc/config');
        if (response.ok) {
            var data = await response.json();
            config.issuer = data.issuer;
            config.clientId = data.clientId;
            // Use local callback URL for testing
            config.redirectUri = window.location.origin + '/test/oidc/callback';
            updateConfigDisplay();
        }
    } catch (e) {
        console.log('Using default config');
        updateConfigDisplay();
    }
}

function updateConfigDisplay() {
    document.getElementById('issuer').textContent = config.issuer;
}

function startAuth(mode) {
    var state = Math.random().toString(36).substring(2);
    var nonce = Math.random().toString(36).substring(2);

    // Store state for validation on callback
    sessionStorage.setItem('oidc_state', state);
    sessionStorage.setItem('oidc_nonce', nonce);

    var params = new URLSearchParams();
    params.append('client_id', config.clientId);
    params.append('redirect_uri', config.redirectUri);
    params.append('response_type', 'code');
    params.append('scope', 'openid profile email');
    params.append('state', state);
    params.append('nonce', nonce);

    // Add ui_mode parameter
    if (mode === 'journey') {
        params.append('ui_mode', 'journey');

        // Add policy if selected
        var policy = document.getElementById('policySelect').value;
        if (policy) {
            params.append('p', policy);
        }

        // Add prompt if selected
        var prompt = document.getElementById('promptSelect').value;
        if (prompt) {
            params.append('prompt', prompt);
        }
    } else {
        params.append('ui_mode', 'standalone');
    }

    window.location.href = config.issuer + '/connect/authorize?' + params.toString();
}

async function fetchDiscovery() {
    var result = document.getElementById('discoveryResult');
    try {
        var response = await fetch('/.well-known/openid-configuration');
        var data = await response.json();
        result.textContent = JSON.stringify(data, null, 2);
    } catch (e) {
        result.textContent = 'Error: ' + e.message;
    }
}

function checkForCallback() {
    // Check if this is a callback with authorization code
    var urlParams = new URLSearchParams(window.location.search);
    var code = urlParams.get('code');
    var state = urlParams.get('state');
    var error = urlParams.get('error');

    if (error) {
        var errorDesc = urlParams.get('error_description') || 'Unknown error';
        showCallbackResult({
            success: false,
            error: error,
            error_description: errorDesc
        });
        return;
    }

    if (code) {
        // Validate state
        var savedState = sessionStorage.getItem('oidc_state');
        if (state !== savedState) {
            showCallbackResult({
                success: false,
                error: 'state_mismatch',
                error_description: 'State parameter does not match'
            });
            return;
        }

        showCallbackResult({
            success: true,
            code: code,
            state: state
        });
    }
}

function showCallbackResult(result) {
    var html = '';
    if (result.success) {
        html = '<div class="card" style="background: #d4edda; border: 1px solid #c3e6cb;">' +
            '<h3>Authorization Successful!</h3>' +
            '<p><strong>Authorization Code:</strong></p>' +
            '<code style="word-break: break-all;">' + result.code + '</code>' +
            '<p style="margin-top: 15px;"><strong>State:</strong> ' + result.state + '</p>' +
            '<p style="margin-top: 15px; color: #666;">Next step: Exchange this code for tokens at the token endpoint.</p>' +
            '<button onclick="window.location.href=\'/test/oidc.html\'" style="margin-top: 10px;">Start Over</button>' +
            '</div>';
    } else {
        html = '<div class="card" style="background: #f8d7da; border: 1px solid #f5c6cb;">' +
            '<h3>Authorization Failed</h3>' +
            '<p><strong>Error:</strong> ' + result.error + '</p>' +
            '<p><strong>Description:</strong> ' + result.error_description + '</p>' +
            '<button onclick="window.location.href=\'/test/oidc.html\'" style="margin-top: 10px;">Try Again</button>' +
            '</div>';
    }

    // Insert at the top of the body
    var resultDiv = document.createElement('div');
    resultDiv.innerHTML = html;
    document.body.insertBefore(resultDiv, document.body.firstChild.nextSibling.nextSibling);
}
