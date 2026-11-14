import { Box, Stack, Typography } from '@mui/material';
import { useTranslation } from 'react-i18next';

export function BrandMark({ size = 38 }: { size?: number }) {
  return (
    <Box
      component="svg"
      viewBox="0 0 40 40"
      width={size}
      height={size}
      aria-hidden="true"
      sx={{ display: 'block', flexShrink: 0 }}
    >
      <defs>
        <linearGradient id="securehub-brand" x1="4" y1="3" x2="36" y2="38" gradientUnits="userSpaceOnUse">
          <stop stopColor="#7C72F2" />
          <stop offset="1" stopColor="#4338CA" />
        </linearGradient>
      </defs>
      <rect width="40" height="40" rx="12" fill="url(#securehub-brand)" />
      <path d="M20 8.5 30 12v7.2c0 6-4.1 10.2-10 12.3-5.9-2.1-10-6.3-10-12.3V12l10-3.5Z" fill="white" fillOpacity=".12" stroke="white" strokeOpacity=".9" strokeWidth="1.5" />
      <path d="m15 17 5-3 5 3m-10 0v6l5 3 5-3v-6m-10 0 5 3 5-3m-5-3v6m0 3v3" fill="none" stroke="white" strokeLinecap="round" strokeLinejoin="round" strokeWidth="1.5" />
      <circle cx="15" cy="17" r="1.5" fill="white" />
      <circle cx="25" cy="17" r="1.5" fill="white" />
      <circle cx="20" cy="26" r="1.5" fill="white" />
    </Box>
  );
}

export function BrandLockup({ inverse = false, subtitle }: { inverse?: boolean; subtitle?: string }) {
  const { t } = useTranslation();
  return (
    <Stack direction="row" spacing={1.25} alignItems="center" minWidth={0}>
      <BrandMark />
      <Box minWidth={0}>
        <Typography variant="subtitle1" color={inverse ? 'common.white' : 'text.primary'} lineHeight={1.25} translate="no">
          {t('app.name')}
        </Typography>
        <Typography variant="caption" color={inverse ? 'rgba(255,255,255,0.56)' : 'text.secondary'} noWrap>
          {subtitle ?? t('app.tagline')}
        </Typography>
      </Box>
    </Stack>
  );
}
