import { createTheme } from '@mui/material/styles';

/**
 * Material Design 3 基线配色（primary #6750A4）+ MD3 形状尺度：
 * extra-small 4 / small 8 / medium 12 / large 16 / extra-large 28。
 */
export const theme = createTheme({
  palette: {
    mode: 'light',
    primary: { main: '#6750A4', contrastText: '#FFFFFF' },
    secondary: { main: '#625B71' },
    error: { main: '#B3261E' },
    background: { default: '#FEF7FF', paper: '#FFFFFF' },
    divider: 'rgba(103, 80, 164, 0.12)'
  },
  shape: { borderRadius: 16 },
  typography: {
    fontFamily: '"Roboto", "Noto Sans SC", "Microsoft YaHei", system-ui, sans-serif',
    h1: { fontSize: '1.75rem', fontWeight: 500 },
    h2: { fontSize: '1.35rem', fontWeight: 500 },
    button: { textTransform: 'none', fontWeight: 500 }
  },
  components: {
    MuiButton: {
      styleOverrides: {
        root: { borderRadius: 999, paddingInline: 20 }
      }
    },
    MuiCard: {
      styleOverrides: {
        root: { borderRadius: 20, boxShadow: '0 1px 2px rgba(0,0,0,0.08)' }
      }
    },
    MuiAppBar: {
      styleOverrides: {
        root: { boxShadow: 'none', borderBottom: '1px solid rgba(103,80,164,0.12)' }
      }
    },
    MuiTabs: {
      styleOverrides: { indicator: { height: 3, borderRadius: 3 } }
    },
    MuiChip: {
      styleOverrides: { root: { borderRadius: 8 } }
    }
  }
});
