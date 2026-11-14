import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Avatar,
  Box,
  Button,
  Card,
  IconButton,
  InputAdornment,
  Stack,
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
import CloudQueueIcon from '@mui/icons-material/CloudQueue';
import RefreshIcon from '@mui/icons-material/Refresh';
import SearchIcon from '@mui/icons-material/Search';
import { Link as RouterLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api, type ProviderRow } from '../api';
import { CopyButton, EmptyState, ErrorState, PageHeader, StatusChip, TableSkeleton } from '../components/ui';
import ProviderOnboardingDialog from '../components/ProviderOnboardingDialog';

export default function ProvidersPage() {
  const { t } = useTranslation();
  const [rows, setRows] = useState<ProviderRow[]>([]);
  const [query, setQuery] = useState('');
  const [open, setOpen] = useState(false);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);

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

  return (
    <Stack spacing={3}>
      <PageHeader title={t('providers.title')} description={t('providers.description')} actions={<>
        <Tooltip title={t('common.refresh')}><IconButton onClick={() => void load()} aria-label={t('common.refresh')}><RefreshIcon /></IconButton></Tooltip>
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => { setOpen(true); setSuccess(false); }}>{t('providers.add')}</Button>
      </>} />
      {error && <ErrorState message={error} retry={() => void load()} />}
      {success && <Alert severity="success" onClose={() => setSuccess(false)}>{t('providers.created')}</Alert>}
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
                    <TableCell align="right"><Button component={RouterLink} to={`/catalog?provider=${encodeURIComponent(row.providerKey)}`} size="small" endIcon={<ArrowForwardIcon />}>{t('nav.catalog')}</Button></TableCell>
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
    </Stack>
  );
}
