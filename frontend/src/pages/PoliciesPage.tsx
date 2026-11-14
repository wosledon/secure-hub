import { useCallback, useEffect, useState } from 'react';
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
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  FormControlLabel,
  IconButton,
  MenuItem,
  Stack,
  Switch,
  TextField,
  Tooltip,
  Typography
} from '@mui/material';
import AddIcon from '@mui/icons-material/Add';
import DownloadIcon from '@mui/icons-material/Download';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import PolicyIcon from '@mui/icons-material/Policy';
import RefreshIcon from '@mui/icons-material/Refresh';
import RestoreIcon from '@mui/icons-material/Restore';
import ScienceIcon from '@mui/icons-material/Science';
import UploadFileIcon from '@mui/icons-material/UploadFile';
import { useTranslation } from 'react-i18next';
import { api, type PolicyDryRunResult, type PolicyRow, type PolicySnapshot, type PublishPolicyBody } from '../api';
import { CodeBlock, ConfirmDialog, EmptyState, ErrorState, PageHeader, StatusChip, TableSkeleton } from '../components/ui';
import { useFormatters } from '../utils/format';

type PolicyForm = { policyKey: string; detector: string; minMatches: number; action: string; priority: number; rollout: number; onError: string; changeReason: string };
const initialForm: PolicyForm = { policyKey: 'pol-pii-block', detector: 'pii.cn_idcard', minMatches: 1, action: 'block', priority: 100, rollout: 100, onError: 'block', changeReason: 'console update' };

export default function PoliciesPage() {
  const { t } = useTranslation();
  const format = useFormatters();
  const [rows, setRows] = useState<PolicyRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [form, setForm] = useState<PolicyForm>(initialForm);
  const [busy, setBusy] = useState<'publish' | 'dry' | 'activate' | null>(null);
  const [formError, setFormError] = useState<string | null>(null);
  const [message, setMessage] = useState<string | null>(null);
  const [dryRun, setDryRun] = useState<PolicyDryRunResult | null>(null);
  const [snapshot, setSnapshot] = useState<PolicySnapshot | null>(null);
  const [snapshotOpen, setSnapshotOpen] = useState(false);
  const [activateTarget, setActivateTarget] = useState<{ policyKey: string; version: number } | null>(null);
  const [importFormat, setImportFormat] = useState<'json' | 'yaml'>('json');
  const [importOpen, setImportOpen] = useState(false);
  const [importText, setImportText] = useState('');
  const [importing, setImporting] = useState(false);
  const [importResult, setImportResult] = useState<string | null>(null);
  const [observation, setObservation] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try { setRows(await api.policies()); } catch (err) { setError((err as Error).message); } finally { setLoading(false); }
  }, []);
  useEffect(() => { void load(); }, [load]);

  const body = (): PublishPolicyBody => ({
    policyKey: form.policyKey.trim(), priority: form.priority, scope: { teams: [], apps: [], models: [] },
    when: { any: [{ detector: form.detector.trim(), minMatches: Math.max(1, form.minMatches) }] },
    then: { action: observation ? 'alert' : form.action, reason: form.detector.trim(), ruleId: form.detector.trim() },
    onError: form.onError, rolloutPercent: form.rollout, changeReason: form.changeReason.trim()
  });

  const publish = async () => {
    setBusy('publish');
    setFormError(null);
    try {
      const result = await api.publishPolicy(body());
      setOpen(false);
      setDryRun(null);
      setMessage(t('policies.published', { version: result.version }));
      await load();
    } catch (err) { setFormError((err as Error).message); } finally { setBusy(null); }
  };

  const runDryRun = async () => {
    setBusy('dry');
    setFormError(null);
    try { setDryRun(await api.dryRunPolicy(body())); } catch (err) { setFormError((err as Error).message); } finally { setBusy(null); }
  };

  const showSnapshot = async () => {
    try {
      setSnapshot(await api.policySnapshot());
      setSnapshotOpen(true);
    } catch (err) { setError((err as Error).message); }
  };

  const exportPolicies = () => {
    const bodies = rows.flatMap((row) => row.versions
      .filter((version) => version.version === row.active_version)
      .map((version) => ({
        policyKey: row.policy_key,
        priority: version.priority,
        scope: safeParse(version.scopeJson),
        when: safeParse(version.whenJson),
        then: safeParse(version.thenJson),
        onError: version.onError,
        rolloutPercent: 100,
        changeReason: 'exported'
      })));
    const blob = new Blob([JSON.stringify(bodies, null, 2)], { type: 'application/json' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `securehub-policies-${new Date().toISOString().slice(0, 10)}.json`;
    link.click();
    URL.revokeObjectURL(url);
  };

  const importPolicies = async () => {
    setImporting(true);
    setImportResult(null);
    try {
      const parsed = importFormat === 'yaml'
        ? (await api.previewPolicyYaml(importText)).policies
        : JSON.parse(importText) as PublishPolicyBody[];
      if (!Array.isArray(parsed) || parsed.length === 0) throw new Error(t('policies.importEmpty'));
      let ok = 0;
      const failures: string[] = [];
      for (const body of parsed) {
        try { await api.publishPolicy(body); ok++; }
        catch (err) { failures.push(`${body.policyKey}: ${(err as Error).message}`); }
      }
      setImportResult(t('policies.importResult', { ok, failed: failures.length }) + (failures.length ? `\n${failures.join('\n')}` : ''));
      await load();
    } catch (err) { setImportResult((err as Error).message); } finally { setImporting(false); }
  };

  const activate = async () => {    if (!activateTarget) return;
    setBusy('activate');
    try {
      await api.activatePolicy(activateTarget.policyKey, activateTarget.version);
      setMessage(t('policies.activated', { version: activateTarget.version }));
      setActivateTarget(null);
      await load();
    } catch (err) { setError((err as Error).message); } finally { setBusy(null); }
  };

  return (
    <Stack spacing={3}>
      <PageHeader title={t('policies.title')} description={t('policies.description')} actions={<>
        <Tooltip title={t('common.refresh')}><IconButton onClick={() => void load()} aria-label={t('common.refresh')}><RefreshIcon /></IconButton></Tooltip>
        <Button variant="outlined" startIcon={<PolicyIcon />} onClick={() => void showSnapshot()}>{t('policies.snapshot')}</Button>
        <Button variant="outlined" startIcon={<DownloadIcon />} onClick={exportPolicies} disabled={rows.length === 0}>{t('policies.export')}</Button>
        <Button variant="outlined" startIcon={<UploadFileIcon />} onClick={() => { setImportOpen(true); setImportResult(null); }}>{t('policies.import')}</Button>
        <Button variant="contained" startIcon={<AddIcon />} onClick={() => { setOpen(true); setMessage(null); setObservation(false); }}>{t('policies.publish')}</Button>
      </>} />
      {error && <ErrorState message={error} retry={() => void load()} />}
      {message && <Alert severity="success" onClose={() => setMessage(null)}>{message}</Alert>}
      {loading ? <Card><TableSkeleton rows={5} columns={5} /></Card> : rows.length === 0 ? (
        <Card><EmptyState title={t('policies.empty')} description={t('policies.emptyHint')} action={<Button variant="contained" startIcon={<AddIcon />} onClick={() => setOpen(true)}>{t('policies.publish')}</Button>} /></Card>
      ) : (
        <Stack spacing={2}>
          {rows.map((row) => {
            const active = row.versions.find((version) => version.version === row.active_version);
            return (
              <Card key={row.policy_key}>
                <CardContent>
                  <Stack direction={{ xs: 'column', md: 'row' }} justifyContent="space-between" spacing={2} alignItems={{ xs: 'stretch', md: 'flex-start' }}>
                    <Stack direction="row" spacing={1.5} alignItems="flex-start">
                      <Box sx={{ width: 38, height: 38, borderRadius: 2, bgcolor: 'action.hover', color: 'primary.main', display: 'grid', placeItems: 'center' }}><PolicyIcon fontSize="small" /></Box>
                      <Box minWidth={0}>
                        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap><Typography variant="h2" sx={{ wordBreak: 'break-word' }}>{row.policy_key}</Typography><StatusChip status={row.enabled ? 'enabled' : 'disabled'} label={row.enabled ? t('common.enabled') : t('common.disabled')} /><Chip size="small" color="primary" variant="outlined" label={`v${row.active_version ?? '—'}`} /></Stack>
                        <Typography variant="body2" color="text.secondary" mt={0.75}>{row.versions.length} {t('policies.versions')}{active ? ` · ${t('policies.priority')} ${active.priority}` : ''}</Typography>
                      </Box>
                    </Stack>
                    {active && <StatusChip status={JSON.parse(active.thenJson || '{}').action ?? 'allow'} />}
                  </Stack>
                  {active && (
                    <Box sx={{ display: 'grid', gridTemplateColumns: { xs: '1fr', md: '1fr 1fr' }, gap: 2, mt: 2.5 }}>
                      <Box sx={{ p: 1.5, bgcolor: 'action.hover', borderRadius: 2, minWidth: 0 }}><Typography variant="overline" color="text.secondary">{t('policies.condition')}</Typography><Box component="code" sx={{ display: 'block', mt: 0.75, fontSize: 11, whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>{active.whenJson}</Box></Box>
                      <Box sx={{ p: 1.5, bgcolor: 'action.hover', borderRadius: 2, minWidth: 0 }}><Typography variant="overline" color="text.secondary">{t('policies.action')}</Typography><Box component="code" sx={{ display: 'block', mt: 0.75, fontSize: 11, whiteSpace: 'pre-wrap', wordBreak: 'break-word' }}>{active.thenJson}</Box></Box>
                    </Box>
                  )}
                  <Accordion disableGutters elevation={0} sx={{ mt: 2, '&::before': { display: 'none' } }}>
                    <AccordionSummary expandIcon={<ExpandMoreIcon />} sx={{ px: 0 }}><Typography variant="body2" fontWeight={600}>{t('policies.versions')}</Typography></AccordionSummary>
                    <AccordionDetails sx={{ px: 0 }}>
                      <Stack spacing={1.5}>
                        {[...row.versions].sort((a, b) => b.version - a.version).map((version) => (
                          <Stack key={version.version} direction={{ xs: 'column', sm: 'row' }} spacing={1.5} alignItems={{ xs: 'stretch', sm: 'center' }} sx={{ p: 1.5, border: 1, borderColor: 'divider', borderRadius: 2 }}>
                            <Chip size="small" color={version.version === row.active_version ? 'primary' : 'default'} variant="outlined" label={`v${version.version}`} />
                            <Box flexGrow={1} minWidth={0}><Typography variant="body2">{version.changeReason || t('policies.reason')}</Typography><Typography variant="caption" color="text.secondary">{format.fullDateTime(version.createdAt)} · {t('policies.priority')} {version.priority} · {version.onError}</Typography></Box>
                            <Button size="small" startIcon={<RestoreIcon />} disabled={version.version === row.active_version} onClick={() => setActivateTarget({ policyKey: row.policy_key, version: version.version })}>{t('policies.activate')}</Button>
                          </Stack>
                        ))}
                      </Stack>
                    </AccordionDetails>
                  </Accordion>
                </CardContent>
              </Card>
            );
          })}
        </Stack>
      )}

      <Dialog open={open} onClose={busy ? undefined : () => setOpen(false)} fullWidth maxWidth="sm">
        <DialogTitle>{t('policies.publishTitle')}</DialogTitle>
        <DialogContent><Stack spacing={2.25} mt={1}>
          <Typography variant="body2" color="text.secondary">{t('policies.publishHint')}</Typography>
          <TextField label={t('policies.policyKey')} value={form.policyKey} onChange={(event) => setForm({ ...form, policyKey: event.target.value })} name="policyKey" autoComplete="off" required />
          <TextField label={t('policies.detector')} value={form.detector} onChange={(event) => setForm({ ...form, detector: event.target.value })} helperText="pii.cn_idcard · pii.cn_phone · pii.email · secret.jwt · secret.high_entropy" name="detector" autoComplete="off" required />
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField select fullWidth label={t('policies.action')} value={form.action} onChange={(event) => setForm({ ...form, action: event.target.value })} disabled={observation}><MenuItem value="block">{t('policies.block')}</MenuItem><MenuItem value="redact">{t('policies.redact')}</MenuItem><MenuItem value="alert">{t('policies.alert')}</MenuItem></TextField>
            <TextField select fullWidth label={t('policies.onError')} value={form.onError} onChange={(event) => setForm({ ...form, onError: event.target.value })}><MenuItem value="block">block</MenuItem><MenuItem value="allow">allow</MenuItem></TextField>
          </Stack>
          <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
            <TextField fullWidth type="number" label={t('policies.priority')} value={form.priority} onChange={(event) => setForm({ ...form, priority: Number(event.target.value) })} inputProps={{ min: 0 }} />
            <TextField fullWidth type="number" label={t('policies.minMatches')} value={form.minMatches} onChange={(event) => setForm({ ...form, minMatches: Number(event.target.value) })} inputProps={{ min: 1 }} helperText={t('policies.minMatchesHint')} />
            <TextField fullWidth type="number" label={t('policies.rollout')} value={form.rollout} onChange={(event) => setForm({ ...form, rollout: Number(event.target.value) })} inputProps={{ min: 0, max: 100 }} />
          </Stack>
          <TextField label={t('policies.changeReason')} value={form.changeReason} onChange={(event) => setForm({ ...form, changeReason: event.target.value })} name="changeReason" autoComplete="off" />
          <FormControlLabel control={<Switch checked={observation} onChange={(event) => setObservation(event.target.checked)} />} label={t('policies.observation')} />
          {observation && <Alert severity="info">{t('policies.observationHint')}</Alert>}
          {dryRun && <Alert severity="warning" icon={<ScienceIcon />}><Typography variant="subtitle2" mb={1}>{t('policies.dryRunTitle')}</Typography><Typography variant="body2">{t('policies.sampled')}: {format.number(dryRun.sampled_events)} · {t('policies.affected')}: {format.number(dryRun.estimated_affected)}</Typography><Typography variant="caption" display="block" mt={0.5}>{t('policies.confidence')}: {dryRun.confidence} · {dryRun.note}</Typography></Alert>}
          {formError && <Alert severity="error">{formError}</Alert>}
        </Stack></DialogContent>
        <DialogActions sx={{ flexWrap: 'wrap' }}><Button onClick={() => setOpen(false)} disabled={!!busy}>{t('common.cancel')}</Button><Box flexGrow={1} /><Button startIcon={<PlayArrowIcon />} onClick={() => void runDryRun()} disabled={!!busy || !form.policyKey || !form.detector}>{busy === 'dry' ? t('common.working') : t('policies.dryRun')}</Button><Button variant="contained" onClick={() => void publish()} disabled={!!busy || !form.policyKey || !form.detector}>{busy === 'publish' ? t('common.working') : t('policies.publish')}</Button></DialogActions>
      </Dialog>
      <Dialog open={snapshotOpen} onClose={() => setSnapshotOpen(false)} fullWidth maxWidth="md"><DialogTitle>{t('policies.snapshot')}</DialogTitle><DialogContent><Stack spacing={2} mt={1}>{snapshot?.error && <Alert severity="error">{snapshot.error}</Alert>}<Alert severity={snapshot?.count ? 'success' : 'info'}>{snapshot?.count ? t('policies.snapshotOk') : t('policies.snapshotEmpty')} · {t('policies.snapshotCount', { count: snapshot?.count ?? 0 })}</Alert><CodeBlock code={JSON.stringify(snapshot?.policies ?? [], null, 2)} label="Compiled policy snapshot" /></Stack></DialogContent><DialogActions><Button onClick={() => setSnapshotOpen(false)}>{t('common.close')}</Button></DialogActions></Dialog>
      <ConfirmDialog open={!!activateTarget} title={t('policies.activateTitle', { version: activateTarget?.version ?? '' })} description={t('policies.activateHint')} confirmLabel={t('policies.activate')} busy={busy === 'activate'} onClose={() => setActivateTarget(null)} onConfirm={() => void activate()} />

      <Dialog open={importOpen} onClose={importing ? undefined : () => setImportOpen(false)} fullWidth maxWidth="md">
        <DialogTitle>{t('policies.import')}</DialogTitle>
        <DialogContent>
          <Stack spacing={2} mt={1}>
            <Alert severity="info">{t('policies.importHint')}</Alert>
            <TextField select label={t('policies.importFormat')} value={importFormat} onChange={(event) => setImportFormat(event.target.value as 'json' | 'yaml')}>
              <MenuItem value="json">JSON</MenuItem>
              <MenuItem value="yaml">YAML</MenuItem>
            </TextField>
            <TextField multiline minRows={10} label={importFormat.toUpperCase()} value={importText} onChange={(event) => setImportText(event.target.value)} autoComplete="off" sx={{ '& textarea': { fontFamily: 'monospace', fontSize: 12 } }} />
            {importResult && <Alert severity={importResult.includes(t('policies.importFailedMarker')) ? 'warning' : 'success'} sx={{ whiteSpace: 'pre-wrap' }}>{importResult}</Alert>}
          </Stack>
        </DialogContent>
        <DialogActions>
          <Button onClick={() => setImportOpen(false)} disabled={importing}>{t('common.close')}</Button>
          <Button variant="contained" onClick={() => void importPolicies()} disabled={importing || !importText.trim()}>{importing ? t('common.working') : t('policies.import')}</Button>
        </DialogActions>
      </Dialog>
    </Stack>
  );
}

function safeParse(json?: string): any {
  try { return json ? JSON.parse(json) : {}; }
  catch { return {}; }
}
