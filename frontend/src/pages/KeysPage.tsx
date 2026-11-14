import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Checkbox,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  FormGroup,
  IconButton,
  InputAdornment,
  MenuItem,
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
import BlockIcon from '@mui/icons-material/Block';
import KeyIcon from '@mui/icons-material/Key';
import RefreshIcon from '@mui/icons-material/Refresh';
import SearchIcon from '@mui/icons-material/Search';
import { useTranslation } from 'react-i18next';
import { api, type KeyRow } from '../api';
import { CodeBlock, ConfirmDialog, CopyButton, EmptyState, ErrorState, PageHeader, SecretDialog, StatusChip, TableSkeleton } from '../components/ui';
import { useFormatters } from '../utils/format';

export default function KeysPage() {
  const { t } = useTranslation();
  const format = useFormatters();
  const [rows, setRows] = useState<KeyRow[]>([]);
  const [query, setQuery] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [displayName, setDisplayName] = useState('');
  const [ownerType, setOwnerType] = useState<0 | 1>(0);
  const [ownerId, setOwnerId] = useState('00000000-0000-0000-0000-000000000000');
  const [scopes, setScopes] = useState(['chat:write', 'models:read']);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [secret, setSecret] = useState<string | null>(null);
  const [revokeTarget, setRevokeTarget] = useState<KeyRow | null>(null);
  const [revoked, setRevoked] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try { setRows(await api.keys()); } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  const filtered = useMemo(() => {
    const search = query.trim().toLowerCase();
    return rows.filter((row) => !search || `${row.display_name} ${row.key_id} ${row.prefix}`.toLowerCase().includes(search));
  }, [rows, query]);

  const create = async () => {
    setBusy(true);
    setFormError(null);
    try {
      const result = await api.createKey({ displayName: displayName.trim(), ownerType, ownerId, scopes });
      setOpen(false);
      setSecret(result.key);
      setDisplayName('');
      await load();
    } catch (err) { setFormError((err as Error).message); } finally { setBusy(false); }
  };

  const revoke = async () => {
    if (!revokeTarget) return;
    setBusy(true);
    try {
      await api.revokeKey(revokeTarget.id);
      setRevokeTarget(null);
      setRevoked(true);
      await load();
    } catch (err) { setError((err as Error).message); } finally { setBusy(false); }
  };

  const curl = `curl ${window.location.origin}/v1/chat/completions \\\n  -H "Authorization: Bearer $SECUREHUB_KEY" \\\n  -H "Content-Type: application/json" \\\n  -d '{\n    "model": "org/<provider>/<model>",\n    "messages": [{"role":"user","content":"Hello"}]\n  }'`;

  return (
    <Stack spacing={3}>
      <PageHeader title={t('keys.title')} description={t('keys.description')} actions={<>
        <Tooltip title={t('common.refresh')}><IconButton onClick={() => void load()} aria-label={t('common.refresh')}><RefreshIcon /></IconButton></Tooltip>
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => { setOpen(true); setRevoked(false); }}>{t('keys.create')}</Button>
      </>} />
      {error && <ErrorState message={error} retry={() => void load()} />}
      {revoked && <Alert severity="success" onClose={() => setRevoked(false)}>{t('keys.revokedSuccess')}</Alert>}
      <Card>
        <Box sx={{ p: 2, borderBottom: 1, borderColor: 'divider', display: 'flex', gap: 2, alignItems: 'center', flexWrap: 'wrap' }}>
          <Box sx={{ flexGrow: 1 }}><Typography variant="h2">{t('keys.title')}</Typography><Typography variant="caption" color="text.secondary">{rows.filter((row) => !row.is_revoked).length} {t('common.active')} · {rows.filter((row) => row.is_revoked).length} {t('common.revoked')}</Typography></Box>
          <TextField size="small" value={query} onChange={(event) => setQuery(event.target.value)} placeholder={t('keys.search')} name="keySearch" autoComplete="off" sx={{ width: { xs: '100%', sm: 320 } }} InputProps={{ startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> }} />
        </Box>
        {loading ? <TableSkeleton rows={6} columns={6} /> : filtered.length === 0 ? (
          <EmptyState title={rows.length ? t('common.empty') : t('keys.empty')} description={rows.length ? undefined : t('keys.emptyHint')} action={!rows.length && <Button variant="contained" startIcon={<AddIcon />} onClick={() => setOpen(true)}>{t('keys.create')}</Button>} />
        ) : (
          <Box sx={{ overflowX: 'auto' }}><Table>
            <TableHead><TableRow><TableCell>{t('common.name')}</TableCell><TableCell>{t('keys.keyId')}</TableCell><TableCell>{t('keys.prefix')}</TableCell><TableCell>{t('keys.owner')}</TableCell><TableCell>{t('common.createdAt')}</TableCell><TableCell>{t('common.status')}</TableCell><TableCell align="right">{t('common.actions')}</TableCell></TableRow></TableHead>
            <TableBody>{filtered.map((row) => (
              <TableRow key={row.id} hover sx={{ opacity: row.is_revoked ? 0.65 : 1 }}>
                <TableCell><Stack direction="row" spacing={1.25} alignItems="center"><Box sx={{ width: 32, height: 32, display: 'grid', placeItems: 'center', borderRadius: 1.5, bgcolor: 'action.hover', color: 'primary.main' }}><KeyIcon fontSize="small" /></Box><Typography variant="subtitle2">{row.display_name}</Typography></Stack></TableCell>
                <TableCell><Box component="code">{row.key_id}</Box></TableCell>
                <TableCell><Stack direction="row" alignItems="center"><Box component="code" sx={{ fontSize: 11 }}>{row.prefix}••••••••</Box><CopyButton text={row.prefix} iconOnly /></Stack></TableCell>
                <TableCell><Chip size="small" variant="outlined" label={row.owner_type} /></TableCell>
                <TableCell sx={{ whiteSpace: 'nowrap' }}>{format.dateTime(row.created_at)}</TableCell>
                <TableCell><StatusChip status={row.is_revoked ? 'revoked' : 'active'} label={row.is_revoked ? t('common.revoked') : t('common.active')} /></TableCell>
                <TableCell align="right"><Button size="small" color="error" startIcon={<BlockIcon />} disabled={row.is_revoked} onClick={() => setRevokeTarget(row)}>{t('keys.revoke')}</Button></TableCell>
              </TableRow>
            ))}</TableBody>
          </Table></Box>
        )}
      </Card>
      <Card><CardContent><Typography variant="h2" mb={0.5}>{t('keys.integration')}</Typography><Typography variant="body2" color="text.secondary" mb={2}>{t('keys.integrationHint')}</Typography><CodeBlock code={curl} /></CardContent></Card>

      <Dialog open={open} onClose={busy ? undefined : () => setOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>{t('keys.createTitle')}</DialogTitle>
        <DialogContent><Stack spacing={2.25} mt={1}>
          <Typography variant="body2" color="text.secondary">{t('keys.createHint')}</Typography>
          <TextField label={t('common.name')} value={displayName} onChange={(event) => setDisplayName(event.target.value)} helperText={t('keys.displayNameHint')} name="displayName" autoComplete="off" required />
          <TextField select label={t('keys.ownerType')} value={ownerType} onChange={(event) => { const value = Number(event.target.value) as 0 | 1; setOwnerType(value); setOwnerId(value === 0 ? '00000000-0000-0000-0000-000000000000' : ''); }}><MenuItem value={0}>{t('keys.organization')}</MenuItem><MenuItem value={1}>{t('keys.user')}</MenuItem></TextField>
          {ownerType === 1 && <TextField label={t('keys.ownerId')} value={ownerId} onChange={(event) => setOwnerId(event.target.value)} name="ownerId" autoComplete="off" required />}
          <Box><Typography variant="body2" fontWeight={600} mb={1}>{t('keys.scopes')}</Typography><FormGroup row>{['chat:write', 'models:read'].map((scope) => <FormControlLabel key={scope} control={<Checkbox size="small" checked={scopes.includes(scope)} onChange={(event) => setScopes((current) => event.target.checked ? [...current, scope] : current.filter((item) => item !== scope))} />} label={<Box component="code" fontSize={12}>{scope}</Box>} />)}</FormGroup></Box>
          {formError && <Alert severity="error">{formError}</Alert>}
        </Stack></DialogContent>
        <DialogActions><Button onClick={() => setOpen(false)} disabled={busy}>{t('common.cancel')}</Button><Button variant="contained" onClick={() => void create()} disabled={busy || !displayName.trim() || !ownerId || scopes.length === 0}>{busy ? t('common.working') : t('keys.create')}</Button></DialogActions>
      </Dialog>
      <SecretDialog open={!!secret} secret={secret ?? ''} onClose={() => setSecret(null)} />
      <ConfirmDialog open={!!revokeTarget} title={t('keys.revokeTitle')} description={t('keys.revokeHint', { name: revokeTarget?.display_name ?? '' })} confirmLabel={t('keys.revoke')} busy={busy} danger onClose={() => setRevokeTarget(null)} onConfirm={() => void revoke()} />
    </Stack>
  );
}
