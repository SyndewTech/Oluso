// LDAP Test Page JavaScript

// Default configuration (will be fetched from server)
var config = {
    host: 'localhost',
    port: 10389,
    baseDn: 'dc=oluso,dc=local',
    adminDn: 'cn=admin,dc=oluso,dc=local'
};

// Initialize page
document.addEventListener('DOMContentLoaded', function() {
    loadConfig();
});

async function loadConfig() {
    try {
        var response = await fetch('/test/ldap/config');
        if (response.ok) {
            var text = await response.text();
            if (text) {
                config = JSON.parse(text);
            }
            updateConfigDisplay();
        } else {
            console.log('Config request failed with status:', response.status);
            updateConfigDisplay();
        }
    } catch (e) {
        console.log('Using default config:', e.message);
        updateConfigDisplay();
    }
}

function updateConfigDisplay() {
    document.getElementById('ldapHost').textContent = config.host;
    document.getElementById('ldapPort').textContent = config.port;
    document.getElementById('baseDn').textContent = config.baseDn;
    document.getElementById('adminDn').textContent = config.adminDn;

    var examples =
'# Test anonymous search (if allowed)\n' +
'ldapsearch -x -H ldap://' + config.host + ':' + config.port + ' -b "' + config.baseDn + '" "(objectClass=*)"\n\n' +
'# Test user bind and search\n' +
'ldapsearch -x -H ldap://' + config.host + ':' + config.port + ' \\\n' +
'  -D "uid=testuser@example.com,ou=users,' + config.baseDn + '" \\\n' +
'  -w "Password123!" \\\n' +
'  -b "ou=users,' + config.baseDn + '" \\\n' +
'  "(uid=*)"\n\n' +
'# Test admin bind\n' +
'ldapsearch -x -H ldap://' + config.host + ':' + config.port + ' \\\n' +
'  -D "' + config.adminDn + '" \\\n' +
'  -w "admin123" \\\n' +
'  -b "' + config.baseDn + '" \\\n' +
'  "(objectClass=*)"';

    document.getElementById('cliExamples').textContent = examples;
}

async function testBind() {
    var username = document.getElementById('username').value;
    var password = document.getElementById('password').value;
    var result = document.getElementById('bindResult');

    result.textContent = 'Testing...';

    try {
        var response = await fetch('/test/ldap/bind', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ username: username, password: password })
        });
        var text = await response.text();
        if (!text) {
            result.innerHTML = '<span class="error">✗ Empty response from server</span>';
            return;
        }
        var data = JSON.parse(text);
        if (response.ok) {
            result.innerHTML = '<span class="success">✓ ' + data.message + '</span>\n\nUser DN: ' + data.dn;
        } else {
            result.innerHTML = '<span class="error">✗ ' + (data.error || 'Unknown error') + '</span>';
        }
    } catch (e) {
        result.innerHTML = '<span class="error">✗ Error: ' + e.message + '</span>';
    }
}

async function testSearch() {
    var filter = document.getElementById('filter').value;
    var scope = document.getElementById('scope').value;
    var result = document.getElementById('searchResult');

    result.textContent = 'Searching...';

    try {
        var response = await fetch('/test/ldap/search', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ filter: filter, scope: scope })
        });
        var text = await response.text();
        if (!text) {
            result.innerHTML = '<span class="error">✗ Empty response from server</span>';
            return;
        }
        var data = JSON.parse(text);
        if (response.ok) {
            result.innerHTML = '<span class="success">✓ Found ' + data.count + ' entries</span>\n\n' +
                JSON.stringify(data.entries, null, 2);
        } else {
            result.innerHTML = '<span class="error">✗ ' + (data.error || 'Unknown error') + '</span>';
        }
    } catch (e) {
        result.innerHTML = '<span class="error">✗ Error: ' + e.message + '</span>';
    }
}
