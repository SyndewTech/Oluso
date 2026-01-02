import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      // Workspace packages - point to source during development
      '@oluso/account-ui': path.resolve(__dirname, '../../AccountUI/src'),
      '@oluso/ui-core': path.resolve(__dirname, '../../ui-core/src'),
      '@oluso/fido2-ui': path.resolve(__dirname, '../../libs/fido2-ui'),
    },
  },
  server: {
    port: 3001,
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
