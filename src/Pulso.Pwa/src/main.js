// Punto de entrada de la PWA PULSO.
import './app.css';

// Fuentes self-hosted (sin depender de Google Fonts / CDN externos).
import '@fontsource/inter/300.css';
import '@fontsource/inter/400.css';
import '@fontsource/inter/500.css';
import '@fontsource/inter/600.css';
import '@fontsource/outfit/400.css';
import '@fontsource/outfit/600.css';
import '@fontsource/outfit/700.css';
import '@fontsource/outfit/800.css';

import App from './App.svelte';

const app = new App({
  target: document.getElementById('app')
});

export default app;
