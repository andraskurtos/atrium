import { useEffect, useId, useState, type KeyboardEvent } from 'react';

interface TimeInputProps {
  id?: string;
  value: string;
  onChange: (nextValue: string) => void;
  timeFormat?: '24h' | '12h';
  className?: string;
  required?: boolean;
  ariaLabel?: string;
}

const SLOTS_24H: string[] = Array.from({ length: 48 }, (_, i) => {
  const h = String(Math.floor(i / 2)).padStart(2, '0');
  const m = i % 2 === 0 ? '00' : '30';
  return `${h}:${m}`;
});

function pad2(n: number): string {
  return String(n).padStart(2, '0');
}

function parse24h(raw: string): { h: number; m: number } | null {
  const cleaned = raw.trim();
  if (!cleaned) return null;

  const colonMatch = cleaned.match(/^(\d{1,2}):(\d{1,2})$/);
  if (colonMatch) {
    const h = Number.parseInt(colonMatch[1], 10);
    const m = Number.parseInt(colonMatch[2], 10);
    if (h >= 0 && h <= 23 && m >= 0 && m <= 59) return { h, m };
    return null;
  }

  const digitsOnly = cleaned.match(/^(\d{1,4})$/);
  if (digitsOnly) {
    const d = digitsOnly[1];
    if (d.length <= 2) {
      const h = Number.parseInt(d, 10);
      if (h >= 0 && h <= 23) return { h, m: 0 };
      return null;
    }
    const h = Number.parseInt(d.slice(0, -2), 10);
    const m = Number.parseInt(d.slice(-2), 10);
    if (h >= 0 && h <= 23 && m >= 0 && m <= 59) return { h, m };
  }

  return null;
}

function parse12h(
  raw: string,
  period: 'AM' | 'PM',
): { h: number; m: number } | null {
  const cleaned = raw.trim();
  if (!cleaned) return null;

  let h12: number;
  let m: number;

  const colonMatch = cleaned.match(/^(\d{1,2}):(\d{1,2})$/);
  if (colonMatch) {
    h12 = Number.parseInt(colonMatch[1], 10);
    m = Number.parseInt(colonMatch[2], 10);
  } else {
    const digitsOnly = cleaned.match(/^(\d{1,4})$/);
    if (!digitsOnly) return null;
    const d = digitsOnly[1];
    if (d.length <= 2) {
      h12 = Number.parseInt(d, 10);
      m = 0;
    } else {
      h12 = Number.parseInt(d.slice(0, -2), 10);
      m = Number.parseInt(d.slice(-2), 10);
    }
  }

  if (h12 < 1 || h12 > 12 || m < 0 || m > 59) return null;
  const h24 =
    period === 'AM' ? (h12 === 12 ? 0 : h12) : h12 === 12 ? 12 : h12 + 12;
  return { h: h24, m };
}

function formatForDisplay(
  hhmm: string,
  timeFormat: '24h' | '12h',
): { text: string; period: 'AM' | 'PM' } {
  const parsed = parse24h(hhmm) ?? { h: 9, m: 0 };
  if (timeFormat === '24h') {
    return { text: `${pad2(parsed.h)}:${pad2(parsed.m)}`, period: 'AM' };
  }
  const period: 'AM' | 'PM' = parsed.h >= 12 ? 'PM' : 'AM';
  const h12 = parsed.h % 12 || 12;
  return { text: `${h12}:${pad2(parsed.m)}`, period };
}

function stepTime(hhmm: string, deltaMinutes: number): string {
  const parsed = parse24h(hhmm) ?? { h: 9, m: 0 };
  const total =
    ((parsed.h * 60 + parsed.m + deltaMinutes) % (24 * 60) + 24 * 60) %
    (24 * 60);
  return `${pad2(Math.floor(total / 60))}:${pad2(total % 60)}`;
}

export function TimeInput({
  id,
  value,
  onChange,
  timeFormat = '24h',
  className = 'input mono-num',
  required = false,
  ariaLabel,
}: TimeInputProps) {
  const listId = useId();
  const initial = formatForDisplay(value, timeFormat);
  const [draft, setDraft] = useState(initial.text);
  const [period, setPeriod] = useState<'AM' | 'PM'>(initial.period);

  useEffect(() => {
    const next = formatForDisplay(value, timeFormat);
    setDraft(next.text);
    setPeriod(next.period);
  }, [timeFormat, value]);

  const commitText = (text: string, activePeriod: 'AM' | 'PM') => {
    const parsed =
      timeFormat === '24h' ? parse24h(text) : parse12h(text, activePeriod);
    if (!parsed) {
      const reset = formatForDisplay(value, timeFormat);
      setDraft(reset.text);
      setPeriod(reset.period);
      return;
    }
    const canonical = `${pad2(parsed.h)}:${pad2(parsed.m)}`;
    const formatted = formatForDisplay(canonical, timeFormat);
    setDraft(formatted.text);
    setPeriod(formatted.period);
    if (canonical !== value) {
      onChange(canonical);
    }
  };

  const handleChange = (nextText: string) => {
    setDraft(nextText);
    const parsed =
      timeFormat === '24h' ? parse24h(nextText) : parse12h(nextText, period);
    if (parsed && nextText.includes(':')) {
      const canonical = `${pad2(parsed.h)}:${pad2(parsed.m)}`;
      if (canonical !== value) {
        onChange(canonical);
      }
    }
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'ArrowUp') {
      e.preventDefault();
      onChange(stepTime(value, 15));
    } else if (e.key === 'ArrowDown') {
      e.preventDefault();
      onChange(stepTime(value, -15));
    }
  };

  const togglePeriod = () => {
    const nextPeriod: 'AM' | 'PM' = period === 'AM' ? 'PM' : 'AM';
    setPeriod(nextPeriod);
    const parsed = parse24h(value);
    if (parsed) {
      const h12 = parsed.h % 12;
      const nextH = nextPeriod === 'AM' ? h12 : h12 + 12;
      onChange(`${pad2(nextH)}:${pad2(parsed.m)}`);
    } else {
      commitText(draft, nextPeriod);
    }
  };

  if (timeFormat === '24h') {
    return (
      <>
        <input
          id={id}
          type="text"
          inputMode="numeric"
          list={listId}
          className={`${className} atrium-time-field`}
          placeholder="HH:mm"
          maxLength={5}
          required={required}
          aria-label={ariaLabel}
          value={draft}
          onChange={(e) => handleChange(e.target.value)}
          onBlur={() => commitText(draft, period)}
          onKeyDown={handleKeyDown}
        />
        <datalist id={listId}>
          {SLOTS_24H.map((slot) => (
            <option key={slot} value={slot} />
          ))}
        </datalist>
      </>
    );
  }

  return (
    <div className="atrium-time-12h">
      <input
        id={id}
        type="text"
        inputMode="numeric"
        className={`${className} atrium-time-field`}
        placeholder="h:mm"
        maxLength={5}
        required={required}
        aria-label={ariaLabel}
        value={draft}
        onChange={(e) => handleChange(e.target.value)}
        onBlur={() => commitText(draft, period)}
        onKeyDown={handleKeyDown}
      />
      <button
        type="button"
        className="btn btn-secondary btn-xs atrium-time-period-btn mono-num"
        onClick={togglePeriod}
        aria-label={`Switch to ${period === 'AM' ? 'PM' : 'AM'}`}
      >
        {period}
      </button>
    </div>
  );
}

