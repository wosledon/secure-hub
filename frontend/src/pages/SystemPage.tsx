import { useCallback, useEffect, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Divider,
  FormControlLabel,
  IconButton,
  MenuItem,
  Stack,
  Switch,
  TextField,
  Tooltip,
  Typography
} from '@mui/material';
import CheckCircleOutlineIcon from '@mui/icons-material/CheckCircleOutline';
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import RefreshIcon from '@mui/icons-material/Refresh';
import WarningAmberIcon from '@mui/icons-material/WarningAmber';
import { useTranslation } from 'react-i18next';
import { api, type SystemBaseline, type SystemSettings } from '../api';
import { ErrorState, PageHeader, TableSkeleton } from '../components/ui';
import { useFormatters } from '../utils/format';

export default function SystemPage() {
  const { t } = useTranslation();
  const format = useFormatters();
  const [baseline, setBaseline] = useState<SystemBaseline | null>(null);
  const [settings, setSettings] = useState<SystemSettings | null>(null);
  const [runtimeValues, setRuntimeValues] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [runtimeError, setRuntimeError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try {
      const [baselineResult, settingsResult, runtimeResult] = await Promise.all([api.systemBaseline(), api.systemSettings(), api.runtimeSettings()]);
      setBaseline(baselineResult);
      setSettings(settingsResult);
      setRuntimeValues(runtimeResult.values);
    } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  const severityIcon = (severity: string) =>
    severity === 'critical' ? <ErrorOutlineIcon color="error" fontSize="small" />
      : severity === 'warning' ? <WarningAmberIcon color="warning" fontSize="small" />
        : <CheckCircleOutlineIcon color="success" fontSize="small" />;

  const update = (key: string, value: string) =>
    setRuntimeValues((current) => ({ ...current, [key]: value }));

  const saveRuntime = async () => {
    setSaving(true);
    setRuntimeError(null);
    try {
      const result = await api.updateRuntimeSettings({
        contentPersist: runtimeValues['content.persist'] === 'true',
        contentRawDays: Number(runtimeValues['content.raw_days']),
        contentRedactedDays: Number(runtimeValues['content.redacted_days']),
        contentCompressedDays: Number(runtimeValues['content.compressed_days']),
        contentMaxBytes: Number(runtimeValues['content.max_bytes']),
        guardrailsOnError: runtimeValues['guardrails.on_error'],
        guardrailsRedactionMode: runtimeValues['guardrails.redaction_mode'],
        guardrailsTokenRetentionHours: Number(runtimeValues['guardrails.token_retention_hours']),
        compressionEnabled: runtimeValues['compression.enabled'] === 'true',
        personalEnabled: runtimeValues['personal.enabled'] === 'true',
        personalAllowedProviders: (runtimeValues['personal.allowed_providers'] ?? '').split(',').map((value) => value.trim()).filter(Boolean),
        personalBlockedUsers: (runtimeValues['personal.blocked_users'] ?? '').split(',').map((value) => value.trim()).filter(Boolean)
      });
      setRuntimeValues(result.values);
      setMessage(t('system.saved'));
    } catch (err) { setRuntimeError((err as Error).message); } finally { setSaving(false); }
  };

  return (
    <Stack spacing={3}>
      <PageHeader
        title={t('system.title')}
        description={t('system.description')}
        actions={<Tooltip title={t('common.refresh')}><IconButton onClick={() => void load()} aria-label={t('common.refresh')}><RefreshIcon /></IconButton></Tooltip>}
      />
      {error && <ErrorState message={error} retry={() => void load()} />}

      {loading ? <Card><TableSkeleton rows={6} columns={3} /></Card> : (
        <Stack spacing={2}>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', lg: 'repeat(4, 1fr)' }, gap: 2 }}>
            <Card><CardContent><Typography variant="caption" color="text.secondary">{t('system.database')}</Typography><Typography variant="h3">{settings?.database.provider}</Typography><Typography variant="caption" color="text.secondary">{t('system.autoMigrate', { value: settings?.database.auto_migrate ? t('common.enabled') : t('common.disabled') })}</Typography></CardContent></Card>
            <Card><CardContent><Typography variant="caption" color="text.secondary">{t('system.https')}</Typography><Typography variant="h3">{settings?.security.require_https ? t('common.enabled') : t('common.disabled')}</Typography><Typography variant="caption" color="text.secondary">{settings?.security.admin_bind_address ?? '—'}</Typography></CardContent></Card>
            <Card><CardContent><Typography variant="caption" color="text.secondary">{t('system.selfRegistration')}</Typography><Typography variant="h3">{settings?.security.allow_self_registration ? t('common.enabled') : t('common.disabled')}</Typography><Typography variant="caption" color="text.secondary">{t('system.selfRegistrationHint')}</Typography></CardContent></Card>
            <Card><CardContent><Typography variant="caption" color="text.secondary">{t('system.byok')}</Typography><Typography variant="h3">{settings?.personal_providers.enabled ? t('common.enabled') : t('common.disabled')}</Typography><Typography variant="caption" color="text.secondary">{t('system.byokDetail', { allowed: settings?.personal_providers.allowed_providers.length ?? 0, blocked: settings?.personal_providers.blocked_users ?? 0 })}</Typography></CardContent></Card>
            <Card><CardContent><Typography variant="caption" color="text.secondary">{t('system.providers')}</Typography><Typography variant="h3">{settings?.providers.total ?? 0}</Typography><Typography variant="caption" color="text.secondary">{t('system.providerHint')}</Typography></CardContent></Card>
          </Box>

          <Card>
            <CardContent sx={{ pb: '12px !important' }}>
              <Stack direction="row" justifyContent="space-between" alignItems="center">
                <Box><Typography variant="h2">{t('system.baseline')}</Typography><Typography variant="body2" color="text.secondary" mt={0.5}>{t('system.baselineHint')}</Typography></Box>
                <Chip size="small" color={baseline?.healthy ? 'success' : 'error'} variant="outlined" label={baseline?.healthy ? t('system.healthy') : t('system.attention')} />
              </Stack>
            </CardContent>
            <Divider />
            <Stack spacing={0}>
              {baseline?.checks.map((check) => (
                <Stack key={check.id} direction="row" spacing={1.5} alignItems="flex-start" sx={{ px: 2.5, py: 1.75, borderBottom: 1, borderColor: 'divider', '&:last-child': { borderBottom: 0 } }}>
                  {severityIcon(check.severity)}
                  <Box minWidth={0} flexGrow={1}>
                    <Typography variant="subtitle2">{t(`system.check.${check.id}`, { defaultValue: check.id })}</Typography>
                    <Typography variant="body2" color="text.secondary" sx={{ wordBreak: 'break-word' }}>{check.message}</Typography>
                  </Box>
                  <Chip size="small" variant="outlined" label={check.severity} />
                </Stack>
              ))}
            </Stack>
            <Box px={2.5} py={1.5}><Typography variant="caption" color="text.secondary">{t('system.checkedAt', { time: format.fullDateTime(baseline?.checked_at ?? new Date()) })}</Typography></Box>
          </Card>

          <Card>
            <CardContent>
              <Stack direction="row" justifyContent="space-between" alignItems="flex-start" spacing={2}>
                <Box><Typography variant="h2">{t('system.runtime')}</Typography><Typography variant="body2" color="text.secondary" mt={0.5}>{t('system.runtimeHint')}</Typography></Box>
                <Button variant="contained" onClick={() => void saveRuntime()} disabled={saving}>{saving ? t('common.working') : t('common.save')}</Button>
              </Stack>
              {message && <Alert severity="success" onClose={() => setMessage(null)} sx={{ mt: 2 }}>{message}</Alert>}
              {runtimeError && <Alert severity="error" sx={{ mt: 2 }}>{runtimeError}</Alert>}
              <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', lg: 'repeat(3, 1fr)' }, gap: 2, mt: 2 }}>
                <FormControlLabel control={<Switch checked={runtimeValues['content.persist'] === 'true'} onChange={(event) => update('content.persist', event.target.checked ? 'true' : 'false')} />} label={t('system.persistToggle')} />
                <FormControlLabel control={<Switch checked={runtimeValues['compression.enabled'] === 'true'} onChange={(event) => update('compression.enabled', event.target.checked ? 'true' : 'false')} />} label={t('system.compressionToggle')} />
                <FormControlLabel control={<Switch checked={runtimeValues['personal.enabled'] === 'true'} onChange={(event) => update('personal.enabled', event.target.checked ? 'true' : 'false')} />} label={t('system.byokToggle')} />
                <TextField type="number" label={t('system.rawDays')} value={runtimeValues['content.raw_days'] ?? ''} onChange={(event) => update('content.raw_days', event.target.value)} inputProps={{ min: 1, max: 3650 }} />
                <TextField type="number" label={t('system.redactedDays')} value={runtimeValues['content.redacted_days'] ?? ''} onChange={(event) => update('content.redacted_days', event.target.value)} inputProps={{ min: 1, max: 3650 }} />
                <TextField type="number" label={t('system.compressedDays')} value={runtimeValues['content.compressed_days'] ?? ''} onChange={(event) => update('content.compressed_days', event.target.value)} inputProps={{ min: 1, max: 3650 }} />
                <TextField type="number" label={t('system.maxBytes')} value={runtimeValues['content.max_bytes'] ?? ''} onChange={(event) => update('content.max_bytes', event.target.value)} inputProps={{ min: 1024, max: 16777216 }} />
                <TextField select label={t('system.onError')} value={runtimeValues['guardrails.on_error'] ?? 'block'} onChange={(event) => update('guardrails.on_error', event.target.value)}>
                  <MenuItem value="block">block</MenuItem><MenuItem value="allow">allow</MenuItem>
                </TextField>
                <TextField select label={t('system.redactionMode')} value={runtimeValues['guardrails.redaction_mode'] ?? 'mask'} onChange={(event) => update('guardrails.redaction_mode', event.target.value)}>
                  <MenuItem value="mask">mask</MenuItem><MenuItem value="placeholder">placeholder</MenuItem><MenuItem value="tokenize">tokenize</MenuItem>
                </TextField>
                <TextField type="number" label={t('system.tokenHours')} value={runtimeValues['guardrails.token_retention_hours'] ?? ''} onChange={(event) => update('guardrails.token_retention_hours', event.target.value)} inputProps={{ min: 1, max: 720 }} />
                <TextField label={t('system.allowedProviders')} value={runtimeValues['personal.allowed_providers'] ?? ''} onChange={(event) => update('personal.allowed_providers', event.target.value)} helperText={t('system.csvHint')} />
                <TextField label={t('system.blockedUsers')} value={runtimeValues['personal.blocked_users'] ?? ''} onChange={(event) => update('personal.blocked_users', event.target.value)} helperText={t('system.csvHint')} />
              </Box>
            </CardContent>
          </Card>

          <Card>
            <CardContent>
              <Typography variant="h2">{t('system.effective')}</Typography>
              <Typography variant="body2" color="text.secondary" mt={0.5} mb={2}>{settings?.note}</Typography>
              <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)' }, gap: 2 }}>
                <Box><Typography variant="caption" color="text.secondary">{t('system.contentRetention')}</Typography><Typography variant="body1">{settings?.content.persist ? t('system.persistOn') : t('system.persistOff')}</Typography><Typography variant="caption" color="text.secondary">{settings && `${settings.content.raw_days}d / ${settings.content.redacted_days}d / ${settings.content.compressed_days}d`}</Typography></Box>
                <Box><Typography variant="caption" color="text.secondary">{t('system.guardrails')}</Typography><Typography variant="body1">{settings?.guardrails.on_error} · {settings?.guardrails.redaction_mode}</Typography><Typography variant="caption" color="text.secondary">{settings?.guardrails.token_retention_hours}h</Typography></Box>
                <Box><Typography variant="caption" color="text.secondary">{t('system.session')}</Typography><Typography variant="body1">{settings?.security.session_hours}h</Typography></Box>
                <Box><Typography variant="caption" color="text.secondary">{t('system.bodyLimit')}</Typography><Typography variant="body1">{format.compact(settings?.security.request_body_limit ?? 0)} B</Typography></Box>
              </Box>
              <Alert severity="info" sx={{ mt: 2 }}>{t('system.configOnly')}</Alert>
            </CardContent>
          </Card>
        </Stack>
      )}
    </Stack>
  );
}
