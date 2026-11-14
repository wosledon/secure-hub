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
  LinearProgress,
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
import GppGoodIcon from '@mui/icons-material/GppGood';
import KeyIcon from '@mui/icons-material/Key';
import MemoryIcon from '@mui/icons-material/Memory';
import RefreshIcon from '@mui/icons-material/Refresh';
import ShieldOutlinedIcon from '@mui/icons-material/ShieldOutlined';
import SpeedIcon from '@mui/icons-material/Speed';
import TokenOutlinedIcon from '@mui/icons-material/TokenOutlined';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import VerifiedIcon from '@mui/icons-material/Verified';
import { Link as RouterLink } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api, type AuditRow, type BudgetRow, type KeyRow, type MetricsReport, type PolicySnapshot, type ProviderRow, type UsageBreakdown, type UsageRow } from '../api';
import { ActivityChart, type ActivityPoint } from '../components/ActivityChart';
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
  const [breakdown, setBreakdown] = useState<UsageBreakdown | null>(null);
  const [telemetry, setTelemetry] = useState<MetricsReport | null>(null);
  const [budgets, setBudgets] = useState<BudgetRow[]>([]);
  const [snapshot, setSnapshot] = useState<PolicySnapshot | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [verify, setVerify] = useState<{ ok: boolean; checked_count: number; first_break_sequence: number | null } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [providerRows, keyRows, auditResult, usageResult, snapshotResult, breakdownResult, metricsResult, budgetRows] = await Promise.all([
        api.providers(), api.keys(), api.audit(), api.adminUsage(), api.policySnapshot(), api.usageBreakdown(7), api.metrics(7), api.budgets()
      ]);
      setProviders(providerRows);
      setKeys(keyRows);
      setAudit(auditResult.data);
      setUsage(usageResult.data);
      setSnapshot(snapshotResult);
      setBreakdown(breakdownResult);
      setTelemetry(metricsResult);
      setBudgets(budgetRows);
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

  const activity = useMemo<ActivityPoint[]>(() => {
    const now = new Date();
    const days = Array.from({ length: 7 }, (_, index) => {
      const date = new Date(Date.UTC(now.getUTCFullYear(), now.getUTCMonth(), now.getUTCDate() - (6 - index)));
      return { date, key: date.toISOString().slice(0, 10), requests: 0, blocked: 0 };
    });

    // 后端按 UTC 日期聚合 by_day；横轴也使用 UTC，避免跨时区错位。
    const requestsByDay = new Map((breakdown?.by_day ?? []).map((row) => [row.date, row.requests]));
    for (const day of days) day.requests = requestsByDay.get(day.key) ?? 0;

    const daysByKey = new Map(days.map((day) => [day.key, day]));
    for (const row of audit) {
      if (row.action !== 'block') continue;
      const day = daysByKey.get(new Date(row.occurredAt).toISOString().slice(0, 10));
      if (day) day.blocked += 1;
    }

    return days.map(({ date, requests, blocked }) => ({ date, requests, blocked }));
  }, [audit, breakdown]);

  const activityTotal = useMemo(
    () => activity.reduce((sum, point) => sum + point.requests, 0),
    [activity]
  );
  const hasBlockedActivity = activity.some((point) => point.blocked > 0);
  const topModels = useMemo(
    () => [...(breakdown?.by_model ?? [])].sort((a, b) => b.requests - a.requests).slice(0, 5),
    [breakdown]
  );
  const maxModelRequests = Math.max(1, ...topModels.map((row) => row.requests));
  const budgetRows = useMemo(
    () => budgets.slice(0, 4).map((budget) => {
      const tokenPercent = budget.tokenLimit ? Math.min(100, (budget.tokensUsed / budget.tokenLimit) * 100) : 0;
      const costPercent = budget.costLimit ? Math.min(100, (budget.costUsed / budget.costLimit) * 100) : 0;
      return { ...budget, percent: Math.max(tokenPercent, costPercent) };
    }),
    [budgets]
  );

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

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', lg: 'repeat(4, minmax(0, 1fr))' }, gap: 2 }}>
        <StatCard label={t('dashboard.requests')} value={loading ? '—' : format.number(metrics.requests)} caption={t('dashboard.vsProviders', { active: providers.filter((row) => row.enabled).length, total: providers.length })} icon={<TrendingUpIcon fontSize="small" />} />
        <StatCard label={t('dashboard.tokens')} value={loading ? '—' : format.compact(metrics.input)} caption={t('dashboard.acrossModels', { count: metrics.models })} icon={<TokenOutlinedIcon fontSize="small" />} tone="secondary" />
        <StatCard label={t('usage.totalCost')} value={loading ? '—' : format.currency(metrics.cost)} caption={`${format.compact(metrics.output)} ${t('usage.output')}`} icon={<CurrencyExchangeIcon fontSize="small" />} tone="success" />
        <StatCard label={t('dashboard.blocked')} value={loading ? '—' : format.number(metrics.blocked)} caption={t('dashboard.blockRate', { value: audit.length ? ((metrics.blocked / audit.length) * 100).toFixed(1) : '0.0' })} icon={<BlockIcon fontSize="small" />} tone={metrics.blocked > 0 ? 'warning' : 'primary'} />
        <StatCard
          label={t('observability.successRate')}
          value={loading ? '—' : (telemetry?.summary.requests ?? 0) === 0 ? '—' : `${format.number(telemetry?.summary.success_rate ?? 0)}%`}
          caption={t('dashboard.errorsCaption', { total: format.number(telemetry?.summary.errors ?? 0) })}
          icon={<GppGoodIcon fontSize="small" />}
          tone="success"
        />
        <StatCard
          label={t('observability.p95')}
          value={loading ? '—' : (telemetry?.summary.requests ?? 0) === 0 ? '—' : `${format.number(telemetry?.summary.p95_ms ?? 0)} ms`}
          caption={t('dashboard.avgCaption', { value: format.number(telemetry?.summary.avg_ms ?? 0) })}
          icon={<SpeedIcon fontSize="small" />}
          tone="secondary"
        />
        <StatCard
          label={t('me.activeKeys')}
          value={loading ? '—' : format.number(keys.filter((key) => !key.is_revoked).length)}
          caption={t('dashboard.keysCaption', { total: format.number(keys.length) })}
          icon={<KeyIcon fontSize="small" />}
          tone="warning"
        />
        <StatCard
          label={t('dashboard.models')}
          value={loading ? '—' : format.number(metrics.models)}
          caption={t('dashboard.modelsCaption', { count: providers.filter((row) => row.enabled).length })}
          icon={<MemoryIcon fontSize="small" />}
        />
      </Box>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(0, 1.7fr) minmax(300px, 0.8fr)' }, gap: 2 }}>
        <Card>
          <CardContent>
            <Stack direction="row" justifyContent="space-between" alignItems="flex-start" spacing={2} mb={2}>
              <Box minWidth={0}>
                <Typography variant="h2">{t('dashboard.activity')}</Typography>
                <Typography variant="body2" color="text.secondary" mt={0.5}>{t('dashboard.activityHint')}</Typography>
                <Stack direction="row" spacing={2} mt={1.25} flexWrap="wrap" useFlexGap>
                  <ChartLegend color="primary.main" label={t('dashboard.activityRequests')} />
                  {hasBlockedActivity && <ChartLegend color="error.main" label={t('dashboard.activityBlocked')} dashed />}
                </Stack>
              </Box>
              <Stack alignItems="flex-end" spacing={0.5} flexShrink={0}>
                <Chip size="small" variant="outlined" label={t('common.last7Days')} />
                <Typography variant="caption" color="text.secondary">
                  {t('dashboard.activityTotal', { total: format.number(activityTotal) })}
                </Typography>
              </Stack>
            </Stack>
            {loading ? <TableSkeleton rows={4} columns={7} /> : activityTotal === 0 && !hasBlockedActivity ? (
              <EmptyState compact title={t('dashboard.noData')} description={t('dashboard.noDataHint')} />
            ) : (
              <ActivityChart
                points={activity}
                height={230}
                ariaLabel={t('dashboard.activityAria', { total: format.number(activityTotal) })}
              />
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

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(0, 1.1fr) minmax(0, 1fr)' }, gap: 2 }}>
        <Card>
          <CardContent>
            <Stack direction="row" justifyContent="space-between" alignItems="flex-start" spacing={2}>
              <Box>
                <Typography variant="h2">{t('dashboard.topModels')}</Typography>
                <Typography variant="body2" color="text.secondary" mt={0.5}>{t('dashboard.topModelsHint')}</Typography>
              </Box>
              <Button component={RouterLink} to="/usage" size="small" endIcon={<ArrowForwardIcon />}>{t('common.showAll')}</Button>
            </Stack>
            {loading ? <TableSkeleton rows={3} columns={2} /> : topModels.length === 0 ? (
              <EmptyState compact title={t('common.empty')} />
            ) : (
              <Stack spacing={2} mt={2}>
                {topModels.map((row) => (
                  <Box key={row.model} sx={{ minWidth: 0 }}>
                    <Stack direction="row" justifyContent="space-between" alignItems="baseline" spacing={1.5}>
                      <Box component="code" sx={{ fontSize: 12, wordBreak: 'break-all' }}>{row.model}</Box>
                      <Typography variant="caption" color="text.secondary" sx={{ flexShrink: 0 }}>
                        {t('dashboard.requestsAndCost', { requests: format.number(row.requests), cost: format.currency(row.cost_usd) })}
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
            <Stack direction="row" justifyContent="space-between" alignItems="flex-start" spacing={2}>
              <Box>
                <Typography variant="h2">{t('usage.budgets')}</Typography>
                <Typography variant="body2" color="text.secondary" mt={0.5}>{t('dashboard.budgetsHint')}</Typography>
              </Box>
              <Button component={RouterLink} to="/usage" size="small" endIcon={<ArrowForwardIcon />}>{t('dashboard.manageBudgets')}</Button>
            </Stack>
            {loading ? <TableSkeleton rows={3} columns={2} /> : budgetRows.length === 0 ? (
              <EmptyState
                compact
                title={t('dashboard.noBudgets')}
                action={<Button component={RouterLink} to="/usage" size="small" variant="outlined">{t('dashboard.manageBudgets')}</Button>}
              />
            ) : (
              <Stack spacing={2} mt={2}>
                {budgetRows.map((budget) => (
                  <Box key={budget.id}>
                    <Stack direction="row" justifyContent="space-between" alignItems="baseline" spacing={1.5}>
                      <Typography variant="body2" fontWeight={600}>
                        {budget.subject === 'org' ? t('usage.org') : t('usage.team')}
                        {budget.subjectId ? ` · ${budget.subjectId.slice(0, 8)}` : ''}
                      </Typography>
                      <Typography variant="caption" color="text.secondary">
                        {t(`usage.${budget.period}`)} · {budget.percent.toFixed(0)}%
                      </Typography>
                    </Stack>
                    <LinearProgress
                      variant="determinate"
                      value={budget.percent}
                      color={budget.percent >= 90 ? 'error' : budget.percent >= 75 ? 'warning' : 'primary'}
                      sx={{ height: 6, borderRadius: 1, mt: 0.75 }}
                    />
                    <Typography variant="caption" color="text.secondary" display="block" mt={0.5}>
                      {format.compact(budget.tokensUsed)} / {budget.tokenLimit ? format.compact(budget.tokenLimit) : '∞'} {t('dashboard.tokenUnit')}
                      {' · '}
                      {format.currency(budget.costUsed)} / {budget.costLimit ? format.currency(budget.costLimit) : '∞'}
                    </Typography>
                  </Box>
                ))}
              </Stack>
            )}
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

function ChartLegend({ color, label, dashed = false }: { color: string; label: string; dashed?: boolean }) {
  return (
    <Stack direction="row" spacing={0.75} alignItems="center">
      <Box
        sx={{
          width: 16,
          borderTopWidth: 2,
          borderTopStyle: dashed ? 'dashed' : 'solid',
          borderColor: color
        }}
      />
      <Typography variant="caption" color="text.secondary">{label}</Typography>
    </Stack>
  );
}
