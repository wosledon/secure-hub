import { lazy, Suspense, useEffect, useMemo, useState, type ReactNode } from 'react';
import {
  AppBar,
  Avatar,
  Box,
  CircularProgress,
  Divider,
  Drawer,
  IconButton,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  Menu,
  MenuItem,
  Select,
  Stack,
  Toolbar,
  Tooltip,
  Typography
} from '@mui/material';
import type { PaletteMode } from '@mui/material';
import AssessmentIcon from '@mui/icons-material/Assessment';
import AutoAwesomeIcon from '@mui/icons-material/AutoAwesome';
import DarkModeIcon from '@mui/icons-material/DarkMode';
import DnsIcon from '@mui/icons-material/Dns';
import GroupsIcon from '@mui/icons-material/Groups';
import KeyIcon from '@mui/icons-material/Key';
import LightModeIcon from '@mui/icons-material/LightMode';
import LogoutIcon from '@mui/icons-material/Logout';
import MenuIcon from '@mui/icons-material/Menu';
import ManageAccountsIcon from '@mui/icons-material/ManageAccounts';
import InsightsIcon from '@mui/icons-material/Insights';
import PaidIcon from '@mui/icons-material/Paid';
import PersonOutlineIcon from '@mui/icons-material/PersonOutline';
import RouteIcon from '@mui/icons-material/Route';
import SecurityIcon from '@mui/icons-material/Security';
import ShieldOutlinedIcon from '@mui/icons-material/ShieldOutlined';
import SettingsIcon from '@mui/icons-material/Settings';
import SpaceDashboardIcon from '@mui/icons-material/SpaceDashboard';
import VerifiedUserIcon from '@mui/icons-material/VerifiedUser';
import { Navigate, Route, Routes, Link as RouterLink, useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api } from './api';
import { setLanguage } from './i18n';
import { BrandLockup } from './components/Brand';
import AccountDialog from './components/AccountDialog';
import LoginPage from './pages/LoginPage';
import type { Session } from './types';
import { ROLE_END_USER } from './types';

const DRAWER_WIDTH = 248;
const DashboardPage = lazy(() => import('./pages/DashboardPage'));
const ProvidersPage = lazy(() => import('./pages/ProvidersPage'));
const CatalogPage = lazy(() => import('./pages/CatalogPage'));
const RoutingPage = lazy(() => import('./pages/RoutingPage'));
const KeysPage = lazy(() => import('./pages/KeysPage'));
const PoliciesPage = lazy(() => import('./pages/PoliciesPage'));
const UsagePage = lazy(() => import('./pages/UsagePage'));
const AuditPage = lazy(() => import('./pages/AuditPage'));
const OrganizationPage = lazy(() => import('./pages/OrganizationPage'));
const EntitlementsPage = lazy(() => import('./pages/EntitlementsPage'));
const ObservabilityPage = lazy(() => import('./pages/ObservabilityPage'));
const SystemPage = lazy(() => import('./pages/SystemPage'));
const AssistantWorkspace = lazy(() => import('./pages/AssistantWorkspace'));
const WorkspacePage = lazy(() => import('./pages/WorkspacePage'));

type NavItem = { path: string; label: string; icon: ReactNode; admin?: boolean };

export default function App({ mode, onToggleMode }: { mode: PaletteMode; onToggleMode: () => void }) {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const [session, setSession] = useState<Session | null>(null);
  const [loading, setLoading] = useState(true);
  const [mobileOpen, setMobileOpen] = useState(false);
  const [accountOpen, setAccountOpen] = useState(false);
  const [userMenu, setUserMenu] = useState<HTMLElement | null>(null);
  const [health, setHealth] = useState<{ status: string; version: string } | null>(null);

  useEffect(() => {
    api.me().then((r) => setSession(r.user)).catch(() => setSession(null)).finally(() => setLoading(false));
  }, []);

  useEffect(() => {
    if (!session) return;
    api.health().then((result) => setHealth({ status: result.status, version: result.version })).catch(() => setHealth({ status: 'unknown', version: '—' }));
  }, [session]);

  useEffect(() => setMobileOpen(false), [location.pathname]);

  const isEndUserOnly = useMemo(
    () => !!session && session.roles.length > 0 && session.roles.every((role) => role === ROLE_END_USER),
    [session]
  );

  const adminItems: NavItem[] = [
    { path: '/overview', label: t('nav.overview'), icon: <SpaceDashboardIcon />, admin: true },
    { path: '/providers', label: t('nav.providers'), icon: <DnsIcon />, admin: true },
    { path: '/catalog', label: t('nav.catalog'), icon: <AssessmentIcon />, admin: true },
    { path: '/organization', label: t('nav.organization'), icon: <GroupsIcon />, admin: true },
    { path: '/entitlements', label: t('nav.entitlements'), icon: <VerifiedUserIcon />, admin: true },
    { path: '/routing', label: t('nav.routing'), icon: <RouteIcon />, admin: true },
    { path: '/keys', label: t('nav.keys'), icon: <KeyIcon />, admin: true },
    { path: '/policies', label: t('nav.policies'), icon: <SecurityIcon />, admin: true },
    { path: '/usage', label: t('nav.budgets'), icon: <PaidIcon />, admin: true },
    { path: '/observability', label: t('nav.observability'), icon: <InsightsIcon />, admin: true },
    { path: '/audit', label: t('nav.audit'), icon: <ShieldOutlinedIcon />, admin: true },
    { path: '/system', label: t('nav.system'), icon: <SettingsIcon />, admin: true }
  ];
  const personalItems: NavItem[] = [
    { path: '/workspace', label: t('nav.workspace'), icon: <PersonOutlineIcon /> },
    { path: '/assistant', label: t('nav.assistant'), icon: <AutoAwesomeIcon /> }
  ];
  const items = [...adminItems, ...personalItems];
  const current = items.find((item) => location.pathname.startsWith(item.path));

  if (loading) {
    return <Box display="flex" height="100vh" alignItems="center" justifyContent="center"><CircularProgress aria-label={t('common.loading')} /></Box>;
  }
  if (!session) return <LoginPage onSuccess={setSession} />;

  const logout = async () => {
    await api.logout().catch(() => undefined);
    setUserMenu(null);
    setSession(null);
    navigate('/');
  };

  const navigation = (
    <Box sx={{ height: '100%', display: 'flex', flexDirection: 'column', color: '#D7DDE8', bgcolor: '#111827' }}>
      <Box sx={{ px: 2.5, height: 72, display: 'flex', alignItems: 'center' }}>
        <BrandLockup inverse subtitle={t('app.console')} />
      </Box>
      <Divider sx={{ borderColor: 'rgba(255,255,255,0.07)', mx: 2 }} />
      <Box sx={{ flex: 1, overflowY: 'auto', px: 1.5, py: 2 }}>
        {!isEndUserOnly && (
          <>
            <Typography variant="overline" sx={{ px: 1.25, color: 'rgba(215,221,232,0.45)', display: 'block', mb: 0.75 }}>
              {t('nav.management')}
            </Typography>
            <List disablePadding aria-label={t('nav.main')}>
              {adminItems.map((item) => (
                <ListItemButton
                  key={item.path}
                  component={RouterLink}
                  to={item.path}
                  selected={location.pathname.startsWith(item.path)}
                  sx={{
                    borderRadius: 2, mb: 0.35, minHeight: 42, color: 'rgba(215,221,232,0.76)',
                    '& .MuiListItemIcon-root': { color: 'rgba(215,221,232,0.52)' },
                    '&:hover': { bgcolor: 'rgba(255,255,255,0.055)', color: '#FFF' },
                    '&.Mui-selected': { bgcolor: 'rgba(113,103,240,0.19)', color: '#FFF' },
                    '&.Mui-selected .MuiListItemIcon-root': { color: '#A5B4FC' },
                    '&.Mui-selected:hover': { bgcolor: 'rgba(113,103,240,0.25)' }
                  }}
                >
                  <ListItemIcon sx={{ minWidth: 36, '& svg': { fontSize: 20 } }}>{item.icon}</ListItemIcon>
                  <ListItemText primary={item.label} primaryTypographyProps={{ fontSize: 13, fontWeight: 550 }} />
                </ListItemButton>
              ))}
            </List>
          </>
        )}
        <Typography variant="overline" sx={{ px: 1.25, color: 'rgba(215,221,232,0.45)', display: 'block', mt: 2.5, mb: 0.75 }}>
          {t('nav.personal')}
        </Typography>
        <List disablePadding>
          {personalItems.map((item) => (
            <ListItemButton
              key={item.path}
              component={RouterLink}
              to={item.path}
              selected={location.pathname.startsWith(item.path)}
              sx={{
                borderRadius: 2, mb: 0.35, minHeight: 42, color: 'rgba(215,221,232,0.76)',
                '& .MuiListItemIcon-root': { color: 'rgba(215,221,232,0.52)' },
                '&:hover': { bgcolor: 'rgba(255,255,255,0.055)', color: '#FFF' },
                '&.Mui-selected': { bgcolor: 'rgba(113,103,240,0.19)', color: '#FFF' },
                '&.Mui-selected .MuiListItemIcon-root': { color: '#A5B4FC' }
              }}
            >
              <ListItemIcon sx={{ minWidth: 36, '& svg': { fontSize: 20 } }}>{item.icon}</ListItemIcon>
              <ListItemText primary={item.label} primaryTypographyProps={{ fontSize: 13, fontWeight: 550 }} />
            </ListItemButton>
          ))}
        </List>
      </Box>
      <Box sx={{ m: 1.5, p: 1.5, border: '1px solid rgba(255,255,255,0.08)', borderRadius: 2.5, bgcolor: 'rgba(255,255,255,0.025)' }}>
        <Stack direction="row" alignItems="center" spacing={1}>
          <Box sx={{ width: 7, height: 7, borderRadius: '50%', bgcolor: health?.status === 'ok' ? '#4ADE80' : '#FBBF24', boxShadow: health?.status === 'ok' ? '0 0 0 4px rgba(74,222,128,0.1)' : 'none' }} />
          <Typography variant="caption" sx={{ color: '#D7DDE8', fontWeight: 600 }}>
            {health?.status === 'ok' ? t('dashboard.healthy') : health?.status === 'unknown' ? t('common.unknown') : t('common.loading')}
          </Typography>
        </Stack>
        <Stack direction="row" justifyContent="space-between" mt={0.75}>
          <Typography variant="caption" sx={{ color: 'rgba(215,221,232,0.42)' }}>{t('app.local')}</Typography>
          <Typography variant="caption" sx={{ color: 'rgba(215,221,232,0.42)' }}>{health?.version ?? '—'}</Typography>
        </Stack>
      </Box>
    </Box>
  );

  return (
    <Box sx={{ display: 'flex', minHeight: '100vh', bgcolor: 'background.default' }}>
      <Box component="a" href="#main-content" sx={{ position: 'fixed', top: -60, left: 16, zIndex: 2000, p: 1.5, bgcolor: 'primary.main', color: '#FFF', borderRadius: 1, '&:focus': { top: 8 } }}>
        Skip to content
      </Box>
      <AppBar
        position="fixed"
        color="inherit"
        sx={{
          width: { md: `calc(100% - ${DRAWER_WIDTH}px)` }, ml: { md: `${DRAWER_WIDTH}px` },
          bgcolor: (theme) => theme.palette.mode === 'dark' ? 'rgba(17,26,44,0.9)' : 'rgba(255,255,255,0.88)',
          backdropFilter: 'blur(14px)', borderBottom: 1, borderColor: 'divider'
        }}
      >
        <Toolbar sx={{ minHeight: '64px !important', px: { xs: 2, md: 3 }, gap: 1 }}>
          <IconButton sx={{ display: { md: 'none' } }} onClick={() => setMobileOpen(true)} aria-label={t('nav.open')}><MenuIcon /></IconButton>
          <Box sx={{ flexGrow: 1, minWidth: 0 }}>
            <Typography variant="caption" color="text.secondary" display="block">{isEndUserOnly ? t('nav.personal') : t('app.console')}</Typography>
            <Typography variant="subtitle1" noWrap>{current?.label ?? t('nav.overview')}</Typography>
          </Box>
          <Select
            size="small"
            value={i18n.language.startsWith('zh') ? 'zh-CN' : 'en'}
            onChange={(event) => setLanguage(event.target.value)}
            inputProps={{ 'aria-label': t('common.language') }}
            sx={{ minWidth: { xs: 86, sm: 104 }, height: 36, fontSize: 12, bgcolor: 'transparent' }}
          >
            <MenuItem value="zh-CN">中文</MenuItem><MenuItem value="en">English</MenuItem>
          </Select>
          <Tooltip title={mode === 'dark' ? 'Light mode' : 'Dark mode'}>
            <IconButton onClick={onToggleMode} aria-label={mode === 'dark' ? 'Switch to light mode' : 'Switch to dark mode'}>
              {mode === 'dark' ? <LightModeIcon fontSize="small" /> : <DarkModeIcon fontSize="small" />}
            </IconButton>
          </Tooltip>
          <Divider orientation="vertical" flexItem sx={{ mx: 0.5, my: 2 }} />
          <Tooltip title={t('nav.account')}>
            <IconButton onClick={(event) => setUserMenu(event.currentTarget)} aria-label={t('nav.account')} sx={{ p: 0.5 }}>
              <Avatar sx={{ width: 32, height: 32, bgcolor: 'primary.main', fontSize: 13, fontWeight: 700 }}>{session.user_name.slice(0, 1).toUpperCase()}</Avatar>
            </IconButton>
          </Tooltip>
          <Menu anchorEl={userMenu} open={!!userMenu} onClose={() => setUserMenu(null)} transformOrigin={{ horizontal: 'right', vertical: 'top' }} anchorOrigin={{ horizontal: 'right', vertical: 'bottom' }}>
            <Box px={2} py={1.25} minWidth={200}>
              <Typography variant="subtitle2">{session.user_name}</Typography>
              <Typography variant="caption" color="text.secondary">{session.roles.map((role) => t(`account.role${role}`)).slice(0, 2).join(' · ')}</Typography>
            </Box>
            <Divider />
            <MenuItem onClick={() => { setUserMenu(null); setAccountOpen(true); }}><ListItemIcon><ManageAccountsIcon fontSize="small" /></ListItemIcon>{t('nav.account')}</MenuItem>
            <MenuItem onClick={() => void logout()}><ListItemIcon><LogoutIcon fontSize="small" /></ListItemIcon>{t('nav.logout')}</MenuItem>
          </Menu>
        </Toolbar>
      </AppBar>

      <Box component="nav" sx={{ width: { md: DRAWER_WIDTH }, flexShrink: { md: 0 } }} aria-label={t('nav.main')}>
        <Drawer variant="temporary" open={mobileOpen} onClose={() => setMobileOpen(false)} ModalProps={{ keepMounted: true }} sx={{ display: { xs: 'block', md: 'none' }, '& .MuiDrawer-paper': { width: DRAWER_WIDTH, border: 0 } }}>{navigation}</Drawer>
        <Drawer variant="permanent" sx={{ display: { xs: 'none', md: 'block' }, '& .MuiDrawer-paper': { width: DRAWER_WIDTH, border: 0 } }} open>{navigation}</Drawer>
      </Box>

      <Box component="main" id="main-content" tabIndex={-1} sx={{ flexGrow: 1, minWidth: 0, pt: '64px' }}>
        <Box sx={{ p: { xs: 2, sm: 3, lg: 4 }, maxWidth: current?.path === '/assistant' ? 1800 : 1560, mx: 'auto' }}>
          <Suspense fallback={<Box display="flex" justifyContent="center" py={10}><CircularProgress aria-label={t('common.loading')} /></Box>}>
            <Routes>
            <Route path="/overview" element={isEndUserOnly ? <Navigate to="/workspace" replace /> : <DashboardPage />} />
            <Route path="/providers" element={isEndUserOnly ? <Navigate to="/workspace" replace /> : <ProvidersPage />} />
            <Route path="/catalog" element={isEndUserOnly ? <Navigate to="/workspace" replace /> : <CatalogPage />} />
            <Route path="/organization" element={isEndUserOnly ? <Navigate to="/workspace" replace /> : <OrganizationPage />} />
            <Route path="/entitlements" element={isEndUserOnly ? <Navigate to="/workspace" replace /> : <EntitlementsPage />} />
            <Route path="/routing" element={isEndUserOnly ? <Navigate to="/workspace" replace /> : <RoutingPage />} />
            <Route path="/keys" element={isEndUserOnly ? <Navigate to="/workspace" replace /> : <KeysPage />} />
            <Route path="/policies" element={isEndUserOnly ? <Navigate to="/workspace" replace /> : <PoliciesPage />} />
            <Route path="/usage" element={isEndUserOnly ? <Navigate to="/workspace" replace /> : <UsagePage />} />
            <Route path="/observability" element={isEndUserOnly ? <Navigate to="/workspace" replace /> : <ObservabilityPage />} />
            <Route path="/audit" element={isEndUserOnly ? <Navigate to="/workspace" replace /> : <AuditPage />} />
            <Route path="/system" element={isEndUserOnly ? <Navigate to="/workspace" replace /> : <SystemPage />} />
            <Route path="/workspace" element={<WorkspacePage />} />
            <Route path="/assistant" element={<AssistantWorkspace />} />
            <Route path="/admin" element={<Navigate to={isEndUserOnly ? '/workspace' : '/overview'} replace />} />
            <Route path="/me" element={<Navigate to="/workspace" replace />} />
            <Route path="*" element={<Navigate to={isEndUserOnly ? '/workspace' : '/overview'} replace />} />
            </Routes>
          </Suspense>
        </Box>
      </Box>
      <AccountDialog session={session} open={accountOpen} onClose={() => setAccountOpen(false)} />
    </Box>
  );
}
