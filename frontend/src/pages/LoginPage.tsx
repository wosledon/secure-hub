import { useEffect, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Divider,
  InputAdornment,
  Stack,
  TextField,
  Typography
} from '@mui/material';
import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import PersonOutlineIcon from '@mui/icons-material/PersonOutline';
import PinOutlinedIcon from '@mui/icons-material/PinOutlined';
import { useLocation, useNavigate } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api, ApiError, type RegistrationPolicy } from '../api';
import type { Session } from '../types';
import { BrandLockup } from '../components/Brand';

export default function LoginPage({ onSuccess }: { onSuccess: (session: Session) => void }) {
  const { t } = useTranslation();
  const location = useLocation();
  const navigate = useNavigate();
  const registerMode = location.pathname === '/register';
  const [userName, setUserName] = useState('admin');
  const [password, setPassword] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [policy, setPolicy] = useState<RegistrationPolicy | null>(null);
  const [totpCode, setTotpCode] = useState('');
  const [showTotp, setShowTotp] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    api.registrationPolicy()
      .then(setPolicy)
      .catch(() => setPolicy({ enabled: false, min_password_length: 12 }));
  }, []);

  useEffect(() => {
    setError(null);
    setShowTotp(false);
    setPassword('');
    setConfirmPassword('');
    setTotpCode('');
    if (registerMode) setUserName('');
  }, [registerMode]);

  const minimumPassword = policy?.min_password_length ?? 12;

  const submit = async (event: React.FormEvent) => {
    event.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = await api.login(userName, password, showTotp ? totpCode : undefined);
      onSuccess(result.user);
    } catch (err) {
      if (err instanceof ApiError && err.code.toLowerCase().includes('totp')) setShowTotp(true);
      setError(err instanceof ApiError ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  };

  const submitRegister = async (event: React.FormEvent) => {
    event.preventDefault();
    setError(null);

    if (password !== confirmPassword) {
      setError(t('login.passwordMismatch'));
      return;
    }

    if (password.length < minimumPassword) {
      setError(t('login.passwordHint', { count: minimumPassword }));
      return;
    }

    setBusy(true);
    try {
      const result = await api.register({
        userName: userName.trim(),
        password,
        displayName: displayName.trim() || undefined,
        email: email.trim() || undefined
      });
      onSuccess(result.user);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : String(err));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Box sx={{ minHeight: '100vh', display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(480px, 1.1fr) minmax(480px, 0.9fr)' }, bgcolor: 'background.paper' }}>
      <Box
        sx={{
          display: { xs: 'none', lg: 'flex' }, flexDirection: 'column', position: 'relative', overflow: 'hidden',
          p: { lg: 6, xl: 8 }, color: '#FFF',
          background: 'radial-gradient(circle at 82% 12%, rgba(99,102,241,0.3), transparent 33%), radial-gradient(circle at 10% 90%, rgba(20,184,166,0.13), transparent 32%), linear-gradient(145deg, #111827 0%, #171B34 52%, #1E1B4B 100%)'
        }}
      >
        <Box sx={{ position: 'absolute', inset: 0, opacity: 0.12, backgroundImage: 'linear-gradient(rgba(255,255,255,.16) 1px, transparent 1px), linear-gradient(90deg, rgba(255,255,255,.16) 1px, transparent 1px)', backgroundSize: '64px 64px', maskImage: 'linear-gradient(to bottom right, black, transparent 80%)' }} />
        <Box sx={{ position: 'relative' }}><BrandLockup inverse subtitle={t('app.tagline')} /></Box>
        <Stack sx={{ position: 'relative', my: 'auto', maxWidth: 620 }} spacing={3.5}>
          <Box>
            <Typography variant="overline" sx={{ color: '#A5B4FC' }}>{t('login.eyebrow')}</Typography>
            <Typography component="h1" sx={{ fontSize: { lg: '2.6rem', xl: '3.2rem' }, lineHeight: 1.16, fontWeight: 700, letterSpacing: '-0.04em', textWrap: 'balance', mt: 2 }}>
              {t('login.title')}
            </Typography>
            <Typography sx={{ color: 'rgba(226,232,240,0.7)', fontSize: '1rem', lineHeight: 1.8, maxWidth: 560, mt: 2.5 }}>
              {t('login.subtitle')}
            </Typography>
          </Box>
          <Stack spacing={1.75}>
            {[t('login.featureOne'), t('login.featureTwo'), t('login.featureThree')].map((feature) => (
              <Stack direction="row" spacing={1.25} alignItems="center" key={feature}>
                <CheckCircleOutlineIcon sx={{ color: '#5EEAD4', fontSize: 20 }} />
                <Typography variant="body2" sx={{ color: 'rgba(239,246,255,0.82)' }}>{feature}</Typography>
              </Stack>
            ))}
          </Stack>
        </Stack>
        <Typography variant="caption" sx={{ position: 'relative', color: 'rgba(226,232,240,0.4)', letterSpacing: '0.02em' }}>
          {t('login.footer')}
        </Typography>
      </Box>

      <Box sx={{ display: 'flex', flexDirection: 'column', minHeight: { xs: '100vh', lg: 'auto' } }}>
        <Box sx={{ display: { lg: 'none' }, p: 3 }}><BrandLockup /></Box>
        <Box sx={{ flex: 1, display: 'grid', placeItems: 'center', p: { xs: 3, sm: 6 } }}>
          <Box sx={{ width: '100%', maxWidth: 400 }}>
            <Box component="form" onSubmit={registerMode ? submitRegister : submit} noValidate>
              <Typography variant="h1" component="h1">{registerMode ? t('login.registerTitle') : t('login.signInTitle')}</Typography>
              <Typography variant="body1" color="text.secondary" mt={1} mb={4}>{registerMode ? t('login.registerSubtitle') : t('login.signInSubtitle')}</Typography>

              {registerMode && policy && !policy.enabled ? (
                <Stack spacing={2}>
                  <Alert severity="info">
                    <Typography variant="subtitle2" mb={0.5}>{t('login.registrationDisabled')}</Typography>
                    {t('login.registrationDisabledHint')}
                  </Alert>
                  <Button variant="outlined" onClick={() => navigate('/login')}>{t('login.backToLogin')}</Button>
                </Stack>
              ) : (
                <Stack spacing={2.25}>
                  {registerMode && (
                    <TextField
                      fullWidth
                      label={t('login.displayName')}
                      name="displayName"
                      value={displayName}
                      onChange={(event) => setDisplayName(event.target.value)}
                      autoComplete="name"
                      helperText={t('common.optional')}
                      slotProps={{ input: { startAdornment: <InputAdornment position="start"><PersonOutlineIcon fontSize="small" /></InputAdornment> } }}
                    />
                  )}
                  {registerMode && (
                    <TextField
                      fullWidth
                      label={t('login.email')}
                      name="email"
                      type="email"
                      value={email}
                      onChange={(event) => setEmail(event.target.value)}
                      autoComplete="email"
                      helperText={t('common.optional')}
                    />
                  )}
                  <TextField
                    fullWidth
                    label={t('login.userName')}
                    name="username"
                    value={userName}
                    onChange={(event) => setUserName(event.target.value)}
                    autoComplete="username"
                    spellCheck={false}
                    slotProps={{ input: { startAdornment: <InputAdornment position="start"><PersonOutlineIcon fontSize="small" /></InputAdornment> } }}
                  />
                  <TextField
                    fullWidth
                    label={t('login.password')}
                    name="password"
                    type="password"
                    value={password}
                    onChange={(event) => setPassword(event.target.value)}
                    autoComplete={registerMode ? 'new-password' : 'current-password'}
                    autoFocus={!registerMode}
                    helperText={registerMode ? t('login.passwordHint', { count: minimumPassword }) : undefined}
                    slotProps={{ input: { startAdornment: <InputAdornment position="start"><LockOutlinedIcon fontSize="small" /></InputAdornment> } }}
                  />
                  {registerMode && (
                    <TextField
                      fullWidth
                      label={t('login.confirmPassword')}
                      name="confirmPassword"
                      type="password"
                      value={confirmPassword}
                      onChange={(event) => setConfirmPassword(event.target.value)}
                      autoComplete="new-password"
                      error={!!confirmPassword && password !== confirmPassword}
                      helperText={confirmPassword && password !== confirmPassword ? t('login.passwordMismatch') : undefined}
                      slotProps={{ input: { startAdornment: <InputAdornment position="start"><LockOutlinedIcon fontSize="small" /></InputAdornment> } }}
                    />
                  )}
                  {!registerMode && !showTotp && (
                    <Button size="small" onClick={() => setShowTotp(true)} sx={{ alignSelf: 'flex-start', px: 0 }}>{t('login.useTotp')}</Button>
                  )}
                  {!registerMode && showTotp && (
                    <Box>
                      <Alert severity="info" sx={{ mb: 2 }}>{t('login.totpHint')}</Alert>
                      <TextField
                        fullWidth
                        label={t('login.totpCode')}
                        name="totpCode"
                        value={totpCode}
                        onChange={(event) => setTotpCode(event.target.value.replace(/\D/g, '').slice(0, 6))}
                        inputProps={{ inputMode: 'numeric', autoComplete: 'one-time-code', pattern: '[0-9]*' }}
                        slotProps={{ input: { startAdornment: <InputAdornment position="start"><PinOutlinedIcon fontSize="small" /></InputAdornment> } }}
                      />
                    </Box>
                  )}
                  {error && <Alert severity="error" aria-live="polite">{error || t('login.failed')}</Alert>}
                  <Button
                    type="submit"
                    variant="contained"
                    size="large"
                    endIcon={<ArrowForwardIcon />}
                    disabled={busy ||
                      (registerMode
                        ? !userName.trim() || !password || !confirmPassword || password !== confirmPassword || policy?.enabled === false
                        : !userName || !password || (showTotp && totpCode.length !== 6))}
                    sx={{ minHeight: 46 }}
                  >
                    {busy
                      ? t(registerMode ? 'login.registering' : 'login.signingIn')
                      : t(registerMode ? 'login.registerSubmit' : 'login.submit')}
                  </Button>
                </Stack>
              )}

              <Typography variant="body2" color="text.secondary" textAlign="center" mt={2.5}>
                {registerMode ? t('login.haveAccount') : t('login.noAccount')}{' '}
                <Button size="small" onClick={() => navigate(registerMode ? '/login' : '/register')}>
                  {registerMode ? t('login.backToLogin') : t('login.createAccount')}
                </Button>
              </Typography>
            </Box>
            <Divider sx={{ my: 4 }} />
            <Stack direction="row" spacing={1} alignItems="center" justifyContent="center">
              <Box sx={{ width: 6, height: 6, borderRadius: '50%', bgcolor: 'success.main' }} />
              <Typography variant="caption" color="text.secondary">{t('app.name')} · 127.0.0.1</Typography>
            </Stack>
          </Box>
        </Box>
        <Typography variant="caption" color="text.secondary" textAlign="center" p={3} display={{ lg: 'none' }}>{t('login.footer')}</Typography>
      </Box>
    </Box>
  );
}
