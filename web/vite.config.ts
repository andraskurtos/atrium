import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import { VitePWA } from 'vite-plugin-pwa';

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'autoUpdate',
      includeAssets: ['icon.svg', 'pwa-192x192.png', 'pwa-512x512.png'],
      workbox: {
        navigateFallbackDenylist: [/^\/api/, /^\/mcp/],
      },
      manifest: {
        name: 'Atrium',
        short_name: 'Atrium',
        description: 'Private self-hosted personal everything-app',
        theme_color: '#141311',
        background_color: '#141311',
        display: 'standalone',
        start_url: '/',
        scope: '/',
        icons: [
          {
            src: 'pwa-192x192.png',
            sizes: '192x192',
            type: 'image/png',
          },
          {
            src: 'pwa-512x512.png',
            sizes: '512x512',
            type: 'image/png',
          },
          {
            src: 'pwa-512x512.png',
            sizes: '512x512',
            type: 'image/png',
            purpose: 'maskable',
          },
        ],
      },
    }),
  ],
  server: {
    port: 5173,
    proxy: {
      '/api': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
      '/mcp': {
        target: 'http://localhost:5000',
        changeOrigin: true,
      },
    },
  },
  build: {
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (
            id.includes('node_modules/react/') ||
            id.includes('node_modules/react-dom/') ||
            id.includes('node_modules/scheduler/')
          ) {
            return 'react-vendor';
          }
          if (id.includes('node_modules/@lezer/')) {
            return 'lezer';
          }
          if (
            id.includes('node_modules/@codemirror/') ||
            id.includes('node_modules/crelt/') ||
            id.includes('node_modules/style-mod/') ||
            id.includes('node_modules/w3c-keyname/')
          ) {
            return 'codemirror';
          }
          if (
            id.includes('node_modules/react-markdown/') ||
            id.includes('node_modules/remark-') ||
            id.includes('node_modules/rehype-') ||
            id.includes('node_modules/unified/') ||
            id.includes('node_modules/micromark') ||
            id.includes('node_modules/mdast-') ||
            id.includes('node_modules/hast-') ||
            id.includes('node_modules/unist-') ||
            id.includes('node_modules/vfile')
          ) {
            return 'markdown-reader';
          }
        },
      },
    },
  },
});
