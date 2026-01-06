import { Component, OnInit, signal, computed, inject, Input, Output, EventEmitter } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { CdkDragDrop, CdkDrag, CdkDropList, CdkDragPlaceholder, CdkDragPreview, CdkDropListGroup } from '@angular/cdk/drag-drop';

interface Zone {
  id: number;
  name: string;
  description?: string;
  icon?: string;
  color?: string;
  parentZoneId?: number;
  childZones: Zone[];
}

interface Device {
  deviceId: string;
  friendlyName: string;
  displayName?: string;
  effectiveDisplayName?: string;
  manufacturer?: string;
  deviceType?: string | number;
  zoneId?: number;
  isAvailable: boolean;
  capabilities?: string[];
}

interface CapabilityType {
  id: string;
  label: string;
  icon: string;
  defaultProperty: string;
}

interface ZoneCapabilityAssignment {
  id: number;
  zoneId: number;
  capability: string;
  deviceId: string;
  property?: string;
  priority: number;
  displayName?: string;
  zoneName?: string;
  deviceName?: string;
}

@Component({
  selector: 'app-zone-capability-editor',
  standalone: true,
  imports: [CommonModule, FormsModule, CdkDropList, CdkDrag, CdkDragPlaceholder, CdkDragPreview, CdkDropListGroup],
  templateUrl: './zone-capability-editor.component.html',
  styleUrl: './zone-capability-editor.component.scss'
})
export class ZoneCapabilityEditorComponent implements OnInit {
  private http = inject(HttpClient);

  @Input() zoneId!: number;
  @Output() close = new EventEmitter<void>();
  @Output() saved = new EventEmitter<void>();

  zone = signal<Zone | null>(null);
  devices = signal<Device[]>([]);
  assignments = signal<ZoneCapabilityAssignment[]>([]);
  capabilityTypes = signal<CapabilityType[]>([]);

  loading = signal(false);
  saving = signal(false);

  // Currently selected capability for assignment
  selectedCapability = signal<string | null>(null);

  // Search filter for devices
  deviceSearch = signal('');

  // Dragging state
  draggingDevice = signal<Device | null>(null);

  // Available devices for assignment (in this zone or unassigned)
  availableDevices = computed(() => {
    const search = this.deviceSearch().toLowerCase();
    return this.devices()
      .filter(d => d.zoneId === this.zoneId || !d.zoneId)
      .filter(d => !search ||
        d.friendlyName.toLowerCase().includes(search) ||
        d.displayName?.toLowerCase().includes(search) ||
        d.effectiveDisplayName?.toLowerCase().includes(search)
      );
  });

  // Get assigned device for a capability
  getAssignedDevice(capability: string): Device | null {
    const assignment = this.assignments().find(a => a.capability === capability);
    if (!assignment) return null;
    return this.devices().find(d => d.deviceId === assignment.deviceId) || null;
  }

  // Get assignment for a capability
  getAssignment(capability: string): ZoneCapabilityAssignment | null {
    return this.assignments().find(a => a.capability === capability) || null;
  }

  // Check if device is assigned to any capability
  isDeviceAssigned(deviceId: string): boolean {
    return this.assignments().some(a => a.deviceId === deviceId);
  }

  // Get capabilities that a device is assigned to
  getDeviceCapabilities(deviceId: string): string[] {
    return this.assignments()
      .filter(a => a.deviceId === deviceId)
      .map(a => a.capability);
  }

  // Device type icons
  getDeviceIcon(deviceType?: string | number): string {
    const typeMap: Record<number, string> = {
      0: 'light', 1: 'switch', 2: 'sensor', 3: 'climate',
      4: 'lock', 5: 'cover', 6: 'fan', 7: 'other'
    };
    const typeStr = typeof deviceType === 'number'
      ? typeMap[deviceType]
      : deviceType?.toString().toLowerCase();
    switch (typeStr) {
      case 'light': return '💡';
      case 'switch': return '🔘';
      case 'sensor': return '📡';
      case 'climate': return '🌡️';
      case 'lock': return '🔒';
      case 'cover': return '🪟';
      case 'fan': return '🌀';
      default: return '📦';
    }
  }

  ngOnInit() {
    this.loadData();
  }

  async loadData() {
    this.loading.set(true);
    try {
      // Load zone, devices, and existing assignments
      const [zone, devices, assignments] = await Promise.all([
        this.http.get<Zone>(`/api/zones/${this.zoneId}`).toPromise().catch(() => null),
        this.http.get<Device[]>('/api/devices').toPromise().catch(() => []),
        this.http.get<ZoneCapabilityAssignment[]>(`/api/zones/${this.zoneId}/capabilities`).toPromise().catch(() => [])
      ]);

      this.zone.set(zone || null);
      this.devices.set(devices || []);
      this.assignments.set(assignments || []);

      // Try to load capability types, with fallback defaults
      try {
        const types = await this.http.get<{ capabilities: string[], labels: Record<string, string>, icons: Record<string, string>, defaultProperties: Record<string, string> }>('/api/zones/capabilities/types').toPromise();

        if (types?.capabilities) {
          this.capabilityTypes.set(types.capabilities.map(cap => ({
            id: cap,
            label: types.labels?.[cap] || cap,
            icon: types.icons?.[cap] || '❓',
            defaultProperty: types.defaultProperties?.[cap] || 'state'
          })));
        } else {
          this.setDefaultCapabilityTypes();
        }
      } catch {
        this.setDefaultCapabilityTypes();
      }
    } catch (error) {
      console.error('Error loading capability data:', error);
      this.setDefaultCapabilityTypes();
    } finally {
      this.loading.set(false);
    }
  }

  private setDefaultCapabilityTypes() {
    // Fallback capability types if API fails
    this.capabilityTypes.set([
      { id: 'motion', label: 'Motion', icon: '🚶', defaultProperty: 'occupancy' },
      { id: 'presence', label: 'Presence', icon: '👤', defaultProperty: 'presence' },
      { id: 'temperature', label: 'Temperature', icon: '🌡️', defaultProperty: 'temperature' },
      { id: 'humidity', label: 'Humidity', icon: '💧', defaultProperty: 'humidity' },
      { id: 'illuminance', label: 'Illuminance', icon: '☀️', defaultProperty: 'illuminance' },
      { id: 'contact', label: 'Contact', icon: '🚪', defaultProperty: 'contact' },
      { id: 'lights', label: 'Lights', icon: '💡', defaultProperty: 'state' },
      { id: 'climate', label: 'Climate', icon: '❄️', defaultProperty: 'state' },
      { id: 'media', label: 'Media', icon: '📺', defaultProperty: 'state' },
      { id: 'switch', label: 'Switch', icon: '🔌', defaultProperty: 'state' },
    ]);
  }

  onDragStarted(device: Device) {
    this.draggingDevice.set(device);
  }

  onDragEnded() {
    this.draggingDevice.set(null);
  }

  async onDeviceDroppedOnCapability(event: CdkDragDrop<any>, capability: string) {
    const device = event.item.data as Device;
    await this.assignDevice(device.deviceId, capability);
  }

  async assignDevice(deviceId: string, capability: string) {
    this.saving.set(true);
    try {
      const capType = this.capabilityTypes().find(c => c.id === capability);
      await this.http.post(`/api/zones/${this.zoneId}/capabilities`, {
        capability,
        deviceId,
        property: capType?.defaultProperty,
        priority: 0
      }).toPromise();

      // Reload assignments
      const assignments = await this.http.get<ZoneCapabilityAssignment[]>(`/api/zones/${this.zoneId}/capabilities`).toPromise();
      this.assignments.set(assignments || []);
      this.saved.emit();
    } catch (error) {
      console.error('Error assigning device:', error);
    } finally {
      this.saving.set(false);
    }
  }

  async removeAssignment(capability: string) {
    this.saving.set(true);
    try {
      await this.http.delete(`/api/zones/${this.zoneId}/capabilities/${capability}`).toPromise();

      // Reload assignments
      const assignments = await this.http.get<ZoneCapabilityAssignment[]>(`/api/zones/${this.zoneId}/capabilities`).toPromise();
      this.assignments.set(assignments || []);
      this.saved.emit();
    } catch (error) {
      console.error('Error removing assignment:', error);
    } finally {
      this.saving.set(false);
    }
  }

  selectCapability(capability: string) {
    if (this.selectedCapability() === capability) {
      this.selectedCapability.set(null);
    } else {
      this.selectedCapability.set(capability);
    }
  }

  closeEditor() {
    this.close.emit();
  }
}
