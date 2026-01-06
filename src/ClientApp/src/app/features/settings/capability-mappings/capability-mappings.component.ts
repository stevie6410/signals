import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

// Local interfaces until API client is regenerated
export interface StateMapping {
  rawValue: boolean | string | number;
  friendlyName: string;
  icon?: string;
  color?: string;
  isActive?: boolean;
}

export interface CapabilityMapping {
  id?: number;
  capability: string;
  property: string;
  displayName: string;
  deviceType?: string;
  icon?: string;
  unit?: string;
  stateMappings?: StateMapping[];
  displayOrder?: number;
  isSystemDefault?: boolean;
}

@Component({
  selector: 'app-capability-mappings',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './capability-mappings.component.html',
  styleUrl: './capability-mappings.component.scss'
})
export class CapabilityMappingsComponent implements OnInit {
  private http = inject(HttpClient);
  private apiBase = '/api/settings/capability-mappings';

  mappings = signal<CapabilityMapping[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);

  selectedMapping = signal<CapabilityMapping | null>(null);
  isEditing = signal(false);
  editForm = signal<Partial<CapabilityMapping>>({});
  editStateMappings = signal<StateMapping[]>([]);

  // Group mappings by type
  booleanMappings = computed(() =>
    this.mappings().filter(m => m.stateMappings && m.stateMappings.length > 0)
  );

  numericMappings = computed(() =>
    this.mappings().filter(m => !m.stateMappings || m.stateMappings.length === 0)
  );

  ngOnInit() {
    this.loadMappings();
  }

  async loadMappings() {
    this.loading.set(true);
    this.error.set(null);

    try {
      const mappings = await firstValueFrom(this.http.get<CapabilityMapping[]>(this.apiBase));
      this.mappings.set(mappings || []);
    } catch (err) {
      console.error('Failed to load capability mappings:', err);
      this.error.set('Failed to load capability mappings');
    } finally {
      this.loading.set(false);
    }
  }

  async seedDefaults() {
    try {
      const seeded = await firstValueFrom(this.http.post<CapabilityMapping[]>(`${this.apiBase}/seed-defaults`, {}));
      await this.loadMappings();
      alert(`Seeded ${seeded?.length || 0} default mappings`);
    } catch (err) {
      console.error('Failed to seed defaults:', err);
      alert('Failed to seed default mappings');
    }
  }

  selectMapping(mapping: CapabilityMapping) {
    this.selectedMapping.set(mapping);
    this.isEditing.set(false);
  }

  startEdit(mapping: CapabilityMapping) {
    this.selectedMapping.set(mapping);
    this.editForm.set({ ...mapping });
    this.editStateMappings.set([...(mapping.stateMappings || [])]);
    this.isEditing.set(true);
  }

  startCreate() {
    this.selectedMapping.set(null);
    this.editForm.set({
      capability: '',
      property: '',
      displayName: '',
      icon: '',
      unit: '',
      displayOrder: this.mappings().length * 10
    });
    this.editStateMappings.set([]);
    this.isEditing.set(true);
  }

  addStateMapping() {
    this.editStateMappings.update(mappings => [
      ...mappings,
      { rawValue: '', friendlyName: '', icon: '', color: '', isActive: false } as StateMapping
    ]);
  }

  removeStateMapping(index: number) {
    this.editStateMappings.update(mappings => mappings.filter((_, i) => i !== index));
  }

  updateStateMapping(index: number, field: keyof StateMapping, value: any) {
    this.editStateMappings.update(mappings => {
      const updated = [...mappings];
      updated[index] = { ...updated[index], [field]: value };
      return updated;
    });
  }

  async saveMapping() {
    const form = this.editForm();
    if (!form.capability || !form.property || !form.displayName) {
      alert('Please fill in capability, property, and display name');
      return;
    }

    const mapping: CapabilityMapping = {
      id: this.selectedMapping()?.id || 0,
      capability: form.capability!,
      property: form.property!,
      displayName: form.displayName!,
      deviceType: form.deviceType,
      icon: form.icon,
      unit: form.unit,
      stateMappings: this.editStateMappings(),
      displayOrder: form.displayOrder || 0,
      isSystemDefault: false
    } as CapabilityMapping;

    try {
      if (this.selectedMapping()?.id) {
        await firstValueFrom(this.http.put(`${this.apiBase}/${mapping.id}`, mapping));
      } else {
        await firstValueFrom(this.http.post(this.apiBase, mapping));
      }
      await this.loadMappings();
      this.isEditing.set(false);
      this.selectedMapping.set(null);
    } catch (err) {
      console.error('Failed to save mapping:', err);
      alert('Failed to save mapping');
    }
  }

  async deleteMapping(mapping: CapabilityMapping) {
    if (mapping.isSystemDefault) {
      alert('Cannot delete system default mappings');
      return;
    }

    if (!confirm(`Delete mapping for "${mapping.capability}"?`)) {
      return;
    }

    try {
      await firstValueFrom(this.http.delete(`${this.apiBase}/${mapping.id}`));
      await this.loadMappings();
      this.selectedMapping.set(null);
    } catch (err) {
      console.error('Failed to delete mapping:', err);
      alert('Failed to delete mapping');
    }
  }

  cancelEdit() {
    this.isEditing.set(false);
    this.editForm.set({});
    this.editStateMappings.set([]);
  }

  updateFormField(field: keyof CapabilityMapping, value: string) {
    this.editForm.update(form => ({ ...form, [field]: value }));
  }

  parseRawValue(value: string): any {
    if (value === 'true') return true;
    if (value === 'false') return false;
    const num = Number(value);
    if (!isNaN(num)) return num;
    return value;
  }

  formatRawValue(value: any): string {
    if (value === null || value === undefined) return '';
    return String(value);
  }
}
