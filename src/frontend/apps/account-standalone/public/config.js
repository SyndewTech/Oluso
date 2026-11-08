/**
 * Runtime configuration for Oluso Account UI
 *
 * When using ngrok or other tunnels:
 * 1. Start ngrok for the backend: ngrok http 5050
 * 2. Update serverUrl below with your ngrok backend URL
 *
 * Example for ngrok:
 *   serverUrl: 'https://abc123.ngrok-free.app'
 */
window.__OLUSO_CONFIG__ = {
  // Backend server URL - change this for ngrok tunnels
  // Default: 'http://localhost:5050'
  serverUrl: 'http://localhost:5050',

  // API URL is derived from serverUrl + '/api/account' by default
  // Override explicitly if needed:
  // apiUrl: 'https://abc123.ngrok-free.app/api/account'

  // Public URL of the Workspace app; shows a "Back to workspace" link when set.
  // workspaceUrl: 'https://workspace.example.com'
  // Branding overrides — uncomment and customise for white-label deployments
  // branding: {
  //   productName: 'My Product',
  //   companyName: 'My Company',
  //   logoUrl: '/assets/logo.svg',
  // }
};
