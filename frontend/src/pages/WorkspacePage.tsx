import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Avatar,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Stack,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Tabs,
  Typography
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import CloudQueueIcon from '@mui/icons-material/CloudQueue';
import CurrencyExchangeIcon from '@mui/icons-material/CurrencyExchange';
import KeyIcon from '@mui/icons-material/Key';
import MemoryIcon from '@mui/icons-material/Memory';
import ReceiptLongIcon from '@mui/icons-material/ReceiptLong';
import TokenOutlinedIcon from '@mui/icons-material/TokenOutlined';
import { useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api, type ModelRow, type MyKeyRow, type MyProviderRow, type UsageRow } from '../api';
import { CodeBlock, CopyButton, EmptyState, ErrorState, PageHeader, SecretDialog, StatCard, StatusChip, TableSkeleton } from '../components/ui';
import ProviderOnboardingDialog from '../components/ProviderOnboardingDialog';
import { useFormatters } from '../utils/format';

const DEVKEY_STORAGE = 'sh.devkey';
const tabs = ['usage', 'providers', 'keys', 'models'] as const;

export default function WorkspacePage() {
  const { t } = useTranslation();
  const [searchParams, setSearchParams] = useSearchParams();
  const active = tabs.includes(searchParams.get('tab') as typeof tabs[number]) ? searchParams.get('tab')! : 'usage';
  return (
    <Stack spacing={3}>
      <PageHeader title={t('me.title')} description={t('me.description')} />
      <Tabs value={active} onChange={(_, value) => setSearchParams(value === 'usage' ? {} : { tab: value })} variant="scrollable" sx={{ borderBottom: 1, borderColor: 'divider' }}>
        <Tab value="usage" label={t('me.usage')} /><Tab value="providers" label={t('me.providers')} /><Tab value="keys" label={t('me.keys')} /><Tab value="models" label={t('me.models')} />
      </Tabs>
      {active === 'usage' && <MyUsage />}
      {active === 'providers' && <MyProviders />}
      {active === 'keys' && <MyKeys />}
      {active === 'models' && <MyModels />}
    </Stack>
  );
}

function MyUsage() {
  const { t } = useTranslation();
  const format = useFormatters();
  const [rows, setRows] = useState<UsageRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try { setRows((await api.myUsage()).data); } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);
  const totals = rows.reduce((sum, row) => ({ requests: sum.requests + row.requests, tokens: sum.tokens + row.input_tokens + row.output_tokens, cost: sum.cost + row.cost_usd }), { requests: 0, tokens: 0, cost: 0 });
  return (
    <Stack spacing={3}>
      {error && <ErrorState message={error} retry={() => void load()} />}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(3, 1fr)' }, gap: 2 }}>
        <StatCard label={t('usage.totalRequests')} value={loading ? '—' : format.number(totals.requests)} caption={t('common.last30Days')} icon={<ReceiptLongIcon fontSize="small" />} />
        <StatCard label={t('usage.totalTokens')} value={loading ? '—' : format.compact(totals.tokens)} caption={t('common.last30Days')} icon={<TokenOutlinedIcon fontSize="small" />} tone="secondary" />
        <StatCard label={t('usage.totalCost')} value={loading ? '—' : format.currency(totals.cost)} caption={t('me.billingUser')} icon={<CurrencyExchangeIcon fontSize="small" />} tone="success" />
      </Box>
      <Card><CardContent sx={{ pb: '16px !important' }}><Typography variant="h2">{t('me.usage')}</Typography><Typography variant="body2" color="text.secondary" mt={0.5}>{t('common.last30Days')}</Typography></CardContent>
        {loading ? <TableSkeleton rows={4} columns={5} /> : rows.length === 0 ? <EmptyState title={t('me.usageEmpty')} /> : <Box sx={{ overflowX: 'auto' }}><Table><TableHead><TableRow><TableCell>{t('audit.model')}</TableCell><TableCell align="right">{t('usage.requests')}</TableCell><TableCell align="right">{t('usage.input')}</TableCell><TableCell align="right">{t('usage.output')}</TableCell><TableCell align="right">{t('usage.cost')}</TableCell></TableRow></TableHead><TableBody>{rows.map((row) => <TableRow key={row.model} hover><TableCell><Box component="code" sx={{ fontSize: 12 }}>{row.model}</Box></TableCell><TableCell align="right">{format.number(row.requests)}</TableCell><TableCell align="right">{format.number(row.input_tokens)}</TableCell><TableCell align="right">{format.number(row.output_tokens)}</TableCell><TableCell align="right">{format.currency(row.cost_usd)}</TableCell></TableRow>)}</TableBody></Table></Box>}
      </Card>
      <QuickStart />
    </Stack>
  );
}

function QuickStart({ model }: { model?: string }) {
  const { t } = useTranslation();
  const code = `curl ${window.location.origin}/v1/chat/completions \\\n  -H "Authorization: Bearer $SECUREHUB_KEY" \\\n  -H "Content-Type: application/json" \\\n  -d '{\n    "model": "${model ?? 'me/<provider>/<model>'}",\n    "messages": [{"role":"user","content":"Hello"}]\n  }'`;
  return <Card><CardContent><Typography variant="h2" mb={0.5}>{t('me.quickStart')}</Typography><Typography variant="body2" color="text.secondary" mb={2}>{t('me.quickStartHint')}</Typography><CodeBlock code={code} /></CardContent></Card>;
}

function MyProviders() {
  const { t } = useTranslation();
  const [rows, setRows] = useState<MyProviderRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [open, setOpen] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try { setRows(await api.myProviders()); } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);
  return (
    <Stack spacing={3}>
      {error && <ErrorState message={error} retry={() => void load()} />}
      <Alert severity="info">{t('me.personalDisclosure')}</Alert>
      <Card><CardContent sx={{ pb: '16px !important' }}><Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ xs: 'stretch', sm: 'flex-start' }} spacing={2}><Box><Typography variant="h2">{t('me.providers')}</Typography><Typography variant="body2" color="text.secondary" mt={0.5}>{t('me.byokHint')}</Typography></Box><Button variant="contained" startIcon={<AddIcon />} onClick={() => setOpen(true)}>{t('me.createByok')}</Button></Stack></CardContent>
        {loading ? <TableSkeleton rows={4} columns={4} /> : rows.length === 0 ? <EmptyState title={t('me.providerEmpty')} action={<Button variant="contained" startIcon={<AddIcon />} onClick={() => setOpen(true)}>{t('me.addFirst')}</Button>} /> : <Box sx={{ overflowX: 'auto' }}><Table><TableHead><TableRow><TableCell>{t('common.alias')}</TableCell><TableCell>{t('common.provider')}</TableCell><TableCell>{t('common.baseUrl')}</TableCell><TableCell>{t('common.models')}</TableCell></TableRow></TableHead><TableBody>{rows.map((row) => <TableRow key={row.id} hover><TableCell><Stack direction="row" spacing={1.25} alignItems="center"><Avatar sx={{ width: 32, height: 32, bgcolor: 'secondary.main' }}><CloudQueueIcon sx={{ fontSize: 17 }} /></Avatar><Typography variant="subtitle2">{row.alias}</Typography></Stack></TableCell><TableCell><Box component="code">{row.providerKey}</Box></TableCell><TableCell sx={{ maxWidth: 300 }}><Typography variant="body2" noWrap>{row.baseUrl}</Typography></TableCell><TableCell><Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>{row.models.map((model) => <Chip key={model} size="small" variant="outlined" label={model} />)}</Stack></TableCell></TableRow>)}</TableBody></Table></Box>}
      </Card>
      <ProviderOnboardingDialog open={open} scope="me" onClose={() => setOpen(false)} onCreated={() => void load()} />
    </Stack>
  );
}

function MyKeys() {
  const { t } = useTranslation();
  const format = useFormatters();
  const [rows, setRows] = useState<MyKeyRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [secret, setSecret] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try { setRows(await api.myKeys()); } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);
  const create = async () => {
    setCreating(true); setError(null);
    try { const result = await api.createMyKey(); localStorage.setItem(DEVKEY_STORAGE, result.key); setSecret(result.key); await load(); } catch (err) { setError((err as Error).message); } finally { setCreating(false); }
  };
  return (
    <Stack spacing={3}>{error && <ErrorState message={error} retry={() => void load()} />}
      <Card><CardContent sx={{ pb: '16px !important' }}><Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ xs: 'stretch', sm: 'flex-start' }} spacing={2}><Box><Typography variant="h2">{t('me.keys')}</Typography><Typography variant="body2" color="text.secondary" mt={0.5}>{t('keys.description')}</Typography></Box><Button variant="contained" startIcon={<KeyIcon />} onClick={() => void create()} disabled={creating}>{creating ? t('common.working') : t('me.createMyKey')}</Button></Stack></CardContent>
        {loading ? <TableSkeleton rows={4} columns={4} /> : rows.length === 0 ? <EmptyState title={t('me.keyEmpty')} action={<Button variant="contained" startIcon={<KeyIcon />} onClick={() => void create()}>{t('me.createFirst')}</Button>} /> : <Box sx={{ overflowX: 'auto' }}><Table><TableHead><TableRow><TableCell>{t('common.name')}</TableCell><TableCell>{t('keys.keyId')}</TableCell><TableCell>{t('keys.prefix')}</TableCell><TableCell>{t('common.createdAt')}</TableCell><TableCell>{t('common.status')}</TableCell></TableRow></TableHead><TableBody>{rows.map((row) => <TableRow key={row.id} hover><TableCell><Typography variant="subtitle2">{row.display_name}</Typography></TableCell><TableCell><Box component="code">{row.key_id}</Box></TableCell><TableCell><Box component="code" fontSize={12}>{row.prefix}••••••••</Box></TableCell><TableCell>{format.dateTime(row.created_at)}</TableCell><TableCell><StatusChip status={row.is_revoked ? 'revoked' : 'active'} label={row.is_revoked ? t('common.revoked') : t('common.active')} /></TableCell></TableRow>)}</TableBody></Table></Box>}
      </Card><QuickStart /><SecretDialog open={!!secret} secret={secret ?? ''} onClose={() => setSecret(null)} />
    </Stack>
  );
}

function MyModels() {
  const { t } = useTranslation();
  const format = useFormatters();
  const [rows, setRows] = useState<ModelRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<string>('');
  const hasKey = !!localStorage.getItem(DEVKEY_STORAGE);
  const load = useCallback(async () => {
    if (!hasKey) { setLoading(false); return; }
    setLoading(true); setError(null);
    try { const result = await api.models(); setRows(result.data); setSelected((current) => current || result.data[0]?.id || ''); } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, [hasKey]);
  useEffect(() => { void load(); }, [load]);
  const selectedRow = useMemo(() => rows.find((row) => row.id === selected), [rows, selected]);
  return (
    <Stack spacing={3}>{error && <ErrorState message={error} retry={() => void load()} />}{!hasKey && <Alert severity="info"><Typography variant="subtitle2">{t('me.keyRequired')}</Typography><Typography variant="body2">{t('me.keyRequiredHint')}</Typography></Alert>}
      <Card><CardContent sx={{ pb: '16px !important' }}><Typography variant="h2">{t('me.models')}</Typography><Typography variant="body2" color="text.secondary" mt={0.5}>{t('me.modelsHint')}</Typography></CardContent>
        {loading ? <TableSkeleton rows={4} columns={5} /> : !hasKey ? <EmptyState title={t('me.keyRequired')} description={t('me.keyRequiredHint')} /> : rows.length === 0 ? <EmptyState title={t('common.empty')} /> : <Box sx={{ overflowX: 'auto' }}><Table><TableHead><TableRow><TableCell>{t('table.modelId')}</TableCell><TableCell>{t('table.source')}</TableCell><TableCell>{t('table.billing')}</TableCell><TableCell align="right">{t('catalog.context')}</TableCell><TableCell align="right">{t('common.actions')}</TableCell></TableRow></TableHead><TableBody>{rows.map((row) => <TableRow key={row.id} hover selected={selected === row.id}><TableCell><Stack direction="row" spacing={1.25} alignItems="center"><Box sx={{ color: 'primary.main', display: 'grid' }}><MemoryIcon fontSize="small" /></Box><Box component="code" sx={{ fontSize: 12, wordBreak: 'break-all' }}>{row.id}</Box></Stack></TableCell><TableCell><StatusChip status={row.x_securehub.source} /></TableCell><TableCell>{row.x_securehub.billing === 'user' ? t('me.billingUser') : t('me.billingOrg')}</TableCell><TableCell align="right">{format.compact(row.x_securehub.context_window)}</TableCell><TableCell align="right"><Stack direction="row" justifyContent="flex-end"><Button size="small" onClick={() => setSelected(row.id)}>{t('common.configure')}</Button><CopyButton text={row.id} iconOnly /></Stack></TableCell></TableRow>)}</TableBody></Table></Box>}
      </Card>{selectedRow ? <QuickStart model={selectedRow.id} /> : <Alert severity="info">{t('me.selectModel')}</Alert>}
    </Stack>
  );
}
