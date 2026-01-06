import { Component, OnInit, signal, computed, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router } from '@angular/router';
import { HttpClient } from '@angular/common/http';

interface Zone {
  id: number;
  name: string;
  icon?: string;
  color?: string;
}

interface Device {
  deviceId: string;
  friendlyName: string;
  displayName?: string;
  manufacturer?: string;
  modelId?: string;
  imageUrl?: string;
  deviceType?: number;
  zoneId?: number;
}

interface ZoneCapability {
  zoneId: number;
  capability: string;
  deviceId: string;
}

interface CapabilityDefinition {
  type: string;
  label: string;
  icon: string;
}

@Component({
  selector: 'app-zone-capabilities-page',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './zone-capabilities-page.component.html',
  styleUrl: './zone-capabilities-page.component.scss'
})
export class ZoneCapabilitiesPageComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private http = inject(HttpClient);

  // State
  zones = signal<Zone[]>([]);
  devices = signal<Device[]>([]);
  capabilities = signal<ZoneCapability[]>([]);
  currentZoneId = signal<number>(0);
  loading = signal(false);
  saving = signal(false);
  searchQuery = signal('');

  // Native drag state
  draggedDevice = signal<Device | null>(null);
  dragOverCapability = signal<string | null>(null);

  // Capability definitions
  readonly capabilityTypes: CapabilityDefinition[] = [
    { type: 'motion', label: 'Motion', icon: '🚶' },
    { type: 'presence', label: 'Presence', icon: '👤' },
    { type: 'temperature', label: 'Temperature', icon: '🌡️' },
    { type: 'humidity', label: 'Humidity', icon: '💧' },
    { type: 'illuminance', label: 'Illuminance', icon: '☀️' },
    { type: 'contact', label: 'Contact', icon: '🚪' },
    { type: 'lights', label: 'Lights', icon: '💡' },
    { type: 'climate', label: 'Climate', icon: '❄️' },
    { type: 'media', label: 'Media', icon: '📺' },
    { type: 'switch', label: 'Switch', icon: '🔘' },
  ];

  // Current zone
  currentZone = computed(() => {
    return this.zones().find(z => z.id === this.currentZoneId()) || null;
  });

  // Devices in current zone (filtered by search)
  zoneDevices = computed(() => {
    const query = this.searchQuery().toLowerCase();
    return this.devices()
      .filter(d => d.zoneId === this.currentZoneId())
      .filter(d => !query ||
        d.friendlyName.toLowerCase().includes(query) ||
        d.manufacturer?.toLowerCase().includes(query)
      );
  });

  // Get assigned device for a capability
  getAssignedDevice(capType: string): Device | null {
    const cap = this.capabilities().find(
      c => c.zoneId === this.currentZoneId() && c.capability === capType
    );
    if (!cap) return null;
    return this.devices().find(d => d.deviceId === cap.deviceId) || null;
  }

  ngOnInit() {
    // Get zone ID from route
    this.route.paramMap.subscribe(params => {
      const zoneId = params.get('zoneId');
      if (zoneId) {
        this.currentZoneId.set(parseInt(zoneId, 10));
        this.loadData();
      }
    });
  }

  async loadData() {
    this.loading.set(true);
    try {
      const [zones, devices, capabilities] = await Promise.all([
        this.http.get<Zone[]>('/api/zones').toPromise(),
        this.http.get<Device[]>('/api/devices').toPromise(),
        this.http.get<ZoneCapability[]>('/api/zones/capabilities/all').toPromise()
      ]);
      this.zones.set(zones || []);
      this.devices.set(devices || []);
      this.capabilities.set(capabilities || []);
    } catch (error) {
      console.error('Error loading data:', error);
    } finally {
      this.loading.set(false);
    }
  }

  goBack() {
    this.router.navigate(['/zones/manage']);
  }

  changeZone(zoneId: number) {
    this.currentZoneId.set(zoneId);
    this.router.navigate(['/zones', zoneId, 'capabilities'], { replaceUrl: true });
  }

  // Native HTML5 Drag & Drop handlers
  onDragStart(event: DragEvent, device: Device) {
    this.draggedDevice.set(device);
    if (event.dataTransfer) {
      event.dataTransfer.effectAllowed = 'move';
      event.dataTransfer.setData('text/plain', device.deviceId);
    }
    document.body.classList.add('is-dragging');
  }

  onDragEnd(event: DragEvent) {
    this.draggedDevice.set(null);
    this.dragOverCapability.set(null);
    document.body.classList.remove('is-dragging');
  }

  onDragOver(event: DragEvent, capType: string) {
    event.preventDefault();
    if (event.dataTransfer) {
      event.dataTransfer.dropEffect = 'move';
    }
    this.dragOverCapability.set(capType);
  }

  onDragLeave(event: DragEvent, capType: string) {
    const relatedTarget = event.relatedTarget as HTMLElement;
    const currentTarget = event.currentTarget as HTMLElement;
    if (!currentTarget.contains(relatedTarget)) {
      this.dragOverCapability.set(null);
    }
  }

  async onDrop(event: DragEvent, capType: string) {
    event.preventDefault();
    this.dragOverCapability.set(null);

    const device = this.draggedDevice();
    if (!device) return;

    await this.assignCapability(capType, device.deviceId);
    this.draggedDevice.set(null);
    document.body.classList.remove('is-dragging');
  }

  async assignCapability(capType: string, deviceId: string) {
    this.saving.set(true);
    try {
      await this.http.post(`/api/zones/${this.currentZoneId()}/capabilities`, {
        capability: capType,
        deviceId: deviceId
      }).toPromise();

      this.capabilities.update(caps => {
        const filtered = caps.filter(
          c => !(c.zoneId === this.currentZoneId() && c.capability === capType)
        );
        return [...filtered, {
          zoneId: this.currentZoneId(),
          capability: capType,
          deviceId: deviceId
        }];
      });
    } catch (error) {
      console.error('Error assigning capability:', error);
    } finally {
      this.saving.set(false);
    }
  }

  async removeCapability(capType: string) {
    this.saving.set(true);
    try {
      await this.http.delete(`/api/zones/${this.currentZoneId()}/capabilities/${capType}`).toPromise();

      this.capabilities.update(caps =>
        caps.filter(c => !(c.zoneId === this.currentZoneId() && c.capability === capType))
      );
    } catch (error) {
      console.error('Error removing capability:', error);
    } finally {
      this.saving.set(false);
    }
  }

  getDeviceIcon(deviceType?: number): string {
    const icons: Record<number, string> = {
      0: '💡', 1: '🔘', 2: '📡', 3: '🌡️', 4: '🔒', 5: '🪟', 6: '🌀', 7: '📦'
    };
    return deviceType !== undefined ? icons[deviceType] || '📦' : '📦';
  }
}
