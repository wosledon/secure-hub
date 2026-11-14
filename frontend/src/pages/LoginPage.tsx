import { useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Stack,
  TextField,
  Typography
} from '@mui/material';
import { useTranslation } from 'react-i18next';
import { api, ApiError } from '../api';
import type { Session } from '../types';

export default function LoginPage({ onSuccess }: { onSuccess: (session: Session) => void }) {
  const { t } = useTranslation();
  const [userName, setUserName] = useState('admin');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const result = await api.login(userName, password);
      onSuccess(result.user);
    } catch (err) {
      const message = err instanceof ApiError ? err.message : String(err);
      setError(message || t('login.failed'));
    } finally {
      setBusy(false);
    }
  };

  return (
    <Box
      minHeight="100vh"
      display="flex"
      alignItems="center"
      justifyContent="center"
      bgcolor="background.default"
      p={2}
    >
      <Card sx={{ width: 400 }}>
        <CardContent sx={{ p: 4 }}>
          <Stack spacing={1} mb={3}>
            <Box
              sx={{
                width: 44,
                height: 44,
                borderRadius: '14px',
                background: 'linear-gradient(135deg,#6750A4,#9A82DB)'
              }}
            />
            <Typography variant="h1">{t('app.name')}</Typography>
            <Typography color="text.secondary">{t('login.title')}</Typography>
          </Stack>

          <form onSubmit={submit}>
            <Stack spacing={2}>
              <TextField
                label={t('login.userName')}
                value={userName}
                onChange={(e) => setUserName(e.target.value)}
                fullWidth
                autoComplete="username"
              />
              <TextField
                label={t('login.password')}
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                fullWidth
                autoComplete="current-password"
                autoFocus
              />
              {error && <Alert severity="error">{error}</Alert>}
              <Button type="submit" variant="contained" size="large" disabled={busy}>
                {t('login.submit')}
              </Button>
            </Stack>
          </form>
        </CardContent>
      </Card>
    </Box>
  );
}
