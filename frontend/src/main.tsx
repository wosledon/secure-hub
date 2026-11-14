import React, { useEffect, useMemo, useState } from 'react';
import ReactDOM from 'react-dom/client';
import type { PaletteMode } from '@mui/material';
import { CssBaseline, ThemeProvider } from '@mui/material';
import { BrowserRouter } from 'react-router-dom';
import './i18n';
import { createAppTheme } from './theme';
import App from './App';

function Root() {
  const [mode, setMode] = useState<PaletteMode>(() =>
    localStorage.getItem('sh.theme') === 'dark' ? 'dark' : 'light'
  );
  const theme = useMemo(() => createAppTheme(mode), [mode]);

  useEffect(() => {
    localStorage.setItem('sh.theme', mode);
    document.documentElement.style.colorScheme = mode;
    document.querySelector('meta[name="theme-color"]')?.setAttribute('content', mode === 'dark' ? '#0B1120' : '#F4F6FA');
  }, [mode]);

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <BrowserRouter>
        <App mode={mode} onToggleMode={() => setMode((current) => current === 'dark' ? 'light' : 'dark')} />
      </BrowserRouter>
    </ThemeProvider>
  );
}

ReactDOM.createRoot(document.getElementById('root') as HTMLElement).render(
  <React.StrictMode>
    <Root />
  </React.StrictMode>
);
