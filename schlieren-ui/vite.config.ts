import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: {
      '/rpc': {
        target: 'http://localhost:8545',
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/rpc/, ''),
      },
    },
  },
})
