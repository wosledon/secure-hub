import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Card,
  Checkbox,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  IconButton,
  InputAdornment,
  ListItemText,
  MenuItem,
  OutlinedInput,
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
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import RefreshIcon from '@mui/icons-material/Refresh';
import SearchIcon from '@mui/icons-material/Search';
import VerifiedUserOutlinedIcon from '@mui/icons-material/VerifiedUserOutlined';
import { useTranslation } from 'react-i18next';
import { api, type EntitlementRow, type OrgTeamRow, type OrgUserRow, type ProviderDetail, type ProviderRow } from '../api';
import { ConfirmDialog, EmptyState, ErrorState, PageHeader, TableSkeleton } from '../components/ui';
import { useFormatters } from '../utils/format';

export default function EntitlementsPage() {
  const { t } = useTranslation();
  const format = useFormatters();
  const [rows, setRows] = useState<EntitlementRow[]>([]);
  const [providers, setProviders] = useState<ProviderRow[]>([]);
  const [teams, setTeams] = useState<OrgTeamRow[]>([]);
  const [users, setUsers] = useState<OrgUserRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [dialog, setDialog] = useState<{ mode: 'create' } | { mode: 'edit'; row: EntitlementRow } | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<EntitlementRow | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setLoading(true); setError(null);
    try {
      const [entitlementRows, providerRows, teamRows, userRows] = await Promise.all([
        api.entitlements(), api.providers(), api.orgTeams(), api.orgUsers()
      ]);
      setRows(entitlementRows);
      setProviders(providerRows);
      setTeams(teamRows);
      setUsers(userRows);
    } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  const filtered = useMemo(() => {
    const search = query.trim().toLowerCase();
    return rows.filter((row) => !search ||
      `${row.provider_alias ?? ''} ${row.subject_label ?? ''} ${row.subject_type} ${row.models.join(' ')}`.toLowerCase().includes(search));
  }, [rows, query]);

  const remove = async () => {
    if (!deleteTarget) return;
    setBusy(true);
    try {
      await api.deleteEntitlement(deleteTarget.id);
      setDeleteTarget(null);
      setMessage(t('entitlements.deleted'));
      await load();
    } catch (err) { setError((err as Error).message); } finally { setBusy(false); }
  };

  const subjectLabel = (row: EntitlementRow) =>
    row.subject_type === 'all'
      ? t('entitlements.subjectAll')
      : row.subject_type === 'team'
        ? `${t('entitlements.subjectTeam')} · ${row.subject_label ?? row.subjectId}`
        : `${t('entitlements.subjectUser')} · ${row.subject_label ?? row.subjectId}`;

  return (
    <Stack spacing={3}>
      <PageHeader title={t('entitlements.title')} description={t('entitlements.description')} actions={<>
        <Tooltip title={t('common.refresh')}><IconButton onClick={() => void load()} aria-label={t('common.refresh')}><RefreshIcon /></IconButton></Tooltip>
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => setDialog({ mode: 'create' })}>{t('entitlements.add')}</Button>
      </>} />
      {error && <ErrorState message={error} retry={() => void load()} />}
      {message && <Alert severity="success" onClose={() => setMessage(null)}>{message}</Alert>}
      <Alert severity="info">{t('entitlements.ruleHint')}</Alert>

      <Card>
        <Box sx={{ p: 2, borderBottom: 1, borderColor: 'divider', display: 'flex', gap: 2, alignItems: 'center', flexWrap: 'wrap' }}>
          <Typography variant="h2" sx={{ flexGrow: 1 }}>{t('entitlements.listTitle')}</Typography>
          <TextField size="small" value={query} onChange={(event) => setQuery(event.target.value)} placeholder={t('entitlements.search')} name="entitlementSearch" autoComplete="off" sx={{ width: { xs: '100%', sm: 320 } }} InputProps={{ startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> }} />
        </Box>
        {loading ? <TableSkeleton rows={5} columns={5} /> : filtered.length === 0 ? (
          <EmptyState
            title={t('entitlements.empty')}
            description={t('entitlements.emptyHint')}
            action={<Button variant="contained" startIcon={<AddIcon />} onClick={() => setDialog({ mode: 'create' })}>{t('entitlements.add')}</Button>}
          />
        ) : (
          <Box sx={{ overflowX: 'auto' }}>
            <Table>
              <TableHead><TableRow>
                <TableCell>{t('entitlements.subject')}</TableCell>
                <TableCell>{t('common.provider')}</TableCell>
                <TableCell>{t('common.models')}</TableCell>
                <TableCell align="right">{t('usage.tokenLimit')}</TableCell>
                <TableCell align="right">{t('usage.costLimit')}</TableCell>
                <TableCell>{t('entitlements.expiresAt')}</TableCell>
                <TableCell align="right">{t('common.actions')}</TableCell>
              </TableRow></TableHead>
              <TableBody>
                {filtered.map((row) => (
                  <TableRow key={row.id} hover>
                    <TableCell><Stack direction="row" spacing={1} alignItems="center"><VerifiedUserOutlinedIcon fontSize="small" color="action" /><Typography variant="subtitle2">{subjectLabel(row)}</Typography></Stack></TableCell>
                    <TableCell><Box component="code" sx={{ fontSize: 12 }}>{row.provider_alias ?? row.providerId}</Box></TableCell>
                    <TableCell sx={{ maxWidth: 280 }}>
                      {row.models.length === 0
                        ? <Chip size="small" variant="outlined" label={t('entitlements.allModels')} />
                        : <Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>{row.models.slice(0, 3).map((model) => <Chip key={model} size="small" variant="outlined" label={model} />)}{row.models.length > 3 && <Chip size="small" label={`+${row.models.length - 3}`} />}</Stack>}
                    </TableCell>
                    <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{row.tokenQuota ? `${format.compact(row.tokensUsed ?? 0)} / ${format.compact(row.tokenQuota)}` : t('usage.unlimited')}</TableCell>
                    <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{row.costQuota ? format.currency(row.costQuota) : t('usage.unlimited')}</TableCell>
                    <TableCell>{row.expiresAt ? format.dateTime(row.expiresAt) : t('entitlements.never')}</TableCell>
                    <TableCell align="right"><Stack direction="row" spacing={0.5} justifyContent="flex-end">
                      <Tooltip title={t('common.configure')}><IconButton size="small" onClick={() => setDialog({ mode: 'edit', row })} aria-label={t('common.configure')}><EditOutlinedIcon fontSize="small" /></IconButton></Tooltip>
                      <Tooltip title={t('common.remove')}><IconButton size="small" color="error" onClick={() => setDeleteTarget(row)} aria-label={t('common.remove')}><DeleteOutlineIcon fontSize="small" /></IconButton></Tooltip>
                    </Stack></TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </Box>
        )}
      </Card>

      {dialog && (
        <EntitlementDialog
          state={dialog}
          providers={providers}
          teams={teams}
          users={users}
          onClose={() => setDialog(null)}
          onSaved={(text) => { setDialog(null); setMessage(text); void load(); }}
        />
      )}
      <ConfirmDialog
        open={!!deleteTarget}
        title={t('entitlements.deleteTitle')}
        description={t('entitlements.deleteHint')}
        confirmLabel={t('common.remove')}
        busy={busy}
        danger
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => void remove()}
      />
    </Stack>
  );
}

function EntitlementDialog({ state, providers, teams, users, onClose, onSaved }: {
  state: { mode: 'create' } | { mode: 'edit'; row: EntitlementRow };
  providers: ProviderRow[];
  teams: OrgTeamRow[];
  users: OrgUserRow[];
  onClose: () => void;
  onSaved: (message: string) => void;
}) {
  const { t } = useTranslation();
  const editing = state.mode === 'edit' ? state.row : null;
  const [subjectType, setSubjectType] = useState(editing?.subject_type ?? 'all');
  const [subjectId, setSubjectId] = useState(editing?.subjectId ?? '');
  const [providerId, setProviderId] = useState(editing?.providerId ?? '');
  const [detail, setDetail] = useState<ProviderDetail | null>(null);
  const [models, setModels] = useState<string[]>(editing?.models ?? []);
  const [tokenQuota, setTokenQuota] = useState(editing?.tokenQuota?.toString() ?? '');
  const [costQuota, setCostQuota] = useState(editing?.costQuota?.toString() ?? '');
  const [expiresAt, setExpiresAt] = useState(editing?.expiresAt?.slice(0, 10) ?? '');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!providerId) { setDetail(null); return; }
    api.providerDetail(providerId).then(setDetail).catch(() => setDetail(null));
  }, [providerId]);

  const offerings = detail?.offerings.filter((offering) => offering.Enabled).map((offering) => offering.UpstreamModel) ?? [];

  const submit = async () => {
    setBusy(true); setError(null);
    try {
      await api.upsertEntitlement({
        id: editing?.id ?? null,
        subjectType: subjectType === 'all' ? 0 : subjectType === 'team' ? 1 : 2,
        subjectId: subjectType === 'all' ? null : subjectId,
        providerId,
        models,
        tokenQuota: tokenQuota ? Number(tokenQuota) : null,
        costQuota: costQuota ? Number(costQuota) : null,
        expiresAt: expiresAt ? new Date(`${expiresAt}T23:59:59Z`).toISOString() : null
      });
      onSaved(t('entitlements.saved'));
    } catch (err) { setError((err as Error).message); } finally { setBusy(false); }
  };

  const valid = providerId && (subjectType === 'all' || subjectId);

  return (
    <Dialog open onClose={busy ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>{editing ? t('entitlements.edit') : t('entitlements.add')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2.25} mt={1}>
          <TextField select label={t('entitlements.subject')} value={subjectType} onChange={(event) => { setSubjectType(event.target.value); setSubjectId(''); }}>
            <MenuItem value="all">{t('entitlements.subjectAll')}</MenuItem>
            <MenuItem value="team">{t('entitlements.subjectTeam')}</MenuItem>
            <MenuItem value="user">{t('entitlements.subjectUser')}</MenuItem>
          </TextField>
          {subjectType === 'team' && (
            <TextField select label={t('org.teams')} value={subjectId} onChange={(event) => setSubjectId(event.target.value)}>
              {teams.map((team) => <MenuItem key={team.id} value={team.id}>{team.name}</MenuItem>)}
            </TextField>
          )}
          {subjectType === 'user' && (
            <TextField select label={t('org.users')} value={subjectId} onChange={(event) => setSubjectId(event.target.value)}>
              {users.map((user) => <MenuItem key={user.id} value={user.id}>{user.userName}</MenuItem>)}
            </TextField>
          )}
          <TextField select label={t('common.provider')} value={providerId} onChange={(event) => { setProviderId(event.target.value); setModels([]); }}>
            {providers.map((provider) => <MenuItem key={provider.id} value={provider.id}>{provider.alias} · {provider.providerKey}</MenuItem>)}
          </TextField>
          <TextField
            select
            label={t('common.models')}
            value={models}
            onChange={(event) => setModels(typeof event.target.value === 'string' ? event.target.value.split(',') : event.target.value)}
            SelectProps={{
              multiple: true,
              input: <OutlinedInput label={t('common.models')} />,
              renderValue: (selected) => (selected as string[]).length === 0
                ? t('entitlements.allModels')
                : (selected as string[]).join(', ')
            }}
            helperText={t('entitlements.modelsHint')}
            disabled={!providerId}
          >
            {offerings.map((model) => (
              <MenuItem key={model} value={model}>
                <Checkbox size="small" checked={models.includes(model)} />
                <ListItemText primary={model} />
              </MenuItem>
            ))}
          </TextField>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth type="number" label={t('usage.tokenLimit')} value={tokenQuota} onChange={(event) => setTokenQuota(event.target.value)} inputProps={{ min: 0 }} helperText={t('entitlements.unlimitedHint')} />
            <TextField fullWidth type="number" label={t('usage.costLimit')} value={costQuota} onChange={(event) => setCostQuota(event.target.value)} inputProps={{ min: 0, step: '0.01' }} />
          </Stack>
          <TextField type="date" label={t('entitlements.expiresAt')} value={expiresAt} onChange={(event) => setExpiresAt(event.target.value)} InputLabelProps={{ shrink: true }} helperText={t('entitlements.expiresHint')} />
          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
        <Button variant="contained" onClick={() => void submit()} disabled={busy || !valid}>{busy ? t('common.working') : t('common.save')}</Button>
      </DialogActions>
    </Dialog>
  );
}
