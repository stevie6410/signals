import { Injectable, inject, signal, computed } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { firstValueFrom } from 'rxjs';

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

export interface TranslatedState {
  rawValue: string;
  friendlyName: string;
  icon?: string;
  color?: string;
  isActive: boolean;
}

@Injectable({ providedIn: 'root' })
export class CapabilityMappingService {
  private http = inject(HttpClient);
  private apiBase = '/api/settings/capability-mappings';

  // Cached mappings
  private _mappings = signal<CapabilityMapping[]>([]);
  private _loaded = signal(false);
  private _loading = signal(false);

  mappings = this._mappings.asReadonly();
  loaded = this._loaded.asReadonly();

  // Index by capability for fast lookup
  private mappingsByCapability = computed(() => {
    const map = new Map<string, CapabilityMapping>();
    for (const m of this._mappings()) {
      map.set(m.capability.toLowerCase(), m);
    }
    return map;
  });

  async ensureLoaded(): Promise<void> {
    if (this._loaded() || this._loading()) return;

    this._loading.set(true);
    try {
      const mappings = await firstValueFrom(this.http.get<CapabilityMapping[]>(this.apiBase));
      this._mappings.set(mappings || []);
      this._loaded.set(true);
    } catch (err) {
      console.error('Failed to load capability mappings:', err);
    } finally {
      this._loading.set(false);
    }
  }

  /**
   * Get the mapping for a capability
   */
  getMapping(capability: string): CapabilityMapping | undefined {
    return this.mappingsByCapability().get(capability.toLowerCase());
  }

  /**
   * Translate a raw value to a friendly state
   */
  translate(capability: string, rawValue: unknown): TranslatedState {
    const mapping = this.getMapping(capability);
    const rawString = String(rawValue);

    if (!mapping || !mapping.stateMappings || mapping.stateMappings.length === 0) {
      // No mapping found, return as-is
      return {
        rawValue: rawString,
        friendlyName: this.formatDefaultValue(capability, rawValue),
        isActive: rawValue === true || rawValue === 'true' || rawValue === 'ON'
      };
    }

    // Find matching state mapping
    const stateMapping = mapping.stateMappings.find(sm => {
      const smRaw = String(sm.rawValue).toLowerCase();
      const valRaw = String(rawValue).toLowerCase();
      return smRaw === valRaw;
    });

    if (stateMapping) {
      return {
        rawValue: rawString,
        friendlyName: stateMapping.friendlyName,
        icon: stateMapping.icon,
        color: stateMapping.color,
        isActive: stateMapping.isActive ?? false
      };
    }

    // No state mapping matched
    return {
      rawValue: rawString,
      friendlyName: this.formatDefaultValue(capability, rawValue),
      isActive: rawValue === true || rawValue === 'true' || rawValue === 'ON'
    };
  }

  /**
   * Get display info for a capability (icon, display name, unit)
   */
  getCapabilityDisplay(capability: string): { icon: string; displayName: string; unit?: string } {
    const mapping = this.getMapping(capability);
    if (mapping) {
      return {
        icon: mapping.icon || this.getDefaultIcon(capability),
        displayName: mapping.displayName || capability,
        unit: mapping.unit
      };
    }
    return {
      icon: this.getDefaultIcon(capability),
      displayName: this.formatCapabilityName(capability)
    };
  }

  private formatDefaultValue(capability: string, value: unknown): string {
    if (value === true) return 'Active';
    if (value === false) return 'Inactive';
    if (value === 'ON') return 'On';
    if (value === 'OFF') return 'Off';
    return String(value);
  }

  private formatCapabilityName(capability: string): string {
    return capability
      .replace(/_/g, ' ')
      .replace(/([A-Z])/g, ' $1')
      .replace(/^\w/, c => c.toUpperCase())
      .trim();
  }

  private getDefaultIcon(capability: string): string {
    const c = capability.toLowerCase();
    if (c.includes('temp')) return '🌡️';
    if (c.includes('humid')) return '💧';
    if (c.includes('occupancy') || c.includes('presence')) return '👤';
    if (c.includes('motion')) return '🏃';
    if (c.includes('contact') || c.includes('door') || c.includes('window')) return '🚪';
    if (c.includes('illuminance') || c.includes('lux') || c.includes('light')) return '☀️';
    if (c.includes('battery')) return '🔋';
    if (c.includes('power') || c.includes('energy')) return '⚡';
    if (c.includes('water') || c.includes('leak')) return '💧';
    if (c.includes('button') || c.includes('action')) return '🔘';
    if (c.includes('vibration')) return '📳';
    if (c.includes('linkquality') || c.includes('signal')) return '📶';
    return '📊';
  }
}
