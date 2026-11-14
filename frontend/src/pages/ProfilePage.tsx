import { useEffect, useState } from 'react';
import {
  Alert,
  Avatar,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Divider,
  Stack,
  TextField,
  Typography
} from '@mui/material';
import ArrowBackIcon from '@mui/icons-material/ArrowBack';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import LockOutlinedIcon from '@mui/icons-material/LockOutlined';
import PhonelinkLockIcon from '@mui/icons-material/PhonelinkLock';
import SecurityIcon from '@mui/icons-material/Security';
import { Link as RouterLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api, type Profile } from '../api';
import { CodeBlock, CopyButton, ErrorState, PageHeader, StatusChip, TableSkeleton } from '../components/ui';
import { useFormatters } from '../utils/format';

export default function ProfilePage() {
  const { t } = useTranslation();
  const format = useFormatters();
  const [profile, setProfile] = useState<Profile | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [displayName, setDisplayName] = useState('');
  const [email, setEmail] = useState('');
  const [profileBusy, setProfileBusy] = useState(false);
  const [profileMessage, setProfileMessage] = useState<string | null>(null);
  const [profileError, setProfileError] = useState<string | null>(null);

  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [passwordBusy, setPasswordBusy] = useState(false);
  const [passwordMessage, setPasswordMessage] = useState<string | null>(null);
  const [passwordError, setPasswordError] = useState<string | null>(null);

  const [setup, setSetup] = useState<{ secret: string; otpauth_uri: string } | null>(null);
  const [code, setCode] = useState('');
  const [totpBusy, setTotpBusy] = useState(false);
  const [totpMessage, setTotpMessage] = useState<string | null>(null);
  const [totpError, setTotpError] = useState<string | null>(null);

  useEffect(() => {
    api.profile()
      .then((result) => {
        setProfile(result);
        setDisplayName(result.display_name);
        setEmail(result.email ?? '');
      })
      .catch((err) => setError((err as Error).message))
      .finally(() => setLoading(false));
  }, []);

  const saveProfile = async (event: React.FormEvent) => {
    event.preventDefault();
    setProfileBusy(true);
    setProfileError(null);
    setProfileMessage(null);
    try {
      const result = await api.updateProfile({
        displayName: displayName.trim(),
        email: email.trim()
      });
      setProfile(result);
      setDisplayName(result.display_name);
      setEmail(result.email ?? '');
      setProfileMessage(t('profile.saved'));
    } catch (err) {
      setProfileError((err as Error).message);
    } finally {
      setProfileBusy(false);
    }
  };

  const changePassword = async (event: React.FormEvent) => {
    event.preventDefault();
    setPasswordError(null);
    setPasswordMessage(null);

    if (newPassword.length < 12) {
      setPasswordError(t('profile.passwordTooShort'));
      return;
    }

    if (newPassword !== confirmPassword) {
      setPasswordError(t('profile.passwordMismatch'));
      return;
    }

    setPasswordBusy(true);
    try {
      await api.changePassword({ currentPassword, newPassword });
      setCurrentPassword('');
      setNewPassword('');
      setConfirmPassword('');
      setPasswordMessage(t('profile.changed'));
    } catch (err) {
      setPasswordError((err as Error).message);
    } finally {
      setPasswordBusy(false);
    }
  };

  const enableTotp = async () => {
    setTotpBusy(true);
    setTotpError(null);
    setTotpMessage(null);
    try {
      setSetup(await api.enableTotp());
    } catch (err) {
      setTotpError((err as Error).message);
    } finally {
      setTotpBusy(false);
    }
  };

  const activateTotp = async () => {
    setTotpBusy(true);
    setTotpError(null);
    try {
      await api.activateTotp(code);
      setSetup(null);
      setCode('');
      setTotpMessage(t('account.activated'));
      setProfile((current) => current ? { ...current, totp_enabled: true } : current);
    } catch (err) {
      setTotpError((err as Error).message);
    } finally {
      setTotpBusy(false);
    }
  };

  return (
    <Stack spacing={3}>
      <PageHeader
        title={t('profile.title')}
        description={t('profile.description')}
        actions={
          <Button component={RouterLink} to={profile?.roles.every((role) => role === 5) ? '/workspace' : '/overview'} startIcon={<ArrowBackIcon />} variant="outlined">
            {t('common.back')}
          </Button>
        }
      />
      {error && <ErrorState message={error} retry={() => window.location.reload()} />}
      {loading ? <Card><TableSkeleton rows={6} columns={3} /></Card> : profile && (
        <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(280px, 0.8fr) minmax(0, 1.6fr)' }, gap: 2, alignItems: 'start' }}>
          <Card>
            <CardContent>
              <Stack alignItems="center" textAlign="center" spacing={1.25}>
                <Avatar sx={{ width: 72, height: 72, bgcolor: 'primary.main', fontSize: 28, fontWeight: 700 }}>
                  {profile.display_name.slice(0, 1).toUpperCase()}
                </Avatar>
                <Box>
                  <Typography variant="h2">{profile.display_name}</Typography>
                  <Typography variant="body2" color="text.secondary">@{profile.user_name}</Typography>
                </Box>
                <Stack direction="row" spacing={0.75} flexWrap="wrap" useFlexGap justifyContent="center">
                  {profile.roles.map((role) => <Chip key={role} size="small" variant="outlined" label={t(`account.role${role}`)} />)}
                </Stack>
              </Stack>
              <Divider sx={{ my: 2.5 }} />
              <Stack spacing={1.5}>
                <ProfileFact label={t('profile.team')} value={profile.team_name ?? t('profile.noTeam')} />
                <ProfileFact label={t('profile.email')} value={profile.email ?? '—'} />
                <ProfileFact label={t('profile.joined')} value={format.fullDateTime(profile.created_at)} />
                <ProfileFact label={t('profile.sessionExpires')} value={format.fullDateTime(profile.session_expires_at)} />
                <Stack direction="row" justifyContent="space-between" alignItems="center">
                  <Typography variant="body2" color="text.secondary">{t('account.totp')}</Typography>
                  <StatusChip
                    status={profile.totp_enabled ? 'enabled' : 'disabled'}
                    label={t(profile.totp_enabled ? 'profile.totpEnabled' : 'profile.totpDisabled')}
                  />
                </Stack>
              </Stack>
            </CardContent>
          </Card>

          <Stack spacing={2}>
            <Card>
              <CardContent>
                <Stack direction="row" spacing={1} alignItems="center" mb={0.5}>
                  <SecurityIcon fontSize="small" color="primary" />
                  <Typography variant="h2">{t('profile.identity')}</Typography>
                </Stack>
                <Typography variant="body2" color="text.secondary" mb={2}>{t('profile.usernameHint')}</Typography>
                <Box component="form" onSubmit={saveProfile}>
                  <Stack spacing={2}>
                    <TextField label={t('login.userName')} value={profile.user_name} disabled />
                    <TextField
                      label={t('profile.displayName')}
                      name="displayName"
                      value={displayName}
                      onChange={(event) => setDisplayName(event.target.value)}
                      inputProps={{ maxLength: 100 }}
                      autoComplete="name"
                    />
                    <TextField
                      label={t('profile.email')}
                      name="email"
                      type="email"
                      value={email}
                      onChange={(event) => setEmail(event.target.value)}
                      helperText={t('profile.emailHint')}
                      inputProps={{ maxLength: 200 }}
                      autoComplete="email"
                    />
                    {profileError && <Alert severity="error">{profileError}</Alert>}
                    {profileMessage && <Alert severity="success" icon={<CheckCircleIcon />}>{profileMessage}</Alert>}
                    <Button type="submit" variant="contained" disabled={profileBusy || !displayName.trim()} sx={{ alignSelf: 'flex-start' }}>
                      {profileBusy ? t('common.working') : t('profile.save')}
                    </Button>
                  </Stack>
                </Box>
              </CardContent>
            </Card>

            <Card>
              <CardContent>
                <Stack direction="row" spacing={1} alignItems="center" mb={0.5}>
                  <LockOutlinedIcon fontSize="small" color="primary" />
                  <Typography variant="h2">{t('profile.password')}</Typography>
                </Stack>
                <Typography variant="body2" color="text.secondary" mb={2}>{t('profile.passwordHint')}</Typography>
                <Box component="form" onSubmit={changePassword}>
                  <Stack spacing={2}>
                    <TextField
                      label={t('profile.currentPassword')}
                      type="password"
                      value={currentPassword}
                      onChange={(event) => setCurrentPassword(event.target.value)}
                      autoComplete="current-password"
                    />
                    <TextField
                      label={t('profile.newPassword')}
                      type="password"
                      value={newPassword}
                      onChange={(event) => setNewPassword(event.target.value)}
                      autoComplete="new-password"
                      helperText={t('login.passwordHint', { count: 12 })}
                    />
                    <TextField
                      label={t('profile.confirmPassword')}
                      type="password"
                      value={confirmPassword}
                      onChange={(event) => setConfirmPassword(event.target.value)}
                      autoComplete="new-password"
                      error={!!confirmPassword && newPassword !== confirmPassword}
                      helperText={confirmPassword && newPassword !== confirmPassword ? t('profile.passwordMismatch') : undefined}
                    />
                    {passwordError && <Alert severity="error">{passwordError}</Alert>}
                    {passwordMessage && <Alert severity="success" icon={<CheckCircleIcon />}>{passwordMessage}</Alert>}
                    <Button
                      type="submit"
                      variant="contained"
                      disabled={passwordBusy || !currentPassword || !newPassword || !confirmPassword}
                      sx={{ alignSelf: 'flex-start' }}
                    >
                      {passwordBusy ? t('common.working') : t('profile.changePassword')}
                    </Button>
                  </Stack>
                </Box>
              </CardContent>
            </Card>

            <Card>
              <CardContent>
                <Stack direction="row" spacing={1} alignItems="center" mb={0.5}>
                  <PhonelinkLockIcon fontSize="small" color="primary" />
                  <Typography variant="h2">{t('account.totp')}</Typography>
                </Stack>
                <Typography variant="body2" color="text.secondary" mb={2}>{t('account.totpHint')}</Typography>
                {totpMessage && <Alert severity="success" sx={{ mb: 2 }}>{totpMessage}</Alert>}
                {!setup && !profile.totp_enabled && (
                  <Button variant="outlined" onClick={() => void enableTotp()} disabled={totpBusy}>
                    {totpBusy ? t('common.working') : t('account.enable')}
                  </Button>
                )}
                {profile.totp_enabled && !setup && (
                  <Stack direction="row" spacing={1} alignItems="center">
                    <StatusChip status="enabled" label={t('profile.totpEnabled')} />
                  </Stack>
                )}
                {setup && (
                  <Stack spacing={2}>
                    <Box sx={{ p: 2, bgcolor: 'action.hover', borderRadius: 2 }}>
                      <Stack direction="row" alignItems="center" justifyContent="space-between" spacing={2}>
                        <Box minWidth={0}>
                          <Typography variant="caption" color="text.secondary">{t('account.secret')}</Typography>
                          <Typography component="code" display="block" sx={{ wordBreak: 'break-all', userSelect: 'all' }}>{setup.secret}</Typography>
                        </Box>
                        <CopyButton text={setup.secret} iconOnly />
                      </Stack>
                    </Box>
                    <CodeBlock code={setup.otpauth_uri} label={t('account.otpauth')} />
                    <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1.5}>
                      <TextField
                        fullWidth
                        size="small"
                        label={t('account.code')}
                        value={code}
                        onChange={(event) => setCode(event.target.value.replace(/\D/g, '').slice(0, 6))}
                        inputProps={{ inputMode: 'numeric', autoComplete: 'one-time-code', pattern: '[0-9]*' }}
                      />
                      <Button variant="contained" onClick={() => void activateTotp()} disabled={totpBusy || code.length !== 6}>
                        {totpBusy ? t('common.working') : t('account.activate')}
                      </Button>
                    </Stack>
                  </Stack>
                )}
                {totpError && <Alert severity="error" sx={{ mt: 2 }}>{totpError}</Alert>}
              </CardContent>
            </Card>
          </Stack>
        </Box>
      )}
    </Stack>
  );
}

function ProfileFact({ label, value }: { label: string; value: string }) {
  return (
    <Stack direction="row" justifyContent="space-between" spacing={2} alignItems="baseline">
      <Typography variant="body2" color="text.secondary">{label}</Typography>
      <Typography variant="body2" sx={{ textAlign: 'right', wordBreak: 'break-word' }}>{value}</Typography>
    </Stack>
  );
}
