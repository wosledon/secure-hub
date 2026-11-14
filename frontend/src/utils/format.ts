import { useMemo } from 'react';
import { useTranslation } from 'react-i18next';

export function useFormatters() {
  const { i18n } = useTranslation();
  const locale = i18n.language;

  return useMemo(() => {
    const number = new Intl.NumberFormat(locale);
    const compact = new Intl.NumberFormat(locale, { notation: 'compact', maximumFractionDigits: 1 });
    const currency = new Intl.NumberFormat(locale, {
      style: 'currency',
      currency: 'USD',
      minimumFractionDigits: 2,
      maximumFractionDigits: 4
    });
    const dateTime = new Intl.DateTimeFormat(locale, {
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit'
    });
    const fullDateTime = new Intl.DateTimeFormat(locale, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit'
    });
    const day = new Intl.DateTimeFormat(locale, { month: 'short', day: 'numeric' });

    const safeDate = (value: string | number | Date) => {
      const date = new Date(value);
      return Number.isNaN(date.getTime()) ? null : date;
    };

    return {
      number: (value: number) => number.format(value),
      compact: (value: number) => compact.format(value),
      currency: (value: number) => currency.format(value),
      dateTime: (value: string | number | Date) => {
        const date = safeDate(value);
        return date ? dateTime.format(date) : '—';
      },
      fullDateTime: (value: string | number | Date) => {
        const date = safeDate(value);
        return date ? fullDateTime.format(date) : '—';
      },
      day: (value: string | number | Date) => {
        const date = safeDate(value);
        return date ? day.format(date) : '—';
      }
    };
  }, [locale]);
}
