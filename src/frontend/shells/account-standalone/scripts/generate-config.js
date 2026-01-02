// Generate runtime config.js from environment variables
// Used by Cloudflare Pages (and other CI/CD) at build time

import { writeFileSync, mkdirSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));

const config = {
  serverUrl: process.env.OLUSO_SERVER_URL || process.env.SERVER_URL,
  apiUrl: process.env.OLUSO_API_URL || process.env.API_URL,
};

// Remove undefined values
const cleanConfig = Object.fromEntries(
  Object.entries(config).filter(([, v]) => v !== undefined)
);

const content = `// Generated at build time - ${new Date().toISOString()}
window.__OLUSO_CONFIG__ = ${JSON.stringify(cleanConfig, null, 2)};
`;

const outputPath = join(__dirname, '../public/config.js');
mkdirSync(dirname(outputPath), { recursive: true });
writeFileSync(outputPath, content);

console.log('Generated config.js:', cleanConfig);
