import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Card,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  Drawer,
  IconButton,
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
import CloseIcon from '@mui/icons-material/Close';
import DownloadIcon from '@mui/icons-material/Download';
import FlagOutlinedIcon from '@mui/icons-material/FlagOutlined';
import RefreshIcon from '@mui/icons-material/Refresh';
import SearchIcon from '@mui/icons-material/Search';
import VerifiedIcon from '@mui/icons-material/Verified';
import { useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api, type AuditDisputeRow, type AuditRow } from '../api';
import { CodeBlock, EmptyState, ErrorState, PageHeader, StatusChip, TableSkeleton } from '../components/ui';
import { useFormatters } from '../utils/format';
import { auditEventTypes, useAuditLabels } from '../utils/audit';

export default function AuditPage() {
  const { t } = useTranslation();
  const format = useFormatters();
  const labels = useAuditLabels();
  const [searchParams, setSearchParams] = useSearchParams();
  const [rows, setRows] = useState<AuditRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [verify, setVerify] = useState<{ ok: boolean; checked_count: number; first_break_sequence: number | null } | null>(null);
  const [page, setPage] = useState(0);
  const [pageSize, setPageSize] = useState(25);
  const [disputesOpen, setDisputesOpen] = useState(false);
  const [disputes, setDisputes] = useState<AuditDisputeRow[]>([]);
  const [disputeOpen, setDisputeOpen] = useState(false);
  const [disputeType, setDisputeType] = useState('false_positive');
  const [disputeNote, setDisputeNote] = useState('');
  const [disputeBusy, setDisputeBusy] = useState(false);
  const [message, setMessage] = useState<string | null>(null);

  const filters = useMemo(() => ({
    from: searchParams.get('from') ?? '', to: searchParams.get('to') ?? '',
    appId: searchParams.get('appId') ?? '', eventType: searchParams.get('eventType') ?? ''
  }), [searchParams]);
  const [form, setForm] = useState(filters);
  useEffect(() => setForm(filters), [filters]);

  const selected = useMemo(() => rows.find((row) => row.eventId === searchParams.get('event')) ?? null, [rows, searchParams]);

  const queryParams = useCallback((range = form) => ({
    from: range.from ? `${range.from}T00:00:00Z` : undefined,
    to: range.to ? `${range.to}T23:59:59Z` : undefined,
    appId: range.appId || undefined,
    eventType: range.eventType || undefined
  }), [form]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await api.audit({
        from: filters.from ? `${filters.from}T00:00:00Z` : undefined,
        to: filters.to ? `${filters.to}T23:59:59Z` : undefined,
        appId: filters.appId || undefined, eventType: filters.eventType || undefined
      });
      setRows(result.data);
    } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, [filters]);
  useEffect(() => { void load(); }, [load]);
  useEffect(() => setPage(0), [filters]);

  const apply = () => {
    const next: Record<string, string> = {};
    Object.entries(form).forEach(([key, value]) => { if (value) next[key] = value; });
    setSearchParams(next);
  };
  const reset = () => { setForm({ from: '', to: '', appId: '', eventType: '' }); setSearchParams({}); };
  const openDetails = (row: AuditRow) => {
    const next = new URLSearchParams(searchParams);
    next.set('event', row.eventId);
    setSearchParams(next);
  };
  const closeDetails = () => {
    const next = new URLSearchParams(searchParams);
    next.delete('event');
    setSearchParams(next);
  };

  const runVerify = async () => setVerify(await api.verifyAudit());
  const loadDisputes = async () => {
    setDisputesOpen(true);
    try { setDisputes(await api.disputes()); } catch (err) { setError((err as Error).message); }
  };
  const submitDispute = async () => {
    if (!selected) return;
    setDisputeBusy(true);
    try {
      await api.disputeAudit(selected.eventId, { disputeType, note: disputeNote });
      setDisputeOpen(false);
      setDisputeNote('');
      setMessage(t('audit.disputeSubmitted'));
    } catch (err) { setError((err as Error).message); } finally { setDisputeBusy(false); }
  };

  const visible = rows.slice(page * pageSize, page * pageSize + pageSize);

  return (
    <Stack spacing={3}>
      <PageHeader title={t('audit.title')} description={t('audit.description')} actions={<>
        <Tooltip title={t('common.refresh')}><IconButton onClick={() => void load()} aria-label={t('common.refresh')}><RefreshIcon /></IconButton></Tooltip>
        <Button variant="outlined" startIcon={<FlagOutlinedIcon />} onClick={() => void loadDisputes()}>{t('audit.disputes')}</Button>
        <Button variant="outlined" startIcon={<VerifiedIcon />} onClick={() => void runVerify()}>{t('audit.verify')}</Button>
        <Button component="a" href={api.auditExportUrl(queryParams(filters))} download variant="contained" startIcon={<DownloadIcon />}>{t('audit.export')}</Button>
      </>} />
      {error && <ErrorState message={error} retry={() => void load()} />}
      {message && <Alert severity="success" closeText={t('common.close')} onClose={() => setMessage(null)}>{message}</Alert>}
      {verify && <Alert severity={verify.ok ? 'success' : 'error'} closeText={t('common.close')} onClose={() => setVerify(null)}>{verify.ok ? t('audit.chainOk', { count: verify.checked_count }) : t('audit.chainBroken', { seq: verify.first_break_sequence })}</Alert>}

      <Card>
        <Box component="form" onSubmit={(event) => { event.preventDefault(); apply(); }} sx={{ p: 2, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', xl: '1.2fr 1.2fr 1.4fr 1.4fr auto' }, gap: 1.5, borderBottom: 1, borderColor: 'divider' }}>
          <TextField
            select
            size="small"
            label={t('audit.searchEvent')}
            value={form.eventType}
            onChange={(event) => setForm({ ...form, eventType: event.target.value })}
            SelectProps={{ renderValue: (value) => value ? `${labels.event(String(value))} · ${String(value)}` : t('audit.allEvents') }}
          >
            <MenuItem value="">{t('audit.allEvents')}</MenuItem>
            {auditEventTypes.map((event) => (
              <MenuItem key={event} value={event}>
                <Box><Typography variant="body2">{labels.event(event)}</Typography><Typography variant="caption" color="text.secondary" component="code">{event}</Typography></Box>
              </MenuItem>
            ))}
          </TextField>
          <TextField size="small" label={t('audit.appId')} value={form.appId} onChange={(event) => setForm({ ...form, appId: event.target.value })} name="appId" autoComplete="off" />
          <TextField size="small" type="date" label={t('audit.from')} value={form.from} onChange={(event) => setForm({ ...form, from: event.target.value })} InputLabelProps={{ shrink: true }} />
          <TextField size="small" type="date" label={t('audit.to')} value={form.to} onChange={(event) => setForm({ ...form, to: event.target.value })} InputLabelProps={{ shrink: true }} />
          <Stack direction="row" spacing={1}><Button type="submit" variant="contained" startIcon={<SearchIcon />}>{t('audit.apply')}</Button><Button onClick={reset}>{t('audit.reset')}</Button></Stack>
        </Box>
        <Stack direction="row" justifyContent="space-between" alignItems="center" px={2.5} py={1.5}><Typography variant="body2" color="text.secondary">{t('common.results', { count: rows.length })}</Typography><Typography variant="caption" color="text.secondary">{t('audit.exportHint')}</Typography></Stack>
        {loading ? <TableSkeleton rows={8} columns={7} /> : rows.length === 0 ? <EmptyState title={t('audit.empty')} description={t('audit.emptyHint')} /> : (
          <Box sx={{ overflowX: 'auto' }}><Table size="small">
            <TableHead><TableRow><TableCell>#</TableCell><TableCell>{t('audit.time')}</TableCell><TableCell>{t('audit.event')}</TableCell><TableCell>{t('audit.action')}</TableCell><TableCell>{t('audit.actor')}</TableCell><TableCell>{t('audit.model')}</TableCell><TableCell align="right">{t('audit.tokensInOut')}</TableCell><TableCell>{t('table.billing')}</TableCell></TableRow></TableHead>
            <TableBody>{visible.map((row) => (
              <TableRow key={row.eventId} hover onClick={() => openDetails(row)} sx={{ cursor: 'pointer' }} tabIndex={0} onKeyDown={(event) => { if (event.key === 'Enter' || event.key === ' ') { event.preventDefault(); openDetails(row); } }}>
                <TableCell sx={{ fontVariantNumeric: 'tabular-nums' }}>{row.sequence}</TableCell><TableCell sx={{ whiteSpace: 'nowrap' }}>{format.dateTime(row.occurredAt)}</TableCell>
                <TableCell sx={{ minWidth: 210 }}>
                  <Typography variant="body2" fontWeight={600}>{labels.event(row.eventType)}</Typography>
                  <Typography variant="caption" color="text.secondary" component="code">{row.eventType}</Typography>
                  {row.reasonCode && row.reasonCode !== 'ok' && <Typography variant="caption" color="text.secondary" display="block">{labels.reason(row.reasonCode)}</Typography>}
                </TableCell>
                <TableCell><StatusChip status={row.action} label={labels.action(row.action)} /></TableCell><TableCell>{row.actorLabel}</TableCell>
                <TableCell sx={{ maxWidth: 220 }}><Box component="code" sx={{ fontSize: 11, wordBreak: 'break-all' }}>{row.modelAlias ?? '—'}</Box></TableCell>
                <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{row.inputTokens != null || row.outputTokens != null ? `${format.number(row.inputTokens ?? 0)} / ${format.number(row.outputTokens ?? 0)}` : '—'}</TableCell>
                <TableCell><StatusChip status={row.billing_owner} label={labels.billing(row.billing_owner)} /></TableCell>
              </TableRow>
            ))}</TableBody>
          </Table></Box>
        )}
        {!loading && rows.length > 0 && <TablePagination component="div" count={rows.length} page={page} onPageChange={(_, next) => setPage(next)} rowsPerPage={pageSize} onRowsPerPageChange={(event) => { setPageSize(Number(event.target.value)); setPage(0); }} rowsPerPageOptions={[10, 25, 50]} labelRowsPerPage={t('common.rowsPerPage')} labelDisplayedRows={({ from, to, count }) => t('common.pageRange', { from, to, count })} getItemAriaLabel={(type) => t(`common.${type}Page`)} />}
      </Card>

      <Drawer anchor="right" open={!!selected} onClose={closeDetails} sx={{ '& .MuiDrawer-paper': { width: { xs: '100%', sm: 500 } } }}>
        {selected && <Box sx={{ height: '100%', overflowY: 'auto' }}>
          <Stack direction="row" alignItems="center" justifyContent="space-between" p={2.5}><Box><Typography variant="overline" color="primary.main">#{selected.sequence}</Typography><Typography variant="h2">{t('audit.details')}</Typography></Box><IconButton onClick={closeDetails} aria-label={t('common.close')}><CloseIcon /></IconButton></Stack><Divider />
          <Stack spacing={2.5} p={2.5}>
            <Box>
              <Typography variant="h2" sx={{ wordBreak: 'break-word' }}>{labels.event(selected.eventType)}</Typography>
              <Stack direction="row" spacing={1} alignItems="center" mt={0.5} flexWrap="wrap" useFlexGap>
                <Typography variant="caption" color="text.secondary" component="code">{selected.eventType}</Typography>
                <Typography variant="body2" color="text.secondary">{format.fullDateTime(selected.occurredAt)}</Typography>
              </Stack>
              <Stack direction="row" spacing={1} mt={1.5} flexWrap="wrap" useFlexGap><StatusChip status={selected.action} label={labels.action(selected.action)} /><StatusChip status={selected.severity} label={labels.severity(selected.severity)} /><StatusChip status={selected.billing_owner} label={labels.billing(selected.billing_owner)} /></Stack>
            </Box>
            <Box sx={{ display: 'grid', gridTemplateColumns: '140px 1fr', gap: 1.5 }}>
              <DetailLabel>{t('audit.actor')}</DetailLabel><DetailValue>{selected.actorLabel} · {labels.actorType(selected.actorType)}</DetailValue>
              <DetailLabel>{t('audit.requestId')}</DetailLabel><DetailValue code>{selected.requestId ?? '—'}</DetailValue>
              <DetailLabel>{t('audit.model')}</DetailLabel><DetailValue code>{selected.modelAlias ?? '—'}</DetailValue>
              <DetailLabel>{t('audit.reason')}</DetailLabel><DetailValue>{selected.reasonCode ? `${labels.reason(selected.reasonCode)} · ${selected.reasonCode}` : '—'}</DetailValue>
              <DetailLabel>{t('audit.tokens')}</DetailLabel><DetailValue>{selected.inputTokens == null && selected.outputTokens == null ? '—' : t('audit.tokenDetail', { input: format.number(selected.inputTokens ?? 0), output: format.number(selected.outputTokens ?? 0) })}</DetailValue>
              <DetailLabel>{t('table.billing')}</DetailLabel><DetailValue>{labels.billing(selected.billing_owner)}</DetailValue>
              <DetailLabel>{t('audit.hash')}</DetailLabel><DetailValue code>{selected.hash}</DetailValue>
            </Box>
            <Box><Typography variant="overline" color="text.secondary">{t('audit.metadata')}</Typography><Typography variant="caption" color="text.secondary" display="block" mt={0.5}>{t('audit.rawMetadataHint')}</Typography><Box mt={1}><CodeBlock code={JSON.stringify(selected, null, 2)} label={selected.eventId} /></Box></Box>
            <Button variant="outlined" color="warning" startIcon={<FlagOutlinedIcon />} onClick={() => setDisputeOpen(true)}>{t('audit.dispute')}</Button>
          </Stack>
        </Box>}
      </Drawer>

      <Dialog open={disputeOpen} onClose={disputeBusy ? undefined : () => setDisputeOpen(false)} fullWidth maxWidth="sm"><DialogTitle>{t('audit.dispute')}</DialogTitle><DialogContent><Stack spacing={2} mt={1}><Alert severity="info">{t('audit.disputeHint')}</Alert><TextField select label={t('audit.disputeType')} value={disputeType} onChange={(event) => setDisputeType(event.target.value)}><MenuItem value="false_positive">{t('audit.falsePositive')}</MenuItem><MenuItem value="other">{t('audit.other')}</MenuItem></TextField><TextField multiline minRows={4} label={t('audit.disputeNote')} value={disputeNote} onChange={(event) => setDisputeNote(event.target.value)} name="disputeNote" autoComplete="off" /></Stack></DialogContent><DialogActions><Button onClick={() => setDisputeOpen(false)} disabled={disputeBusy}>{t('common.cancel')}</Button><Button variant="contained" onClick={() => void submitDispute()} disabled={disputeBusy}>{disputeBusy ? t('common.working') : t('audit.dispute')}</Button></DialogActions></Dialog>

      <Dialog open={disputesOpen} onClose={() => setDisputesOpen(false)} fullWidth maxWidth="md"><DialogTitle>{t('audit.disputeList')}</DialogTitle><DialogContent><Typography variant="body2" color="text.secondary" mb={2}>{t('audit.disputeListHint')}</Typography>{disputes.length === 0 ? <EmptyState compact title={t('audit.noDisputes')} /> : <Stack spacing={1.5}>{disputes.map((dispute) => <Box key={dispute.id} sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 2 }}><Stack direction="row" justifyContent="space-between" alignItems="flex-start" spacing={2}><Box minWidth={0}><Typography variant="subtitle2">{labels.disputeType(dispute.dispute_type)}</Typography><Typography variant="caption" color="text.secondary" display="block" sx={{ wordBreak: 'break-all' }}>{dispute.eventId}</Typography><Typography variant="body2" mt={1}>{dispute.note || t('audit.noNote')}</Typography><Typography variant="caption" color="text.secondary" display="block" mt={1}>{t('audit.createdBy')}: {dispute.createdBy} · {format.fullDateTime(dispute.createdAt)}</Typography></Box><Chip size="small" color={dispute.resolvedAt ? 'success' : 'warning'} variant="outlined" label={dispute.resolvedAt ? t('audit.resolved') : t('audit.unresolved')} /></Stack></Box>)}</Stack>}</DialogContent><DialogActions><Button onClick={() => setDisputesOpen(false)}>{t('common.close')}</Button></DialogActions></Dialog>
    </Stack>
  );
}

function DetailLabel({ children }: { children: React.ReactNode }) {
  return <Typography variant="body2" color="text.secondary">{children}</Typography>;
}
function DetailValue({ children, code = false }: { children: React.ReactNode; code?: boolean }) {
  return <Typography variant="body2" component={code ? 'code' : 'p'} sx={{ wordBreak: 'break-word', minWidth: 0 }}>{children}</Typography>;
}
