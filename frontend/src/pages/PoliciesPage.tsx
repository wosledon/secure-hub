import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  Box,
  Button,
  Card,
  CardContent,
  Chip,
  Collapse,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  IconButton,
  Menu,
  MenuItem,
  Stack,
  Switch,
  TextField,
  Tooltip,
  Typography
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import AlternateEmailIcon from '@mui/icons-material/AlternateEmail';
import BlockIcon from '@mui/icons-material/Block';
import DeleteOutlineIcon from '@mui/icons-material/DeleteOutline';
import DownloadIcon from '@mui/icons-material/Download';
import EditIcon from '@mui/icons-material/EditOutlined';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import GppGoodIcon from '@mui/icons-material/GppGood';
import KeyIcon from '@mui/icons-material/Key';
import MoreVertIcon from '@mui/icons-material/MoreVert';
import NotificationsActiveIcon from '@mui/icons-material/NotificationsActive';
import PhoneIphoneIcon from '@mui/icons-material/PhoneIphone';
import PolicyIcon from '@mui/icons-material/Policy';
import RefreshIcon from '@mui/icons-material/Refresh';
import RestoreIcon from '@mui/icons-material/Restore';
import SearchIcon from '@mui/icons-material/Search';
import ShieldIcon from '@mui/icons-material/Shield';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOff';
import { useTranslation } from 'react-i18next';
import {
  api,
  type OrgAppRow,
  type OrgTeamRow,
  type PolicyDetectorInfo,
  type PolicyRow,
  type PolicySnapshot,
  type PolicyVersionRow,
  type PublishPolicyBody
} from '../api';
import { PolicyEditorDialog } from '../components/PolicyEditorDialog';
import { CodeBlock, ConfirmDialog, EmptyState, ErrorState, PageHeader, StatCard, StatusChip, TableSkeleton } from '../components/ui';
import { useFormatters } from '../utils/format';
import {
  actionLabel,
  activeVersion,
  blankDraft,
  detectorLabel,
  draftFromVersion,
  fallbackDetectors,
  policyTemplates,
  scopeSummary,
  toPublishBody,
  type PolicyDraft,
  versionAction
} from '../utils/policy';

type EditorState = { original?: PolicyRow; draft: PolicyDraft };

const actionTone = (action: string): 'error' | 'warning' | 'info' =>
  action === 'block' ? 'error' : action === 'redact' ? 'warning' : 'info';

function sortRows(rows: PolicyRow[]): PolicyRow[] {
  return [...rows].sort((a, b) => {
    const priorityA = activeVersion(a)?.priority ?? 0;
    const priorityB = activeVersion(b)?.priority ?? 0;
    return priorityB - priorityA || a.policy_key.localeCompare(b.policy_key);
  });
}

function PolicyCard({
  row,
  busy,
  onEdit,
  onToggle,
  onDelete,
  onActivate
}: {
  row: PolicyRow;
  busy: boolean;
  onEdit: (row: PolicyRow) => void;
  onToggle: (row: PolicyRow, enabled: boolean) => void;
  onDelete: (row: PolicyRow) => void;
  onActivate: (row: PolicyRow, version: number) => void;
}) {
  const { t } = useTranslation();
  const format = useFormatters();
  const [expanded, setExpanded] = useState(false);
  const version = activeVersion(row);

  if (!version) return null;

  const action = version.then.action;
  const requirements = version.when.all.length > 0 ? version.when.all : version.when.any;
  const actionIcon =
    action === 'block' ? <BlockIcon /> : action === 'redact' ? <VisibilityOffIcon /> : <NotificationsActiveIcon />;

  return (
    <Card>
      <CardContent>
        <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} alignItems={{ xs: 'stretch', md: 'flex-start' }}>
          <Stack direction="row" spacing={1.5} alignItems="flex-start" minWidth={0}>
            <Box
              sx={{
                width: 42,
                height: 42,
                borderRadius: 2,
                display: 'grid',
                placeItems: 'center',
                flexShrink: 0,
                color: `${actionTone(action)}.main`,
                bgcolor: 'action.hover'
              }}
            >
              {actionIcon}
            </Box>
            <Box minWidth={0}>
              <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                <Typography variant="h2" sx={{ wordBreak: 'break-word' }}>{row.policy_key}</Typography>
                <Chip size="small" color={row.enabled ? 'primary' : 'default'} variant="outlined" label={`v${version.version}`} />
                <StatusChip status={row.enabled ? 'enabled' : 'disabled'} label={row.enabled ? t('common.enabled') : t('common.disabled')} />
                <StatusChip status={action} label={actionLabel(action, t)} />
              </Stack>
              <Typography variant="body2" color="text.secondary" mt={0.75}>
                {requirements.length > 0
                  ? t(version.when.all.length > 0 ? 'policies.summaryAll' : 'policies.summaryAny', {
                      detectors: requirements.map((requirement) => detectorLabel(requirement.detector, t)).join(t('policies.listSeparator')),
                      action: actionLabel(action, t)
                    })
                  : t('policies.summaryScopeOnly', { scope: scopeSummary(version.scope, t), action: actionLabel(action, t) })}
              </Typography>
            </Box>
          </Stack>
          <Stack direction="row" spacing={1} alignItems="center" justifyContent="flex-start" flexWrap="wrap" useFlexGap>
            <Tooltip title={row.enabled ? t('policies.disableHint') : t('policies.enableHint')}>
              <Switch
                checked={row.enabled}
                disabled={busy}
                onChange={(event) => onToggle(row, event.target.checked)}
                inputProps={{ 'aria-label': t('policies.toggleLabel', { key: row.policy_key }) }}
              />
            </Tooltip>
            <Button size="small" variant="outlined" startIcon={<EditIcon />} onClick={() => onEdit(row)} disabled={busy}>
              {t('policies.edit')}
            </Button>
            <Box sx={{ flexGrow: 1 }} />
            <Tooltip title={t('policies.delete')}>
              <IconButton size="small" color="error" onClick={() => onDelete(row)} disabled={busy} aria-label={t('policies.delete')}>
                <DeleteOutlineIcon fontSize="small" />
              </IconButton>
            </Tooltip>
          </Stack>
        </Stack>

        <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap mt={2}>
          <Chip size="small" variant="outlined" label={t('policies.priorityChip', { value: version.priority })} />
          <Chip size="small" variant="outlined" label={t('policies.rolloutChip', { value: version.rollout_percent })} />
          <Chip size="small" variant="outlined" label={scopeSummary(version.scope, t)} sx={{ maxWidth: '100%' }} />
          {version.change_reason && (
            <Chip size="small" variant="outlined" label={t('policies.changeChip', { reason: version.change_reason })} sx={{ maxWidth: '100%' }} />
          )}
          <Chip size="small" variant="outlined" label={format.fullDateTime(version.created_at)} />
        </Stack>

        <Button
          size="small"
          onClick={() => setExpanded((value) => !value)}
          endIcon={<ExpandMoreIcon sx={{ transform: expanded ? 'rotate(180deg)' : 'none', transition: 'transform 150ms' }} />}
          sx={{ mt: 1.5 }}
          aria-expanded={expanded}
        >
          {expanded ? t('policies.hideDetails') : t('policies.details')}
        </Button>

        <Collapse in={expanded} timeout="auto" unmountOnExit>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2, mt: 2 }}>
            <DetailBlock label={t('policies.condition')}>
              {requirements.length === 0 ? (
                <Typography variant="body2" color="text.secondary">{t('policies.scopeOnly')}</Typography>
              ) : (
                <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap>
                  {requirements.map((requirement) => (
                    <Chip
                      key={requirement.detector}
                      size="small"
                      label={`${detectorLabel(requirement.detector, t)} × ${Math.max(1, requirement.min_matches)}`}
                    />
                  ))}
                </Stack>
              )}
              <Typography variant="caption" color="text.secondary" display="block" mt={1}>
                {t('policies.reasonCode')}: <Box component="code">{version.then.reason || '—'}</Box>
                {version.then.rule_id ? ` · ${t('policies.ruleId')}: ` : ''}
                {version.then.rule_id && <Box component="code">{version.then.rule_id}</Box>}
              </Typography>
            </DetailBlock>
            <DetailBlock label={t('policies.scope')}>
              <Typography variant="body2">{scopeSummary(version.scope, t)}</Typography>
              <Typography variant="caption" color="text.secondary" display="block" mt={1}>
                {t('policies.rolloutChip', { value: version.rollout_percent })} · {t('policies.priorityChip', { value: version.priority })}
              </Typography>
            </DetailBlock>
          </Box>

          <Divider sx={{ my: 2 }} />
          <Typography variant="subtitle2" mb={0.5}>{t('policies.versions')}</Typography>
          <Typography variant="caption" color="text.secondary">{t('policies.versionsHint')}</Typography>
          <Stack spacing={1} mt={1.5}>
            {row.versions.map((item) => (
              <Stack
                key={item.version}
                direction={{ xs: 'column', sm: 'row' }}
                spacing={1.5}
                alignItems={{ xs: 'stretch', sm: 'center' }}
                sx={{ p: 1.5, border: 1, borderColor: item.version === row.active_version ? 'primary.main' : 'divider', borderRadius: 2 }}
              >
                <Chip
                  size="small"
                  color={item.version === row.active_version ? 'primary' : 'default'}
                  variant={item.version === row.active_version ? 'filled' : 'outlined'}
                  label={`v${item.version}`}
                />
                <Box flexGrow={1} minWidth={0}>
                  <Typography variant="body2">{item.change_reason || t('policies.noChangeReason')}</Typography>
                  <Typography variant="caption" color="text.secondary">
                    {format.fullDateTime(item.created_at)} · {t('policies.priority')} {item.priority}
                  </Typography>
                </Box>
                <StatusChip status={versionAction(item)} label={actionLabel(versionAction(item), t)} />
                <Button
                  size="small"
                  startIcon={<RestoreIcon />}
                  disabled={item.version === row.active_version || busy}
                  onClick={() => onActivate(row, item.version)}
                >
                  {item.version === row.active_version ? t('policies.currentVersion') : t('policies.activate')}
                </Button>
              </Stack>
            ))}
          </Stack>

          <Accordion disableGutters elevation={0} sx={{ mt: 2, '&::before': { display: 'none' } }}>
            <AccordionSummary expandIcon={<ExpandMoreIcon />} sx={{ px: 0 }}>
              <Typography variant="body2" fontWeight={600}>{t('policies.rawConfig')}</Typography>
            </AccordionSummary>
            <AccordionDetails sx={{ px: 0 }}>
              <CodeBlock
                code={JSON.stringify(
                  {
                    scope: version.scope,
                    when: version.when,
                    then: version.then,
                    rollout_percent: version.rollout_percent
                  },
                  null,
                  2
                )}
                label="JSON"
              />
            </AccordionDetails>
          </Accordion>
        </Collapse>
      </CardContent>
    </Card>
  );
}

function DetailBlock({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <Box sx={{ p: 2, bgcolor: 'action.hover', borderRadius: 2, minWidth: 0 }}>
      <Typography variant="overline" color="text.secondary">{label}</Typography>
      <Box mt={0.75}>{children}</Box>
    </Box>
  );
}

const templateIcons = {
  shield: <ShieldIcon />,
  key: <KeyIcon />,
  mobile: <PhoneIphoneIcon />,
  mail: <AlternateEmailIcon />
};

export default function PoliciesPage() {
  const { t } = useTranslation();
  const [rows, setRows] = useState<PolicyRow[]>([]);
  const [detectors, setDetectors] = useState<PolicyDetectorInfo[]>(fallbackDetectors);
  const [teams, setTeams] = useState<OrgTeamRow[]>([]);
  const [apps, setApps] = useState<OrgAppRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [busyKey, setBusyKey] = useState<string | null>(null);
  const [search, setSearch] = useState('');
  const [actionFilter, setActionFilter] = useState('all');
  const [statusFilter, setStatusFilter] = useState('all');
  const [editor, setEditor] = useState<EditorState | null>(null);
  const [editorBusy, setEditorBusy] = useState(false);
  const [editorError, setEditorError] = useState<string | null>(null);
  const [deleteTarget, setDeleteTarget] = useState<PolicyRow | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [snapshot, setSnapshot] = useState<PolicySnapshot | null>(null);
  const [snapshotOpen, setSnapshotOpen] = useState(false);
  const [importOpen, setImportOpen] = useState(false);
  const [importFormat, setImportFormat] = useState<'json' | 'yaml'>('json');
  const [importText, setImportText] = useState('');
  const [importing, setImporting] = useState(false);
  const [importResult, setImportResult] = useState<string | null>(null);
  const [menuAnchor, setMenuAnchor] = useState<HTMLElement | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [policyRows, detectorRows, teamRows, appRows] = await Promise.all([
        api.policies(),
        api.policyDetectors().catch(() => fallbackDetectors),
        api.orgTeams().catch(() => [] as OrgTeamRow[]),
        api.orgApps().catch(() => [] as OrgAppRow[])
      ]);
      setRows(policyRows);
      setDetectors(detectorRows.length > 0 ? detectorRows : fallbackDetectors);
      setTeams(teamRows);
      setApps(appRows);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const enabledRows = rows.filter((row) => row.enabled);
  const actionCounts = useMemo(() => {
    const counts = { block: 0, redact: 0, alert: 0 };
    for (const row of enabledRows) {
      const action = activeVersion(row)?.then.action;
      if (action === 'block' || action === 'redact' || action === 'alert') counts[action]++;
    }
    return counts;
  }, [enabledRows]);

  const usedDetectors = useMemo(() => {
    const ids = new Set<string>();
    for (const row of rows) {
      for (const version of row.versions) {
        for (const requirement of [...version.when.any, ...version.when.all]) ids.add(requirement.detector);
      }
    }
    return ids;
  }, [rows]);

  const highestPriority = useMemo(
    () => Math.max(0, ...enabledRows.map((row) => activeVersion(row)?.priority ?? 0)),
    [enabledRows]
  );

  const filtered = useMemo(() => {
    const query = search.trim().toLowerCase();
    return sortRows(rows.filter((row) => {
      const version = activeVersion(row);
      const action = version?.then.action ?? '';
      if (actionFilter !== 'all' && action !== actionFilter) return false;
      if (statusFilter === 'enabled' && !row.enabled) return false;
      if (statusFilter === 'disabled' && row.enabled) return false;
      if (!query) return true;

      const detectorNames = version
        ? [...version.when.any, ...version.when.all].map((requirement) => detectorLabel(requirement.detector, t))
        : [];
      return [row.policy_key, version?.change_reason ?? '', version?.then.reason ?? '', ...detectorNames]
        .join(' ')
        .toLowerCase()
        .includes(query);
    }));
  }, [rows, search, actionFilter, statusFilter, t]);

  const startCreate = () => {
    setEditorError(null);
    setEditor({ draft: blankDraft() });
  };

  const startFromTemplate = (draft: PolicyDraft) => {
    setEditorError(null);
    setEditor({ draft: { ...draft, requirements: draft.requirements.map((requirement) => ({ ...requirement })) } });
  };

  const startEdit = (row: PolicyRow) => {
    const version = activeVersion(row);
    if (!version) return;
    setEditorError(null);
    setEditor({ original: row, draft: draftFromVersion(row, version) });
  };

  const nextVersion = (row?: PolicyRow) =>
    row ? Math.max(0, ...row.versions.map((version) => version.version)) + 1 : undefined;

  const submit = async (draft: PolicyDraft) => {
    setEditorBusy(true);
    setEditorError(null);
    try {
      const result = await api.publishPolicy({
        ...toPublishBody(draft),
        changeReason: draft.changeReason.trim() || t(editor?.original ? 'policies.defaultUpdateReason' : 'policies.defaultCreateReason')
      });
      setEditor(null);
      if (result.snapshot_reloaded === false) {
        setError(t('policies.snapshotReloadFailed', { error: result.error ?? '' }));
      } else {
        setMessage(t('policies.published', { version: result.version }));
      }
      await load();
    } catch (err) {
      setEditorError((err as Error).message);
    } finally {
      setEditorBusy(false);
    }
  };

  const toggle = async (row: PolicyRow, enabled: boolean) => {
    setBusyKey(row.policy_key);
    setError(null);
    try {
      const result = await api.setPolicyEnabled(row.policy_key, enabled);
      setRows((current) => current.map((item) => (item.policy_key === row.policy_key ? { ...item, enabled } : item)));
      if (result.snapshot_reloaded === false) {
        setError(t('policies.snapshotReloadFailed', { error: '' }));
        await load();
      } else {
        setMessage(t(enabled ? 'policies.enabledSuccess' : 'policies.disabledSuccess'));
      }
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusyKey(null);
    }
  };

  const remove = async () => {
    if (!deleteTarget) return;
    setDeleting(true);
    try {
      const result = await api.deletePolicy(deleteTarget.policy_key);
      setDeleteTarget(null);
      if (result.snapshot_reloaded === false) {
        setError(t('policies.snapshotReloadFailed', { error: '' }));
      } else {
        setMessage(t('policies.deleted'));
      }
      await load();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setDeleting(false);
    }
  };

  const activate = async (row: PolicyRow, version: number) => {
    setBusyKey(row.policy_key);
    setError(null);
    try {
      const result = await api.activatePolicy(row.policy_key, version);
      if (result.snapshot_reloaded === false) {
        setError(t('policies.snapshotReloadFailed', { error: '' }));
      } else {
        setMessage(t('policies.activated', { version }));
      }
      await load();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusyKey(null);
    }
  };

  const showSnapshot = async () => {
    try {
      setSnapshot(await api.policySnapshot());
      setSnapshotOpen(true);
    } catch (err) {
      setError((err as Error).message);
    }
  };

  const exportPolicies = () => {
    const bodies = rows.flatMap((row) => {
      const version = activeVersion(row);
      return version ? [toBodyFromVersion(row.policy_key, version)] : [];
    });
    downloadJson(bodies, `securehub-policies-${new Date().toISOString().slice(0, 10)}.json`);
  };

  const downloadTemplate = () => {
    downloadJson(
      policyTemplates.map((template) => toPublishBody({ ...template.draft, changeReason: 'template' })),
      'securehub-policy-template.json'
    );
  };

  const importPolicies = async () => {
    setImporting(true);
    setImportResult(null);
    try {
      const parsed = importFormat === 'yaml'
        ? (await api.previewPolicyYaml(importText)).policies
        : (JSON.parse(importText) as PublishPolicyBody[]);
      if (!Array.isArray(parsed) || parsed.length === 0) throw new Error(t('policies.importEmpty'));

      let ok = 0;
      const failures: string[] = [];
      for (const body of parsed) {
        try {
          await api.publishPolicy(body);
          ok++;
        } catch (err) {
          failures.push(`${body.policyKey}: ${(err as Error).message}`);
        }
      }

      setImportResult(
        t('policies.importResult', { ok, failed: failures.length }) +
        (failures.length > 0 ? `\n${failures.join('\n')}` : '')
      );
      await load();
    } catch (err) {
      setImportResult((err as Error).message);
    } finally {
      setImporting(false);
    }
  };

  const resetFilters = () => {
    setSearch('');
    setActionFilter('all');
    setStatusFilter('all');
  };

  return (
    <Stack spacing={3}>
      <PageHeader
        title={t('policies.title')}
        description={t('policies.description')}
        actions={
          <>
            <Tooltip title={t('common.refresh')}>
              <IconButton onClick={() => void load()} aria-label={t('common.refresh')}>
                <RefreshIcon />
              </IconButton>
            </Tooltip>
            <Tooltip title={t('policies.moreActions')}>
              <IconButton onClick={(event) => setMenuAnchor(event.currentTarget)} aria-label={t('policies.moreActions')}>
                <MoreVertIcon />
              </IconButton>
            </Tooltip>
            <Button variant="contained" startIcon={<AddIcon />} onClick={startCreate}>
              {t('policies.create')}
            </Button>
            <Menu anchorEl={menuAnchor} open={!!menuAnchor} onClose={() => setMenuAnchor(null)}>
              <MenuItem onClick={() => { setMenuAnchor(null); void showSnapshot(); }}>
                {t('policies.snapshot')}
              </MenuItem>
              <MenuItem onClick={() => { setMenuAnchor(null); setImportOpen(true); setImportResult(null); }}>
                {t('policies.import')}
              </MenuItem>
              <MenuItem onClick={() => { setMenuAnchor(null); exportPolicies(); }} disabled={rows.length === 0}>
                {t('policies.export')}
              </MenuItem>
            </Menu>
          </>
        }
      />

      {error && <ErrorState message={error} retry={() => void load()} />}
      {message && <Alert severity="success" onClose={() => setMessage(null)}>{message}</Alert>}
      {loading && <Card><TableSkeleton rows={5} columns={4} /></Card>}

      {!loading && (
        <>
          <Box sx={{ display: 'grid', gridTemplateColumns: { xs: 'repeat(2, minmax(0, 1fr))', lg: 'repeat(4, minmax(0, 1fr))' }, gap: 2 }}>
            <StatCard
              label={t('policies.summaryEnabled')}
              value={`${enabledRows.length}/${rows.length}`}
              caption={t('policies.summaryEnabledCaption', { enabled: enabledRows.length, disabled: rows.length - enabledRows.length })}
              icon={<GppGoodIcon />}
              tone="success"
            />
            <StatCard
              label={t('policies.summaryBlock')}
              value={actionCounts.block}
              caption={t('policies.summaryBlockCaption', { priority: highestPriority || '—' })}
              icon={<BlockIcon />}
              tone="error"
            />
            <StatCard
              label={t('policies.summaryRedactAlert')}
              value={actionCounts.redact + actionCounts.alert}
              caption={t('policies.summaryRedactAlertCaption', { redact: actionCounts.redact, alert: actionCounts.alert })}
              icon={<VisibilityOffIcon />}
              tone="warning"
            />
            <StatCard
              label={t('policies.summaryCoverage')}
              value={`${usedDetectors.size}/${detectors.length}`}
              caption={t('policies.summaryCoverageCaption', { used: usedDetectors.size, total: detectors.length })}
              icon={<PolicyIcon />}
            />
          </Box>

          {rows.length === 0 ? (
            <Card>
              <CardContent>
                <Stack spacing={3}>
                  <Stack alignItems="center" textAlign="center" spacing={1}>
                    <Box sx={{ color: 'primary.main', bgcolor: 'action.hover', p: 1.5, borderRadius: 3, display: 'grid' }}>
                      <PolicyIcon />
                    </Box>
                    <Typography variant="h2">{t('policies.empty')}</Typography>
                    <Typography variant="body2" color="text.secondary" maxWidth={560}>{t('policies.emptyHint')}</Typography>
                  </Stack>
                  <Divider />
                  <Box>
                    <Typography variant="subtitle2">{t('policies.templatesTitle')}</Typography>
                    <Typography variant="body2" color="text.secondary" mb={2}>{t('policies.templatesHint')}</Typography>
                    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', sm: 'repeat(2, minmax(0, 1fr))' }, gap: 2 }}>
                      {policyTemplates.map((template) => (
                        <Box
                          key={template.id}
                          sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 2, display: 'flex', flexDirection: 'column', gap: 1 }}
                        >
                          <Stack direction="row" spacing={1} alignItems="center">
                            <Box sx={{ color: 'primary.main', display: 'grid', placeItems: 'center' }}>{templateIcons[template.icon]}</Box>
                            <Typography variant="subtitle1">{t(`policies.template.${template.id}.name`)}</Typography>
                            <Box flexGrow={1} />
                            <StatusChip status={template.draft.action} label={actionLabel(template.draft.action, t)} />
                          </Stack>
                          <Typography variant="body2" color="text.secondary">{t(`policies.template.${template.id}.desc`)}</Typography>
                          <Stack direction="row" spacing={0.75} flexWrap="wrap" useFlexGap>
                            {template.draft.requirements.map((requirement) => (
                              <Chip key={requirement.detector} size="small" variant="outlined" label={detectorLabel(requirement.detector, t)} />
                            ))}
                          </Stack>
                          <Box flexGrow={1} />
                          <Button size="small" variant="outlined" onClick={() => startFromTemplate(template.draft)} sx={{ alignSelf: 'flex-start' }}>
                            {t('policies.useTemplate')}
                          </Button>
                        </Box>
                      ))}
                    </Box>
                  </Box>
                  <Alert severity="info">
                    {t('policies.emptyTemplateHint')}{' '}
                    <Button size="small" onClick={startCreate}>{t('policies.startBlank')}</Button>
                  </Alert>
                </Stack>
              </CardContent>
            </Card>
          ) : (
            <>
              <Card>
                <CardContent>
                  <Box
                    sx={{
                      display: 'grid',
                      gridTemplateColumns: { xs: '1fr 1fr', lg: 'minmax(220px, 1fr) 170px 160px auto' },
                      gap: 1.5,
                      alignItems: 'center'
                    }}
                  >
                    <Box sx={{ gridColumn: { xs: '1 / -1', lg: 'auto' } }}>
                      <TextField
                        fullWidth
                        size="small"
                        value={search}
                        onChange={(event) => setSearch(event.target.value)}
                        placeholder={t('policies.searchPlaceholder')}
                        InputProps={{ startAdornment: <SearchIcon fontSize="small" sx={{ mr: 1, color: 'text.secondary' }} /> }}
                        inputProps={{ 'aria-label': t('policies.searchPlaceholder') }}
                      />
                    </Box>
                    <TextField
                      select
                      fullWidth
                      size="small"
                      label={t('policies.actionFilter')}
                      value={actionFilter}
                      onChange={(event) => setActionFilter(event.target.value)}
                    >
                      <MenuItem value="all">{t('policies.allActions')}</MenuItem>
                      <MenuItem value="block">{t('policies.actionLabel.block')}</MenuItem>
                      <MenuItem value="redact">{t('policies.actionLabel.redact')}</MenuItem>
                      <MenuItem value="alert">{t('policies.actionLabel.alert')}</MenuItem>
                    </TextField>
                    <TextField
                      select
                      fullWidth
                      size="small"
                      label={t('policies.statusFilter')}
                      value={statusFilter}
                      onChange={(event) => setStatusFilter(event.target.value)}
                    >
                      <MenuItem value="all">{t('policies.allStatuses')}</MenuItem>
                      <MenuItem value="enabled">{t('common.enabled')}</MenuItem>
                      <MenuItem value="disabled">{t('common.disabled')}</MenuItem>
                    </TextField>
                    <Stack
                      direction="row"
                      spacing={1}
                      alignItems="center"
                      sx={{
                        gridColumn: { xs: '1 / -1', lg: 'auto' },
                        justifySelf: { xs: 'end', lg: 'auto' }
                      }}
                    >
                      <Typography variant="caption" color="text.secondary">
                        {t('policies.filtered', { shown: filtered.length, total: rows.length })}
                      </Typography>
                      <Button size="small" onClick={resetFilters}>{t('policies.resetFilters')}</Button>
                    </Stack>
                  </Box>
                </CardContent>
              </Card>

              <Typography variant="caption" color="text.secondary">
                {t('policies.sortedHint')}
              </Typography>

              {filtered.length === 0 ? (
                <Card>
                  <EmptyState
                    title={t('policies.noMatch')}
                    description={t('policies.noMatchHint')}
                    action={<Button size="small" onClick={resetFilters}>{t('policies.resetFilters')}</Button>}
                  />
                </Card>
              ) : (
                <Stack spacing={2}>
                  {filtered.map((row) => (
                    <PolicyCard
                      key={row.policy_key}
                      row={row}
                      busy={busyKey === row.policy_key}
                      onEdit={startEdit}
                      onToggle={toggle}
                      onDelete={setDeleteTarget}
                      onActivate={activate}
                    />
                  ))}
                </Stack>
              )}
            </>
          )}
        </>
      )}

      {editor && (
        <PolicyEditorDialog
          key={editor.original?.policy_key ?? 'new'}
          title={
            editor.original
              ? t('policies.editTitle', {
                  key: editor.original.policy_key,
                  version: nextVersion(editor.original) ?? '—'
                })
              : t('policies.createTitle')
          }
          initial={editor.draft}
          editing={!!editor.original}
          nextVersion={nextVersion(editor.original)}
          detectors={detectors}
          teams={teams}
          apps={apps}
          busy={editorBusy}
          error={editorError}
          onClose={() => setEditor(null)}
          onSubmit={(draft) => void submit(draft)}
          onTest={(body) => api.testPolicy(body)}
          onHistoryImpact={(body) => api.dryRunPolicy(body)}
        />
      )}

      <ConfirmDialog
        open={!!deleteTarget}
        title={t('policies.deleteTitle', { key: deleteTarget?.policy_key ?? '' })}
        description={t('policies.deleteHint', { count: deleteTarget?.versions.length ?? 0 })}
        confirmLabel={t('policies.delete')}
        danger
        busy={deleting}
        onClose={() => setDeleteTarget(null)}
        onConfirm={() => void remove()}
      />

      <Dialog open={snapshotOpen} onClose={() => setSnapshotOpen(false)} fullWidth maxWidth="md">
        <DialogTitle>{t('policies.snapshotTitle')}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} mt={1}>
            {snapshot?.error ? (
              <Alert severity="error">{t('policies.snapshotError')} · {snapshot.error}</Alert>
            ) : (
              <Alert severity={(snapshot?.count ?? 0) > 0 ? 'success' : 'info'}>
                {snapshot?.count ? t('policies.snapshotOk') : t('policies.snapshotEmpty')} · {t('policies.snapshotCount', { count: snapshot?.count ?? 0 })}
              </Alert>
            )}
            <Typography variant="body2" color="text.secondary">{t('policies.snapshotHint')}</Typography>
            {snapshot?.policies.map((policy) => {
              const requirements = [...policy.all, ...policy.any];
              return (
                <Box key={policy.policy_key} sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 2 }}>
                  <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                    <Typography variant="subtitle1" sx={{ wordBreak: 'break-word' }}>{policy.policy_key}</Typography>
                    <Chip size="small" variant="outlined" label={`v${policy.version}`} />
                    <StatusChip status={policy.action} label={actionLabel(policy.action, t)} />
                  </Stack>
                  <Typography variant="caption" color="text.secondary" display="block" mt={0.5}>
                    {t('policies.priority')} {policy.priority} · {t('policies.rollout')} {policy.rollout}% ·{' '}
                    {scopeSummary({ teams: policy.teams, apps: policy.apps, models: policy.models }, t)}
                  </Typography>
                  <Stack direction="row" spacing={0.75} flexWrap="wrap" useFlexGap mt={1}>
                    {requirements.length === 0 ? (
                      <Chip size="small" variant="outlined" label={t('policies.scopeOnly')} />
                    ) : (
                      requirements.map((detector) => (
                        <Chip
                          key={`${policy.policy_key}-${detector}`}
                          size="small"
                          variant="outlined"
                          label={`${detectorLabel(detector, t)} × ${policy.min_matches[detector] ?? 1}`}
                        />
                      ))
                    )}
                  </Stack>
                </Box>
              );
            })}
            <Accordion disableGutters elevation={0} sx={{ '&::before': { display: 'none' } }}>
              <AccordionSummary expandIcon={<ExpandMoreIcon />}>
                <Typography variant="body2" fontWeight={600}>{t('policies.snapshotRaw')}</Typography>
              </AccordionSummary>
              <AccordionDetails>
                <CodeBlock code={JSON.stringify(snapshot?.policies ?? [], null, 2)} label="JSON" />
              </AccordionDetails>
            </Accordion>
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setSnapshotOpen(false)}>{t('common.close')}</Button>
        </DialogActions>
      </Dialog>

      <Dialog open={importOpen} onClose={importing ? undefined : () => setImportOpen(false)} fullWidth maxWidth="md">
        <DialogTitle>{t('policies.import')}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} mt={1}>
            <Alert severity="info">{t('policies.importHint')}</Alert>
            <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
              <TextField
                select
                label={t('policies.importFormat')}
                value={importFormat}
                onChange={(event) => setImportFormat(event.target.value as 'json' | 'yaml')}
                sx={{ minWidth: 140 }}
              >
                <MenuItem value="json">JSON</MenuItem>
                <MenuItem value="yaml">YAML</MenuItem>
              </TextField>
              <Box flexGrow={1} />
              <Button size="small" startIcon={<DownloadIcon />} onClick={downloadTemplate}>
                {t('policies.downloadTemplate')}
              </Button>
            </Stack>
            <TextField
              multiline
              minRows={10}
              label={importFormat.toUpperCase()}
              value={importText}
              onChange={(event) => setImportText(event.target.value)}
              autoComplete="off"
              sx={{ '& textarea': { fontFamily: 'monospace', fontSize: 12 } }}
            />
            {importResult && (
              <Alert
                severity={importResult.includes(t('policies.importFailedMarker')) ? 'warning' : 'success'}
                sx={{ whiteSpace: 'pre-wrap' }}
              >
                {importResult}
              </Alert>
            )}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setImportOpen(false)} disabled={importing}>{t('common.close')}</Button>
          <Button variant="contained" onClick={() => void importPolicies()} disabled={importing || !importText.trim()}>
            {importing ? t('common.working') : t('policies.import')}
          </Button>
        </DialogActions>
      </Dialog>

      <Typography variant="caption" color="text.secondary">
        {t('policies.footnote', { detectors: detectors.length })}
      </Typography>
    </Stack>
  );
}

function toBodyFromVersion(policyKey: string, version: PolicyVersionRow): PublishPolicyBody {
  const requirements = (version.when.all.length > 0 ? version.when.all : version.when.any).map((requirement) => ({
    detector: requirement.detector,
    minMatches: Math.max(1, requirement.min_matches)
  }));

  return {
    policyKey,
    priority: version.priority,
    scope: version.scope,
    when: version.when.all.length > 0 ? { all: requirements } : { any: requirements },
    then: {
      action: version.then.action,
      reason: version.then.reason,
      ruleId: version.then.rule_id ?? undefined
    },
    onError: 'block',
    rolloutPercent: version.rollout_percent,
    changeReason: 'exported'
  };
}

function downloadJson(payload: unknown, filename: string) {
  const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = filename;
  link.click();
  URL.revokeObjectURL(url);
}
