import { defineConfig } from 'vite';

// One self-contained script that websites embed with a <script> tag; the API serves it as /widget.js.
export default defineConfig({
  build: {
    outDir: '../backend/src/Citra.Api/wwwroot',
    emptyOutDir: false,
    lib: { entry: 'src/widget.ts', formats: ['iife'], name: 'CitraWidget', fileName: () => 'widget.js' },
  },
});
