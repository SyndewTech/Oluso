import { TestBed } from '@angular/core/testing';
import { DynamicFormComponent } from './dynamic-form.component';
import type { DynamicFieldConfig, DynamicFormConfig } from './dynamic-form.types';

const fields: DynamicFieldConfig[] = [
  { id: 'brakes', name: 'brakes', label: 'Brakes', type: 'passfail', critical: true },
  { id: 'leak', name: 'leak', label: 'Any leaks?', type: 'yesno', passingValue: 'no' },
  { id: 'notes', name: 'notes', label: 'Comments', type: 'text' },
  { id: 'lights', name: 'lights', label: 'Lights', type: 'passfail' },
];

function create(readonly = false) {
  const fixture = TestBed.createComponent(DynamicFormComponent);
  fixture.componentRef.setInput('config', { fields } as DynamicFormConfig);
  fixture.componentRef.setInput('initialValues', [
    { fieldId: 'brakes', value: 'fail' },
    { fieldId: 'leak', value: 'no' },
    { fieldId: 'notes', value: 'Tyres worn' },
    { fieldId: 'lights', value: 'pass', outcome: 'fail' },
  ]);
  fixture.componentRef.setInput('readonly', readonly);
  fixture.detectChanges();
  return fixture.componentInstance;
}

describe('DynamicFormComponent outcomes', () => {
  it('colours each answer by its result, preferring the verdict it was given', () => {
    const form = create();
    expect(form.fieldOutcome(fields[0])).toBe('fail');
    expect(form.fieldOutcome(fields[1])).toBe('pass');
    expect(form.fieldOutcome(fields[2])).toBe('neutral');
    expect(form.fieldOutcome(fields[3])).toBe('fail');
    expect(form.outcomeStripe(fields[0])).toContain('border-l-red-500');
    expect(form.outcomeStripe(fields[3])).toContain('border-l-amber-400');
    expect(form.outcomeStripe(fields[1])).toContain('border-l-green-500');
  });

  it('ignores changes while read-only', () => {
    const form = create(true);
    let emitted = 0;
    form.formValueChange.subscribe(() => emitted++);
    form.onFieldValueChange({ fieldId: 'brakes', value: 'pass' });
    expect(form.fieldOutcome(fields[0])).toBe('fail');
    expect(emitted).toBe(0);
  });
});
