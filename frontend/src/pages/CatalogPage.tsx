import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Card,
  Chip,
  IconButton,
  InputAdornment,
  MenuItem,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TablePagination,
  TableRow,
  TextField,
  Tooltip,
  Typography
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import CheckIcon from '@mui/icons-material/Check';
import CloseIcon from '@mui/icons-material/Close';
import PsychologyIcon from '@mui/icons-material/Psychology';
import RefreshIcon from '@mui/icons-material/Refresh';
import SearchIcon from '@mui/icons-material/Search';
import SyncIcon from '@mui/icons-material/Sync';
import { useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api, type CatalogModelRow, type CatalogProviderRow } from '../api';
import { CopyButton, EmptyState, ErrorState, PageHeader, TableSkeleton } from '../components/ui';
import ProviderOnboardingDialog from '../components/ProviderOnboardingDialog';
import { useFormatters } from '../utils/format';

export default function CatalogPage({ readOnly = false }: { readOnly?: boolean }) {
  const { t } = useTranslation();
  const format = useFormatters();
  const [searchParams, setSearchParams] = useSearchParams();
  const provider = searchParams.get('provider') ?? '';
  const query = searchParams.get('q') ?? '';
  const [input, setInput] = useState(query);
  const [capability, setCapability] = useState('all');
  const [rows, setRows] = useState<CatalogModelRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [syncing, setSyncing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(25);
  const [catalogProviders, setCatalogProviders] = useState<CatalogProviderRow[]>([]);
  const [onboarding, setOnboarding] = useState(false);

  useEffect(() => setInput(query), [query]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await api.catalog({ provider: provider || undefined, q: query || undefined });
      setRows(result);
    } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, [provider, query]);
  useEffect(() => { void load(); }, [load]);
  useEffect(() => setPage(0), [provider, query, capability]);

  useEffect(() => {
    api.catalogProviders('admin').then(setCatalogProviders).catch(() => undefined);
  }, []);

  const providers = useMemo(
    () => catalogProviders.map((item) => ({ key: item.provider_key, name: item.name, count: item.model_count })),
    [catalogProviders]
  );
  const filtered = useMemo(() => rows.filter((row) => capability === 'all' || (capability === 'tools' ? row.supportsTools : row.supportsReasoning)), [rows, capability]);
  const visible = filtered.slice(page * pageSize, page * pageSize + pageSize);

  const updateFilters = (nextProvider: string, nextQuery: string) => {
    const next: Record<string, string> = {};
    if (nextProvider) next.provider = nextProvider;
    if (nextQuery.trim()) next.q = nextQuery.trim();
    setSearchParams(next);
  };

  const sync = async () => {
    setSyncing(true);
    setMessage(null);
    try {
      const result = await api.catalogSync();
      setMessage(result.ok ? t('catalog.syncSuccess', { providers: result.providers, models: result.models }) : (result.error ?? t('common.empty')));
      await load();
    } catch (err) { setError((err as Error).message); } finally { setSyncing(false); }
  };

  return (
    <Stack spacing={3}>
      <PageHeader title={t('catalog.title')} description={t('catalog.description')} actions={<>
        <Tooltip title={t('common.refresh')}><IconButton onClick={() => void load()} aria-label={t('common.refresh')}><RefreshIcon /></IconButton></Tooltip>
        {!readOnly && <Button variant="outlined" startIcon={<SyncIcon />} onClick={() => void sync()} disabled={syncing}>{syncing ? t('catalog.syncing') : t('catalog.sync')}</Button>}
        {!readOnly && <Button variant="contained" startIcon={<AddIcon />} onClick={() => setOnboarding(true)}>{t('providers.add')}</Button>}
      </>} />
      {error && <ErrorState message={error} retry={() => void load()} />}
      {message && <Alert severity="info" onClose={() => setMessage(null)}>{message}</Alert>}
      <Card>
        <Box component="form" onSubmit={(event) => { event.preventDefault(); updateFilters(provider, input); }} sx={{ p: 2, display: 'flex', gap: 1.5, flexWrap: 'wrap', borderBottom: 1, borderColor: 'divider' }}>
          <TextField size="small" value={input} onChange={(event) => setInput(event.target.value)} placeholder={t('catalog.search')} name="catalogSearch" autoComplete="off" sx={{ flex: '1 1 300px' }} InputProps={{ startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> }} />
          <TextField select size="small" label={t('common.provider')} value={provider} onChange={(event) => updateFilters(event.target.value, query)} sx={{ minWidth: 160 }}>
            <MenuItem value="">{t('catalog.allProviders')}</MenuItem>
            {providers.map((item) => <MenuItem value={item.key} key={item.key}>{item.name} · {item.key} ({item.count})</MenuItem>)}
          </TextField>
          <TextField select size="small" label={t('catalog.capabilities')} value={capability} onChange={(event) => setCapability(event.target.value)} sx={{ minWidth: 140 }}>
            <MenuItem value="all">{t('common.all')}</MenuItem><MenuItem value="tools">{t('catalog.tools')}</MenuItem><MenuItem value="reasoning">{t('catalog.reasoning')}</MenuItem>
          </TextField>
          <Button type="submit" variant="outlined">{t('common.search')}</Button>
        </Box>
        <Stack direction="row" justifyContent="space-between" alignItems="center" px={2.5} py={1.5}>
          <Typography variant="body2" color="text.secondary">{t('common.results', { count: filtered.length })}</Typography>
          <Typography variant="caption" color="text.secondary">{t('catalog.priceNote')}</Typography>
        </Stack>
        {loading ? <TableSkeleton rows={8} columns={6} /> : filtered.length === 0 ? <EmptyState title={t('catalog.empty')} description={t('catalog.emptyHint')} /> : (
          <Box sx={{ overflowX: 'auto' }}>
            <Table size="small" sx={{ minWidth: 1080 }}>
              <TableHead><TableRow><TableCell>{t('catalog.model')}</TableCell><TableCell>{t('catalog.capabilities')}</TableCell><TableCell align="right">{t('catalog.context')}</TableCell><TableCell align="right">{t('catalog.inputPrice')}</TableCell><TableCell align="right">{t('catalog.outputPrice')}</TableCell><TableCell>{t('catalog.synced')}</TableCell><TableCell align="right">{t('common.actions')}</TableCell></TableRow></TableHead>
              <TableBody>
                {visible.map((row) => (
                  <TableRow key={row.id} hover>
                    <TableCell sx={{ minWidth: 240, maxWidth: 320 }}><Stack direction="row" spacing={1.25} alignItems="center"><Box minWidth={0}><Typography variant="subtitle2" fontSize={13} noWrap title={row.model}>{row.model}</Typography><Typography variant="caption" color="text.secondary" noWrap>{row.provider}{row.family ? ` · ${row.family}` : ''}</Typography></Box></Stack></TableCell>
                    <TableCell sx={{ minWidth: 180 }}><Stack direction="row" spacing={0.75}>{row.supportsTools && <Chip size="small" icon={<CheckIcon />} label={t('catalog.tools')} variant="outlined" />}{row.supportsReasoning && <Chip size="small" icon={<PsychologyIcon />} label={t('catalog.reasoning')} variant="outlined" />}{!row.supportsTools && !row.supportsReasoning && <Chip size="small" icon={<CloseIcon />} label="—" variant="outlined" />}</Stack></TableCell>
                    <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{format.number(row.contextWindow)}</TableCell>
                    <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{format.currency(row.input_price)}</TableCell>
                    <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums', whiteSpace: 'nowrap' }}>{format.currency(row.output_price)}</TableCell>
                    <TableCell sx={{ whiteSpace: 'nowrap' }}>{format.dateTime(row.syncedAt)}</TableCell>
                    <TableCell align="right"><CopyButton text={row.model} iconOnly /></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Box>
        )}
        {!loading && filtered.length > 0 && <TablePagination component="div" count={filtered.length} page={Math.min(page, Math.max(0, Math.ceil(filtered.length / pageSize) - 1))} onPageChange={(_, next) => setPage(next)} rowsPerPage={pageSize} onRowsPerPageChange={(event) => { setPageSize(Number(event.target.value)); setPage(0); }} rowsPerPageOptions={[10, 25, 50]} labelRowsPerPage={t('common.rowsPerPage')} labelDisplayedRows={({ from, to, count }) => t('common.pageRange', { from, to, count })} getItemAriaLabel={(type) => t(`common.${type}Page`)} />}
      </Card>
      <ProviderOnboardingDialog open={onboarding} scope="admin" onClose={() => setOnboarding(false)} onCreated={() => void load()} />
    </Stack>
  );
}
