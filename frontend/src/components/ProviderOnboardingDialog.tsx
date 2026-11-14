import { useCallback, useEffect, useMemo, useState } from 'react';
import {
  Alert,
  Box,
  Button,
  Checkbox,
  Chip,
  CircularProgress,
  Dialog,
  DialogActions,
  DialogContent,
  DialogTitle,
  Divider,
  InputAdornment,
  Radio,
  Stack,
  Step,
  StepLabel,
  Stepper,
  TextField,
  Typography
} from '@mui/material';
import CheckIcon from '@mui/icons-material/Check';
import MemoryIcon from '@mui/icons-material/Memory';
import PsychologyIcon from '@mui/icons-material/Psychology';
import SearchIcon from '@mui/icons-material/Search';
import { useTranslation } from 'react-i18next';
import {
  api,
  type CatalogProviderModel,
  type CatalogProviderRow
} from '../api';
import { ErrorState } from './ui';
import { useFormatters } from '../utils/format';

type Scope = 'admin' | 'me';

export default function ProviderOnboardingDialog({
  open,
  scope,
  onClose,
  onCreated
}: {
  open: boolean;
  scope: Scope;
  onClose: () => void;
  onCreated: () => void;
}) {
  const { t } = useTranslation();
  const format = useFormatters();
  const [step, setStep] = useState(0);
  const [providers, setProviders] = useState<CatalogProviderRow[]>([]);
  const [providerQuery, setProviderQuery] = useState('');
  const [selectedProvider, setSelectedProvider] = useState<CatalogProviderRow | null>(null);
  const [models, setModels] = useState<CatalogProviderModel[]>([]);
  const [modelQuery, setModelQuery] = useState('');
  const [selectedModels, setSelectedModels] = useState<string[]>([]);
  const [alias, setAlias] = useState('');
  const [baseUrl, setBaseUrl] = useState('');
  const [apiKey, setApiKey] = useState('');
  const [loading, setLoading] = useState(false);
  const [loadingModels, setLoadingModels] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const reset = useCallback(() => {
    setStep(0);
    setProviderQuery('');
    setSelectedProvider(null);
    setModels([]);
    setModelQuery('');
    setSelectedModels([]);
    setAlias('');
    setBaseUrl('');
    setApiKey('');
    setError(null);
    setLoadingModels(false);
  }, []);

  useEffect(() => {
    if (!open) return;
    reset();
    setLoading(true);
    api.catalogProviders(scope)
      .then(setProviders)
      .catch((err) => setError((err as Error).message))
      .finally(() => setLoading(false));
  }, [open, scope, reset]);

  const filteredProviders = useMemo(() => {
    const search = providerQuery.trim().toLowerCase();
    return providers.filter((provider) =>
      !search || `${provider.name} ${provider.provider_key}`.toLowerCase().includes(search));
  }, [providers, providerQuery]);

  const chooseProvider = async (provider: CatalogProviderRow) => {
    setSelectedProvider(provider);
    setAlias(provider.provider_key);
    setBaseUrl(provider.api ?? '');
    setSelectedModels([]);
    setStep(1);
    setLoadingModels(true);
    setError(null);
    try {
      setModels(await api.catalogProviderModels(provider.provider_key, scope));
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setLoadingModels(false);
    }
  };

  const filteredModels = useMemo(() => {
    const search = modelQuery.trim().toLowerCase();
    return models.filter((model) =>
      !search || `${model.modelId} ${model.family ?? ''}`.toLowerCase().includes(search));
  }, [models, modelQuery]);

  const toggleModel = (modelId: string) => {
    setSelectedModels((current) =>
      current.includes(modelId) ? current.filter((item) => item !== modelId) : [...current, modelId]);
  };

  const submit = async () => {
    if (!selectedProvider) return;
    setBusy(true);
    setError(null);
    try {
      const body = {
        alias: alias.trim(),
        providerKey: selectedProvider.provider_key,
        apiKey: apiKey.trim(),
        models: selectedModels,
        baseUrl: baseUrl.trim() || undefined
      };
      if (scope === 'admin') await api.createProviderFromCatalog(body);
      else await api.createMyProviderFromCatalog(body);
      onCreated();
      onClose();
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  };

  const endpointMissing = !!selectedProvider && !selectedProvider.api;
  const canSubmit = !!selectedProvider && selectedModels.length > 0 && alias.trim().length > 0 &&
    apiKey.trim().length > 0 && baseUrl.trim().length > 0;

  return (
    <Dialog open={open} onClose={busy ? undefined : onClose} fullWidth maxWidth="md">
      <DialogTitle>{scope === 'admin' ? t('onboarding.titleAdmin') : t('onboarding.titlePersonal')}</DialogTitle>
      <DialogContent>
        <Stepper activeStep={step} sx={{ mb: 3 }}>
          {[t('onboarding.stepProvider'), t('onboarding.stepModels'), t('onboarding.stepCredentials')].map((label) => (
            <Step key={label}><StepLabel>{label}</StepLabel></Step>
          ))}
        </Stepper>

        {error && <Box mb={2}><ErrorState message={error} /></Box>}

        {step === 0 && (
          <Stack spacing={2}>
            <Typography variant="body2" color="text.secondary">{t('onboarding.providerHint')}</Typography>
            <TextField
              fullWidth
              size="small"
              value={providerQuery}
              onChange={(event) => setProviderQuery(event.target.value)}
              placeholder={t('onboarding.searchProvider')}
              name="catalogProviderSearch"
              autoComplete="off"
              InputProps={{ startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> }}
            />
            {loading ? (
              <Box display="flex" justifyContent="center" py={6}><CircularProgress /></Box>
            ) : (
              <Box sx={{ maxHeight: 400, overflowY: 'auto', border: 1, borderColor: 'divider', borderRadius: 2 }}>
                {filteredProviders.map((provider) => (
                  <Box
                    key={provider.provider_key}
                    component="button"
                    type="button"
                    onClick={() => void chooseProvider(provider)}
                    sx={{
                      width: '100%', display: 'flex', alignItems: 'center', gap: 1.5, p: 1.75,
                      border: 0, borderBottom: 1, borderColor: 'divider', bgcolor: 'transparent',
                      textAlign: 'left', cursor: 'pointer', color: 'text.primary',
                      '&:hover': { bgcolor: 'action.hover' }
                    }}
                  >
                    <Radio checked={selectedProvider?.provider_key === provider.provider_key} readOnly size="small" />
                    <Box flexGrow={1} minWidth={0}>
                      <Typography variant="subtitle2">{provider.name}</Typography>
                      <Typography variant="caption" color="text.secondary" component="code">{provider.provider_key}</Typography>
                    </Box>
                    <Stack direction="row" spacing={0.5} alignItems="center">
                      <Chip size="small" variant="outlined" label={t('onboarding.modelCount', { count: provider.model_count })} />
                      {!provider.api && <Chip size="small" color="warning" variant="outlined" label={t('onboarding.needsEndpoint')} />}
                    </Stack>
                  </Box>
                ))}
                {filteredProviders.length === 0 && (
                  <Box p={3} textAlign="center"><Typography variant="body2" color="text.secondary">{t('onboarding.noProviders')}</Typography></Box>
                )}
              </Box>
            )}
          </Stack>
        )}

        {step === 1 && selectedProvider && (
          <Stack spacing={2}>
            <Stack direction="row" spacing={1} alignItems="center">
              <Typography variant="subtitle1">{selectedProvider.name}</Typography>
              <Box component="code" sx={{ fontSize: 12, color: 'text.secondary' }}>{selectedProvider.provider_key}</Box>
            </Stack>
            <TextField
              fullWidth
              size="small"
              value={modelQuery}
              onChange={(event) => setModelQuery(event.target.value)}
              placeholder={t('onboarding.searchModel')}
              name="catalogModelSearch"
              autoComplete="off"
              InputProps={{ startAdornment: <InputAdornment position="start"><SearchIcon fontSize="small" /></InputAdornment> }}
            />
            <Stack direction="row" justifyContent="space-between" alignItems="center">
              <Typography variant="caption" color="text.secondary">{t('onboarding.selectedCount', { selected: selectedModels.length, total: models.length })}</Typography>
              <Stack direction="row" spacing={1}>
                <Button size="small" onClick={() => setSelectedModels(filteredModels.map((model) => model.modelId))}>{t('onboarding.selectAll')}</Button>
                <Button size="small" onClick={() => setSelectedModels([])}>{t('onboarding.clear')}</Button>
              </Stack>
            </Stack>
            {loadingModels ? <Box display="flex" justifyContent="center" py={6}><CircularProgress /></Box> : (
              <Box sx={{ maxHeight: 400, overflowY: 'auto', border: 1, borderColor: 'divider', borderRadius: 2 }}>
                {filteredModels.map((model) => {
                  const checked = selectedModels.includes(model.modelId);
                  return (
                    <Box
                      key={model.modelId}
                      sx={{ display: 'flex', gap: 1.5, p: 1.5, borderBottom: 1, borderColor: 'divider', alignItems: 'flex-start', cursor: 'pointer', '&:hover': { bgcolor: 'action.hover' } }}
                      onClick={() => toggleModel(model.modelId)}
                      role="checkbox"
                      aria-checked={checked}
                      tabIndex={0}
                      onKeyDown={(event) => {
                        if (event.key === 'Enter' || event.key === ' ') {
                          event.preventDefault();
                          toggleModel(model.modelId);
                        }
                      }}
                    >
                      <Checkbox checked={checked} size="small" tabIndex={-1} sx={{ p: 0.25 }} />
                      <Box flexGrow={1} minWidth={0}>
                        <Typography variant="subtitle2" sx={{ wordBreak: 'break-word' }}>{model.modelId}</Typography>
                        <Stack direction="row" spacing={0.75} flexWrap="wrap" useFlexGap mt={0.75}>
                          {model.family && <Typography variant="caption" color="text.secondary">{model.family}</Typography>}
                          {model.supports_tools && <Chip size="small" icon={<CheckIcon />} label={t('catalog.tools')} variant="outlined" />}
                          {model.supports_reasoning && <Chip size="small" icon={<PsychologyIcon />} label={t('catalog.reasoning')} variant="outlined" />}
                          <Chip size="small" icon={<MemoryIcon />} variant="outlined" label={format.compact(model.context_window)} />
                        </Stack>
                      </Box>
                      <Box textAlign="right" flexShrink={0}>
                        <Typography variant="caption" color="text.secondary" display="block">{t('onboarding.inPrice')} {format.currency(model.input_price)}</Typography>
                        <Typography variant="caption" color="text.secondary" display="block">{t('onboarding.outPrice')} {format.currency(model.output_price)}</Typography>
                      </Box>
                    </Box>
                  );
                })}
                {filteredModels.length === 0 && <Box p={3} textAlign="center"><Typography variant="body2" color="text.secondary">{t('catalog.empty')}</Typography></Box>}
              </Box>
            )}
          </Stack>
        )}

        {step === 2 && selectedProvider && (
          <Stack spacing={2.25}>
            <Box sx={{ p: 2, border: 1, borderColor: 'divider', borderRadius: 2 }}>
              <Typography variant="subtitle2">{t('common.provider')}: {selectedProvider.name}</Typography>
              <Typography variant="body2" color="text.secondary">{t('onboarding.modelSummary', { count: selectedModels.length })}</Typography>
            </Box>
            <TextField
              label={t('common.alias')}
              value={alias}
              onChange={(event) => setAlias(event.target.value)}
              helperText={t('providers.aliasHint')}
              name="providerAlias"
              autoComplete="off"
              spellCheck={false}
              required
            />
            <TextField
              label={t('common.baseUrl')}
              value={baseUrl}
              onChange={(event) => setBaseUrl(event.target.value)}
              helperText={endpointMissing ? t('onboarding.endpointRequired') : t('onboarding.endpointFromCatalog')}
              name="providerBaseUrl"
              autoComplete="off"
              spellCheck={false}
              required
              error={endpointMissing && !baseUrl.trim()}
            />
            <TextField
              label={t('common.apiKey')}
              type="password"
              value={apiKey}
              onChange={(event) => setApiKey(event.target.value)}
              helperText={t('providers.apiKeyHint')}
              name="providerApiKey"
              autoComplete="new-password"
              required
            />
            {selectedProvider.env?.length > 0 && (
              <Typography variant="caption" color="text.secondary">
                {t('onboarding.envHint', { names: selectedProvider.env.join(', ') })}
              </Typography>
            )}
            {scope === 'me' && <Alert severity="warning">{t('me.personalDisclosure')}</Alert>}
            <Divider />
            <Stack direction="row" spacing={1} alignItems="center">
              <CheckIcon fontSize="small" color="success" />
              <Typography variant="caption" color="text.secondary">{t('onboarding.credentialNote')}</Typography>
            </Stack>
          </Stack>
        )}
      </DialogContent>
      <DialogActions>
        <Button onClick={onClose} disabled={busy}>{t('common.cancel')}</Button>
        <Box flexGrow={1} />
        {step > 0 && <Button onClick={() => setStep((current) => current - 1)} disabled={busy}>{t('common.back')}</Button>}
        {step === 0 && <Button variant="contained" disabled={!selectedProvider} onClick={() => setStep(1)}>{t('common.next')}</Button>}
        {step === 1 && <Button variant="contained" disabled={selectedModels.length === 0} onClick={() => setStep(2)}>{t('common.next')}</Button>}
        {step === 2 && <Button variant="contained" onClick={() => void submit()} disabled={busy || !canSubmit}>{busy ? t('common.working') : t('onboarding.connect')}</Button>}
      </DialogActions>
    </Dialog>
  );
}

export { ProviderOnboardingDialog };
