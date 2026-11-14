import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Avatar,
  Box,
  Button,
  Card,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  IconButton,
  InputAdornment,
  Stack,
  Switch,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  TextField,
  Tooltip,
  Typography
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import BlockIcon from '@mui/icons-material/Block';
import CloudQueueIcon from '@mui/icons-material/CloudQueue';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import NetworkCheckIcon from '@mui/icons-material/NetworkCheck';
import RefreshIcon from '@mui/icons-material/Refresh';
import SearchIcon from '@mui/icons-material/Search';
import VpnKeyIcon from '@mui/icons-material/VpnKey';
import { Link as RouterLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api, type ProviderRow } from '../api';
import { ConfirmDialog, CopyButton, EmptyState, ErrorState, PageHeader, StatusChip, TableSkeleton } from '../components/ui';
import ProviderOnboardingDialog from '../components/ProviderOnboardingDialog';

export default function ProvidersPage() {
  const { t } = useTranslation();
  const [rows, setRows] = useState<ProviderRow[]>([]);
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const [editTarget, setEditTarget] = useState<ProviderRow | null>(null);
  const [rotateTarget, setRotateTarget] = useState<ProviderRow | null>(null);
  const [disableTarget, setDisableTarget] = useState<ProviderRow | null>(null);
  const [busy, setBusy] = useState(false);
  const [testingId, setTestingId] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try { setRows(await api.providers()); } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  const filtered = useMemo(() => {
    const search = query.trim().toLowerCase();
    return rows.filter((row) => !search || `${row.alias} ${row.providerKey} ${row.baseUrl} ${row.displayName}`.toLowerCase().includes(search));
  }, [rows, query]);

  const test = async (row: ProviderRow) => {
    setTestingId(row.id);
    setMessage(null);
    try {
      const result = await api.testProvider(row.id);
      setMessage(result.healthy
        ? t('providers.testOk', { name: row.alias })
        : t('providers.testFailed', { name: row.alias, error: result.error ?? result.health_status }));
      await load();
    } catch (err) { setError((err as Error).message); } finally { setTestingId(null); }
  };

  const disable = async () => {
    if (!disableTarget) return;
    setBusy(true);
    try {
      await api.disableProvider(disableTarget.id);
      setDisableTarget(null);
      setMessage(t('providers.disabled', { name: disableTarget.alias }));
      await load();
    } catch (err) { setError((err as Error).message); } finally { setBusy(false); }
  };

  return (
    <Stack spacing={3}>
      <PageHeader title={t('providers.title')} description={t('providers.description')} actions={<>
        <Tooltip title={t('common.refresh')}><IconButton onClick={() => void load()} aria-label={t('common.refresh')}><RefreshIcon /></IconButton></Tooltip>
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => { setOpen(true); setSuccess(false); }}>{t('providers.add')}</Button>
      </>} />
      {error && <ErrorState message={error} retry={() => void load()} />}
      {success && <Alert severity="success" onClose={() => setSuccess(false)}>{t('providers.created')}</Alert>}
      {message && <Alert severity="info" onClose={() => setMessage(null)}>{message}</Alert>}
      <Card>
        <Box sx={{ p: 2, borderBottom: 1, borderColor: 'divider', display: 'flex', gap: 2, alignItems: 'center', flexWrap: 'wrap' }}>
          <Box sx={{ flexGrow: 1 }}><Typography variant="h2">{t('providers.listTitle')}</Typography><Typography variant="caption" color="text.secondary">{t('common.results', { count: filtered.length })}</Typography></Box>
          <TextField size="small" value={query} onChange={(event) => setQuery(event.target.value)} placeholder={t('providers.search')} name="providerSearch" autoComplete="off" sx={{ width: { xs: '100%', sm: 320 } }} InputProps={{ startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> }} />
        </Box>
        {loading ? <TableSkeleton rows={5} columns={5} /> : filtered.length === 0 ? (
          <EmptyState title={rows.length ? t('common.empty') : t('providers.empty')} description={rows.length ? undefined : t('providers.emptyHint')} action={!rows.length && <Button variant="contained" startIcon={<AddIcon />} onClick={() => setOpen(true)}>{t('providers.add')}</Button>} />
        ) : (
          <Box sx={{ overflowX: 'auto' }}>
            <Table>
              <TableHead><TableRow><TableCell>{t('common.alias')}</TableCell><TableCell>{t('common.provider')}</TableCell><TableCell>{t('common.baseUrl')}</TableCell><TableCell>Scope</TableCell><TableCell>{t('common.status')}</TableCell><TableCell align="right">{t('common.actions')}</TableCell></TableRow></TableHead>
              <TableBody>
                {filtered.map((row) => (
                  <TableRow key={row.id} hover>
                    <TableCell><Stack direction="row" spacing={1.5} alignItems="center"><Avatar sx={{ width: 34, height: 34, bgcolor: 'primary.main', fontSize: 12 }}><CloudQueueIcon sx={{ fontSize: 18 }} /></Avatar><Box><Typography variant="subtitle2" fontSize={13}>{row.alias}</Typography><Typography variant="caption" color="text.secondary">{row.displayName}</Typography></Box></Stack></TableCell>
                    <TableCell><Box component="code" sx={{ fontSize: 12 }}>{row.providerKey}</Box></TableCell>
                    <TableCell sx={{ maxWidth: 360 }}><Stack direction="row" alignItems="center" spacing={0.5}><Typography variant="body2" color="text.secondary" noWrap>{row.baseUrl}</Typography><CopyButton text={row.baseUrl} iconOnly /></Stack></TableCell>
                    <TableCell><StatusChip status={row.scope} /></TableCell>
                    <TableCell><StatusChip status={row.enabled ? row.healthStatus : 'disabled'} /></TableCell>
                    <TableCell align="right">
                      <Stack direction="row" spacing={0.5} justifyContent="flex-end">
                        <Tooltip title={t('providers.test')}>
                          <span><IconButton size="small" onClick={() => void test(row)} disabled={testingId === row.id} aria-label={t('providers.test')}>{testingId === row.id ? <CircularProgress size={16} /> : <NetworkCheckIcon fontSize="small" />}</IconButton></span>
                        </Tooltip>
                        <Tooltip title={t('common.configure')}><IconButton size="small" onClick={() => setEditTarget(row)} aria-label={t('common.configure')}><EditOutlinedIcon fontSize="small" /></IconButton></Tooltip>
                        <Tooltip title={t('providers.rotate')}><IconButton size="small" onClick={() => setRotateTarget(row)} aria-label={t('providers.rotate')}><VpnKeyIcon fontSize="small" /></IconButton></Tooltip>
                        <Tooltip title={t('providers.disable')}><span><IconButton size="small" color="error" onClick={() => setDisableTarget(row)} disabled={!row.enabled} aria-label={t('providers.disable')}><BlockIcon fontSize="small" /></IconButton></span></Tooltip>
                        <Button component={RouterLink} to={`/catalog?provider=${encodeURIComponent(row.providerKey)}`} size="small" endIcon={<ArrowForwardIcon />}>{t('nav.catalog')}</Button>
                      </Stack>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Box>
        )}
      </Card>

      <Alert severity="info">{t('onboarding.catalogSourceNote')}</Alert>

      <ProviderOnboardingDialog
        open={open}
        scope="admin"
        onClose={() => setOpen(false)}
        onCreated={() => { setSuccess(true); void load(); }}
      />

      {editTarget && (
        <EditProviderDialog
          provider={editTarget}
          onClose={() => setEditTarget(null)}
          onSaved={(text) => { setEditTarget(null); setMessage(text); void load(); }}
        />
      )}
      {rotateTarget && (
        <RotateProviderDialog
          provider={rotateTarget}
          onClose={() => setRotateTarget(null)}
          onSaved={(text) => { setRotateTarget(null); setMessage(text); void load(); }}
        />
      )}
      <ConfirmDialog
        open={!!disableTarget}
        title={t('providers.disableTitle')}
        description={t('providers.disableHint', { name: disableTarget?.alias ?? '' })}
        confirmLabel={t('providers.disable')}
        busy={busy}
        danger
        onClose={() => setDisableTarget(null)}
        onConfirm={() => void disable()}
      />
    </Stack>
  );
}

function EditProviderDialog({ provider, onClose, onSaved }: {
  provider: ProviderRow;
  onClose: () => void;
  onSaved: (message: string) => void;
}) {
  const { t } = useTranslation();
  const [displayName, setDisplayName] = useState(provider.displayName);
  const [baseUrl, setBaseUrl] = useState(provider.baseUrl);
  const [enabled, setEnabled] = useState(provider.enabled);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setBusy(true); setError(null);
    try {
      await api.updateProvider(provider.id, { displayName: displayName.trim(), baseUrl: baseUrl.trim(), enabled });
      onSaved(t('providers.updated', { name: provider.alias }));
    } catch (err) { setError((err as Error).message); } finally { setBusy(false); }
  };

  return (
    <Dialog open onClose={busy ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t('providers.editTitle', { name: provider.alias })}</DialogTitle>
      <DialogContent>
        <Stack spacing={2.25} mt={1}>
          <TextField label={t('common.name')} value={displayName} onChange={(event) => setDisplayName(event.target.value)} name="displayName" autoComplete="off" />
          <TextField label={t('common.baseUrl')} value={baseUrl} onChange={(event) => setBaseUrl(event.target.value)} name="baseUrl" autoComplete="off" spellCheck={false} />
          <FormControlLabel control={<Switch checked={enabled} onChange={(event) => setEnabled(event.target.checked)} />} label={t('common.enabled')} />
          {provider.lastTestError && <Alert severity="warning">{provider.lastTestError}</Alert>}
          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
        <Button variant="contained" onClick={() => void submit()} disabled={busy || !displayName.trim() || !baseUrl.trim()}>{busy ? t('common.working') : t('common.save')}</Button>
      </DialogActions>
    </Dialog>
  );
}

function RotateProviderDialog({ provider, onClose, onSaved }: {
  provider: ProviderRow;
  onClose: () => void;
  onSaved: (message: string) => void;
}) {
  const { t } = useTranslation();
  const [apiKey, setApiKey] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setBusy(true); setError(null);
    try {
      await api.rotateProviderCredential(provider.id, apiKey.trim());
      onSaved(t('providers.rotated', { name: provider.alias }));
    } catch (err) { setError((err as Error).message); } finally { setBusy(false); }
  };

  return (
    <Dialog open onClose={busy ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>{t('providers.rotateTitle', { name: provider.alias })}</DialogTitle>
      <DialogContent>
        <Stack spacing={2} mt={1}>
          <Alert severity="warning">{t('providers.rotateHint')}</Alert>
          <TextField label={t('common.apiKey')} type="password" value={apiKey} onChange={(event) => setApiKey(event.target.value)} autoComplete="new-password" required />
          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
        <Button variant="contained" onClick={() => void submit()} disabled={busy || !apiKey.trim()}>{busy ? t('common.working') : t('providers.rotate')}</Button>
      </DialogActions>
    </Dialog>
  );
}
