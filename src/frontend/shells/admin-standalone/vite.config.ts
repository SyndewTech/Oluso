import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      // Workspace packages - point to source during development
      // Styles subpath must come before main package alias
      '@oluso/admin-ui/styles': path.resolve(__dirname, '../../AdminUI/src/assets/styles/index.css'),
      '@oluso/admin-ui': path.resolve(__dirname, '../../AdminUI/src'),
      '@oluso/ui-core': path.resolve(__dirname, '../../ui-core/src'),
      '@oluso/fido2-ui': path.resolve(__dirname, '../../libs/fido2-ui'),
      '@oluso/saml-ui': path.resolve(__dirname, '../../libs/saml-ui'),
      '@oluso/scim-ui': path.resolve(__dirname, '../../libs/scim-ui'),
      '@oluso/telemetry-ui': path.resolve(__dirname, '../../libs/telemetry-ui'),
      '@oluso/ldap-ui': path.resolve(__dirname, '../../libs/ldap-ui'),
    },
  },
  server: {
    port: 3000,
    proxy: {
      // Proxy only actual API paths, not SPA routes that happen to start with /api
      '/api/': {
        target: 'https://localhost:5050',
        changeOrigin: true,
        secure: false,
      },
      '/.well-known': {
        target: 'https://localhost:5050',
        changeOrigin: true,
        secure: false,
      },
    },
  },
});
