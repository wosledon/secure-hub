import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  LinearProgress,
  MenuItem,
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
import CurrencyExchangeIcon from '@mui/icons-material/CurrencyExchange';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import PaidOutlinedIcon from '@mui/icons-material/PaidOutlined';
import PersonOutlineIcon from '@mui/icons-material/PersonOutline';
import ReceiptLongIcon from '@mui/icons-material/ReceiptLong';
import RefreshIcon from '@mui/icons-material/Refresh';
import TokenOutlinedIcon from '@mui/icons-material/TokenOutlined';
import IconButton from '@mui/material/IconButton';
import { useTranslation } from 'react-i18next';
import { api, type BudgetRow, type UsageRow } from '../api';
import { EmptyState, ErrorState, PageHeader, StatCard, StatusChip, TableSkeleton } from '../components/ui';
import { useFormatters } from '../utils/format';

type BudgetForm = { subject: number; subjectId: string; period: string; tokenLimit: string; costLimit: string; enabled: boolean };
const emptyBudget: BudgetForm = { subject: 0, subjectId: '', period: 'month', tokenLimit: '1000000', costLimit: '', enabled: true };

export default function UsagePage() {
  const { t } = useTranslation();
  const format = useFormatters();
  const [usage, setUsage] = useState<UsageRow[]>([]);
  const [budgets, setBudgets] = useState<BudgetRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState<BudgetForm>(emptyBudget);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [usageResult, budgetRows] = await Promise.all([api.adminUsage(), api.budgets()]);
      setUsage(usageResult.data);
      setBudgets(budgetRows);
    } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  const metrics = useMemo(() => ({
    requests: usage.reduce((sum, row) => sum + row.requests, 0),
    tokens: usage.reduce((sum, row) => sum + row.input_tokens + row.output_tokens, 0),
    cost: usage.reduce((sum, row) => sum + row.cost_usd, 0),
    personal: usage.filter((row) => row.billing_owner === 'user').reduce((sum, row) => sum + row.requests, 0)
  }), [usage]);
  const sortedUsage = useMemo(() => [...usage].sort((a, b) => b.requests - a.requests), [usage]);
  const maxRequests = Math.max(...usage.map((row) => row.requests), 1);

  const editBudget = (budget?: BudgetRow) => {
    setForm(budget ? {
      subject: budgetSubjectValue(budget.subject), subjectId: budget.subjectId ?? '', period: budget.period,
      tokenLimit: budget.tokenLimit?.toString() ?? '', costLimit: budget.costLimit?.toString() ?? '', enabled: budget.enabled
    } : emptyBudget);
    setFormError(null);
    setSaved(false);
    setOpen(true);
  };

  const save = async () => {
    setBusy(true);
    setFormError(null);
    try {
      await api.upsertBudget({
        subject: form.subject, subjectId: form.subject === 0 ? null : form.subjectId.trim(), period: form.period,
        tokenLimit: form.tokenLimit ? Number(form.tokenLimit) : null, costLimit: form.costLimit ? Number(form.costLimit) : null, enabled: form.enabled
      });
      setOpen(false);
      setSaved(true);
      await load();
    } catch (err) { setFormError((err as Error).message); } finally { setBusy(false); }
  };

  const subjectLabel = (subject: string | number) => {
    const value = String(subject).toLowerCase();
    if (value === '0' || value.includes('org') || value.includes('all')) return t('usage.org');
    if (value === '1' || value.includes('team')) return t('usage.team');
    return t('usage.app');
  };
  const budgetSubjectValue = (subject: string | number) => {
    const value = String(subject).toLowerCase();
    if (value.includes('team') || value === '1') return 1;
    if (value.includes('app') || value === '2') return 2;
    return 0;
  };

  return (
    <Stack spacing={3}>
      <PageHeader title={t('usage.title')} description={t('usage.description')} actions={<>
        <Tooltip title={t('common.refresh')}><IconButton onClick={() => void load()} aria-label={t('common.refresh')}><RefreshIcon /></IconButton></Tooltip>
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => editBudget()}>{t('usage.addBudget')}</Button>
      </>} />
      {error && <ErrorState message={error} retry={() => void load()} />}
      {saved && <Alert severity="success" onClose={() => setSaved(false)}>{t('usage.saved')}</Alert>}
      <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, 1fr)', lg: 'repeat(4, 1fr)' }, gap: 2 }}>
        <StatCard label={t('usage.totalRequests')} value={loading ? '—' : format.number(metrics.requests)} caption={t('common.last7Days')} icon={<ReceiptLongIcon fontSize="small" />} />
        <StatCard label={t('usage.totalTokens')} value={loading ? '—' : format.compact(metrics.tokens)} caption={`${format.compact(usage.reduce((sum, row) => sum + row.input_tokens, 0))} ${t('usage.input')}`} icon={<TokenOutlinedIcon fontSize="small" />} tone="secondary" />
        <StatCard label={t('usage.totalCost')} value={loading ? '—' : format.currency(metrics.cost)} caption="USD" icon={<CurrencyExchangeIcon fontSize="small" />} tone="success" />
        <StatCard label={t('usage.personalRequests')} value={loading ? '—' : format.number(metrics.personal)} caption={t('me.billingUser')} icon={<PersonOutlineIcon fontSize="small" />} tone="warning" />
      </Box>

      <Card>
        <CardContent sx={{ pb: '16px !important' }}><Typography variant="h2">{t('usage.usageByModel')}</Typography><Typography variant="body2" color="text.secondary" mt={0.5}>{t('usage.usageHint')}</Typography></CardContent>
        {loading ? <TableSkeleton rows={5} columns={6} /> : usage.length === 0 ? <EmptyState title={t('common.empty')} /> : (
          <Box sx={{ overflowX: 'auto' }}><Table>
            <TableHead><TableRow><TableCell>{t('audit.model')}</TableCell><TableCell>{t('usage.billingOwner')}</TableCell><TableCell>{t('usage.requests')}</TableCell><TableCell align="right">{t('usage.input')}</TableCell><TableCell align="right">{t('usage.output')}</TableCell><TableCell align="right">{t('usage.cost')}</TableCell></TableRow></TableHead>
            <TableBody>{sortedUsage.map((row, index) => (
              <TableRow key={`${row.model}-${row.billing_owner}-${index}`} hover>
                <TableCell sx={{ minWidth: 220 }}><Box component="code" sx={{ fontSize: 12, wordBreak: 'break-all' }}>{row.model}</Box></TableCell>
                <TableCell><StatusChip status={row.billing_owner ?? 'org'} label={row.billing_owner === 'user' ? t('me.billingUser') : t('me.billingOrg')} /></TableCell>
                <TableCell sx={{ minWidth: 180 }}><Stack direction="row" spacing={1.5} alignItems="center"><Typography variant="body2" minWidth={42} sx={{ fontVariantNumeric: 'tabular-nums' }}>{format.number(row.requests)}</Typography><Box sx={{ flex: 1, height: 6, bgcolor: 'action.hover', borderRadius: 2, overflow: 'hidden' }}><Box sx={{ height: '100%', width: `${(row.requests / maxRequests) * 100}%`, bgcolor: 'primary.main', borderRadius: 2 }} /></Box></Stack></TableCell>
                <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{format.number(row.input_tokens)}</TableCell><TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{format.number(row.output_tokens)}</TableCell><TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{format.currency(row.cost_usd)}</TableCell>
              </TableRow>
            ))}</TableBody>
          </Table></Box>
        )}
      </Card>

      <Box>
        <Stack direction="row" justifyContent="space-between" alignItems="flex-start" mb={2}><Box><Typography variant="h2">{t('usage.budgets')}</Typography><Typography variant="body2" color="text.secondary" mt={0.5}>{t('usage.budgetsHint')}</Typography></Box><Button size="small" startIcon={<AddIcon />} onClick={() => editBudget()}>{t('usage.addBudget')}</Button></Stack>
        {loading ? <Card><TableSkeleton rows={3} columns={3} /></Card> : budgets.length === 0 ? <Card><EmptyState title={t('usage.addBudget')} action={<Button variant="outlined" startIcon={<AddIcon />} onClick={() => editBudget()}>{t('usage.addBudget')}</Button>} /></Card> : (
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: 'repeat(2, 1fr)', xl: 'repeat(3, 1fr)' }, gap: 2 }}>
            {budgets.map((budget) => {
              const tokenPercent = budget.tokenLimit ? Math.min(100, (budget.tokensUsed / budget.tokenLimit) * 100) : 0;
              const costPercent = budget.costLimit ? Math.min(100, (budget.costUsed / budget.costLimit) * 100) : 0;
              const percent = Math.max(tokenPercent, costPercent);
              return (
                <Card key={budget.id}>
                  <CardContent>
                    <Stack direction="row" justifyContent="space-between" alignItems="flex-start" mb={2}><Stack direction="row" spacing={1.25} alignItems="center"><Box sx={{ width: 34, height: 34, display: 'grid', placeItems: 'center', borderRadius: 2, bgcolor: 'action.hover', color: 'primary.main' }}><PaidOutlinedIcon fontSize="small" /></Box><Box><Typography variant="subtitle1">{subjectLabel(budget.subject)}</Typography><Typography variant="caption" color="text.secondary">{t(`usage.${budget.period}`)}{budget.subjectId ? ` · ${budget.subjectId}` : ''}</Typography></Box></Stack><Tooltip title={t('common.configure')}><IconButton size="small" onClick={() => editBudget(budget)} aria-label={t('common.configure')}><EditOutlinedIcon fontSize="small" /></IconButton></Tooltip></Stack>
                    <Stack spacing={2}>
                      <Box><Stack direction="row" justifyContent="space-between" mb={0.75}><Typography variant="body2" color="text.secondary">{t('usage.tokenLimit')}</Typography><Typography variant="caption" sx={{ fontVariantNumeric: 'tabular-nums' }}>{format.compact(budget.tokensUsed)} / {budget.tokenLimit ? format.compact(budget.tokenLimit) : '∞'}</Typography></Stack><LinearProgress variant="determinate" value={tokenPercent} color={tokenPercent >= 90 ? 'error' : tokenPercent >= 75 ? 'warning' : 'primary'} /></Box>
                      <Box><Stack direction="row" justifyContent="space-between" mb={0.75}><Typography variant="body2" color="text.secondary">{t('usage.cost')}</Typography><Typography variant="caption" sx={{ fontVariantNumeric: 'tabular-nums' }}>{format.currency(budget.costUsed)} / {budget.costLimit ? format.currency(budget.costLimit) : '∞'}</Typography></Stack><LinearProgress variant="determinate" value={costPercent} color={costPercent >= 90 ? 'error' : costPercent >= 75 ? 'warning' : 'secondary'} /></Box>
                    </Stack>
                    <Stack direction="row" justifyContent="space-between" alignItems="center" mt={2}><StatusChip status={budget.enabled ? 'enabled' : 'disabled'} label={budget.enabled ? t('common.enabled') : t('common.disabled')} /><Chip size="small" color={percent >= 90 ? 'error' : 'default'} variant="outlined" label={`${percent.toFixed(0)}% ${t('usage.used')}`} /></Stack>
                  </CardContent>
                </Card>
              );
            })}
          </Box>
        )}
      </Box>

      <Dialog open={open} onClose={busy ? undefined : () => setOpen(false)} fullWidth maxWidth="sm"><DialogTitle>{t('usage.addBudget')}</DialogTitle><DialogContent><Stack spacing={2.25} mt={1}>
        <TextField select label={t('usage.subject')} value={form.subject} onChange={(event) => setForm({ ...form, subject: Number(event.target.value) })}><MenuItem value={0}>{t('usage.org')}</MenuItem><MenuItem value={1}>{t('usage.team')}</MenuItem><MenuItem value={2}>{t('usage.app')}</MenuItem></TextField>
        {form.subject !== 0 && <TextField label={t('keys.ownerId')} value={form.subjectId} onChange={(event) => setForm({ ...form, subjectId: event.target.value })} helperText={t('usage.subjectIdHint')} autoComplete="off" required />}
        <TextField select label={t('usage.period')} value={form.period} onChange={(event) => setForm({ ...form, period: event.target.value })}><MenuItem value="day">{t('usage.day')}</MenuItem><MenuItem value="week">{t('usage.week')}</MenuItem><MenuItem value="month">{t('usage.month')}</MenuItem></TextField>
        <TextField type="number" label={t('usage.tokenLimit')} value={form.tokenLimit} onChange={(event) => setForm({ ...form, tokenLimit: event.target.value })} inputProps={{ min: 0 }} />
        <TextField type="number" label={t('usage.costLimit')} value={form.costLimit} onChange={(event) => setForm({ ...form, costLimit: event.target.value })} inputProps={{ min: 0, step: '0.01' }} />
        <FormControlLabel control={<Switch checked={form.enabled} onChange={(event) => setForm({ ...form, enabled: event.target.checked })} />} label={t('common.enabled')} />
        {formError && <Alert severity="error">{formError}</Alert>}
      </Stack></DialogContent><DialogActions><Button onClick={() => setOpen(false)} disabled={busy}>{t('common.cancel')}</Button><Button variant="contained" onClick={() => void save()} disabled={busy || (form.subject !== 0 && !form.subjectId.trim()) || (!form.tokenLimit && !form.costLimit)}>{busy ? t('common.working') : t('common.save')}</Button></DialogActions></Dialog>
    </Stack>
  );
}
