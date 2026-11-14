import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Divider,
  IconButton,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Tooltip,
  Typography
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import ArrowForwardIcon from '@mui/icons-material/ArrowForward';
import BlockIcon from '@mui/icons-material/Block';
import CurrencyExchangeIcon from '@mui/icons-material/CurrencyExchange';
import RefreshIcon from '@mui/icons-material/Refresh';
import ShieldOutlinedIcon from '@mui/icons-material/ShieldOutlined';
import TokenOutlinedIcon from '@mui/icons-material/TokenOutlined';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import VerifiedIcon from '@mui/icons-material/Verified';
import { Link as RouterLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api, type AuditRow, type KeyRow, type PolicySnapshot, type ProviderRow, type UsageRow } from '../api';
import { EmptyState, ErrorState, PageHeader, StatCard, StatusChip, TableSkeleton } from '../components/ui';
import { useFormatters } from '../utils/format';
import { useAuditLabels } from '../utils/audit';

export default function DashboardPage() {
  const { t } = useTranslation();
  const format = useFormatters();
  const labels = useAuditLabels();
  const [providers, setProviders] = useState<ProviderRow[]>([]);
  const [keys, setKeys] = useState<KeyRow[]>([]);
  const [audit, setAudit] = useState<AuditRow[]>([]);
  const [usage, setUsage] = useState<UsageRow[]>([]);
  const [snapshot, setSnapshot] = useState<PolicySnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [verify, setVerify] = useState<{ ok: boolean; checked_count: number; first_break_sequence: number | null } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [providerRows, keyRows, auditResult, usageResult, snapshotResult] = await Promise.all([
        api.providers(), api.keys(), api.audit(), api.adminUsage(), api.policySnapshot()
      ]);
      setProviders(providerRows);
      setKeys(keyRows);
      setAudit(auditResult.data);
      setUsage(usageResult.data);
      setSnapshot(snapshotResult);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const metrics = useMemo(() => {
    const requests = usage.reduce((sum, row) => sum + row.requests, 0);
    const input = usage.reduce((sum, row) => sum + row.input_tokens, 0);
    const output = usage.reduce((sum, row) => sum + row.output_tokens, 0);
    const cost = usage.reduce((sum, row) => sum + row.cost_usd, 0);
    const org = usage.filter((row) => row.billing_owner !== 'user').reduce((sum, row) => sum + row.requests, 0);
    const personal = usage.filter((row) => row.billing_owner === 'user').reduce((sum, row) => sum + row.requests, 0);
    const blocked = audit.filter((row) => row.action === 'block').length;
    const models = new Set(usage.map((row) => row.model)).size;
    return { requests, input, output, cost, org, personal, blocked, models };
  }, [usage, audit]);

  const activity = useMemo(() => {
    const days = Array.from({ length: 7 }, (_, index) => {
      const date = new Date();
      date.setHours(0, 0, 0, 0);
      date.setDate(date.getDate() - (6 - index));
      return { date, count: 0 };
    });
    for (const row of audit) {
      const date = new Date(row.occurredAt);
      const match = days.find((day) => day.date.toDateString() === date.toDateString());
      if (match) match.count += 1;
    }
    const max = Math.max(...days.map((day) => day.count), 1);
    return { days, max };
  }, [audit]);

  const runVerify = async () => {
    setVerify(await api.verifyAudit());
  };

  return (
    <Stack spacing={3}>
      <PageHeader
        eyebrow={t('dashboard.eyebrow')}
        title={t('dashboard.title')}
        description={t('dashboard.description')}
        actions={<>
          <Tooltip title={t('common.refresh')}><IconButton onClick={() => void load()} aria-label={t('common.refresh')}><RefreshIcon /></IconButton></Tooltip>
          <Button component={RouterLink} to="/keys" variant="contained" startIcon={<AddIcon />}>{t('keys.create')}</Button>
        </>}
      />
      {error && <ErrorState message={error} retry={() => void load()} />}

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', lg: 'repeat(4, 1fr)' }, gap: 2 }}>
        <StatCard label={t('dashboard.requests')} value={loading ? '—' : format.number(metrics.requests)} caption={t('dashboard.vsProviders', { active: providers.filter((row) => row.enabled).length, total: providers.length })} icon={<TrendingUpIcon fontSize="small" />} />
        <StatCard label={t('dashboard.tokens')} value={loading ? '—' : format.compact(metrics.input)} caption={t('dashboard.acrossModels', { count: metrics.models })} icon={<TokenOutlinedIcon fontSize="small" />} tone="secondary" />
        <StatCard label={t('usage.totalCost')} value={loading ? '—' : format.currency(metrics.cost)} caption={`${format.compact(metrics.output)} ${t('usage.output')}`} icon={<CurrencyExchangeIcon fontSize="small" />} tone="success" />
        <StatCard label={t('dashboard.blocked')} value={loading ? '—' : format.number(metrics.blocked)} caption={t('dashboard.blockRate', { value: audit.length ? ((metrics.blocked / audit.length) * 100).toFixed(1) : '0.0' })} icon={<BlockIcon fontSize="small" />} tone={metrics.blocked > 0 ? 'warning' : 'primary'} />
      </Box>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(0, 1.7fr) minmax(300px, 0.8fr)' }, gap: 2 }}>
        <Card>
          <CardContent>
            <Stack direction="row" justifyContent="space-between" alignItems="flex-start" mb={3}>
              <Box><Typography variant="h2">{t('dashboard.activity')}</Typography><Typography variant="body2" color="text.secondary" mt={0.5}>{t('dashboard.activityHint')}</Typography></Box>
              <Chip size="small" variant="outlined" label={t('common.last7Days')} />
            </Stack>
            {loading ? <TableSkeleton rows={4} columns={7} /> : metrics.requests === 0 && audit.length === 0 ? (
              <EmptyState compact title={t('dashboard.noData')} description={t('dashboard.noDataHint')} />
            ) : (
              <Box sx={{ height: 210, display: 'flex', alignItems: 'stretch', gap: { xs: 1, sm: 2 }, pt: 2 }}>
                {activity.days.map((day, index) => (
                  <Stack key={day.date.toISOString()} flex={1} alignItems="center" spacing={1} minWidth={0}>
                    <Typography variant="caption" color="text.secondary" sx={{ fontVariantNumeric: 'tabular-nums' }}>{day.count}</Typography>
                    <Box sx={{ flex: 1, width: '100%', display: 'flex', alignItems: 'flex-end', justifyContent: 'center', bgcolor: 'action.hover', borderRadius: 1.5, overflow: 'hidden' }}>
                      <Box sx={{ width: '100%', maxWidth: 46, height: `${Math.max(day.count ? 12 : 2, (day.count / activity.max) * 100)}%`, bgcolor: index === 6 ? 'primary.main' : 'primary.light', opacity: index === 6 ? 1 : 0.55, borderRadius: '6px 6px 0 0', transition: 'height 240ms ease' }} />
                    </Box>
                    <Typography variant="caption" color="text.secondary" noWrap>{format.day(day.date)}</Typography>
                  </Stack>
                ))}
              </Box>
            )}
          </CardContent>
        </Card>

        <Card>
          <CardContent>
            <Typography variant="h2">{t('dashboard.billing')}</Typography>
            <Typography variant="body2" color="text.secondary" mt={0.5}>{t('dashboard.billingHint')}</Typography>
            <Stack direction="row" spacing={3} alignItems="center" justifyContent="center" my={3.5}>
              <Box sx={{ width: 124, height: 124, borderRadius: '50%', display: 'grid', placeItems: 'center', background: `conic-gradient(#5146E5 0 ${metrics.requests ? (metrics.org / metrics.requests) * 100 : 50}%, #14B8A6 0 100%)`, position: 'relative', '&::after': { content: '""', position: 'absolute', inset: 13, borderRadius: '50%', bgcolor: 'background.paper' } }}>
                <Box sx={{ position: 'relative', zIndex: 1, textAlign: 'center' }}><Typography variant="h3">{format.compact(metrics.requests)}</Typography><Typography variant="caption" color="text.secondary">{t('dashboard.requestUnit')}</Typography></Box>
              </Box>
              <Stack spacing={2}>
                <Box><Stack direction="row" spacing={1} alignItems="center"><Box sx={{ width: 8, height: 8, borderRadius: 2, bgcolor: 'primary.main' }} /><Typography variant="body2" color="text.secondary">{t('me.billingOrg')}</Typography></Stack><Typography variant="subtitle1" mt={0.5}>{format.number(metrics.org)}</Typography></Box>
                <Box><Stack direction="row" spacing={1} alignItems="center"><Box sx={{ width: 8, height: 8, borderRadius: 2, bgcolor: '#14B8A6' }} /><Typography variant="body2" color="text.secondary">{t('me.billingUser')}</Typography></Stack><Typography variant="subtitle1" mt={0.5}>{format.number(metrics.personal)}</Typography></Box>
              </Stack>
            </Stack>
            <Divider />
            <Stack direction="row" justifyContent="space-between" alignItems="center" mt={2}>
              <Typography variant="body2" color="text.secondary">{t('nav.keys')}</Typography>
              <Typography variant="subtitle2">{keys.filter((key) => !key.is_revoked).length} {t('common.active')}</Typography>
            </Stack>
          </CardContent>
        </Card>
      </Box>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(0, 1.7fr) minmax(300px, 0.8fr)' }, gap: 2 }}>
        <Card sx={{ overflow: 'hidden' }}>
          <CardContent sx={{ pb: '16px !important' }}>
            <Stack direction="row" justifyContent="space-between" alignItems="center">
              <Box><Typography variant="h2">{t('dashboard.recent')}</Typography><Typography variant="body2" color="text.secondary" mt={0.5}>{t('dashboard.recentHint')}</Typography></Box>
              <Button component={RouterLink} to="/audit" endIcon={<ArrowForwardIcon />} size="small">{t('common.showAll')}</Button>
            </Stack>
          </CardContent>
          {loading ? <TableSkeleton rows={5} columns={5} /> : (
            <Box sx={{ overflowX: 'auto' }}>
              <Table size="small">
                <TableHead><TableRow><TableCell>{t('audit.time')}</TableCell><TableCell>{t('audit.event')}</TableCell><TableCell>{t('audit.actor')}</TableCell><TableCell>{t('audit.model')}</TableCell><TableCell>{t('audit.action')}</TableCell></TableRow></TableHead>
                <TableBody>
                  {audit.slice(0, 7).map((row) => (
                    <TableRow key={row.eventId} hover>
                      <TableCell sx={{ whiteSpace: 'nowrap' }}>{format.dateTime(row.occurredAt)}</TableCell>
                      <TableCell><Typography variant="body2" fontWeight={600}>{labels.event(row.eventType)}</Typography><Typography variant="caption" color="text.secondary" component="code">{row.eventType} · #{row.sequence}</Typography></TableCell>
                      <TableCell>{row.actorLabel}</TableCell>
                      <TableCell><Box component="code" sx={{ fontSize: 11, wordBreak: 'break-all' }}>{row.modelAlias ?? '—'}</Box></TableCell>
                      <TableCell><StatusChip status={row.action} label={labels.action(row.action)} /></TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
              {audit.length === 0 && <EmptyState compact title={t('common.empty')} />}
            </Box>
          )}
        </Card>

        <Card>
          <CardContent>
            <Stack direction="row" spacing={1} alignItems="center" mb={1}><ShieldOutlinedIcon color="primary" fontSize="small" /><Typography variant="h2">{t('dashboard.security')}</Typography></Stack>
            <Typography variant="body2" color="text.secondary" mb={3}>{t('dashboard.securityHint')}</Typography>
            <Stack spacing={2}>
              <Stack direction="row" justifyContent="space-between" alignItems="center"><Typography variant="body2" color="text.secondary">{t('policies.snapshot')}</Typography><Chip size="small" color={snapshot?.count ? 'success' : 'default'} variant="outlined" label={t('policies.snapshotCount', { count: snapshot?.count ?? 0 })} /></Stack>
              <Stack direction="row" justifyContent="space-between" alignItems="center"><Typography variant="body2" color="text.secondary">{t('nav.audit')}</Typography><Typography variant="subtitle2">{format.number(audit.length)}</Typography></Stack>
              {verify && <Alert severity={verify.ok ? 'success' : 'error'}>{verify.ok ? t('audit.chainOk', { count: verify.checked_count }) : t('audit.chainBroken', { seq: verify.first_break_sequence })}</Alert>}
              <Button variant="outlined" startIcon={<VerifiedIcon />} onClick={() => void runVerify()} fullWidth>{t('dashboard.verified')}</Button>
              <Button component={RouterLink} to="/policies" endIcon={<ArrowForwardIcon />} fullWidth>{t('nav.policies')}</Button>
            </Stack>
          </CardContent>
        </Card>
      </Box>
    </Stack>
  );
}
