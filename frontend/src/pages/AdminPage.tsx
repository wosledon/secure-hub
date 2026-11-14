import { useCallback, useEffect, useState } from 'react';
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
  MenuItem,
  Stack,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Tabs,
  TextField,
  Typography
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import RefreshIcon from '@mui/icons-material/Refresh';
import VerifiedIcon from '@mui/icons-material/Verified';
import { useTranslation } from 'react-i18next';
import { api, type AuditRow, type BudgetRow, type KeyRow, type PolicyRow, type ProviderRow, type UsageRow } from '../api';

export default function AdminPage() {
  const { t } = useTranslation();
  const [tab, setTab] = useState(0);

  return (
    <Stack spacing={3}>
      <Box>
        <Typography variant="h1">{t('nav.admin')}</Typography>
        <Typography color="text.secondary">{t('admin.overviewHint')}</Typography>
      </Box>

      <Tabs value={tab} onChange={(_, value) => setTab(value)} variant="scrollable">
        <Tab label={t('nav.overview')} />
        <Tab label={t('nav.providers')} />
        <Tab label={t('nav.keys')} />
        <Tab label={t('nav.audit')} />
        <Tab label="策略" />
        <Tab label="预算" />
      </Tabs>

      {tab === 0 && <OverviewTab />}
      {tab === 1 && <ProvidersTab />}
      {tab === 2 && <KeysTab />}
      {tab === 3 && <AuditTab />}
      {tab === 4 && <PoliciesTab />}
      {tab === 5 && <BudgetsTab />}
    </Stack>
  );
}

function PoliciesTab() {
  const [rows, setRows] = useState<PolicyRow[]>([]);
  const [policyKey, setPolicyKey] = useState('pol-pii-block');
  const [detector, setDetector] = useState('pii.cn_idcard');
  const [action, setAction] = useState('block');
  const [message, setMessage] = useState<string | null>(null);

  const load = useCallback(async () => setRows(await api.policies()), []);
  useEffect(() => {
    void load();
  }, [load]);

  const publish = async () => {
    try {
      const result = await api.publishPolicy({
        policyKey,
        priority: 100,
        scope: { teams: [], apps: [], models: [] },
        when: { any: [{ detector, minMatches: 1 }] },
        then: { action, reason: detector, ruleId: detector },
        onError: 'block',
        rolloutPercent: 100,
        changeReason: 'ui'
      });
      setMessage(`v${result.version} 已发布，快照重载=${result.snapshot_reloaded}`);
      await load();
    } catch (err) {
      setMessage(String(err));
    }
  };

  return (
    <Card>
      <CardContent>
        <Typography variant="h2" mb={2}>
          安全策略
        </Typography>

        <Stack direction={{ xs: 'column', md: 'row' }} spacing={2} mb={2}>
          <TextField label="policyKey" value={policyKey} onChange={(e) => setPolicyKey(e.target.value)} size="small" />
          <TextField label="detector" value={detector} onChange={(e) => setDetector(e.target.value)} size="small" />
          <TextField select label="action" value={action} onChange={(e) => setAction(e.target.value)} size="small" sx={{ minWidth: 140 }}>
            <MenuItem value="block">block</MenuItem>
            <MenuItem value="redact">redact</MenuItem>
            <MenuItem value="alert">alert</MenuItem>
          </TextField>
          <Button variant="contained" onClick={() => void publish()}>
            发布新版本
          </Button>
        </Stack>

        {message && <Alert severity="info" sx={{ mb: 2 }}>{message}</Alert>}

        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>policy</TableCell>
              <TableCell>active</TableCell>
              <TableCell>priority</TableCell>
              <TableCell>when</TableCell>
              <TableCell>then</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((row) => {
              const active = row.versions.find((v) => v.version === row.active_version);
              return (
                <TableRow key={row.policy_key}>
                  <TableCell>{row.policy_key}</TableCell>
                  <TableCell>v{row.active_version ?? '-'}</TableCell>
                  <TableCell>{active?.priority ?? '-'}</TableCell>
                  <TableCell sx={{ maxWidth: 260, overflow: 'hidden', textOverflow: 'ellipsis' }}>
                    {active?.whenJson ?? '-'}
                  </TableCell>
                  <TableCell>{active?.thenJson ?? '-'}</TableCell>
                </TableRow>
              );
            })}
            {rows.length === 0 && (
              <TableRow>
                <TableCell colSpan={5}>暂无策略</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

function BudgetsTab() {
  const [rows, setRows] = useState<BudgetRow[]>([]);
  const [tokenLimit, setTokenLimit] = useState('1000000');
  const [period, setPeriod] = useState('month');
  const [message, setMessage] = useState<string | null>(null);

  const load = useCallback(async () => setRows(await api.budgets()), []);
  useEffect(() => {
    void load();
  }, [load]);

  const save = async () => {
    try {
      await api.upsertBudget({
        subject: 0,
        subjectId: null,
        period,
        tokenLimit: Number(tokenLimit),
        costLimit: null,
        enabled: true
      });
      setMessage('已保存');
      await load();
    } catch (err) {
      setMessage(String(err));
    }
  };

  return (
    <Card>
      <CardContent>
        <Typography variant="h2" mb={2}>
          预算与配额
        </Typography>

        <Stack direction="row" spacing={2} mb={2}>
          <TextField label="token 上限" value={tokenLimit} onChange={(e) => setTokenLimit(e.target.value)} size="small" />
          <TextField select label="周期" value={period} onChange={(e) => setPeriod(e.target.value)} size="small" sx={{ minWidth: 140 }}>
            <MenuItem value="day">day</MenuItem>
            <MenuItem value="week">week</MenuItem>
            <MenuItem value="month">month</MenuItem>
          </TextField>
          <Button variant="contained" onClick={() => void save()}>
            保存
          </Button>
        </Stack>

        {message && <Alert severity="info" sx={{ mb: 2 }}>{message}</Alert>}

        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>subject</TableCell>
              <TableCell>period</TableCell>
              <TableCell>tokens</TableCell>
              <TableCell>cost</TableCell>
              <TableCell>enabled</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.id}>
                <TableCell>{row.subject}</TableCell>
                <TableCell>{row.period}</TableCell>
                <TableCell>
                  {row.tokensUsed.toLocaleString()} / {row.tokenLimit?.toLocaleString() ?? '∞'}
                </TableCell>
                <TableCell>
                  {row.costUsed.toFixed(2)} / {row.costLimit ?? '∞'}
                </TableCell>
                <TableCell>{row.enabled ? 'on' : 'off'}</TableCell>
              </TableRow>
            ))}
            {rows.length === 0 && (
              <TableRow>
                <TableCell colSpan={5}>暂无预算</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </CardContent>
    </Card>
  );
}

function StatCard({ label, value }: { label: string; value: string | number }) {
  return (
    <Card sx={{ flex: 1, minWidth: 160 }}>
      <CardContent>
        <Typography variant="body2" color="text.secondary">
          {label}
        </Typography>
        <Typography variant="h4" fontWeight={600}>
          {value}
        </Typography>
      </CardContent>
    </Card>
  );
}

function OverviewTab() {
  const { t } = useTranslation();
  const [providers, setProviders] = useState<ProviderRow[]>([]);
  const [keys, setKeys] = useState<KeyRow[]>([]);
  const [audit, setAudit] = useState<AuditRow[]>([]);
  const [usage, setUsage] = useState<UsageRow[]>([]);

  const load = useCallback(async () => {
    const [p, k, a] = await Promise.all([api.providers(), api.keys(), api.audit()]);
    setProviders(p);
    setKeys(k);
    setAudit(a.data);
    setUsage([]);
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const blocked = audit.filter((row) => row.action === 'block').length;
  const personal = audit.filter((row) => row.billing_owner === 'user').length;

  return (
    <Stack spacing={3}>
      <Stack direction={{ xs: 'column', md: 'row' }} spacing={2}>
        <StatCard label={t('nav.providers')} value={providers.length} />
        <StatCard label={t('nav.keys')} value={keys.filter((k) => !k.is_revoked).length} />
        <StatCard label={t('nav.audit')} value={audit.length} />
        <StatCard label="blocked" value={blocked} />
        <StatCard label="billing=user" value={personal} />
      </Stack>

      <Card>
        <CardContent>
          <Stack direction="row" justifyContent="space-between" alignItems="center" mb={2}>
            <Typography variant="h2">Recent audit</Typography>
            <Button startIcon={<RefreshIcon />} onClick={() => void load()}>
              {t('common.refresh')}
            </Button>
          </Stack>
          <AuditTable rows={audit.slice(0, 10)} />
          {usage.length > 0 && <Typography color="text.secondary">{usage.length} usage rows</Typography>}
        </CardContent>
      </Card>
    </Stack>
  );
}

function ProvidersTab() {
  const { t } = useTranslation();
  const [rows, setRows] = useState<ProviderRow[]>([]);
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState({
    alias: '',
    providerKey: 'openai',
    baseUrl: 'https://api.openai.com/v1',
    apiKey: '',
    models: 'gpt-4o'
  });
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => setRows(await api.providers()), []);
  useEffect(() => {
    void load();
  }, [load]);

  const submit = async () => {
    setError(null);
    try {
      await api.createProvider({
        alias: form.alias,
        providerKey: form.providerKey,
        baseUrl: form.baseUrl,
        apiKey: form.apiKey,
        models: form.models.split(',').map((m) => m.trim()).filter(Boolean)
      });
      setOpen(false);
      await load();
    } catch (err) {
      setError(String(err));
    }
  };

  return (
    <Card>
      <CardContent>
        <Stack direction="row" justifyContent="space-between" mb={2}>
          <Typography variant="h2">{t('nav.providers')}</Typography>
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => setOpen(true)}>
            {t('admin.createProvider')}
          </Button>
        </Stack>

        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>{t('common.alias')}</TableCell>
              <TableCell>{t('common.provider')}</TableCell>
              <TableCell>{t('common.baseUrl')}</TableCell>
              <TableCell>scope</TableCell>
              <TableCell>health</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.id}>
                <TableCell>
                  <Chip size="small" label={row.alias} />
                </TableCell>
                <TableCell>{row.providerKey}</TableCell>
                <TableCell>{row.baseUrl}</TableCell>
                <TableCell>{row.scope}</TableCell>
                <TableCell>
                  <Chip size="small" color={row.healthStatus === 'healthy' ? 'success' : 'default'} label={row.healthStatus} />
                </TableCell>
              </TableRow>
            ))}
            {rows.length === 0 && (
              <TableRow>
                <TableCell colSpan={5}>{t('common.empty')}</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </CardContent>

      <Dialog open={open} onClose={() => setOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>{t('admin.createProvider')}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} mt={1}>
            <TextField label={t('common.alias')} value={form.alias} onChange={(e) => setForm({ ...form, alias: e.target.value })} helperText="全局唯一，用于 org/<alias>/<model>" />
            <TextField label={t('common.provider')} value={form.providerKey} onChange={(e) => setForm({ ...form, providerKey: e.target.value })} />
            <TextField label={t('common.baseUrl')} value={form.baseUrl} onChange={(e) => setForm({ ...form, baseUrl: e.target.value })} />
            <TextField label={t('common.apiKey')} type="password" value={form.apiKey} onChange={(e) => setForm({ ...form, apiKey: e.target.value })} />
            <TextField label={t('common.models')} value={form.models} onChange={(e) => setForm({ ...form, models: e.target.value })} helperText="逗号分隔" />
            {error && <Alert severity="error">{error}</Alert>}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>{t('common.cancel')}</Button>
          <Button variant="contained" onClick={() => void submit()} disabled={!form.alias || !form.apiKey}>
            {t('common.save')}
          </Button>
        </DialogActions>
      </Dialog>
    </Card>
  );
}

function KeysTab() {
  const { t } = useTranslation();
  const [rows, setRows] = useState<KeyRow[]>([]);
  const [open, setOpen] = useState(false);
  const [displayName, setDisplayName] = useState('app key');
  const [created, setCreated] = useState<string | null>(null);

  const load = useCallback(async () => setRows(await api.keys()), []);
  useEffect(() => {
    void load();
  }, [load]);

  const create = async () => {
    const result = await api.createKey({
      displayName,
      ownerType: 0,
      ownerId: '00000000-0000-0000-0000-000000000000'
    });
    setCreated(result.key);
    await load();
  };

  return (
    <Card>
      <CardContent>
        <Stack direction="row" justifyContent="space-between" mb={2}>
          <Typography variant="h2">{t('nav.keys')}</Typography>
          <Button variant="contained" startIcon={<AddIcon />} onClick={() => { setOpen(true); setCreated(null); }}>
            {t('admin.createKey')}
          </Button>
        </Stack>

        <Table size="small">
          <TableHead>
            <TableRow>
              <TableCell>{t('common.name')}</TableCell>
              <TableCell>key id</TableCell>
              <TableCell>owner</TableCell>
              <TableCell>status</TableCell>
              <TableCell>{t('common.createdAt')}</TableCell>
              <TableCell>{t('common.actions')}</TableCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {rows.map((row) => (
              <TableRow key={row.id}>
                <TableCell>{row.display_name}</TableCell>
                <TableCell>{row.key_id}</TableCell>
                <TableCell>{row.owner_type}</TableCell>
                <TableCell>
                  <Chip size="small" color={row.is_revoked ? 'default' : 'success'} label={row.is_revoked ? 'revoked' : 'active'} />
                </TableCell>
                <TableCell>{new Date(row.created_at).toLocaleString()}</TableCell>
                <TableCell>
                  <Button size="small" disabled={row.is_revoked} onClick={() => void api.revokeKey(row.id).then(load)}>
                    revoke
                  </Button>
                </TableCell>
              </TableRow>
            ))}
            {rows.length === 0 && (
              <TableRow>
                <TableCell colSpan={6}>{t('common.empty')}</TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </CardContent>

      <Dialog open={open} onClose={() => setOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>{t('admin.createKey')}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} mt={1}>
            <TextField label={t('common.name')} value={displayName} onChange={(e) => setDisplayName(e.target.value)} />
            {created && (
              <Alert severity="warning">
                <Typography variant="body2" mb={1}>
                  {t('admin.keyOnce')}
                </Typography>
                <Box component="code" sx={{ wordBreak: 'break-all' }}>
                  {created}
                </Box>
              </Alert>
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setOpen(false)}>{t('common.cancel')}</Button>
          <Button variant="contained" onClick={() => void create()}>
            {t('common.create')}
          </Button>
        </DialogActions>
      </Dialog>
    </Card>
  );
}

function AuditTab() {
  const { t } = useTranslation();
  const [rows, setRows] = useState<AuditRow[]>([]);
  const [verify, setVerify] = useState<{ ok: boolean; checked: number; seq: number | null } | null>(null);
  const [eventType, setEventType] = useState('');

  const load = useCallback(async () => {
    const result = await api.audit(eventType ? { eventType } : undefined);
    setRows(result.data);
  }, [eventType]);

  useEffect(() => {
    void load();
  }, [load]);

  const runVerify = async () => {
    const result = await api.verifyAudit();
    setVerify({ ok: result.ok, checked: result.checked_count, seq: result.first_break_sequence });
  };

  return (
    <Card>
      <CardContent>
        <Stack direction="row" spacing={2} alignItems="center" mb={2}>
          <Typography variant="h2" flexGrow={1}>
            {t('nav.audit')}
          </Typography>
          <TextField
            select
            size="small"
            label="eventType"
            value={eventType}
            onChange={(e) => setEventType(e.target.value)}
            sx={{ minWidth: 200 }}
          >
            <MenuItem value="">all</MenuItem>
            <MenuItem value="route_selected">route_selected</MenuItem>
            <MenuItem value="guardrail_blocked">guardrail_blocked</MenuItem>
            <MenuItem value="auth_failed">auth_failed</MenuItem>
            <MenuItem value="key_created">key_created</MenuItem>
          </TextField>
          <Button startIcon={<RefreshIcon />} onClick={() => void load()}>
            {t('common.refresh')}
          </Button>
          <Button startIcon={<VerifiedIcon />} variant="outlined" onClick={() => void runVerify()}>
            {t('admin.verifyChain')}
          </Button>
        </Stack>

        {verify && (
          <Alert severity={verify.ok ? 'success' : 'error'} sx={{ mb: 2 }}>
            {verify.ok
              ? t('admin.chainOk', { count: verify.checked })
              : t('admin.chainBroken', { seq: verify.seq })}
          </Alert>
        )}

        <AuditTable rows={rows} />
      </CardContent>
    </Card>
  );
}

function AuditTable({ rows }: { rows: AuditRow[] }) {
  const { t } = useTranslation();
  return (
    <Table size="small">
      <TableHead>
        <TableRow>
          <TableCell>#</TableCell>
          <TableCell>time</TableCell>
          <TableCell>event</TableCell>
          <TableCell>action</TableCell>
          <TableCell>actor</TableCell>
          <TableCell>model</TableCell>
          <TableCell>tokens</TableCell>
          <TableCell>{t('table.billing')}</TableCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {rows.map((row) => (
          <TableRow key={row.eventId}>
            <TableCell>{row.sequence}</TableCell>
            <TableCell>{new Date(row.occurredAt).toLocaleString()}</TableCell>
            <TableCell>
              <Chip size="small" label={row.eventType} variant="outlined" />
            </TableCell>
            <TableCell>
              <Chip size="small" color={row.action === 'block' ? 'error' : 'default'} label={row.action} />
            </TableCell>
            <TableCell>{row.actorLabel}</TableCell>
            <TableCell>{row.modelAlias ?? '—'}</TableCell>
            <TableCell>
              {row.inputTokens ?? '—'}
              {row.inputTokensOriginal ? ` (was ${row.inputTokensOriginal})` : ''}
            </TableCell>
            <TableCell>
              <Chip size="small" variant="outlined" label={row.billing_owner} />
            </TableCell>
          </TableRow>
        ))}
        {rows.length === 0 && (
          <TableRow>
            <TableCell colSpan={8}>{t('common.empty')}</TableCell>
          </TableRow>
        )}
      </TableBody>
    </Table>
  );
}
