import { defineConfig } from 'tsup';

export default defineConfig([
  // Main entry (vanilla JS)
  {
    entry: ['src/index.ts'],
    format: ['cjs', 'esm'],
    dts: true,
    clean: true,
    sourcemap: true,
  },
  // React entry
  {
    entry: ['src/react.tsx'],
    format: ['cjs', 'esm'],
    dts: true,
    sourcemap: true,
    external: ['react', 'react-dom'],
  },
]);
