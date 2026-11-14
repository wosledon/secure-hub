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
  FormControlLabel,
  FormGroup,
  IconButton,
  InputAdornment,
  MenuItem,
  Stack,
  Switch,
  Tab,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableRow,
  Tabs,
  TextField,
  Tooltip,
  Typography
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import EditOutlinedIcon from '@mui/icons-material/EditOutlined';
import GroupsOutlinedIcon from '@mui/icons-material/GroupsOutlined';
import KeyIcon from '@mui/icons-material/Key';
import PersonOutlineIcon from '@mui/icons-material/PersonOutline';
import RefreshIcon from '@mui/icons-material/Refresh';
import SearchIcon from '@mui/icons-material/Search';
import { useSearchParams } from 'react-router-dom';
import { useTranslation } from 'react-i18next';
import { api } from '../api';
import type { OrgAppRow, OrgTeamRow, OrgUserRow } from '../api';
import { ConfirmDialog, EmptyState, ErrorState, PageHeader, StatusChip, TableSkeleton } from '../components/ui';
import { useFormatters } from '../utils/format';

const tabs = ['users', 'teams', 'apps'] as const;
const allRoles = [0, 1, 2, 3, 4, 5];

export default function OrganizationPage() {
  const { t } = useTranslation();
  const [searchParams, setSearchParams] = useSearchParams();
  const [users, setUsers] = useState<OrgUserRow[]>([]);
  const [teams, setTeams] = useState<OrgTeamRow[]>([]);
  const [apps, setApps] = useState<OrgAppRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [query, setQuery] = useState('');
  const [userDialog, setUserDialog] = useState<{ mode: 'create' } | { mode: 'edit'; user: OrgUserRow } | null>(null);
  const [teamDialog, setTeamDialog] = useState<{ mode: 'create' } | { mode: 'edit'; team: OrgTeamRow } | null>(null);
  const [appDialog, setAppDialog] = useState<{ mode: 'create' } | { mode: 'edit'; app: OrgAppRow } | null>(null);
  const [passwordTarget, setPasswordTarget] = useState<OrgUserRow | null>(null);

  const active = tabs.includes(searchParams.get('tab') as typeof tabs[number]) ? searchParams.get('tab')! : 'users';

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [userRows, teamRows, appRows] = await Promise.all([api.orgUsers(), api.orgTeams(), api.orgApps()]);
      setUsers(userRows);
      setTeams(teamRows);
      setApps(appRows);
    } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  const search = query.trim().toLowerCase();
  const filteredUsers = useMemo(() => users.filter((user) => !search || `${user.userName} ${user.team_name ?? ''}`.toLowerCase().includes(search)), [users, search]);
  const filteredTeams = useMemo(() => teams.filter((team) => !search || team.name.toLowerCase().includes(search)), [teams, search]);
  const filteredApps = useMemo(() => apps.filter((app) => !search || `${app.name} ${app.team_name ?? ''}`.toLowerCase().includes(search)), [apps, search]);

  return (
    <Stack spacing={3}>
      <PageHeader title={t('org.title')} description={t('org.description')} actions={<>
        <Tooltip title={t('common.refresh')}><IconButton onClick={() => void load()} aria-label={t('common.refresh')}><RefreshIcon /></IconButton></Tooltip>
        <Button
          variant="contained"
          startIcon={<AddIcon />}
          onClick={() => active === 'users' ? setUserDialog({ mode: 'create' }) : active === 'teams' ? setTeamDialog({ mode: 'create' }) : setAppDialog({ mode: 'create' })}
        >
          {active === 'users' ? t('org.addUser') : active === 'teams' ? t('org.addTeam') : t('org.addApp')}
        </Button>
      </>} />
      {error && <ErrorState message={error} retry={() => void load()} />}
      {message && <Alert severity="success" onClose={() => setMessage(null)}>{message}</Alert>}

      <Tabs value={active} onChange={(_, value) => setSearchParams(value === 'users' ? {} : { tab: value })} sx={{ borderBottom: 1, borderColor: 'divider' }}>
        <Tab value="users" label={`${t('org.users')} (${users.length})`} />
        <Tab value="teams" label={`${t('org.teams')} (${teams.length})`} />
        <Tab value="apps" label={`${t('org.apps')} (${apps.length})`} />
      </Tabs>

      <Card>
        <Box sx={{ p: 2, borderBottom: 1, borderColor: 'divider', display: 'flex', gap: 2, alignItems: 'center', flexWrap: 'wrap' }}>
          <Typography variant="h2" sx={{ flexGrow: 1 }}>{active === 'users' ? t('org.users') : active === 'teams' ? t('org.teams') : t('org.apps')}</Typography>
          <TextField size="small" value={query} onChange={(event) => setQuery(event.target.value)} placeholder={t('org.search')} name="orgSearch" autoComplete="off" sx={{ width: { xs: '100%', sm: 300 } }} InputProps={{ startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> }} />
        </Box>
        {loading ? <TableSkeleton rows={6} columns={5} /> : active === 'users' ? (
          filteredUsers.length === 0 ? <EmptyState title={t('org.emptyUsers')} description={t('org.emptyUsersHint')} /> : <UsersTable rows={filteredUsers} onEdit={(user) => setUserDialog({ mode: 'edit', user })} onResetPassword={setPasswordTarget} />
        ) : active === 'teams' ? (
          filteredTeams.length === 0 ? <EmptyState title={t('org.emptyTeams')} description={t('org.emptyTeamsHint')} /> : <TeamsTable rows={filteredTeams} onEdit={(team) => setTeamDialog({ mode: 'edit', team })} />
        ) : (
          filteredApps.length === 0 ? <EmptyState title={t('org.emptyApps')} description={t('org.emptyAppsHint')} /> : <AppsTable rows={filteredApps} onEdit={(app) => setAppDialog({ mode: 'edit', app })} />
        )}
      </Card>

      {userDialog && (
        <UserDialog
          state={userDialog}
          teams={teams}
          onClose={() => setUserDialog(null)}
          onSaved={(text) => { setUserDialog(null); setMessage(text); void load(); }}
        />
      )}
      {teamDialog && (
        <TeamDialog
          state={teamDialog}
          onClose={() => setTeamDialog(null)}
          onSaved={(text) => { setTeamDialog(null); setMessage(text); void load(); }}
        />
      )}
      {appDialog && (
        <AppDialog
          state={appDialog}
          teams={teams}
          onClose={() => setAppDialog(null)}
          onSaved={(text) => { setAppDialog(null); setMessage(text); void load(); }}
        />
      )}
      {passwordTarget && (
        <PasswordDialog
          user={passwordTarget}
          onClose={() => setPasswordTarget(null)}
          onSaved={() => { setPasswordTarget(null); setMessage(t('org.passwordReset')); }}
        />
      )}
      <Alert severity="info">{t('org.deactivationNote')}</Alert>
    </Stack>
  );
}

function UsersTable({ rows, onEdit, onResetPassword }: {
  rows: OrgUserRow[];
  onEdit: (user: OrgUserRow) => void;
  onResetPassword: (user: OrgUserRow) => void;
}) {
  const { t } = useTranslation();
  const format = useFormatters();
  return (
    <Box sx={{ overflowX: 'auto' }}>
      <Table>
        <TableHead><TableRow><TableCell>{t('login.userName')}</TableCell><TableCell>{t('account.roles')}</TableCell><TableCell>{t('account.team')}</TableCell><TableCell>{t('common.status')}</TableCell><TableCell>{t('account.totp')}</TableCell><TableCell>{t('common.createdAt')}</TableCell><TableCell align="right">{t('common.actions')}</TableCell></TableRow></TableHead>
        <TableBody>
          {rows.map((user) => (
            <TableRow key={user.id} hover sx={{ opacity: user.is_active ? 1 : 0.6 }}>
              <TableCell><Stack direction="row" spacing={1.25} alignItems="center"><PersonOutlineIcon color="action" fontSize="small" /><Typography variant="subtitle2">{user.userName}</Typography></Stack></TableCell>
              <TableCell><Stack direction="row" spacing={0.5} flexWrap="wrap" useFlexGap>{user.roles.map((role) => <Chip key={role} size="small" variant="outlined" label={t(`account.role${role}`)} />)}</Stack></TableCell>
              <TableCell>{user.team_name ?? '—'}</TableCell>
              <TableCell><StatusChip status={user.is_active ? 'active' : 'disabled'} label={user.is_active ? t('common.enabled') : t('common.disabled')} /></TableCell>
              <TableCell><Chip size="small" color={user.totp_enabled ? 'success' : 'default'} variant="outlined" label={user.totp_enabled ? t('org.enabled') : t('org.notEnabled')} /></TableCell>
              <TableCell sx={{ whiteSpace: 'nowrap' }}>{format.dateTime(user.createdAt)}</TableCell>
              <TableCell align="right"><Stack direction="row" spacing={0.5} justifyContent="flex-end"><Tooltip title={t('org.resetPassword')}><IconButton size="small" onClick={() => onResetPassword(user)} aria-label={t('org.resetPassword')}><KeyIcon fontSize="small" /></IconButton></Tooltip><Tooltip title={t('common.configure')}><IconButton size="small" onClick={() => onEdit(user)} aria-label={t('common.configure')}><EditOutlinedIcon fontSize="small" /></IconButton></Tooltip></Stack></TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </Box>
  );
}

function TeamsTable({ rows, onEdit }: { rows: OrgTeamRow[]; onEdit: (team: OrgTeamRow) => void }) {
  const { t } = useTranslation();
  const format = useFormatters();
  return (
    <Box sx={{ overflowX: 'auto' }}>
      <Table>
        <TableHead><TableRow><TableCell>{t('common.name')}</TableCell><TableCell align="right">{t('org.users')}</TableCell><TableCell align="right">{t('org.apps')}</TableCell><TableCell>{t('common.status')}</TableCell><TableCell>{t('common.createdAt')}</TableCell><TableCell align="right">{t('common.actions')}</TableCell></TableRow></TableHead>
        <TableBody>{rows.map((team) => (
          <TableRow key={team.id} hover sx={{ opacity: team.is_active ? 1 : 0.6 }}>
            <TableCell><Stack direction="row" spacing={1.25} alignItems="center"><GroupsOutlinedIcon color="action" fontSize="small" /><Typography variant="subtitle2">{team.name}</Typography></Stack></TableCell>
            <TableCell align="right">{team.user_count}</TableCell><TableCell align="right">{team.app_count}</TableCell>
            <TableCell><StatusChip status={team.is_active ? 'active' : 'disabled'} label={team.is_active ? t('common.enabled') : t('common.disabled')} /></TableCell>
            <TableCell>{format.dateTime(team.createdAt)}</TableCell>
            <TableCell align="right"><Tooltip title={t('common.configure')}><IconButton size="small" onClick={() => onEdit(team)} aria-label={t('common.configure')}><EditOutlinedIcon fontSize="small" /></IconButton></Tooltip></TableCell>
          </TableRow>
        ))}</TableBody>
      </Table>
    </Box>
  );
}

function AppsTable({ rows, onEdit }: { rows: OrgAppRow[]; onEdit: (app: OrgAppRow) => void }) {
  const { t } = useTranslation();
  const format = useFormatters();
  return (
    <Box sx={{ overflowX: 'auto' }}>
      <Table>
        <TableHead><TableRow><TableCell>{t('common.name')}</TableCell><TableCell>{t('account.team')}</TableCell><TableCell align="right">{t('nav.keys')}</TableCell><TableCell>{t('common.status')}</TableCell><TableCell>{t('common.createdAt')}</TableCell><TableCell align="right">{t('common.actions')}</TableCell></TableRow></TableHead>
        <TableBody>{rows.map((app) => (
          <TableRow key={app.id} hover sx={{ opacity: app.is_active ? 1 : 0.6 }}>
            <TableCell><Typography variant="subtitle2">{app.name}</Typography></TableCell>
            <TableCell>{app.team_name ?? '—'}</TableCell><TableCell align="right">{app.key_count}</TableCell>
            <TableCell><StatusChip status={app.is_active ? 'active' : 'disabled'} label={app.is_active ? t('common.enabled') : t('common.disabled')} /></TableCell>
            <TableCell>{format.dateTime(app.createdAt)}</TableCell>
            <TableCell align="right"><Tooltip title={t('common.configure')}><IconButton size="small" onClick={() => onEdit(app)} aria-label={t('common.configure')}><EditOutlinedIcon fontSize="small" /></IconButton></Tooltip></TableCell>
          </TableRow>
        ))}</TableBody>
      </Table>
    </Box>
  );
}

function UserDialog({ state, teams, onClose, onSaved }: {
  state: { mode: 'create' } | { mode: 'edit'; user: OrgUserRow };
  teams: OrgTeamRow[];
  onClose: () => void;
  onSaved: (message: string) => void;
}) {
  const { t } = useTranslation();
  const editing = state.mode === 'edit' ? state.user : null;
  const [userName, setUserName] = useState(editing?.userName ?? '');
  const [password, setPassword] = useState('');
  const [teamId, setTeamId] = useState(editing?.teamId ?? '');
  const [roles, setRoles] = useState<number[]>(editing?.roles ?? [5]);
  const [isActive, setIsActive] = useState(editing?.is_active ?? true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setBusy(true);
    setError(null);
    try {
      if (editing) {
        await api.updateUser(editing.id, { teamId: teamId || null, roles, isActive });
        onSaved(t('org.userUpdated'));
      } else {
        await api.createUser({ userName: userName.trim(), password, teamId: teamId || null, roles });
        onSaved(t('org.userCreated'));
      }
    } catch (err) { setError((err as Error).message); } finally { setBusy(false); }
  };

  return (
    <Dialog open onClose={busy ? undefined : onClose} fullWidth maxWidth="sm">
      <DialogTitle>{editing ? t('org.editUser') : t('org.addUser')}</DialogTitle>
      <DialogContent>
        <Stack spacing={2.25} mt={1}>
          <TextField label={t('login.userName')} value={userName} onChange={(event) => setUserName(event.target.value)} name="orgUserName" autoComplete="off" spellCheck={false} disabled={!!editing} required helperText={editing ? t('org.userNameImmutable') : undefined} />
          {!editing && <TextField label={t('login.password')} type="password" value={password} onChange={(event) => setPassword(event.target.value)} name="orgUserPassword" autoComplete="new-password" helperText={t('org.passwordHint')} required />}
          <TextField select label={t('account.team')} value={teamId} onChange={(event) => setTeamId(event.target.value)}>
            <MenuItem value="">{t('org.noTeam')}</MenuItem>
            {teams.filter((team) => team.is_active).map((team) => <MenuItem key={team.id} value={team.id}>{team.name}</MenuItem>)}
          </TextField>
          <Box>
            <Typography variant="body2" fontWeight={600} mb={1}>{t('account.roles')}</Typography>
            <FormGroup>
              {allRoles.map((role) => (
                <FormControlLabel
                  key={role}
                  control={<Checkbox size="small" checked={roles.includes(role)} onChange={(event) => setRoles((current) => event.target.checked ? [...current, role].sort() : current.filter((item) => item !== role))} />}
                  label={t(`account.role${role}`)}
                />
              ))}
            </FormGroup>
          </Box>
          {editing && <FormControlLabel control={<Switch checked={isActive} onChange={(event) => setIsActive(event.target.checked)} />} label={t('common.enabled')} />}
          {error && <Alert severity="error">{error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
        <Button variant="contained" onClick={() => void submit()} disabled={busy || !userName.trim() || roles.length === 0 || (!editing && password.length < 12)}>{busy ? t('common.working') : t('common.save')}</Button>
      </DialogActions>
    </Dialog>
  );
}

function TeamDialog({ state, onClose, onSaved }: {
  state: { mode: 'create' } | { mode: 'edit'; team: OrgTeamRow };
  onClose: () => void;
  onSaved: (message: string) => void;
}) {
  const { t } = useTranslation();
  const editing = state.mode === 'edit' ? state.team : null;
  const [name, setName] = useState(editing?.name ?? '');
  const [isActive, setIsActive] = useState(editing?.is_active ?? true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setBusy(true); setError(null);
    try {
      if (editing) {
        await api.updateTeam(editing.id, { name: name.trim(), isActive });
        onSaved(t('org.teamUpdated'));
      } else {
        await api.createTeam({ name: name.trim() });
        onSaved(t('org.teamCreated'));
      }
    } catch (err) { setError((err as Error).message); } finally { setBusy(false); }
  };

  return (
    <Dialog open onClose={busy ? undefined : onClose} fullWidth maxWidth="xs">
      <DialogTitle>{editing ? t('org.editTeam') : t('org.addTeam')}</DialogTitle>
      <DialogContent><Stack spacing={2.25} mt={1}>
        <TextField label={t('common.name')} value={name} onChange={(event) => setName(event.target.value)} name="teamName" autoComplete="off" required />
        {editing && <FormControlLabel control={<Switch checked={isActive} onChange={(event) => setIsActive(event.target.checked)} />} label={t('common.enabled')} />}
        {error && <Alert severity="error">{error}</Alert>}
      </Stack></DialogContent>
      <DialogActions><Button onClick={onClose} disabled={busy}>{t('common.cancel')}</Button><Button variant="contained" onClick={() => void submit()} disabled={busy || !name.trim()}>{busy ? t('common.working') : t('common.save')}</Button></DialogActions>
    </Dialog>
  );
}

function AppDialog({ state, teams, onClose, onSaved }: {
  state: { mode: 'create' } | { mode: 'edit'; app: OrgAppRow };
  teams: OrgTeamRow[];
  onClose: () => void;
  onSaved: (message: string) => void;
}) {
  const { t } = useTranslation();
  const editing = state.mode === 'edit' ? state.app : null;
  const [name, setName] = useState(editing?.name ?? '');
  const [teamId, setTeamId] = useState(editing?.team_id ?? '');
  const [isActive, setIsActive] = useState(editing?.is_active ?? true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setBusy(true); setError(null);
    try {
      if (editing) {
        await api.updateApp(editing.id, { name: name.trim(), teamId: teamId || null, isActive });
        onSaved(t('org.appUpdated'));
      } else {
        await api.createApp({ name: name.trim(), teamId: teamId || null });
        onSaved(t('org.appCreated'));
      }
    } catch (err) { setError((err as Error).message); } finally { setBusy(false); }
  };

  return (
    <Dialog open onClose={busy ? undefined : onClose} fullWidth maxWidth="xs">
      <DialogTitle>{editing ? t('org.editApp') : t('org.addApp')}</DialogTitle>
      <DialogContent><Stack spacing={2.25} mt={1}>
        <TextField label={t('common.name')} value={name} onChange={(event) => setName(event.target.value)} name="appName" autoComplete="off" required />
        <TextField select label={t('account.team')} value={teamId} onChange={(event) => setTeamId(event.target.value)}><MenuItem value="">{t('org.noTeam')}</MenuItem>{teams.filter((team) => team.is_active).map((team) => <MenuItem key={team.id} value={team.id}>{team.name}</MenuItem>)}</TextField>
        {editing && <FormControlLabel control={<Switch checked={isActive} onChange={(event) => setIsActive(event.target.checked)} />} label={t('common.enabled')} />}
        {error && <Alert severity="error">{error}</Alert>}
      </Stack></DialogContent>
      <DialogActions><Button onClick={onClose} disabled={busy}>{t('common.cancel')}</Button><Button variant="contained" onClick={() => void submit()} disabled={busy || !name.trim()}>{busy ? t('common.working') : t('common.save')}</Button></DialogActions>
    </Dialog>
  );
}

function PasswordDialog({ user, onClose, onSaved }: { user: OrgUserRow; onClose: () => void; onSaved: () => void }) {
  const { t } = useTranslation();
  const [password, setPassword] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const submit = async () => {
    setBusy(true); setError(null);
    try { await api.resetUserPassword(user.id, password); onSaved(); }
    catch (err) { setError((err as Error).message); } finally { setBusy(false); }
  };

  return (
    <ConfirmDialog
      open
      title={t('org.resetPassword')}
      description={t('org.resetPasswordHint', { name: user.userName })}
      confirmLabel={t('org.resetPassword')}
      busy={busy}
      onClose={onClose}
      onConfirm={() => void submit()}
    >
      <Stack spacing={2} mt={2}>
        <TextField label={t('login.password')} type="password" value={password} onChange={(event) => setPassword(event.target.value)} autoComplete="new-password" helperText={t('org.passwordHint')} />
        {error && <Alert severity="error">{error}</Alert>}
      </Stack>
    </ConfirmDialog>
  );
}
