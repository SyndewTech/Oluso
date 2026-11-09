import { Component, Input, Output, EventEmitter, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { IconComponent } from '@syndew/oluso-ui-core';
import {
  ClaimMapping,
  TransformMapping,
  SOURCE_TYPES,
  TransformCatalogEntry,
  JOURNEY_DATA_SUGGESTIONS,
  CLAIM_SUGGESTIONS,
  CONTEXT_SUGGESTIONS,
  USER_SUGGESTIONS,
} from '../../models/journey.model';

type MappingMode = 'transform' | 'output' | 'input';

@Component({
  selector: 'admin-mapping-builder',
  standalone: true,
  imports: [CommonModule, FormsModule, IconComponent],
  templateUrl: './mapping-builder.component.html',
})
export class MappingBuilderComponent {
  @Input() mode: MappingMode = 'transform';
  @Input() mappings: (ClaimMapping | TransformMapping)[] = [];
  @Input() title?: string;
  /** Transforms the step runs (its schema's x-transforms); transform mode only. */
  @Input() transforms: TransformCatalogEntry[] = [];
  /** Source types the step reads (its schema's sourceType enum); transform mode only. */
  @Input() transformSourceTypes: string[] = [];
  @Input() description?: string;

  @Output() mappingsChange = new EventEmitter<(ClaimMapping | TransformMapping)[]>();

  readonly expandedIndex = signal<number | null>(null);

  readonly sourceTypes = SOURCE_TYPES;

  /** Sources token claims can be mapped from at the end of a journey. */
  readonly outputSourceTypes = SOURCE_TYPES.filter((t) => ['journeyData', 'claim', 'context', 'literal'].includes(t.value));

  sourceTypeLabel(value: string): string {
    return SOURCE_TYPES.find((t) => t.value === value)?.label ?? value;
  }

  transformFor(name: string | undefined): TransformCatalogEntry | undefined {
    return this.transforms.find((t) => t.name === (name || 'copy'));
  }

  optionValue(mapping: TransformMapping, option: { name: string; default?: string | null }): string {
    return mapping.transformConfig?.[option.name] ?? option.default ?? '';
  }

  setOption(index: number, mapping: TransformMapping, name: string, value: string): void {
    this.updateMapping(index, { transformConfig: { ...(mapping.transformConfig ?? {}), [name]: value } });
  }

  /** Changing transform drops the old transform's options. */
  setTransform(index: number, name: string): void {
    this.updateMapping(index, { transform: name || undefined, transformConfig: undefined });
  }

  get effectiveTitle(): string {
    if (this.title) return this.title;
    switch (this.mode) {
      case 'output':
        return 'Output Claim Mappings';
      case 'input':
        return 'Input Mappings';
      default:
        return 'Transform Mappings';
    }
  }

  get effectiveDescription(): string {
    if (this.description) return this.description;
    switch (this.mode) {
      case 'output':
        return 'Map journey data or claims to output token claims';
      case 'input':
        return 'Map data to step input parameters';
      default:
        return 'Transform and map data between steps';
    }
  }

  addMapping(): void {
    let newMapping: ClaimMapping | TransformMapping;
    if (this.mode === 'transform') {
      newMapping = {
        source: '',
        sourceType: 'journeyData',
        target: '',
      } as TransformMapping;
    } else {
      newMapping = {
        sourceType: 'journeyData',
        sourcePath: '',
        targetClaimType: '',
      } as ClaimMapping;
    }
    this.mappings = [...this.mappings, newMapping];
    this.mappingsChange.emit(this.mappings);
    this.expandedIndex.set(this.mappings.length - 1);
  }

  updateMapping(index: number, updates: Partial<ClaimMapping> | Partial<TransformMapping>): void {
    this.mappings = this.mappings.map((m, i) =>
      i === index ? { ...m, ...updates } as ClaimMapping | TransformMapping : m
    );
    this.mappingsChange.emit(this.mappings);
  }

  removeMapping(index: number): void {
    this.mappings = this.mappings.filter((_, i) => i !== index);
    this.mappingsChange.emit(this.mappings);
    if (this.expandedIndex() === index) {
      this.expandedIndex.set(null);
    }
  }

  toggleExpanded(index: number): void {
    this.expandedIndex.set(this.expandedIndex() === index ? null : index);
  }

  getSuggestionsForType(sourceType: string): string[] {
    switch (sourceType) {
      case 'journeyData':
        return JOURNEY_DATA_SUGGESTIONS;
      case 'claim':
        return CLAIM_SUGGESTIONS;
      case 'context':
        return CONTEXT_SUGGESTIONS;
      case 'user':
        return USER_SUGGESTIONS;
      default:
        return [];
    }
  }

  getSourceType(mapping: ClaimMapping | TransformMapping): string {
    return 'sourceType' in mapping ? mapping.sourceType : 'journeyData';
  }

  getSourcePath(mapping: ClaimMapping | TransformMapping): string {
    if ('sourcePath' in mapping) {
      return mapping.sourcePath;
    }
    return (mapping as TransformMapping).source;
  }

  getTarget(mapping: ClaimMapping | TransformMapping): string {
    if ('targetClaimType' in mapping) {
      return mapping.targetClaimType;
    }
    return (mapping as TransformMapping).target;
  }

  getTransform(mapping: ClaimMapping | TransformMapping): string | undefined {
    if ('transform' in mapping) {
      return mapping.transform;
    }
    return undefined;
  }

  isTransformMapping(mapping: ClaimMapping | TransformMapping): mapping is TransformMapping {
    return this.mode === 'transform';
  }

  isClaimMapping(mapping: ClaimMapping | TransformMapping): mapping is ClaimMapping {
    return this.mode !== 'transform';
  }
}
