import { useCallback, useEffect, useState } from 'react';
import {
  Alert,
  Box,
  Card,
  CardContent,
  Chip,
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
import ErrorOutlineIcon from '@mui/icons-material/ErrorOutline';
import RefreshIcon from '@mui/icons-material/Refresh';
import SpeedIcon from '@mui/icons-material/Speed';
import TrendingUpIcon from '@mui/icons-material/TrendingUp';
import VerifiedOutlinedIcon from '@mui/icons-material/VerifiedOutlined';
import { useTranslation } from 'react-i18next';
import { api, type MetricsReport } from '../api';
import { EmptyState, ErrorState, PageHeader, StatCard, TableSkeleton } from '../components/ui';
import { useFormatters } from '../utils/format';

export default function ObservabilityPage() {
  const { t } = useTranslation();
  const format = useFormatters();
  const [report, setReport] = useState<MetricsReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try { setReport(await api.metrics(7)); } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  const maxHourly = Math.max(...(report?.by_hour.map((row) => row.requests) ?? [0]), 1);

  return (
    <Stack spacing={3}>
      <PageHeader
        title={t('observability.title')}
        description={t('observability.description')}
        actions={<Tooltip title={t('common.refresh')}><IconButton onClick={() => void load()} aria-label={t('common.refresh')}><RefreshIcon /></IconButton></Tooltip>}
      />
      {error && <ErrorState message={error} retry={() => void load()} />}

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', lg: 'repeat(4, 1fr)' }, gap: 2 }}>
        <StatCard label={t('observability.requests')} value={loading ? '—' : format.number(report?.summary.requests ?? 0)} caption={t('common.last7Days')} icon={<TrendingUpIcon fontSize="small" />} />
        <StatCard label={t('observability.successRate')} value={loading ? '—' : `${report?.summary.success_rate ?? 100}%`} caption={`${format.number(report?.summary.errors ?? 0)} ${t('observability.errors')}`} icon={<VerifiedOutlinedIcon fontSize="small" />} tone="success" />
        <StatCard label={t('observability.p95')} value={loading ? '—' : `${report?.summary.p95_ms ?? 0} ms`} caption={`${t('observability.p50')} ${report?.summary.p50_ms ?? 0} ms · ${t('observability.p99')} ${report?.summary.p99_ms ?? 0} ms`} icon={<SpeedIcon fontSize="small" />} tone="secondary" />
        <StatCard label={t('observability.avgLatency')} value={loading ? '—' : `${report?.summary.avg_ms ?? 0} ms`} caption={t('observability.avgHint')} icon={<SpeedIcon fontSize="small" />} tone="warning" />
      </Box>

      <Card>
        <CardContent sx={{ pb: '16px !important' }}>
          <Typography variant="h2">{t('observability.hourly')}</Typography>
          <Typography variant="body2" color="text.secondary" mt={0.5}>{t('observability.hourlyHint')}</Typography>
        </CardContent>
        {loading ? <TableSkeleton rows={3} columns={8} /> : (
          <Box sx={{ display: 'flex', alignItems: 'flex-end', gap: { xs: 0.5, sm: 1 }, height: 190, px: 2.5, pb: 2.5 }}>
            {report?.by_hour.map((row) => (
              <Stack key={row.hour} flex={1} alignItems="center" spacing={0.75} minWidth={0}>
                <Box sx={{ flex: 1, width: '100%', display: 'flex', alignItems: 'flex-end', justifyContent: 'center', bgcolor: 'action.hover', borderRadius: 1, overflow: 'hidden' }}>
                  <Box sx={{ width: '100%', height: `${Math.max(row.requests ? 6 : 1, (row.requests / maxHourly) * 100)}%`, bgcolor: row.errors > 0 ? 'warning.main' : 'primary.main', opacity: 0.75, borderRadius: '4px 4px 0 0' }} />
                </Box>
                <Typography variant="caption" color="text.secondary" sx={{ fontSize: 9 }}>{row.hour}</Typography>
              </Stack>
            ))}
          </Box>
        )}
      </Card>

      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', lg: 'minmax(0, 1.5fr) minmax(280px, 1fr)' }, gap: 2 }}>
        <Card>
          <CardContent sx={{ pb: '16px !important' }}>
            <Typography variant="h2">{t('observability.byModel')}</Typography>
            <Typography variant="body2" color="text.secondary" mt={0.5}>{t('observability.byModelHint')}</Typography>
          </CardContent>
          {loading ? <TableSkeleton rows={5} columns={5} /> : (report?.by_model.length ?? 0) === 0 ? <EmptyState compact title={t('common.empty')} /> : (
            <Box sx={{ overflowX: 'auto' }}>
              <Table size="small">
                <TableHead><TableRow><TableCell>{t('audit.model')}</TableCell><TableCell align="right">{t('usage.requests')}</TableCell><TableCell align="right">{t('observability.errors')}</TableCell><TableCell align="right">{t('observability.avgLatency')}</TableCell><TableCell align="right">P95</TableCell></TableRow></TableHead>
                <TableBody>
                  {report?.by_model.map((row) => (
                    <TableRow key={row.model} hover>
                      <TableCell><Box component="code" sx={{ fontSize: 12, wordBreak: 'break-all' }}>{row.model}</Box></TableCell>
                      <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{format.number(row.requests)}</TableCell>
                      <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{format.number(row.errors)}</TableCell>
                      <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{row.avg_ms} ms</TableCell>
                      <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{row.p95_ms} ms</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </Box>
          )}
        </Card>

        <Stack spacing={2}>
          <Card>
            <CardContent>
              <Stack direction="row" spacing={1} alignItems="center" mb={1.5}><ErrorOutlineIcon fontSize="small" color="warning" /><Typography variant="h2">{t('observability.security')}</Typography></Stack>
              <Typography variant="body2" color="text.secondary" mb={2}>{t('observability.securityHint')}</Typography>
              {loading ? <TableSkeleton rows={2} columns={2} /> : (report?.security.length ?? 0) === 0 ? (
                <Alert severity="success">{t('observability.noBlocks')}</Alert>
              ) : (
                <Stack spacing={1}>
                  {report?.security.map((row) => (
                    <Stack key={row.event_type} direction="row" justifyContent="space-between" alignItems="center">
                      <Typography variant="body2" color="text.secondary">{row.event_type}</Typography>
                      <Chip size="small" color="warning" variant="outlined" label={format.number(row.count)} />
                    </Stack>
                  ))}
                </Stack>
              )}
            </CardContent>
          </Card>
          <Card>
            <CardContent>
              <Typography variant="h2" mb={1.5}>{t('observability.byProvider')}</Typography>
              {loading ? <TableSkeleton rows={3} columns={2} /> : (report?.by_provider.length ?? 0) === 0 ? <Typography variant="body2" color="text.secondary">{t('common.empty')}</Typography> : (
                <Stack spacing={1.25}>
                  {report?.by_provider.map((row) => (
                    <Stack key={row.provider} direction="row" justifyContent="space-between" alignItems="center">
                      <Box minWidth={0}><Typography variant="body2" noWrap>{row.provider}</Typography><Typography variant="caption" color="text.secondary">{format.number(row.requests)} · {row.avg_ms} ms</Typography></Box>
                      <Chip size="small" color={row.errors > 0 ? 'warning' : 'success'} variant="outlined" label={`${row.errors} / ${row.requests}`} />
                    </Stack>
                  ))}
                </Stack>
              )}
            </CardContent>
          </Card>
        </Stack>
      </Box>
    </Stack>
  );
}
