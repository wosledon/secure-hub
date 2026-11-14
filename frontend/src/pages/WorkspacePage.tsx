import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react';
import {
  Alert,
  Avatar,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Divider,
  LinearProgress,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Typography
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import BlockIcon from '@mui/icons-material/Block';
import CloudQueueIcon from '@mui/icons-material/CloudQueue';
import CurrencyExchangeIcon from '@mui/icons-material/CurrencyExchange';
import GppGoodIcon from '@mui/icons-material/GppGood';
import KeyIcon from '@mui/icons-material/Key';
import MemoryIcon from '@mui/icons-material/Memory';
import ReceiptLongIcon from '@mui/icons-material/ReceiptLong';
import SpeedIcon from '@mui/icons-material/Speed';
import TokenOutlinedIcon from '@mui/icons-material/TokenOutlined';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import { Link as RouterLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api, type ModelRow, type MyKeyRow, type MyProviderRow, type PersonalOverview, type UsageRow } from '../api';
import { ActivityChart, type ActivityPoint } from '../components/ActivityChart';
import { CodeBlock, CopyButton, ConfirmDialog, EmptyState, ErrorState, PageHeader, SecretDialog, StatCard, StatusChip, TableSkeleton } from '../components/ui';
import ProviderOnboardingDialog from '../components/ProviderOnboardingDialog';
import { useAuditLabels } from '../utils/audit';
import { useFormatters } from '../utils/format';

const DEVKEY_STORAGE = 'sh.devkey';

export type WorkspaceSection = 'overview' | 'usage' | 'providers' | 'keys' | 'models';

export default function WorkspacePage({ section = 'overview' }: { section?: WorkspaceSection }) {
  const { t } = useTranslation();
  const titles: Record<WorkspaceSection, { title: string; description: string }> = {
    overview: { title: t('me.overview'), description: t('me.description') },
    usage: { title: t('me.usage'), description: t('me.usageHint') },
    providers: { title: t('me.providers'), description: t('me.byokHint') },
    keys: { title: t('me.keys'), description: t('me.keysHint') },
    models: { title: t('me.models'), description: t('me.modelsHint') }
  };

  return (
    <Stack spacing={3}>
      <PageHeader title={titles[section].title} description={titles[section].description} />
      {section === 'overview' && <WorkspaceOverview />}
      {section === 'usage' && <MyUsage />}
      {section === 'providers' && <MyProviders />}
      {section === 'keys' && <MyKeys />}
      {section === 'models' && <MyModels />}
    </Stack>
  );
}

function WorkspaceOverview() {
  const { t } = useTranslation();
  const format = useFormatters();
  const labels = useAuditLabels();
  const [overview, setOverview] = useState<PersonalOverview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setLoading(true);
    api.myOverview(30)
      .then((result) => setOverview(result))
      .catch((err) => setError((err as Error).message))
      .finally(() => setLoading(false));
  }, []);

  const totals = overview?.totals;
  const points = useMemo<ActivityPoint[]>(
    () => (overview?.by_day ?? []).map((row) => ({
      date: new Date(`${row.date}T00:00:00Z`),
      requests: row.requests,
      blocked: row.blocked
    })),
    [overview]
  );
  const topModels = (overview?.by_model ?? []).slice(0, 5);
  const maxModelRequests = Math.max(1, ...topModels.map((row) => row.requests));
  const hasTraffic = (totals?.requests ?? 0) > 0 || (totals?.blocked ?? 0) > 0;
  const blockRate = totals && totals.requests > 0 ? Math.round((totals.blocked / totals.requests) * 1000) / 10 : 0;

  return (
    <Stack spacing={3}>
      {error && <ErrorState message={error} />}

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', lg: 'repeat(4, minmax(0, 1fr))' }, gap: 2 }}>
        <StatCard
          label={t('usage.totalRequests')}
          value={loading ? '—' : format.number(totals?.requests ?? 0)}
          caption={loading ? '—' : (totals?.requests ?? 0) === 0 ? t('me.noCalls') : t('me.successCaption', { value: `${format.number(totals?.success_rate ?? 0)}%` })}
          icon={<TrendingUpIcon fontSize="small" />}
        />
        <StatCard
          label={t('usage.totalTokens')}
          value={loading ? '—' : format.compact(totals?.tokens ?? 0)}
          caption={t('me.tokensSplit', {
            input: format.compact(totals?.input_tokens ?? 0),
            output: format.compact(totals?.output_tokens ?? 0)
          })}
          icon={<TokenOutlinedIcon fontSize="small" />}
          tone="secondary"
        />
        <StatCard
          label={t('usage.totalCost')}
          value={loading ? '—' : format.currency(totals?.cost_usd ?? 0)}
          caption={t('me.billingUser')}
          icon={<CurrencyExchangeIcon fontSize="small" />}
          tone="success"
        />
        <StatCard
          label={t('dashboard.blocked')}
          value={loading ? '—' : format.number(totals?.blocked ?? 0)}
          caption={t('me.blockRate', { value: `${format.number(blockRate)}%` })}
          icon={<BlockIcon fontSize="small" />}
          tone={totals?.blocked ? 'warning' : 'primary'}
        />
        <StatCard
          label={t('observability.successRate')}
          value={loading ? '—' : (totals?.requests ?? 0) === 0 ? '—' : `${format.number(totals?.success_rate ?? 0)}%`}
          caption={t('me.errorsCount', { total: format.number(totals?.errors ?? 0) })}
          icon={<GppGoodIcon fontSize="small" />}
          tone="success"
        />
        <StatCard
          label={t('observability.p95')}
          value={loading ? '—' : (totals?.requests ?? 0) === 0 ? '—' : `${format.number(totals?.p95_ms ?? 0)} ms`}
          caption={t('me.avgLatency', { value: `${format.number(totals?.avg_ms ?? 0)} ms` })}
          icon={<SpeedIcon fontSize="small" />}
          tone="secondary"
        />
        <StatCard
          label={t('me.activeKeys')}
          value={loading ? '—' : format.number(overview?.keys.active ?? 0)}
          caption={t('me.keysTotal', { total: format.number(overview?.keys.total ?? 0) })}
          icon={<KeyIcon fontSize="small" />}
          tone="warning"
        />
        <StatCard
          label={t('me.modelsUsed')}
          value={loading ? '—' : format.number(overview?.models.used ?? 0)}
          caption={t('me.modelsAvailable', { total: format.number(overview?.models.available ?? 0) })}
          icon={<MemoryIcon fontSize="small" />}
        />
      </Box>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(0, 1.6fr) minmax(300px, 0.8fr)' }, gap: 2 }}>
        <Card>
          <CardContent>
            <Stack direction="row" justifyContent="space-between" alignItems="flex-start" spacing={2} mb={2}>
              <Box minWidth={0}>
                <Typography variant="h2">{t('me.activity')}</Typography>
                <Typography variant="body2" color="text.secondary" mt={0.5}>{t('me.activityHint')}</Typography>
              </Box>
              <Chip size="small" variant="outlined" label={t('common.last30Days')} sx={{ flexShrink: 0 }} />
            </Stack>
            {loading ? <TableSkeleton rows={4} columns={4} /> : hasTraffic ? (
              <ActivityChart
                points={points}
                height={230}
                ariaLabel={t('me.activityAria', { total: format.number(totals?.requests ?? 0) })}
              />
            ) : (
              <EmptyState
                compact
                title={t('me.usageEmpty')}
                description={t('me.noTrafficHint')}
                action={
                  <Button component={RouterLink} to="/workspace/keys" variant="contained" size="small" startIcon={<AddIcon />}>
                    {t('me.createMyKey')}
                  </Button>
                }
              />
            )}
          </CardContent>
        </Card>

        <Card>
          <CardContent>
            <Typography variant="h2">{t('me.access')}</Typography>
            <Typography variant="body2" color="text.secondary" mt={0.5}>{t('me.accessHint')}</Typography>
            <Stack spacing={2} mt={2.5}>
              <AccessRow
                icon={<KeyIcon fontSize="small" />}
                label={t('me.activeKeys')}
                value={`${format.number(overview?.keys.active ?? 0)} / ${format.number(overview?.keys.total ?? 0)}`}
                action={t('me.viewKeys')}
                to="/workspace/keys"
              />
              <AccessRow
                icon={<CloudQueueIcon fontSize="small" />}
                label={t('me.providers')}
                value={`${format.number(overview?.providers.enabled ?? 0)} / ${format.number(overview?.providers.total ?? 0)}`}
                action={t('me.viewProviders')}
                to="/workspace/providers"
              />
              <AccessRow
                icon={<MemoryIcon fontSize="small" />}
                label={t('me.models')}
                value={`${format.number(overview?.models.available ?? 0)} ${t('me.availableShort')} · ${format.number(overview?.models.used ?? 0)} ${t('me.usedShort')}`}
                action={t('me.viewModels')}
                to="/workspace/models"
              />
            </Stack>
            <Divider sx={{ my: 2 }} />
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1}>
              <Button component={RouterLink} to="/workspace/keys" variant="contained" startIcon={<AddIcon />}>
                {t('me.createMyKey')}
              </Button>
              <Button component={RouterLink} to="/workspace/providers" variant="outlined" startIcon={<CloudQueueIcon />}>
                {t('me.createByok')}
              </Button>
            </Stack>
          </CardContent>
        </Card>
      </Box>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(0, 1.1fr) minmax(0, 1fr)' }, gap: 2 }}>
        <Card>
          <CardContent>
            <Typography variant="h2">{t('me.topModels')}</Typography>
            <Typography variant="body2" color="text.secondary" mt={0.5}>{t('me.topModelsHint')}</Typography>
            {loading ? <TableSkeleton rows={3} columns={2} /> : topModels.length === 0 ? (
              <EmptyState compact title={t('me.usageEmpty')} />
            ) : (
              <Stack spacing={2} mt={2}>
                {topModels.map((row) => (
                  <Box key={row.model} sx={{ minWidth: 0 }}>
                    <Stack direction="row" justifyContent="space-between" alignItems="baseline" spacing={1.5}>
                      <Box component="code" sx={{ fontSize: 12, wordBreak: 'break-all' }}>{row.model}</Box>
                      <Typography variant="caption" color="text.secondary" sx={{ flexShrink: 0 }}>
                        {t('me.requestsAndCost', {
                          requests: format.number(row.requests),
                          cost: format.currency(row.cost_usd)
                        })}
                      </Typography>
                    </Stack>
                    <LinearProgress
                      variant="determinate"
                      value={(row.requests / maxModelRequests) * 100}
                      sx={{ height: 6, borderRadius: 1, mt: 0.75 }}
                    />
                  </Box>
                ))}
              </Stack>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardContent>
            <Typography variant="h2">{t('me.recentActivity')}</Typography>
            <Typography variant="body2" color="text.secondary" mt={0.5}>{t('me.recentActivityHint')}</Typography>
            {loading ? <TableSkeleton rows={3} columns={2} /> : (overview?.recent.length ?? 0) === 0 ? (
              <EmptyState compact title={t('me.noRecent')} />
            ) : (
              <Stack divider={<Divider />} mt={1.5}>
                {overview?.recent.map((event) => (
                  <Stack key={event.event_id} direction="row" spacing={1.5} alignItems="center" py={1.25} minWidth={0}>
                    <Box
                      sx={{
                        width: 8,
                        height: 8,
                        borderRadius: '50%',
                        flexShrink: 0,
                        bgcolor: event.action === 'block' ? 'error.main' : event.action === 'redact' ? 'warning.main' : 'success.main'
                      }}
                    />
                    <Box flexGrow={1} minWidth={0}>
                      <Typography variant="body2" fontWeight={600}>{labels.event(event.event_type)}</Typography>
                      <Typography variant="caption" color="text.secondary" display="block">
                        {format.dateTime(event.occurred_at)}
                        {event.reason_code ? <> · <Box component="code" sx={{ fontSize: 11 }}>{event.reason_code}</Box></> : null}
                      </Typography>
                    </Box>
                    <StatusChip status={event.action} label={labels.action(event.action)} />
                  </Stack>
                ))}
              </Stack>
            )}
          </CardContent>
        </Card>
      </Box>
    </Stack>
  );
}

function AccessRow({
  icon,
  label,
  value,
  action,
  to
}: {
  icon: ReactNode;
  label: string;
  value: string;
  action: string;
  to: string;
}) {
  return (
    <Stack direction="row" spacing={1.5} alignItems="center" minWidth={0}>
      <Box sx={{ width: 34, height: 34, borderRadius: 2, display: 'grid', placeItems: 'center', bgcolor: 'action.hover', color: 'primary.main', flexShrink: 0 }}>
        {icon}
      </Box>
      <Box flexGrow={1} minWidth={0}>
        <Typography variant="body2" fontWeight={600}>{label}</Typography>
        <Typography variant="caption" color="text.secondary">{value}</Typography>
      </Box>
      <Button component={RouterLink} to={to} size="small" endIcon={<ArrowForwardIcon />}>
        {action}
      </Button>
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
  const [message, setMessage] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [disableTarget, setDisableTarget] = useState<MyProviderRow | null>(null);
  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try { setRows(await api.myProviders()); } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);
  const test = async (row: MyProviderRow) => {
    setBusyId(row.id); setMessage(null);
    try {
      const result = await api.testMyProvider(row.id);
      setMessage(result.healthy ? t('providers.testOk', { name: row.alias }) : t('providers.testFailed', { name: row.alias, error: result.error ?? '' }));
    } catch (err) { setError((err as Error).message); } finally { setBusyId(null); }
  };
  const disable = async () => {
    if (!disableTarget) return;
    setBusyId(disableTarget.id);
    try { await api.disableMyProvider(disableTarget.id); setDisableTarget(null); await load(); }
    catch (err) { setError((err as Error).message); } finally { setBusyId(null); }
  };
  return (
    <Stack spacing={3}>
      {error && <ErrorState message={error} retry={() => void load()} />}
      {message && <Alert severity="info" onClose={() => setMessage(null)}>{message}</Alert>}
      <Alert severity="info">{t('me.personalDisclosure')}</Alert>
      <Card><CardContent sx={{ pb: '16px !important' }}><Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ xs: 'stretch', sm: 'flex-start' }} spacing={2}><Box><Typography variant="h2">{t('me.providers')}</Typography><Typography variant="body2" color="text.secondary" mt={0.5}>{t('me.byokHint')}</Typography></Box><Button variant="contained" startIcon={<AddIcon />} onClick={() => setOpen(true)}>{t('me.createByok')}</Button></Stack></CardContent>
        {loading ? <TableSkeleton rows={4} columns={4} /> : rows.length === 0 ? <EmptyState title={t('me.providerEmpty')} action={<Button variant="contained" startIcon={<AddIcon />} onClick={() => setOpen(true)}>{t('me.addFirst')}</Button>} /> : <Box sx={{ overflowX: 'auto' }}><Table><TableHead><TableRow><TableCell>{t('common.alias')}</TableCell><TableCell>{t('common.provider')}</TableCell><TableCell>{t('common.baseUrl')}</TableCell><TableCell>{t('common.models')}</TableCell><TableCell align="right">{t('common.actions')}</TableCell></TableRow></TableHead><TableBody>{rows.map((row) => <TableRow key={row.id} hover><TableCell><Stack direction="row" spacing={1.25} alignItems="center"><Avatar sx={{ width: 32, height: 32, bgcolor: 'secondary.main' }}><CloudQueueIcon sx={{ fontSize: 17 }} /></Avatar><Typography variant="subtitle2">{row.alias}</Typography></Stack></TableCell><TableCell><Box component="code">{row.providerKey}</Box></TableCell><TableCell sx={{ maxWidth: 300 }}><Typography variant="body2" noWrap>{row.baseUrl}</Typography></TableCell><TableCell><Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>{row.models.map((model) => <Chip key={model} size="small" variant="outlined" label={model} />)}</Stack></TableCell><TableCell align="right"><Stack direction="row" spacing={0.5} justifyContent="flex-end"><Button size="small" disabled={busyId === row.id} onClick={() => void test(row)}>{t('me.testProvider')}</Button><Button size="small" color="error" onClick={() => setDisableTarget(row)}>{t('me.disableProvider')}</Button></Stack></TableCell></TableRow>)}</TableBody></Table></Box>}
      </Card>
      <ProviderOnboardingDialog open={open} scope="me" onClose={() => setOpen(false)} onCreated={() => void load()} />
      <ConfirmDialog open={!!disableTarget} title={t('providers.disableTitle')} description={t('providers.disableHint', { name: disableTarget?.alias ?? '' })} confirmLabel={t('me.disableProvider')} busy={!!busyId} danger onClose={() => setDisableTarget(null)} onConfirm={() => void disable()} />
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
  const [busyId, setBusyId] = useState<string | null>(null);
  const [revokeTarget, setRevokeTarget] = useState<MyKeyRow | null>(null);
  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try { setRows(await api.myKeys()); } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);
  const create = async () => {
    setCreating(true); setError(null);
    try { const result = await api.createMyKey(); localStorage.setItem(DEVKEY_STORAGE, result.key); setSecret(result.key); await load(); } catch (err) { setError((err as Error).message); } finally { setCreating(false); }
  };
  const rotate = async (row: MyKeyRow) => {
    setBusyId(row.id); setError(null);
    try {
      const result = await api.rotateMyKey(row.id);
      localStorage.setItem(DEVKEY_STORAGE, result.key);
      setSecret(result.key);
      await load();
    } catch (err) { setError((err as Error).message); } finally { setBusyId(null); }
  };
  const revoke = async () => {
    if (!revokeTarget) return;
    setBusyId(revokeTarget.id);
    try { await api.revokeMyKey(revokeTarget.id); setRevokeTarget(null); await load(); }
    catch (err) { setError((err as Error).message); } finally { setBusyId(null); }
  };
  return (
    <Stack spacing={3}>{error && <ErrorState message={error} retry={() => void load()} />}
      <Card><CardContent sx={{ pb: '16px !important' }}><Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ xs: 'stretch', sm: 'flex-start' }} spacing={2}><Box><Typography variant="h2">{t('me.keys')}</Typography><Typography variant="body2" color="text.secondary" mt={0.5}>{t('keys.description')}</Typography></Box><Button variant="contained" startIcon={<KeyIcon />} onClick={() => void create()} disabled={creating}>{creating ? t('common.working') : t('me.createMyKey')}</Button></Stack></CardContent>
        {loading ? <TableSkeleton rows={4} columns={4} /> : rows.length === 0 ? <EmptyState title={t('me.keyEmpty')} action={<Button variant="contained" startIcon={<KeyIcon />} onClick={() => void create()}>{t('me.createFirst')}</Button>} /> : <Box sx={{ overflowX: 'auto' }}><Table>        <TableHead><TableRow><TableCell>{t('common.name')}</TableCell><TableCell>{t('keys.keyId')}</TableCell><TableCell>{t('keys.prefix')}</TableCell><TableCell>{t('common.createdAt')}</TableCell><TableCell>{t('common.status')}</TableCell><TableCell align="right">{t('common.actions')}</TableCell></TableRow></TableHead><TableBody>{rows.map((row) => <TableRow key={row.id} hover sx={{ opacity: row.is_revoked ? 0.65 : 1 }}><TableCell><Typography variant="subtitle2">{row.display_name}</Typography></TableCell><TableCell><Box component="code">{row.key_id}</Box></TableCell><TableCell><Box component="code" fontSize={12}>{row.prefix}••••••••</Box></TableCell><TableCell>{format.dateTime(row.created_at)}</TableCell><TableCell><StatusChip status={row.is_revoked ? 'revoked' : 'active'} label={row.is_revoked ? t('common.revoked') : t('common.active')} /></TableCell><TableCell align="right"><Stack direction="row" spacing={0.5} justifyContent="flex-end"><Button size="small" disabled={row.is_revoked || busyId === row.id} onClick={() => void rotate(row)}>{t('me.rotateKey')}</Button><Button size="small" color="error" disabled={row.is_revoked} onClick={() => setRevokeTarget(row)}>{t('me.revokeKey')}</Button></Stack></TableCell></TableRow>)}</TableBody></Table></Box>}
      </Card><QuickStart /><SecretDialog open={!!secret} secret={secret ?? ''} onClose={() => setSecret(null)} />
              <ConfirmDialog open={!!revokeTarget} title={t('me.revokeKey')} description={t('me.revokeKeyHint', { name: revokeTarget?.display_name ?? '' })} confirmLabel={t('me.revokeKey')} busy={!!busyId} danger onClose={() => setRevokeTarget(null)} onConfirm={() => void revoke()} />
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
