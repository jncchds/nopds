import { defineConfig, minimal2023Preset } from '@vite-pwa/assets-generator/config'

// Generates favicon.ico, apple-touch-icon, 64/192/512 and maskable icons from public/favicon.svg:
//   npm run generate-pwa-assets
export default defineConfig({
  headLinkOptions: { preset: '2023' },
  preset: {
    ...minimal2023Preset,
    maskable: { ...minimal2023Preset.maskable, resizeOptions: { background: '#8f4f17' } },
    apple: { ...minimal2023Preset.apple, resizeOptions: { background: '#8f4f17' } },
  },
  images: ['public/favicon.svg'],
})
