/**
 * Runtime configuration for Oluso Workspace Portal
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

  // URLs of the companion apps when they are served from their own origins.
  // A tenant can override these under Settings → Workspace Apps → App Links.
  // Unset, the workspace assumes /admin and /account on its own origin.
  // adminUrl: 'https://admin.example.com',
  // accountUrl: 'https://account.example.com',

  // Branding overrides — uncomment and customise for white-label deployments
  // branding: {
  //   productName: 'My Product',
  //   companyName: 'My Company',
  //   logoUrl: '/assets/logo.svg',
  // }
};
