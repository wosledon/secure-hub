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
  Divider,
  FormControlLabel,
  IconButton,
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
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import RefreshIcon from '@mui/icons-material/Refresh';
import RouteIcon from '@mui/icons-material/Route';
import { useTranslation } from 'react-i18next';
import { api, type ModelGroupRow, type ProviderRow } from '../api';
import { EmptyState, ErrorState, PageHeader, StatusChip, TableSkeleton } from '../components/ui';

type MemberForm = { providerId: string; upstreamModel: string; priority: number; weight: number; enabled: boolean };
const emptyMember: MemberForm = { providerId: '', upstreamModel: '', priority: 100, weight: 100, enabled: true };

export default function RoutingPage() {
  const { t } = useTranslation();
  const [rows, setRows] = useState<ModelGroupRow[]>([]);
  const [providers, setProviders] = useState<ProviderRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [success, setSuccess] = useState(false);
  const [name, setName] = useState('');
  const [strategy, setStrategy] = useState('priority-weighted');
  const [crossScope, setCrossScope] = useState(false);
  const [members, setMembers] = useState<MemberForm[]>([{ ...emptyMember }]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [groups, providerRows] = await Promise.all([api.modelGroups(), api.providers()]);
      setRows(groups);
      setProviders(providerRows);
    } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  const startCreate = () => {
    setName('');
    setStrategy('priority-weighted');
    setCrossScope(false);
    setMembers([{ ...emptyMember, providerId: providers[0]?.id ?? '' }]);
    setFormError(null);
    setSuccess(false);
    setOpen(true);
  };

  const startEdit = (group: ModelGroupRow) => {
    setName(group.name);
    setStrategy(group.strategy);
    setCrossScope(group.allowCrossScope);
    setMembers(group.members.map((member) => ({
      providerId: member.providerId ?? providers.find((provider) => provider.alias === member.provider)?.id ?? '',
      upstreamModel: member.upstreamModel,
      priority: member.priority,
      weight: member.weight,
      enabled: member.enabled
    })));
    setFormError(null);
    setOpen(true);
  };

  const updateMember = (index: number, patch: Partial<MemberForm>) => {
    setMembers((current) => current.map((member, memberIndex) => memberIndex === index ? { ...member, ...patch } : member));
  };

  const save = async () => {
    setBusy(true);
    setFormError(null);
    try {
      await api.upsertModelGroup({ name: name.trim(), allowCrossScope: crossScope, strategy, members: members.filter((member) => member.providerId && member.upstreamModel.trim()) });
      setOpen(false);
      setSuccess(true);
      await load();
    } catch (err) { setFormError((err as Error).message); } finally { setBusy(false); }
  };

  return (
    <Stack spacing={3}>
      <PageHeader title={t('routing.title')} description={t('routing.description')} actions={<>
        <Tooltip title={t('common.refresh')}><IconButton onClick={() => void load()} aria-label={t('common.refresh')}><RefreshIcon /></IconButton></Tooltip>
        <Button variant="contained" startIcon={<AddIcon />} onClick={startCreate}>{t('routing.create')}</Button>
      </>} />
      {error && <ErrorState message={error} retry={() => void load()} />}
      {success && <Alert severity="success" onClose={() => setSuccess(false)}>{t('routing.saved')}</Alert>}
      {loading ? <Card><TableSkeleton rows={6} columns={5} /></Card> : rows.length === 0 ? (
        <Card><EmptyState title={t('routing.empty')} description={t('routing.emptyHint')} action={<Button variant="contained" startIcon={<AddIcon />} onClick={startCreate}>{t('routing.create')}</Button>} /></Card>
      ) : (
        <Stack spacing={2}>
          {rows.map((group) => (
            <Card key={group.id}>
              <CardContent sx={{ pb: '16px !important' }}>
                <Stack direction={{ xs: 'column', sm: 'row' }} justifyContent="space-between" alignItems={{ xs: 'stretch', sm: 'flex-start' }} spacing={2}>
                  <Stack direction="row" spacing={1.5} alignItems="flex-start">
                    <Box sx={{ width: 38, height: 38, borderRadius: 2, bgcolor: 'action.hover', color: 'primary.main', display: 'grid', placeItems: 'center' }}><RouteIcon fontSize="small" /></Box>
                    <Box>
                      <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap><Typography variant="h2">{group.name}</Typography><Chip size="small" variant="outlined" label={group.isAuto ? t('routing.automatic') : t('routing.manual')} />{group.allowCrossScope && <Chip size="small" color="info" variant="outlined" label="Cross-scope" />}</Stack>
                      <Typography variant="body2" color="text.secondary" mt={0.75}>{group.strategy} · {group.members.length} {t('routing.members')}</Typography>
                    </Box>
                  </Stack>
                  <Button size="small" startIcon={<EditOutlinedIcon />} onClick={() => startEdit(group)}>{t('common.configure')}</Button>
                </Stack>
              </CardContent>
              <Box sx={{ overflowX: 'auto' }}>
                <Table size="small">
                  <TableHead><TableRow><TableCell>{t('common.provider')}</TableCell><TableCell>{t('routing.upstreamModel')}</TableCell><TableCell align="right">{t('routing.priority')}</TableCell><TableCell align="right">{t('routing.weight')}</TableCell><TableCell>{t('routing.memberState')}</TableCell></TableRow></TableHead>
                  <TableBody>
                    {group.members.map((member, index) => (
                      <TableRow key={`${member.provider}-${member.upstreamModel}-${index}`} hover>
                        <TableCell><Typography variant="subtitle2">{member.provider}</Typography></TableCell>
                        <TableCell><Box component="code" sx={{ fontSize: 12 }}>{member.upstreamModel}</Box></TableCell>
                        <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{member.priority}</TableCell>
                        <TableCell align="right" sx={{ fontVariantNumeric: 'tabular-nums' }}>{member.weight}</TableCell>
                        <TableCell><StatusChip status={member.enabled ? (member.state || 'enabled') : 'disabled'} /></TableCell>
                      </TableRow>
                    ))}
                  </TableBody>
                </Table>
              </Box>
            </Card>
          ))}
        </Stack>
      )}

      <Dialog open={open} onClose={busy ? undefined : () => setOpen(false)} fullWidth maxWidth="md">
        <DialogTitle>{t('routing.create')}</DialogTitle>
        <DialogContent>
          <Stack spacing={2.5} mt={1}>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <TextField fullWidth label={t('routing.groupName')} value={name} onChange={(event) => setName(event.target.value)} name="groupName" autoComplete="off" required />
              <TextField select fullWidth label={t('routing.strategy')} value={strategy} onChange={(event) => setStrategy(event.target.value)}><MenuItem value="priority-weighted">priority-weighted</MenuItem></TextField>
            </Stack>
            <FormControlLabel control={<Switch checked={crossScope} onChange={(event) => setCrossScope(event.target.checked)} />} label={t('routing.crossScope')} />
            <Divider />
            <Stack direction="row" justifyContent="space-between" alignItems="center"><Typography variant="h2">{t('routing.members')}</Typography><Button size="small" startIcon={<AddIcon />} onClick={() => setMembers((current) => [...current, { ...emptyMember, providerId: providers[0]?.id ?? '' }])}>{t('routing.addMember')}</Button></Stack>
            {members.map((member, index) => (
              <Box key={index} sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 2 }}>
                <Stack direction="row" spacing={2} alignItems="flex-start">
                  <Box sx={{ flexGrow: 1, display: 'grid', gridTemplateColumns: { xs: '1fr', sm: '1fr 1fr 100px 100px' }, gap: 1.5 }}>
                    <TextField select size="small" label={t('routing.selectProvider')} value={member.providerId} onChange={(event) => updateMember(index, { providerId: event.target.value })} required>
                      {providers.map((provider) => <MenuItem key={provider.id} value={provider.id}>{provider.alias}</MenuItem>)}
                    </TextField>
                    <TextField size="small" label={t('routing.upstreamModel')} value={member.upstreamModel} onChange={(event) => updateMember(index, { upstreamModel: event.target.value })} name={`upstreamModel-${index}`} autoComplete="off" required />
                    <TextField size="small" type="number" label={t('routing.priority')} value={member.priority} onChange={(event) => updateMember(index, { priority: Number(event.target.value) })} inputProps={{ min: 0 }} />
                    <TextField size="small" type="number" label={t('routing.weight')} value={member.weight} onChange={(event) => updateMember(index, { weight: Number(event.target.value) })} inputProps={{ min: 0 }} />
                  </Box>
                  <Stack alignItems="center">
                    <Switch size="small" checked={member.enabled} onChange={(event) => updateMember(index, { enabled: event.target.checked })} inputProps={{ 'aria-label': t('common.enabled') }} />
                    <IconButton size="small" color="error" onClick={() => setMembers((current) => current.filter((_, memberIndex) => memberIndex !== index))} disabled={members.length === 1} aria-label={t('common.remove')}><DeleteOutlineIcon fontSize="small" /></IconButton>
                  </Stack>
                </Stack>
              </Box>
            ))}
            <Alert severity="info">{t('routing.saveHint')}</Alert>
            {formError && <Alert severity="error">{formError}</Alert>}
          </Stack>
        </DialogContent>
        <DialogActions><Button onClick={() => setOpen(false)} disabled={busy}>{t('common.cancel')}</Button><Button variant="contained" onClick={() => void save()} disabled={busy || !name.trim() || !members.some((member) => member.enabled && member.providerId && member.upstreamModel.trim())}>{busy ? t('common.working') : t('common.save')}</Button></DialogActions>
      </Dialog>
    </Stack>
  );
}
