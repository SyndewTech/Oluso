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
  // Embed script (self-contained IIFE for <script> tag usage)
  {
    entry: ['src/embed.ts'],
    format: ['iife'],
    globalName: 'Oluso',
    outDir: 'dist',
    minify: true,
    sourcemap: true,
    platform: 'browser',
  },
  // Auth SDK (ESM/CJS library)
  {
    entry: ['src/auth/index.ts'],
    outDir: 'dist/auth',
    format: ['cjs', 'esm'],
    dts: true,
    sourcemap: true,
  },
  // Auth embed script (self-contained IIFE for <script> tag usage)
  {
    entry: ['src/auth/embed.ts'],
    outDir: 'dist/auth',
    format: ['iife'],
    globalName: 'OlusoAuth',
    minify: true,
    sourcemap: true,
    platform: 'browser',
  },
]);
