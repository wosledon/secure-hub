import { useMemo, useState } from 'react';
import {
  Accordion,
  AccordionDetails,
  AccordionSummary,
  Alert,
  Autocomplete,
  Box,
  Button,
  Chip,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  FormControlLabel,
  IconButton,
  MenuItem,
  Slider,
  Stack,
  Switch,
  TextField,
  Typography,
  ButtonBase,
  useMediaQuery,
  useTheme
} from '@mui/material';
import { alpha } from '@mui/material/styles';
import BlockIcon from '@mui/icons-material/Block';
import CheckCircleIcon from '@mui/icons-material/CheckCircle';
import CloseIcon from '@mui/icons-material/Close';
import ExpandMoreIcon from '@mui/icons-material/ExpandMore';
import NotificationsActiveIcon from '@mui/icons-material/NotificationsActive';
import PlayArrowIcon from '@mui/icons-material/PlayArrow';
import ScienceIcon from '@mui/icons-material/Science';
import VisibilityOffIcon from '@mui/icons-material/VisibilityOff';
import { useTranslation } from 'react-i18next';
import type {
  OrgAppRow,
  OrgTeamRow,
  PolicyDetectorInfo,
  PolicyDryRunResult,
  PolicyTestBody,
  PolicyTestResult,
  PublishPolicyBody
} from '../api';
import { useFormatters } from '../utils/format';
import {
  actionLabel,
  detectorDescription,
  detectorLabel,
  detectorSample,
  fallbackDetectors,
  type PolicyAction,
  type PolicyDraft,
  toPublishBody
} from '../utils/policy';
import { StatusChip } from './ui';

const actions: Array<{ value: PolicyAction; tone: 'error' | 'warning' | 'info' }> = [
  { value: 'block', tone: 'error' },
  { value: 'redact', tone: 'warning' },
  { value: 'alert', tone: 'info' }
];

const actionIcon = (action: PolicyAction) =>
  action === 'block' ? <BlockIcon /> : action === 'redact' ? <VisibilityOffIcon /> : <NotificationsActiveIcon />;

function severityTone(severity: string): 'error' | 'warning' | 'info' {
  return severity === 'critical' ? 'error' : severity === 'warning' ? 'warning' : 'info';
}

export function PolicyEditorDialog({
  title,
  initial,
  editing,
  nextVersion,
  detectors,
  teams,
  apps,
  busy,
  error,
  onClose,
  onSubmit,
  onTest,
  onHistoryImpact
}: {
  title: string;
  initial: PolicyDraft;
  editing: boolean;
  nextVersion?: number;
  detectors: PolicyDetectorInfo[];
  teams: OrgTeamRow[];
  apps: OrgAppRow[];
  busy: boolean;
  error: string | null;
  onClose: () => void;
  onSubmit: (draft: PolicyDraft) => void;
  onTest: (body: PolicyTestBody) => Promise<PolicyTestResult>;
  onHistoryImpact: (body: PublishPolicyBody) => Promise<PolicyDryRunResult>;
}) {
  const { t } = useTranslation();
  const format = useFormatters();
  const theme = useTheme();
  const fullScreen = useMediaQuery(theme.breakpoints.down('sm'));
  const [draft, setDraft] = useState<PolicyDraft>(initial);
  const [formError, setFormError] = useState<string | null>(null);
  const [sample, setSample] = useState(() =>
    initial.requirements
      .map((requirement) => detectorSample(requirement.detector, detectors))
      .filter(Boolean)
      .join('\n')
  );
  const [testContext, setTestContext] = useState({ appName: '', teamName: '', model: '' });
  const [testBusy, setTestBusy] = useState(false);
  const [testError, setTestError] = useState<string | null>(null);
  const [testResult, setTestResult] = useState<PolicyTestResult | null>(null);
  const [impact, setImpact] = useState<PolicyDryRunResult | null>(null);
  const [impactBusy, setImpactBusy] = useState(false);

  const selected = useMemo(
    () =>
      draft.requirements.map(
        (requirement) =>
          detectors.find((detector) => detector.id === requirement.detector) ??
          fallbackDetectors.find((detector) => detector.id === requirement.detector) ?? {
            id: requirement.detector,
            category: 'pii',
            severity: 'info',
            sample: ''
          }
      ),
    [detectors, draft.requirements]
  );

  const keyValid = /^[a-z][a-z0-9-]{1,63}$/.test(draft.policyKey.trim());
  const hasScope = draft.teams.length > 0 || draft.apps.length > 0 || draft.models.length > 0;
  const hasConditions = draft.requirements.length > 0;

  const fillSample = () => {
    const text = draft.requirements
      .map((requirement) => detectorSample(requirement.detector, detectors))
      .filter(Boolean)
      .join('\n');
    setSample(text);
  };

  const test = async () => {
    setTestError(null);
    setImpact(null);

    if (!keyValid) {
      setTestError(t('policies.keyInvalid'));
      return;
    }

    if (!hasConditions && !hasScope) {
      setTestError(t('policies.detectorRequired'));
      return;
    }

    if (!sample.trim()) {
      setTestError(t('policies.testEmpty'));
      return;
    }

    setTestBusy(true);
    try {
      const body = toPublishBody(draft);
      setTestResult(await onTest({
        policy: body,
        sampleText: sample,
        appName: testContext.appName.trim() || undefined,
        teamName: testContext.teamName.trim() || undefined,
        model: testContext.model.trim() || undefined
      }));
    } catch (err) {
      setTestResult(null);
      setTestError((err as Error).message);
    } finally {
      setTestBusy(false);
    }
  };

  const evaluateImpact = async () => {
    setImpactBusy(true);
    setTestError(null);
    try {
      setImpact(await onHistoryImpact(toPublishBody(draft)));
    } catch (err) {
      setTestError((err as Error).message);
    } finally {
      setImpactBusy(false);
    }
  };

  const submit = () => {
    setFormError(null);

    if (!keyValid) {
      setFormError(t('policies.keyInvalid'));
      return;
    }

    if (!hasConditions && !hasScope) {
      setFormError(t('policies.detectorRequired'));
      return;
    }

    onSubmit({ ...draft, policyKey: draft.policyKey.trim() });
  };

  return (
    <Dialog open onClose={busy ? undefined : onClose} fullWidth maxWidth="md" fullScreen={fullScreen} aria-labelledby="policy-editor-title">
      <DialogTitle sx={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', gap: 1 }}>
        <span id="policy-editor-title">{title}</span>
        <IconButton onClick={onClose} disabled={busy} aria-label={t('common.close')} size="small">
          <CloseIcon fontSize="small" />
        </IconButton>
      </DialogTitle>
      <DialogContent>
        <Stack spacing={2.5} mt={0.5}>
          <Typography variant="body2" color="text.secondary">
            {editing
              ? t('policies.editHint', { version: nextVersion ?? '—' })
              : t('policies.createHint')}
          </Typography>

          <Box>
            <Typography variant="subtitle2" mb={1}>{t('policies.basics')}</Typography>
            <Stack spacing={2}>
              <TextField
                fullWidth
                required
                label={t('policies.policyKey')}
                value={draft.policyKey}
                InputProps={{ readOnly: editing }}
                onChange={(event) => setDraft({ ...draft, policyKey: event.target.value })}
                name="policyKey"
                autoComplete="off"
                error={draft.policyKey.length > 0 && !keyValid}
                helperText={t('policies.keyHint')}
              />
              <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                <TextField
                  fullWidth
                  label={t('policies.changeReason')}
                  value={draft.changeReason}
                  onChange={(event) => setDraft({ ...draft, changeReason: event.target.value })}
                  name="changeReason"
                  autoComplete="off"
                  helperText={t('policies.changeReasonHint')}
                />
                <TextField
                  type="number"
                  label={t('policies.priority')}
                  value={draft.priority}
                  onChange={(event) =>
                    setDraft({ ...draft, priority: Math.min(1000, Math.max(0, Number(event.target.value) || 0)) })
                  }
                  inputProps={{ min: 0, max: 1000 }}
                  helperText={t('policies.priorityHint')}
                  sx={{ width: { xs: '100%', sm: 180 }, flexShrink: 0 }}
                />
              </Stack>
            </Stack>
          </Box>

          <Divider />

          <Box>
            <Typography variant="subtitle2" mb={0.5}>{t('policies.conditions')}</Typography>
            <Typography variant="body2" color="text.secondary" mb={1.5}>{t('policies.conditionsHint')}</Typography>
            <Autocomplete
              multiple
              disableCloseOnSelect
              options={detectors}
              value={selected}
              onChange={(_, value) =>
                setDraft({
                  ...draft,
                  requirements: value.map((detector) => {
                    const existing = draft.requirements.find((requirement) => requirement.detector === detector.id);
                    return { detector: detector.id, min_matches: existing?.min_matches ?? 1 };
                  })
                })
              }
              groupBy={(option) => t(`policies.category.${option.category}`, { defaultValue: option.category })}
              getOptionLabel={(option) => detectorLabel(option.id, t)}
              isOptionEqualToValue={(option, value) => option.id === value.id}
              renderOption={(props, option) => (
                <li {...props} key={option.id}>
                  <Stack direction="row" spacing={1} alignItems="center" width="100%" minWidth={0}>
                    <Box flexGrow={1} minWidth={0}>
                      <Typography variant="body2">{detectorLabel(option.id, t)}</Typography>
                      <Typography variant="caption" color="text.secondary" component="div">{option.id}</Typography>
                    </Box>
                    <StatusChip
                      status={severityTone(option.severity)}
                      label={t(`policies.severity.${option.severity}`, { defaultValue: option.severity })}
                    />
                  </Stack>
                </li>
              )}
              renderTags={(value, getTagProps) =>
                value.map((option, index) => (
                  <Chip {...getTagProps({ index })} key={option.id} size="small" label={detectorLabel(option.id, t)} />
                ))
              }
              renderInput={(params) => (
                <TextField {...params} label={t('policies.detectors')} placeholder={t('policies.detectorsPlaceholder')} />
              )}
            />

            {selected.length > 0 && (
              <Stack spacing={1} mt={1.5}>
                {draft.requirements.map((requirement) => {
                  const detector = selected.find((item) => item.id === requirement.detector);
                  return (
                    <Stack
                      key={requirement.detector}
                      direction={{ xs: 'column', sm: 'row' }}
                      spacing={1.5}
                      alignItems={{ xs: 'stretch', sm: 'center' }}
                      sx={{ p: 1.5, border: 1, borderColor: 'divider', borderRadius: 2 }}
                    >
                      <Box flexGrow={1} minWidth={0}>
                        <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                          <Typography variant="body2" fontWeight={600}>{detectorLabel(requirement.detector, t)}</Typography>
                          <Box component="code" sx={{ fontSize: 11, color: 'text.secondary' }}>{requirement.detector}</Box>
                          {detector && (
                            <StatusChip
                              status={severityTone(detector.severity)}
                              label={t(`policies.severity.${detector.severity}`, { defaultValue: detector.severity })}
                            />
                          )}
                        </Stack>
                        <Typography variant="caption" color="text.secondary">
                          {detectorDescription(requirement.detector, t)}
                        </Typography>
                      </Box>
                      <Stack direction="row" spacing={1} alignItems="center">
                        <Typography variant="caption" color="text.secondary">{t('policies.minMatchesInline')}</Typography>
                        <TextField
                          type="number"
                          size="small"
                          value={requirement.min_matches}
                          onChange={(event) =>
                            setDraft({
                              ...draft,
                              requirements: draft.requirements.map((item) =>
                                item.detector === requirement.detector
                                  ? { ...item, min_matches: Math.min(100, Math.max(1, Number(event.target.value) || 1)) }
                                  : item
                              )
                            })
                          }
                          inputProps={{ min: 1, max: 100, 'aria-label': t('policies.minMatches') }}
                          sx={{ width: 88 }}
                        />
                        <Typography variant="caption" color="text.secondary">{t('policies.times')}</Typography>
                        <IconButton
                          size="small"
                          aria-label={t('common.remove')}
                          onClick={() =>
                            setDraft({
                              ...draft,
                              requirements: draft.requirements.filter((item) => item.detector !== requirement.detector)
                            })
                          }
                        >
                          <CloseIcon fontSize="small" />
                        </IconButton>
                      </Stack>
                    </Stack>
                  );
                })}
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={1} alignItems={{ xs: 'stretch', sm: 'center' }}>
                  <TextField
                    select
                    size="small"
                    label={t('policies.matchMode')}
                    value={draft.mode}
                    onChange={(event) => setDraft({ ...draft, mode: event.target.value as PolicyDraft['mode'] })}
                    sx={{ minWidth: 180 }}
                  >
                    <MenuItem value="any">{t('policies.matchAny')}</MenuItem>
                    <MenuItem value="all">{t('policies.matchAll')}</MenuItem>
                  </TextField>
                  <Typography variant="caption" color="text.secondary">
                    {draft.mode === 'any' ? t('policies.matchAnyHint') : t('policies.matchAllHint')}
                  </Typography>
                </Stack>
              </Stack>
            )}
          </Box>

          <Accordion disableGutters elevation={0} sx={{ '&::before': { display: 'none' }, border: 1, borderColor: 'divider', borderRadius: 2 }}>
            <AccordionSummary expandIcon={<ExpandMoreIcon />}>
              <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                <Typography variant="subtitle2">{t('policies.scope')}</Typography>
                <Typography variant="caption" color="text.secondary">{t('policies.scopeShort')}</Typography>
              </Stack>
            </AccordionSummary>
            <AccordionDetails>
              <Stack spacing={2}>
                <Typography variant="body2" color="text.secondary">{t('policies.scopeHint')}</Typography>
                <Autocomplete
                  multiple
                  options={teams}
                  value={teams.filter((team) => draft.teams.includes(team.name))}
                  onChange={(_, value) => setDraft({ ...draft, teams: value.map((team) => team.name) })}
                  getOptionLabel={(option) => option.name}
                  renderInput={(params) => <TextField {...params} label={t('policies.teams')} />}
                />
                <Autocomplete
                  multiple
                  options={apps}
                  value={apps.filter((app) => draft.apps.includes(app.name))}
                  onChange={(_, value) => setDraft({ ...draft, apps: value.map((app) => app.name) })}
                  getOptionLabel={(option) => (option.team_name ? `${option.name} · ${option.team_name}` : option.name)}
                  renderInput={(params) => <TextField {...params} label={t('policies.apps')} />}
                />
                <Autocomplete
                  multiple
                  freeSolo
                  options={[] as string[]}
                  value={draft.models}
                  onChange={(_, value) => setDraft({ ...draft, models: value })}
                  renderInput={(params) => (
                    <TextField
                      {...params}
                      label={t('policies.models')}
                      placeholder="gpt-*"
                      helperText={t('policies.modelsHint')}
                    />
                  )}
                />
              </Stack>
            </AccordionDetails>
          </Accordion>

          <Box>
            <Typography variant="subtitle2" mb={0.5}>{t('policies.actionSection')}</Typography>
            <Typography variant="body2" color="text.secondary" mb={1.5}>{t('policies.actionHint')}</Typography>
            <Stack spacing={1} role="radiogroup" aria-label={t('policies.action')}>
              {actions.map((option) => {
                const selectedAction = draft.action === option.value && !draft.observation;
                return (
                  <ButtonBase
                    key={option.value}
                    role="radio"
                    aria-checked={selectedAction}
                    onClick={() => setDraft({ ...draft, action: option.value, observation: false })}
                    sx={{
                      p: 1.5,
                      border: 1,
                      borderColor: selectedAction ? 'primary.main' : 'divider',
                      bgcolor: (theme) => (selectedAction ? alpha(theme.palette.primary.main, 0.05) : 'transparent'),
                      borderRadius: 2,
                      justifyContent: 'flex-start',
                      textAlign: 'left',
                      width: '100%'
                    }}
                  >
                    <Stack direction="row" spacing={1.5} alignItems="flex-start" width="100%">
                      <Box sx={{ color: `${option.tone}.main`, display: 'grid', placeItems: 'center', mt: 0.25 }}>
                        {actionIcon(option.value)}
                      </Box>
                      <Box flexGrow={1} minWidth={0}>
                        <Typography variant="body2" fontWeight={600}>{actionLabel(option.value, t)}</Typography>
                        <Typography variant="caption" color="text.secondary">
                          {t(`policies.${option.value}Desc`)}
                        </Typography>
                      </Box>
                      {selectedAction && <CheckCircleIcon color="primary" fontSize="small" />}
                    </Stack>
                  </ButtonBase>
                );
              })}
            </Stack>

            <FormControlLabel
              sx={{ mt: 1.5 }}
              control={
                <Switch
                  checked={draft.observation}
                  onChange={(event) => setDraft({ ...draft, observation: event.target.checked })}
                />
              }
              label={t('policies.observation')}
            />
            {draft.observation && <Alert severity="info" sx={{ mt: 1 }}>{t('policies.observationHint')}</Alert>}

            <Box mt={2}>
              <Stack direction="row" justifyContent="space-between" alignItems="center">
                <Typography variant="body2">{t('policies.rollout')}</Typography>
                <Chip size="small" label={`${draft.rollout}%`} />
              </Stack>
              <Slider
                value={draft.rollout}
                onChange={(_, value) => setDraft({ ...draft, rollout: value as number })}
                marks={[{ value: 0 }, { value: 25 }, { value: 50 }, { value: 75 }, { value: 100 }]}
                step={null}
                valueLabelDisplay="auto"
                aria-label={t('policies.rollout')}
              />
              <Typography variant="caption" color="text.secondary">{t('policies.rolloutHint')}</Typography>
            </Box>
          </Box>

          <Divider />

          <Box>
            <Stack direction="row" spacing={1} alignItems="center" mb={0.5}>
              <ScienceIcon fontSize="small" color="action" />
              <Typography variant="subtitle2">{t('policies.testSection')}</Typography>
            </Stack>
            <Typography variant="body2" color="text.secondary" mb={1.5}>{t('policies.testHint')}</Typography>
            <TextField
              multiline
              minRows={4}
              fullWidth
              label={t('policies.testInput')}
              value={sample}
              onChange={(event) => setSample(event.target.value)}
              name="sampleText"
              autoComplete="off"
              sx={{ '& textarea': { fontFamily: 'monospace', fontSize: 12 } }}
            />
            <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap mt={1.5}>
              <Button size="small" variant="outlined" onClick={fillSample} disabled={!hasConditions}>
                {t('policies.testFillSample')}
              </Button>
              <Button
                size="small"
                variant="contained"
                startIcon={<PlayArrowIcon />}
                onClick={() => void test()}
                disabled={testBusy || busy}
              >
                {testBusy ? t('common.working') : t('policies.testRun')}
              </Button>
              <Button
                size="small"
                variant="text"
                onClick={() => void evaluateImpact()}
                disabled={impactBusy || busy || !hasConditions}
              >
                {impactBusy ? t('common.working') : t('policies.historyImpact')}
              </Button>
            </Stack>

            <Accordion disableGutters elevation={0} sx={{ mt: 1.5, '&::before': { display: 'none' }, border: 1, borderColor: 'divider', borderRadius: 2 }}>
              <AccordionSummary expandIcon={<ExpandMoreIcon />}>
                <Box>
                  <Typography variant="body2" fontWeight={600}>{t('policies.testContext')}</Typography>
                  <Typography variant="caption" color="text.secondary">{t('policies.testContextHint')}</Typography>
                </Box>
              </AccordionSummary>
              <AccordionDetails>
                <Stack direction={{ xs: 'column', sm: 'row' }} spacing={2}>
                  <TextField
                    fullWidth
                    size="small"
                    label={t('policies.testAppName')}
                    value={testContext.appName}
                    onChange={(event) => setTestContext({ ...testContext, appName: event.target.value })}
                    name="testAppName"
                    autoComplete="off"
                  />
                  <TextField
                    fullWidth
                    size="small"
                    label={t('policies.testTeamName')}
                    value={testContext.teamName}
                    onChange={(event) => setTestContext({ ...testContext, teamName: event.target.value })}
                    name="testTeamName"
                    autoComplete="off"
                  />
                  <TextField
                    fullWidth
                    size="small"
                    label={t('policies.testModel')}
                    value={testContext.model}
                    onChange={(event) => setTestContext({ ...testContext, model: event.target.value })}
                    name="testModel"
                    autoComplete="off"
                  />
                </Stack>
              </AccordionDetails>
            </Accordion>

            {testError && <Alert severity="error" sx={{ mt: 1.5 }}>{testError}</Alert>}
            {impact && (
              <Alert severity="info" sx={{ mt: 1.5 }}>
                {t('policies.historyImpactHint', {
                  sampled: format.number(impact.sampled_events),
                  affected: format.number(impact.estimated_affected)
                })}
              </Alert>
            )}
            {testResult && (
              <Stack spacing={1.5} mt={1.5}>
                <Alert
                  severity={testResult.effective_action === 'block' ? 'error' : testResult.effective_action === 'redact' ? 'warning' : 'info'}
                  icon={<ScienceIcon />}
                >
                  <Stack direction="row" spacing={1} alignItems="center" flexWrap="wrap" useFlexGap>
                    <Typography variant="body2" fontWeight={600}>{t('policies.testEffective')}</Typography>
                    <StatusChip status={testResult.effective_action} label={actionLabel(testResult.effective_action, t)} />
                    <Typography variant="caption" color="text.secondary">
                      {t('policies.testGuardrail')} {actionLabel(testResult.guardrail_action, t)} · {t('policies.testPolicy')} {actionLabel(testResult.policy_action, t)}
                    </Typography>
                  </Stack>
                  {!testResult.scope_matched && (
                    <Typography variant="body2" mt={0.5}>{t('policies.testScopeUnmatched')}</Typography>
                  )}
                  {testResult.failed_closed && (
                    <Typography variant="body2" mt={0.5}>{t('policies.testFailedClosed')}</Typography>
                  )}
                </Alert>

                <Box>
                  <Typography variant="overline" color="text.secondary">{t('policies.testDetections')}</Typography>
                  <Stack direction="row" spacing={1} flexWrap="wrap" useFlexGap mt={0.5}>
                    {testResult.detections.length === 0 && (
                      <Typography variant="body2" color="text.secondary">{t('policies.testNoMatch')}</Typography>
                    )}
                    {testResult.detections.map((detection) => (
                      <Chip
                        key={detection.detector}
                        size="small"
                        color={severityTone(detection.severity)}
                        variant="outlined"
                        label={`${detectorLabel(detection.detector, t)} × ${detection.match_count}`}
                      />
                    ))}
                  </Stack>
                </Box>
              </Stack>
            )}
          </Box>

          {(formError || error) && <Alert severity="error">{formError ?? error}</Alert>}
        </Stack>
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
        <Box flexGrow={1} />
        <Button
          variant="contained"
          onClick={submit}
          disabled={busy}
        >
          {busy ? t('common.working') : editing ? t('policies.publishVersion', { version: nextVersion ?? '' }) : t('policies.publish')}
        </Button>
      </DialogActions>
    </Dialog>
  );
}
