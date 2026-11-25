import type { SVGProps } from 'react';

type IconProps = SVGProps<SVGSVGElement>;

export function CalendarIcon(props: IconProps) {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true" {...props}>
      <rect x="2" y="3" width="12" height="11" rx="2" stroke="currentColor" strokeWidth="1.4" />
      <path d="M5 1.75V4M11 1.75V4M2 6.25H14" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
    </svg>
  );
}

export function ChevronLeftIcon(props: IconProps) {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true" {...props}>
      <path d="M9.75 3.75L5.5 8L9.75 12.25" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

export function ChevronRightIcon(props: IconProps) {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true" {...props}>
      <path d="M6.25 3.75L10.5 8L6.25 12.25" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

export function SearchIcon(props: IconProps) {
  return (
    <svg width="15" height="15" viewBox="0 0 16 16" fill="none" aria-hidden="true" {...props}>
      <circle cx="7.25" cy="7.25" r="4.75" stroke="currentColor" strokeWidth="1.4" />
      <path d="M10.75 10.75L14 14" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
    </svg>
  );
}

export function SunIcon(props: IconProps) {
  return (
    <svg width="15" height="15" viewBox="0 0 16 16" fill="none" aria-hidden="true" {...props}>
      <circle cx="8" cy="8" r="3" stroke="currentColor" strokeWidth="1.4" />
      <path d="M8 1.75V3M8 13V14.25M1.75 8H3M13 8H14.25M3.6 3.6L4.5 4.5M11.5 11.5L12.4 12.4M12.4 3.6L11.5 4.5M4.5 11.5L3.6 12.4" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
    </svg>
  );
}

export function MoonIcon(props: IconProps) {
  return (
    <svg width="15" height="15" viewBox="0 0 16 16" fill="none" aria-hidden="true" {...props}>
      <path d="M13.2 9.95A5.5 5.5 0 0 1 6.05 2.8a5.5 5.5 0 1 0 7.15 7.15Z" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

export function SystemThemeIcon(props: IconProps) {
  return (
    <svg width="15" height="15" viewBox="0 0 16 16" fill="none" aria-hidden="true" {...props}>
      <circle cx="8" cy="8" r="5.5" stroke="currentColor" strokeWidth="1.4" />
      <path d="M8 2.5A5.5 5.5 0 0 1 8 13.5V2.5Z" fill="currentColor" />
    </svg>
  );
}

export function RefreshIcon(props: IconProps) {
  return (
    <svg width="14" height="14" viewBox="0 0 16 16" fill="none" aria-hidden="true" {...props}>
      <path d="M13.25 8A5.25 5.25 0 1 1 11.7 4.25M13.25 2.75V5.5H10.5" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

export function PlusIcon(props: IconProps) {
  return (
    <svg width="14" height="14" viewBox="0 0 16 16" fill="none" aria-hidden="true" {...props}>
      <path d="M8 3.25V12.75M3.25 8H12.75" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  );
}

export function CloseIcon(props: IconProps) {
  return (
    <svg width="15" height="15" viewBox="0 0 16 16" fill="none" aria-hidden="true" {...props}>
      <path d="M4 4L12 12M12 4L4 12" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" />
    </svg>
  );
}

export function ArrowRightIcon(props: IconProps) {
  return (
    <svg width="15" height="15" viewBox="0 0 16 16" fill="none" aria-hidden="true" {...props}>
      <path d="M3.25 8H12.75M9 4.25L12.75 8L9 11.75" stroke="currentColor" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

export function StreamIcon(props: IconProps) {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true" {...props}>
      <path d="M2.5 4.25H13.5M2.5 8H10.5M2.5 11.75H12" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
    </svg>
  );
}

export function VaultIcon(props: IconProps) {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true" {...props}>
      <path d="M3.25 2.75H10.25L12.75 5.25V13.25H3.25V2.75Z" stroke="currentColor" strokeWidth="1.4" strokeLinejoin="round" />
      <path d="M9.75 2.75V5.75H12.75" stroke="currentColor" strokeWidth="1.4" strokeLinejoin="round" />
    </svg>
  );
}

export function AgentIcon(props: IconProps) {
  return (
    <svg width="16" height="16" viewBox="0 0 16 16" fill="none" aria-hidden="true" {...props}>
      <rect x="2.75" y="2.75" width="10.5" height="10.5" rx="2.5" stroke="currentColor" strokeWidth="1.4" />
      <circle cx="6.25" cy="7.5" r="1" fill="currentColor" />
      <circle cx="9.75" cy="7.5" r="1" fill="currentColor" />
      <path d="M6 10.5H10" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" />
    </svg>
  );
}

export function ExpandIcon(props: IconProps) {
  return (
    <svg width="14" height="14" viewBox="0 0 16 16" fill="none" aria-hidden="true" {...props}>
      <path d="M9.75 2.75H13.25V6.25M6.25 13.25H2.75V9.75M13.25 2.75L9 7M2.75 13.25L7 9" stroke="currentColor" strokeWidth="1.4" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}
