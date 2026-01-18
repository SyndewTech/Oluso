import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

// https://vitejs.dev/config/
export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@oluso/workspace-ui': path.resolve(__dirname, '../../WorkspacePortal/src'),
      '@oluso/ui-core': path.resolve(__dirname, '../../ui-core/src'),
    },
  },
  server: {
    port: 5175,
    proxy: {
      '/api': {
        target: 'http://localhost:5050',
        changeOrigin: true,
      },
      '/.well-known': {
        target: 'http://localhost:5050',
        changeOrigin: true,
      },
      '/connect': {
        target: 'http://localhost:5050',
        changeOrigin: true,
      },
    },
  },
  build: {
    outDir: 'dist',
    sourcemap: true,
  },
});
