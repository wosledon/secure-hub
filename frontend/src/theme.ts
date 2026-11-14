import type { PaletteMode } from '@mui/material';
import { createTheme } from '@mui/material/styles';

export function createAppTheme(mode: PaletteMode) {
  const dark = mode === 'dark';

  return createTheme({
    palette: {
      mode,
      primary: { main: dark ? '#8B85F8' : '#5146E5', contrastText: '#FFFFFF' },
      secondary: { main: dark ? '#5EEAD4' : '#0F766E' },
      success: { main: dark ? '#4ADE80' : '#15803D' },
      warning: { main: dark ? '#FBBF24' : '#B45309' },
      error: { main: dark ? '#F87171' : '#C53D3D' },
      info: { main: dark ? '#60A5FA' : '#2563EB' },
      background: { default: dark ? '#0B1120' : '#F4F6FA', paper: dark ? '#111A2C' : '#FFFFFF' },
      text: { primary: dark ? '#EEF2FF' : '#182230', secondary: dark ? '#9AA8BD' : '#667085' },
      divider: dark ? 'rgba(148,163,184,0.16)' : '#E7EAF0'
    },
    shape: { borderRadius: 12 },
    typography: {
      fontFamily: 'Inter, "SF Pro Display", "Segoe UI", "Noto Sans SC", "Microsoft YaHei", sans-serif',
      h1: { fontSize: '1.75rem', lineHeight: 1.25, fontWeight: 700, letterSpacing: '-0.025em', textWrap: 'balance' },
      h2: { fontSize: '1.125rem', lineHeight: 1.4, fontWeight: 650, letterSpacing: '-0.015em', textWrap: 'balance' },
      h3: { fontSize: '1rem', lineHeight: 1.45, fontWeight: 650 },
      h4: { fontSize: '1.75rem', lineHeight: 1.2, fontWeight: 700, letterSpacing: '-0.025em' },
      subtitle1: { fontSize: '0.9375rem', fontWeight: 600 },
      subtitle2: { fontSize: '0.8125rem', fontWeight: 600 },
      body1: { fontSize: '0.875rem', lineHeight: 1.6 },
      body2: { fontSize: '0.8125rem', lineHeight: 1.55 },
      caption: { fontSize: '0.75rem', lineHeight: 1.5 },
      button: { textTransform: 'none', fontWeight: 600, letterSpacing: 0 },
      overline: { fontSize: '0.6875rem', fontWeight: 700, letterSpacing: '0.08em' }
    },
    components: {
      MuiCssBaseline: {
        styleOverrides: {
          html: { colorScheme: mode, scrollBehavior: 'smooth' },
          body: { minWidth: 320, WebkitTapHighlightColor: 'transparent' },
          '*': { boxSizing: 'border-box' },
          '*::selection': { backgroundColor: dark ? '#3730A3' : '#DCD9FF' },
          'a, button, input, select, textarea': { touchAction: 'manipulation' },
          ':focus-visible': { outline: '2px solid #7167F0', outlineOffset: 3 },
          '::-webkit-scrollbar': { width: 8, height: 8 },
          '::-webkit-scrollbar-thumb': { backgroundColor: dark ? '#344054' : '#CBD2DC', borderRadius: 8 },
          '::-webkit-scrollbar-track': { backgroundColor: 'transparent' },
          '@media (prefers-reduced-motion: reduce)': {
            '*, *::before, *::after': {
              animationDuration: '0.01ms !important',
              animationIterationCount: '1 !important',
              scrollBehavior: 'auto !important',
              transitionDuration: '0.01ms !important'
            }
          },
          '@media (pointer: fine)': { html: { overscrollBehavior: 'none' } },
          code: { fontFamily: '"SFMono-Regular", Consolas, "Liberation Mono", monospace' }
        }
      },
      MuiButton: {
        defaultProps: { disableElevation: true },
        styleOverrides: {
          root: { minHeight: 38, borderRadius: 9, paddingInline: 16, whiteSpace: 'nowrap' },
          sizeSmall: { minHeight: 32, paddingInline: 12 },
          contained: { boxShadow: '0 1px 2px rgba(16,24,40,0.08)' },
          outlined: { backgroundColor: dark ? 'rgba(255,255,255,0.02)' : '#FFFFFF' }
        }
      },
      MuiIconButton: { styleOverrides: { root: { borderRadius: 9 } } },
      MuiCard: {
        defaultProps: { elevation: 0 },
        styleOverrides: {
          root: {
            border: `1px solid ${dark ? 'rgba(148,163,184,0.16)' : '#E7EAF0'}`,
            borderRadius: 14,
            boxShadow: dark ? 'none' : '0 1px 2px rgba(16,24,40,0.025)',
            backgroundImage: 'none',
            // Wide tables may scroll inside a card, but must never expand the root document.
            contain: 'paint'
          }
        }
      },
      MuiCardContent: {
        styleOverrides: { root: { padding: 20, '&:last-child': { paddingBottom: 20 } } }
      },
      MuiPaper: { styleOverrides: { root: { backgroundImage: 'none' } } },
      MuiAppBar: { styleOverrides: { root: { boxShadow: 'none' } } },
      MuiDrawer: { styleOverrides: { paper: { overscrollBehavior: 'contain' } } },
      MuiDialog: { styleOverrides: { paper: { overscrollBehavior: 'contain' } } },
      MuiDialogTitle: { styleOverrides: { root: { fontSize: '1.125rem', fontWeight: 650, padding: '22px 24px 12px' } } },
      MuiDialogContent: { styleOverrides: { root: { padding: '12px 24px' } } },
      MuiDialogActions: { styleOverrides: { root: { padding: '16px 24px 22px', gap: 8 } } },
      MuiOutlinedInput: {
        styleOverrides: {
          root: { borderRadius: 9, backgroundColor: dark ? 'rgba(255,255,255,0.025)' : '#FFFFFF' },
          input: { fontSize: '0.875rem' },
          notchedOutline: { borderColor: dark ? 'rgba(148,163,184,0.24)' : '#DDE2EA' }
        }
      },
      MuiInputLabel: { styleOverrides: { root: { fontSize: '0.875rem' } } },
      MuiFormHelperText: { styleOverrides: { root: { marginLeft: 2, fontSize: '0.75rem' } } },
      MuiSelect: { styleOverrides: { icon: { color: dark ? '#9AA8BD' : '#667085' } } },
      MuiChip: {
        styleOverrides: {
          root: { height: 26, borderRadius: 7, fontWeight: 600, fontSize: '0.75rem' },
          sizeSmall: { height: 24 },
          label: { paddingInline: 9 }
        }
      },
      MuiTableHead: {
        styleOverrides: {
          root: {
            '& .MuiTableCell-root': {
              backgroundColor: dark ? 'rgba(148,163,184,0.045)' : '#F8F9FC',
              color: dark ? '#9AA8BD' : '#667085',
              fontSize: '0.6875rem',
              fontWeight: 700,
              letterSpacing: '0.035em',
              textTransform: 'uppercase',
              whiteSpace: 'nowrap'
            }
          }
        }
      },
      MuiTableCell: {
        styleOverrides: {
          root: { padding: '13px 16px', borderBottomColor: dark ? 'rgba(148,163,184,0.12)' : '#ECEFF4' },
          sizeSmall: { padding: '11px 16px' }
        }
      },
      MuiTableRow: {
        styleOverrides: { root: { '&:last-child .MuiTableCell-root': { borderBottom: 0 } } }
      },
      MuiTabs: {
        styleOverrides: { indicator: { height: 2, borderRadius: 2 }, root: { minHeight: 42 } }
      },
      MuiTab: {
        styleOverrides: {
          root: { minHeight: 42, paddingInline: 16, textTransform: 'none', fontWeight: 600, fontSize: '0.875rem' }
        }
      },
      MuiTooltip: { styleOverrides: { tooltip: { fontSize: '0.75rem', borderRadius: 7 } } },
      MuiAlert: { styleOverrides: { root: { borderRadius: 10, fontSize: '0.8125rem', alignItems: 'center' } } },
      MuiLinearProgress: { styleOverrides: { root: { height: 7, borderRadius: 8 } } },
      MuiSkeleton: { defaultProps: { animation: 'wave' } },
      MuiLink: { styleOverrides: { root: { textUnderlineOffset: 3 } } }
    }
  });
}
