import { TestBed } from '@angular/core/testing';
import { PromptDialogComponent } from './prompt-dialog.component';

describe('PromptDialogComponent', () => {
  function setup(inputs: Record<string, unknown> = {}) {
    const fixture = TestBed.createComponent(PromptDialogComponent);
    fixture.componentRef.setInput('open', true);
    fixture.componentRef.setInput('title', 'Room');
    for (const [key, value] of Object.entries(inputs)) fixture.componentRef.setInput(key, value);
    fixture.detectChanges();
    const emitted: string[] = [];
    let cancelled = 0;
    fixture.componentInstance.confirmed.subscribe((v) => emitted.push(v));
    fixture.componentInstance.cancelled.subscribe(() => cancelled++);
    return { fixture, component: fixture.componentInstance, emitted, cancelled: () => cancelled };
  }

  it('starts from the initial value and emits it trimmed', () => {
    const { component, emitted } = setup({ initialValue: '  Home  ' });
    component.onConfirm();
    expect(emitted).toEqual(['Home']);
  });

  it('holds back a required value until one is entered', () => {
    const { fixture, component, emitted } = setup({ required: true });
    component.onConfirm();
    fixture.detectChanges();
    expect(emitted).toEqual([]);
    expect(fixture.nativeElement.textContent).toContain('This field is required.');

    component.onInput('12B');
    component.onConfirm();
    expect(emitted).toEqual(['12B']);
  });

  it('applies the validate callback', () => {
    const { component, emitted } = setup({ validate: (v: string) => (v.startsWith('R') ? null : 'Rooms start with R') });
    component.onInput('12');
    component.onConfirm();
    expect(component.error()).toBe('Rooms start with R');
    expect(emitted).toEqual([]);
    component.onInput('R12');
    component.onConfirm();
    expect(emitted).toEqual(['R12']);
  });

  it('enforces maxLength', () => {
    const { component, emitted } = setup({ maxLength: 3 });
    component.onInput('abcd');
    component.onConfirm();
    expect(emitted).toEqual([]);
  });

  it('cancels, but not while loading', () => {
    const { fixture, component, cancelled } = setup();
    component.onCancel();
    expect(cancelled()).toBe(1);
    fixture.componentRef.setInput('loading', true);
    component.onCancel();
    expect(cancelled()).toBe(1);
  });

  it('resets to the initial value when reopened', () => {
    const { fixture, component, emitted } = setup({ initialValue: 'Home' });
    component.onInput('Ward 3');
    fixture.componentRef.setInput('open', false);
    fixture.detectChanges();
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    component.onConfirm();
    expect(emitted).toEqual(['Home']);
  });
});
