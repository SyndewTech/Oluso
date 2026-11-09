import { Component, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '@syndew/oluso-ui-core';
import {
  StepCondition,
  CONDITION_OPERATORS,
  NO_VALUE_OPERATORS,
  JOURNEY_DATA_SUGGESTIONS,
  CLAIM_SUGGESTIONS,
  CONTEXT_SUGGESTIONS,
  USER_SUGGESTIONS,
} from '../../models/journey.model';

@Component({
  selector: 'admin-condition-builder',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  templateUrl: './condition-builder.component.html',
})
export class ConditionBuilderComponent {
  @Input() conditions: StepCondition[] = [];
  @Input() title = 'Step Conditions';
  @Input() description?: string;

  @Output() conditionsChange = new EventEmitter<StepCondition[]>();

  readonly typeOptions = [
    { value: 'journeyData', label: 'Journey Data' },
    { value: 'claim', label: 'Claim' },
    { value: 'context', label: 'Context' },
    { value: 'user', label: 'User Profile' },
    { value: 'path', label: 'Custom Path' },
  ];

  readonly operatorOptions = CONDITION_OPERATORS;
  readonly noValueOperators = NO_VALUE_OPERATORS;

  addCondition(): void {
    this.conditions = [
      ...this.conditions,
      {
        type: 'journeyData',
        field: '',
        operator: 'eq',
        value: '',
        logicalOperator: 'and',
        negate: false,
      },
    ];
    this.conditionsChange.emit(this.conditions);
  }

  updateCondition(index: number, updates: Partial<StepCondition>): void {
    this.conditions = this.conditions.map((c, i) =>
      i === index ? { ...c, ...updates } : c
    );
    this.conditionsChange.emit(this.conditions);
  }

  removeCondition(index: number): void {
    this.conditions = this.conditions.filter((_, i) => i !== index);
    this.conditionsChange.emit(this.conditions);
  }

  getFieldSuggestions(type: string): string[] {
    switch (type) {
      case 'context':
        return CONTEXT_SUGGESTIONS;
      case 'journeyData':
        return JOURNEY_DATA_SUGGESTIONS;
      case 'claim':
        return CLAIM_SUGGESTIONS;
      case 'user':
        return USER_SUGGESTIONS;
      default:
        return [];
    }
  }

  requiresValue(operator: string): boolean {
    return !this.noValueOperators.includes(operator);
  }

  getPlaceholder(operator: string): string {
    if (operator === 'in' || operator === 'not_in') {
      return 'val1, val2, val3';
    }
    return 'Value';
  }
}
