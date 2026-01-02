// SAML Test Page JavaScript

var config = {
    idpEntityId: 'http://localhost:5050',
    ssoEndpoint: 'http://localhost:5050/saml/idp/sso',
    acsUrl: 'http://localhost:5050/test/saml/acs',
    spEntityId: 'http://localhost:5050/test/saml'
};

document.addEventListener('DOMContentLoaded', function() {
    loadConfig();
});

async function loadConfig() {
    try {
        var response = await fetch('/test/saml/config');
        if (response.ok) {
            config = await response.json();
            updateConfigDisplay();
        }
    } catch (e) {
        console.log('Using default config');
        updateConfigDisplay();
    }
}

function updateConfigDisplay() {
    document.getElementById('idpEntityId').textContent = config.idpEntityId;
    document.getElementById('ssoEndpoint').textContent = config.ssoEndpoint;
}

function initiateSso() {
    // Use HTTP-POST binding (no DEFLATE compression required)
    var authnRequest = createAuthnRequest();
    var encoded = btoa(authnRequest);

    // Create a form and submit it via POST
    var form = document.createElement('form');
    form.method = 'POST';
    form.action = config.ssoEndpoint;

    var samlInput = document.createElement('input');
    samlInput.type = 'hidden';
    samlInput.name = 'SAMLRequest';
    samlInput.value = encoded;
    form.appendChild(samlInput);

    document.body.appendChild(form);
    form.submit();
}

function createAuthnRequest() {
    var id = '_' + Math.random().toString(36).substring(2, 15);
    var issueInstant = new Date().toISOString();

    return '<?xml version="1.0" encoding="UTF-8"?>' +
        '<samlp:AuthnRequest ' +
            'xmlns:samlp="urn:oasis:names:tc:SAML:2.0:protocol" ' +
            'xmlns:saml="urn:oasis:names:tc:SAML:2.0:assertion" ' +
            'ID="' + id + '" ' +
            'Version="2.0" ' +
            'IssueInstant="' + issueInstant + '" ' +
            'AssertionConsumerServiceURL="' + config.acsUrl + '" ' +
            'Destination="' + config.ssoEndpoint + '" ' +
            'ProtocolBinding="urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST">' +
            '<saml:Issuer>' + config.spEntityId + '</saml:Issuer>' +
            '<samlp:NameIDPolicy ' +
                'Format="urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress" ' +
                'AllowCreate="true"/>' +
        '</samlp:AuthnRequest>';
}

function viewMetadata() {
    window.open('/saml/idp/metadata', '_blank');
}
