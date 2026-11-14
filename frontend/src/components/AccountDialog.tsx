import { useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  Stack,
  TextField,
  Typography
} from '@mui/material';
import PhonelinkLockIcon from '@mui/icons-material/PhonelinkLock';
import { useTranslation } from 'react-i18next';
import { api } from '../api';
import type { Session } from '../types';
import { CodeBlock, CopyButton } from './ui';

export default function AccountDialog({ session, open, onClose }: { session: Session; open: boolean; onClose: () => void }) {
  const { t } = useTranslation();
  const [setup, setSetup] = useState<{ secret: string; otpauth_uri: string } | null>(null);
  const [code, setCode] = useState('');
  const [busy, setBusy] = useState(false);
  const [activated, setActivated] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const enable = async () => {
    setBusy(true);
    setError(null);
    try {
      setSetup(await api.enableTotp());
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const activate = async () => {
    setBusy(true);
    setError(null);
    try {
      await api.activateTotp(code);
      setActivated(true);
      setSetup(null);
      setCode('');
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog open={open} onClose={onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t('nav.account')}</DialogTitle>
      <DialogContent>
        <Stack spacing={3} mt={1}>
          <Box>
            <Typography variant="overline" color="text.secondary">{t('account.signedInAs')}</Typography>
            <Typography variant="h2" mt={0.5}>{session.user_name}</Typography>
            <Stack direction="row" spacing={0.75} flexWrap="wrap" useFlexGap mt={1.25}>
              {session.roles.map((role) => <Chip key={role} size="small" variant="outlined" label={t(`account.role${role}`)} />)}
            </Stack>
            {session.team_id && (
              <Typography variant="caption" color="text.secondary" display="block" mt={1.5} sx={{ wordBreak: 'break-all' }}>
                {t('account.team')}: {session.team_id}
              </Typography>
            )}
          </Box>
          <Divider />
          <Box>
            <Stack direction="row" spacing={1} alignItems="center" mb={1}>
              <PhonelinkLockIcon color="primary" fontSize="small" />
              <Typography variant="h2">{t('account.totp')}</Typography>
            </Stack>
            <Typography variant="body2" color="text.secondary" mb={2}>{t('account.totpHint')}</Typography>
            {activated && <Alert severity="success">{t('account.activated')}</Alert>}
            {!setup && !activated && (
              <Button variant="outlined" onClick={() => void enable()} disabled={busy}>
                {busy ? t('common.working') : t('account.enable')}
              </Button>
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
                    name="totpCode"
                    label={t('account.code')}
                    value={code}
                    onChange={(event) => setCode(event.target.value.replace(/\D/g, '').slice(0, 6))}
                    inputProps={{ inputMode: 'numeric', autoComplete: 'one-time-code', pattern: '[0-9]*' }}
                  />
                  <Button variant="contained" onClick={() => void activate()} disabled={busy || code.length !== 6}>
                    {busy ? t('common.working') : t('account.activate')}
                  </Button>
                </Stack>
              </Stack>
            )}
            {error && <Alert severity="error" sx={{ mt: 2 }}>{error}</Alert>}
          </Box>
        </Stack>
      </DialogContent>
      <DialogActions><Button onClick={onClose}>{t('common.close')}</Button></DialogActions>
    </Dialog>
  );
}
