import { useEffect, useMemo, useState } from 'react';
import {
  AppBar,
  Avatar,
  Box,
  Button,
  Chip,
  CircularProgress,
  Divider,
  Drawer,
  List,
  ListItemButton,
  ListItemIcon,
  ListItemText,
  MenuItem,
  Select,
  Stack,
  Toolbar,
  Typography
} from '@mui/material';
import AssessmentIcon from '@mui/icons-material/Assessment';
import KeyIcon from '@mui/icons-material/Key';
import LogoutIcon from '@mui/icons-material/Logout';
import StorageIcon from '@mui/icons-material/Storage';
import TravelExploreIcon from '@mui/icons-material/TravelExplore';
import { Navigate, Route, Routes, useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api } from './api';
import { setLanguage } from './i18n';
import LoginPage from './pages/LoginPage';
import AdminPage from './pages/AdminPage';
import MePage from './pages/MePage';
import type { Session } from './types';
import { ROLE_END_USER } from './types';

const DRAWER_WIDTH = 236;

export default function App() {
  const { t, i18n } = useTranslation();
  const navigate = useNavigate();
  const location = useLocation();
  const [session, setSession] = useState<Session | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api
      .me()
      .then((r) => setSession(r.user))
      .catch(() => setSession(null))
      .finally(() => setLoading(false));
  }, []);

  const isEndUserOnly = useMemo(
    () => !!session && session.roles.length > 0 && session.roles.every((r) => r === ROLE_END_USER),
    [session]
  );

  if (loading) {
    return (
      <Box display="flex" height="100vh" alignItems="center" justifyContent="center">
        <CircularProgress />
      </Box>
    );
  }

  if (!session) {
    return <LoginPage onSuccess={setSession} />;
  }

  const logout = async () => {
    await api.logout().catch(() => undefined);
    setSession(null);
  };

  const items = [
    { path: '/admin', label: t('nav.admin'), icon: <AssessmentIcon />, hidden: isEndUserOnly },
    { path: '/me', label: t('nav.me'), icon: <TravelExploreIcon />, hidden: false }
  ];

  return (
    <Box display="flex" minHeight="100vh" bgcolor="background.default">
      <AppBar
        position="fixed"
        color="inherit"
        sx={{ bgcolor: 'background.paper', zIndex: (theme) => theme.zIndex.drawer + 1 }}
      >
        <Toolbar>
          <Stack direction="row" spacing={1.5} alignItems="center" flexGrow={1}>
            <Box
              sx={{
                width: 32,
                height: 32,
                borderRadius: '10px',
                background: 'linear-gradient(135deg,#6750A4,#9A82DB)'
              }}
            />
            <Box>
              <Typography variant="subtitle1" fontWeight={600} lineHeight={1.1}>
                {t('app.name')}
              </Typography>
              <Typography variant="caption" color="text.secondary">
                {t('app.tagline')}
              </Typography>
            </Box>
          </Stack>

          <Select
            size="small"
            value={i18n.language.startsWith('zh') ? 'zh-CN' : 'en'}
            onChange={(e) => setLanguage(e.target.value)}
            sx={{ mr: 2, borderRadius: 999, fontSize: 14 }}
          >
            <MenuItem value="zh-CN">中文</MenuItem>
            <MenuItem value="en">English</MenuItem>
          </Select>

          <Chip
            avatar={<Avatar>{session.user_name.slice(0, 1).toUpperCase()}</Avatar>}
            label={session.user_name}
            variant="outlined"
            sx={{ mr: 1 }}
          />
          <Button startIcon={<LogoutIcon />} onClick={logout} size="small">
            {t('nav.logout')}
          </Button>
        </Toolbar>
      </AppBar>

      <Drawer
        variant="permanent"
        sx={{
          width: DRAWER_WIDTH,
          flexShrink: 0,
          '& .MuiDrawer-paper': {
            width: DRAWER_WIDTH,
            boxSizing: 'border-box',
            borderRight: '1px solid rgba(103,80,164,0.12)',
            bgcolor: 'background.default'
          }
        }}
      >
        <Toolbar />
        <List sx={{ px: 1.5, pt: 2 }}>
          {items
            .filter((item) => !item.hidden)
            .map((item) => (
              <ListItemButton
                key={item.path}
                selected={location.pathname.startsWith(item.path)}
                onClick={() => navigate(item.path)}
                sx={{ borderRadius: 999, mb: 0.5 }}
              >
                <ListItemIcon sx={{ minWidth: 36 }}>{item.icon}</ListItemIcon>
                <ListItemText primary={item.label} />
              </ListItemButton>
            ))}
        </List>
        <Divider sx={{ mx: 2, my: 1 }} />
        <List sx={{ px: 1.5 }}>
          <ListItemButton onClick={() => navigate('/me')} sx={{ borderRadius: 999 }}>
            <ListItemIcon sx={{ minWidth: 36 }}>
              <StorageIcon />
            </ListItemIcon>
            <ListItemText primary={t('nav.providers')} />
          </ListItemButton>
          <ListItemButton onClick={() => navigate('/admin')} sx={{ borderRadius: 999 }} disabled={isEndUserOnly}>
            <ListItemIcon sx={{ minWidth: 36 }}>
              <KeyIcon />
            </ListItemIcon>
            <ListItemText primary={t('nav.keys')} />
          </ListItemButton>
        </List>
      </Drawer>

      <Box component="main" flexGrow={1} p={3}>
        <Toolbar />
        <Routes>
          <Route path="/admin" element={isEndUserOnly ? <Navigate to="/me" replace /> : <AdminPage />} />
          <Route path="/me" element={<MePage />} />
          <Route path="*" element={<Navigate to={isEndUserOnly ? '/me' : '/admin'} replace />} />
        </Routes>
      </Box>
    </Box>
  );
}
