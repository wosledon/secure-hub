import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  build: {
    outDir: '../src/SecureHub.Api/wwwroot',
    emptyOutDir: true,
    rollupOptions: {
      output: {
        manualChunks: {
          'react-vendor': ['react', 'react-dom', 'react-router-dom'],
          'mui-vendor': ['@mui/material'],
          'emotion-vendor': ['@emotion/react', '@emotion/styled'],
          'i18n-vendor': ['i18next', 'react-i18next']
        }
      }
    }
  },
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://127.0.0.1:5080',
      '/v1': 'http://127.0.0.1:5080',
      '/health': 'http://127.0.0.1:5080',
      '/healthz': 'http://127.0.0.1:5080'
    }
  }
});
