import { useEffect, useId, useMemo, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react';
import { Box, Stack, Typography } from '@mui/material';
import { useTheme } from '@mui/material/styles';
import { useTranslation } from 'react-i18next';
import { useFormatters } from '../utils/format';

export interface ActivityPoint {
  date: Date;
  requests: number;
  blocked: number;
}

const PADDING = { top: 18, right: 12, bottom: 28, left: 42 };

function clamp(value: number, min: number, max: number) {
  return Math.min(max, Math.max(min, value));
}

function niceMax(value: number) {
  if (value <= 4) return 4;
  const magnitude = 10 ** Math.floor(Math.log10(value));
  const normalized = value / magnitude;
  const step = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
  return Math.ceil((step * magnitude) / 2) * 2;
}

function smoothPath(points: Array<{ x: number; y: number }>, top: number, bottom: number) {
  if (points.length === 0) return '';
  let path = `M ${points[0].x} ${points[0].y}`;

  for (let index = 0; index < points.length - 1; index++) {
    const previous = points[index - 1] ?? points[index];
    const current = points[index];
    const next = points[index + 1];
    const afterNext = points[index + 2] ?? next;
    const tension = 0.16;

    const control1X = current.x + (next.x - previous.x) * tension;
    const control1Y = clamp(current.y + (next.y - previous.y) * tension, top, bottom);
    const control2X = next.x - (afterNext.x - current.x) * tension;
    const control2Y = clamp(next.y - (afterNext.y - current.y) * tension, top, bottom);

    path += ` C ${control1X} ${control1Y} ${control2X} ${control2Y} ${next.x} ${next.y}`;
  }

  return path;
}

export function ActivityChart({
  points,
  height = 230,
  ariaLabel
}: {
  points: ActivityPoint[];
  height?: number;
  ariaLabel: string;
}) {
  const { t, i18n } = useTranslation();
  const theme = useTheme();
  const format = useFormatters();
  const containerRef = useRef<HTMLDivElement>(null);
  const [width, setWidth] = useState(0);
  const [hovered, setHovered] = useState<number | null>(null);
  const gradientId = useId().replace(/:/g, '');
  const tooltipDate = useMemo(
    () => new Intl.DateTimeFormat(i18n.language, {
      weekday: 'short',
      month: 'short',
      day: 'numeric',
      timeZone: 'UTC'
    }),
    [i18n.language]
  );

  useEffect(() => {
    const element = containerRef.current;
    if (!element) return;

    const observer = new ResizeObserver((entries) => {
      const next = entries[0]?.contentRect.width ?? 0;
      if (next > 0) setWidth(next);
    });
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const geometry = useMemo(() => {
    const plotWidth = Math.max(120, width - PADDING.left - PADDING.right);
    const plotHeight = Math.max(80, height - PADDING.top - PADDING.bottom);
    const yMax = niceMax(Math.max(1, ...points.map((point) => Math.max(point.requests, point.blocked))));
    const baseline = PADDING.top + plotHeight;
    const x = (index: number) =>
      PADDING.left + (points.length <= 1 ? plotWidth / 2 : (index / (points.length - 1)) * plotWidth);
    const y = (value: number) => baseline - (value / yMax) * plotHeight;

    const requestPath = smoothPath(
      points.map((point, index) => ({ x: x(index), y: y(point.requests) })),
      PADDING.top,
      baseline
    );
    const blockedPath = smoothPath(
      points.map((point, index) => ({ x: x(index), y: y(point.blocked) })),
      PADDING.top,
      baseline
    );
    const areaPath = requestPath
      ? `${requestPath} L ${x(points.length - 1)} ${baseline} L ${x(0)} ${baseline} Z`
      : '';

    return { plotWidth, plotHeight, yMax, baseline, x, y, requestPath, blockedPath, areaPath };
  }, [height, points, width]);

  const active = hovered !== null ? points[hovered] : null;
  const activeX = hovered !== null ? geometry.x(hovered) : 0;
  const activeY = active ? geometry.y(active.requests) : 0;
  const tooltipBelow = activeY < 92;
  const hasBlocked = points.some((point) => point.blocked > 0);

  const handlePointer = (event: ReactPointerEvent<SVGRectElement>) => {
    if (points.length === 0) return;
    const rect = event.currentTarget.getBoundingClientRect();
    const ratio = clamp((event.clientX - rect.left) / Math.max(1, rect.width), 0, 1);
    setHovered(Math.round(ratio * (points.length - 1)));
  };

  return (
    <Box ref={containerRef} sx={{ position: 'relative', width: '100%', minWidth: 0, minHeight: height }}>
      {width > 0 && (
        <svg
          width={width}
          height={height}
          viewBox={`0 0 ${width} ${height}`}
          role="img"
          aria-label={ariaLabel}
          style={{ display: 'block', overflow: 'visible' }}
        >
          <defs>
            <linearGradient id={gradientId} x1="0" y1="0" x2="0" y2="1">
              <stop offset="0%" stopColor={theme.palette.primary.main} stopOpacity={0.24} />
              <stop offset="100%" stopColor={theme.palette.primary.main} stopOpacity={0} />
            </linearGradient>
            <style>{'@keyframes activity-chart-draw { from { stroke-dashoffset: 1; } to { stroke-dashoffset: 0; } }'}</style>
          </defs>

          {[0, geometry.yMax / 2, geometry.yMax].map((tick) => (
            <g key={tick}>
              <line
                x1={PADDING.left}
                x2={PADDING.left + geometry.plotWidth}
                y1={geometry.y(tick)}
                y2={geometry.y(tick)}
                stroke={theme.palette.divider}
                strokeDasharray={tick === 0 ? undefined : '3 5'}
              />
              <text
                x={PADDING.left - 8}
                y={geometry.y(tick)}
                textAnchor="end"
                dominantBaseline="middle"
                fontSize={11}
                fill={theme.palette.text.secondary}
              >
                {format.number(tick)}
              </text>
            </g>
          ))}

          {geometry.areaPath && <path d={geometry.areaPath} fill={`url(#${gradientId})`} />}

          {geometry.requestPath && (
            <path
              d={geometry.requestPath}
              fill="none"
              stroke={theme.palette.primary.main}
              strokeWidth={2.4}
              strokeLinecap="round"
              strokeLinejoin="round"
              pathLength={1}
              strokeDasharray={1}
              style={{ animation: 'activity-chart-draw 900ms ease-out' }}
            />
          )}

          {hasBlocked && geometry.blockedPath && (
            <path
              d={geometry.blockedPath}
              fill="none"
              stroke={theme.palette.error.main}
              strokeWidth={1.8}
              strokeLinecap="round"
              strokeLinejoin="round"
              strokeDasharray="5 5"
            />
          )}

          {points.map((point, index) => (
            <circle
              key={point.date.toISOString()}
              cx={geometry.x(index)}
              cy={geometry.y(point.requests)}
              r={index === points.length - 1 ? 4 : 3}
              fill={theme.palette.primary.main}
              stroke={theme.palette.background.paper}
              strokeWidth={2}
            />
          ))}

          {points.map((point, index) => (
            <text
              key={`label-${point.date.toISOString()}`}
              x={geometry.x(index)}
              y={height - 8}
              textAnchor="middle"
              fontSize={11}
              fill={theme.palette.text.secondary}
            >
              {`${point.date.getUTCMonth() + 1}/${point.date.getUTCDate()}`}
            </text>
          ))}

          {hovered !== null && (
            <g pointerEvents="none">
              <line
                x1={geometry.x(hovered)}
                x2={geometry.x(hovered)}
                y1={PADDING.top}
                y2={geometry.baseline}
                stroke={theme.palette.divider}
                strokeDasharray="3 4"
              />
              <circle
                cx={geometry.x(hovered)}
                cy={geometry.y(points[hovered].requests)}
                r={5}
                fill={theme.palette.primary.main}
                stroke={theme.palette.background.paper}
                strokeWidth={2}
              />
              {points[hovered].blocked > 0 && (
                <circle
                  cx={geometry.x(hovered)}
                  cy={geometry.y(points[hovered].blocked)}
                  r={4}
                  fill={theme.palette.error.main}
                  stroke={theme.palette.background.paper}
                  strokeWidth={2}
                />
              )}
            </g>
          )}

          <rect
            x={PADDING.left}
            y={PADDING.top}
            width={geometry.plotWidth}
            height={geometry.plotHeight}
            fill="transparent"
            pointerEvents="all"
            style={{ cursor: 'crosshair', touchAction: 'pan-y' }}
            onPointerMove={handlePointer}
            onPointerDown={handlePointer}
            onPointerLeave={() => setHovered(null)}
          />
        </svg>
      )}

      {active && (
        <Box
          sx={{
            position: 'absolute',
            left: clamp(activeX, 86, Math.max(86, width - 86)),
            top: tooltipBelow ? activeY + 14 : activeY - 14,
            transform: tooltipBelow ? 'translate(-50%, 0)' : 'translate(-50%, -100%)',
            pointerEvents: 'none',
            minWidth: 132,
            maxWidth: 220,
            p: 1.25,
            borderRadius: 2,
            border: 1,
            borderColor: 'divider',
            bgcolor: 'background.paper',
            boxShadow: 3,
            zIndex: 3
          }}
        >
          <Typography variant="caption" fontWeight={700} display="block">
            {tooltipDate.format(active.date)}
          </Typography>
          <Stack spacing={0.4} mt={0.6}>
            <TooltipRow
              color="primary.main"
              label={t('dashboard.activityRequests')}
              value={format.number(active.requests)}
            />
            {active.blocked > 0 && (
              <TooltipRow
                color="error.main"
                label={t('dashboard.activityBlocked')}
                value={format.number(active.blocked)}
              />
            )}
          </Stack>
        </Box>
      )}

      <Box
        component="table"
        sx={{
          position: 'absolute',
          width: '1px',
          height: '1px',
          p: 0,
          m: '-1px',
          overflow: 'hidden',
          clip: 'rect(0 0 0 0)',
          whiteSpace: 'nowrap',
          border: 0
        }}
      >
        <caption>{ariaLabel}</caption>
        <thead>
          <tr>
            <th scope="col">{t('audit.time')}</th>
            <th scope="col">{t('dashboard.activityRequests')}</th>
            <th scope="col">{t('dashboard.activityBlocked')}</th>
          </tr>
        </thead>
        <tbody>
          {points.map((point) => (
            <tr key={`a11y-${point.date.toISOString()}`}>
              <th scope="row">{tooltipDate.format(point.date)}</th>
              <td>{point.requests}</td>
              <td>{point.blocked}</td>
            </tr>
          ))}
        </tbody>
      </Box>
    </Box>
  );
}

function TooltipRow({ color, label, value }: { color: string; label: string; value: string }) {
  return (
    <Stack direction="row" spacing={1} alignItems="center" justifyContent="space-between">
      <Stack direction="row" spacing={0.75} alignItems="center">
        <Box sx={{ width: 7, height: 7, borderRadius: '50%', bgcolor: color }} />
        <Typography variant="caption" color="text.secondary">{label}</Typography>
      </Stack>
      <Typography variant="caption" fontWeight={700} sx={{ fontVariantNumeric: 'tabular-nums' }}>{value}</Typography>
    </Stack>
  );
}
