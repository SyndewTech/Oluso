/** Default look for rendered journeys. Override the custom properties on `.oluso-journey`, or pass `unstyled`. */
export const JOURNEY_STYLES = `
.oluso-journey {
  --oluso-accent: #4f46e5;
  --oluso-accent-contrast: #ffffff;
  --oluso-fg: #111827;
  --oluso-muted: #6b7280;
  --oluso-bg: #ffffff;
  --oluso-field-bg: #ffffff;
  --oluso-border: #d1d5db;
  --oluso-danger: #b91c1c;
  --oluso-danger-bg: #fef2f2;
  --oluso-notice-bg: #eff6ff;
  --oluso-notice: #1d4ed8;
  --oluso-radius: 8px;
  --oluso-gap: 14px;
  --oluso-font: inherit;
  font-family: var(--oluso-font);
  color: var(--oluso-fg);
  font-size: 15px;
  line-height: 1.5;
}
.oluso-journey[data-theme="dark"] {
  --oluso-fg: #f3f4f6;
  --oluso-muted: #9ca3af;
  --oluso-bg: #111827;
  --oluso-field-bg: #1f2937;
  --oluso-border: #374151;
  --oluso-danger: #fca5a5;
  --oluso-danger-bg: rgba(185, 28, 28, 0.15);
  --oluso-notice-bg: rgba(59, 130, 246, 0.15);
  --oluso-notice: #93c5fd;
}
@media (prefers-color-scheme: dark) {
  .oluso-journey[data-theme="auto"] {
    --oluso-fg: #f3f4f6;
    --oluso-muted: #9ca3af;
    --oluso-bg: #111827;
    --oluso-field-bg: #1f2937;
    --oluso-border: #374151;
    --oluso-danger: #fca5a5;
    --oluso-danger-bg: rgba(185, 28, 28, 0.15);
    --oluso-notice-bg: rgba(59, 130, 246, 0.15);
    --oluso-notice: #93c5fd;
  }
}
.oluso-journey *, .oluso-journey *::before, .oluso-journey *::after { box-sizing: border-box; }
.oluso-journey__form { display: flex; flex-direction: column; gap: var(--oluso-gap); margin: 0; }
.oluso-journey__title { font-size: 1.25em; font-weight: 600; margin: 0; }
.oluso-journey__description { color: var(--oluso-muted); margin: 0; white-space: pre-line; }
.oluso-journey__alert { border-radius: var(--oluso-radius); padding: 10px 12px; margin: 0; }
.oluso-journey__alert--error { background: var(--oluso-danger-bg); color: var(--oluso-danger); }
.oluso-journey__alert--notice { background: var(--oluso-notice-bg); color: var(--oluso-notice); }
.oluso-journey__group { display: flex; flex-direction: column; gap: var(--oluso-gap); border: 0; margin: 0; padding: 0; }
.oluso-journey__field { display: flex; flex-direction: column; gap: 6px; }
.oluso-journey__field[hidden] { display: none; }
.oluso-journey__label { font-weight: 500; }
.oluso-journey__required { color: var(--oluso-danger); margin-left: 2px; }
.oluso-journey__input, .oluso-journey__select, .oluso-journey__textarea {
  width: 100%; font: inherit; color: var(--oluso-fg); background: var(--oluso-field-bg);
  border: 1px solid var(--oluso-border); border-radius: var(--oluso-radius); padding: 9px 11px; min-height: 40px;
}
.oluso-journey__input:focus, .oluso-journey__select:focus, .oluso-journey__textarea:focus {
  outline: 2px solid var(--oluso-accent); outline-offset: 1px; border-color: transparent;
}
.oluso-journey__input[aria-invalid="true"], .oluso-journey__select[aria-invalid="true"], .oluso-journey__textarea[aria-invalid="true"] {
  border-color: var(--oluso-danger);
}
.oluso-journey__input--otp { letter-spacing: 0.4em; font-variant-numeric: tabular-nums; text-align: center; font-size: 1.2em; }
.oluso-journey__phone { display: flex; gap: 8px; }
.oluso-journey__phone .oluso-journey__select { width: auto; max-width: 45%; }
.oluso-journey__choices { display: flex; flex-direction: column; gap: 8px; }
.oluso-journey__choice { display: flex; gap: 10px; align-items: flex-start; cursor: pointer; }
.oluso-journey__choice input { margin-top: 3px; accent-color: var(--oluso-accent); }
.oluso-journey__choice-description { display: block; color: var(--oluso-muted); font-size: 0.9em; }
.oluso-journey__help, .oluso-journey__hint { color: var(--oluso-muted); font-size: 0.875em; margin: 0; }
.oluso-journey__error { color: var(--oluso-danger); font-size: 0.875em; margin: 0; }
.oluso-journey__content { margin: 0; white-space: pre-line; }
.oluso-journey__actions { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; }
.oluso-journey__button {
  font: inherit; font-weight: 500; border-radius: var(--oluso-radius); padding: 9px 16px; min-height: 40px; cursor: pointer;
  border: 1px solid var(--oluso-border); background: var(--oluso-field-bg); color: var(--oluso-fg);
}
.oluso-journey__button--primary { background: var(--oluso-accent); color: var(--oluso-accent-contrast); border-color: var(--oluso-accent); }
.oluso-journey__button--primary:only-child, .oluso-journey__actions--stacked .oluso-journey__button--primary { flex: 1 1 auto; }
.oluso-journey__button--link { background: none; border: 0; color: var(--oluso-accent); padding: 4px 0; min-height: 0; }
.oluso-journey__button--cancel { background: none; }
.oluso-journey__button:disabled { opacity: 0.6; cursor: progress; }
.oluso-journey__enter { position: absolute; left: -10000px; width: 1px; height: 1px; overflow: hidden; }
.oluso-journey__action-line { display: flex; flex-wrap: wrap; gap: 6px; align-items: baseline; margin: 0; }
.oluso-journey__links { display: flex; flex-wrap: wrap; gap: 12px; font-size: 0.9em; }
.oluso-journey__links a { color: var(--oluso-accent); }
.oluso-journey__qr { display: flex; flex-direction: column; align-items: center; gap: 8px; }
.oluso-journey__qr img { width: 180px; height: 180px; background: #fff; padding: 8px; border-radius: var(--oluso-radius); }
.oluso-journey__code { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; word-break: break-all; }
.oluso-journey__codes { display: grid; grid-template-columns: repeat(2, minmax(0, 1fr)); gap: 6px; margin: 0; padding: 0; list-style: none; }
.oluso-journey__codes li { font-family: ui-monospace, SFMono-Regular, Menlo, monospace; padding: 6px 8px; border: 1px solid var(--oluso-border); border-radius: 6px; text-align: center; }
.oluso-journey__status { color: var(--oluso-muted); text-align: center; padding: 16px 0; }
.oluso-journey__done { text-align: center; padding: 8px 0; }
`;

let injected = false;

export function injectJourneyStyles(): void {
  if (injected || typeof document === 'undefined') return;
  const style = document.createElement('style');
  style.setAttribute('data-oluso-journey', '');
  style.textContent = JOURNEY_STYLES;
  document.head.appendChild(style);
  injected = true;
}
