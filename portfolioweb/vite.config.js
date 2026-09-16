import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv } from 'vite'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '')

  // The Functions host runs on 7234 locally (portfolioapp/Properties/launchSettings.json).
  // Proxying keeps the browser same-origin, so no CORS configuration is needed in development.
  const apiTarget = env.VITE_API_PROXY_TARGET || 'http://localhost:7234'

  return {
    plugins: [react()],
    server: {
      proxy: {
        '/api': {
          target: apiTarget,
          changeOrigin: true,
        },
      },
    },
  }
})
