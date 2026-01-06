import { Component, OnInit, signal, computed, inject, ViewChildren, QueryList, AfterViewInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Router } from '@angular/router';
import { CdkDragDrop, CdkDrag, CdkDropList, CdkDropListGroup, CdkDragPlaceholder, moveItemInArray, transferArrayItem } from '@angular/cdk/drag-drop';

interface Zone {
  id: number;
  name: string;
  description?: string;
  icon?: string;
  color?: string;
  parentZoneId?: number;
  sortOrder: number;
  childZones: Zone[];
}

interface Device {
  deviceId: string;
  friendlyName: string;
  displayName?: string;
  manufacturer?: string;
  deviceType?: string | number;
  imageUrl?: string;
  zoneId?: number;
  isAvailable: boolean;
}

@Component({
  selector: 'app-zone-manager',
  standalone: true,
  imports: [CommonModule, FormsModule, CdkDropList, CdkDrag, CdkDragPlaceholder],
  templateUrl: './zone-manager.component.html',
  styleUrl: './zone-manager.component.scss'
})
export class ZoneManagerComponent implements OnInit, AfterViewInit {
  private http = inject(HttpClient);
  private router = inject(Router);

  @ViewChildren(CdkDropList) dropLists!: QueryList<CdkDropList>;

  zones = signal<Zone[]>([]);
  devices = signal<Device[]>([]);
  loading = signal(false);
  saving = signal(false);

  // View mode: tree or nested boxes
  zoneViewMode = signal<'tree' | 'nested'>('nested');

  // Search filter for devices
  deviceSearch = signal('');

  // Currently dragging device
  draggingDevice = signal<Device | null>(null);

  // Highlight zone on drag over
  highlightedZoneId = signal<number | null>(null);

  // All drop list IDs for connecting
  allDropListIds = computed(() => {
    const ids = ['unassigned-list'];
    const addZoneIds = (zones: Zone[]) => {
      for (const zone of zones) {
        ids.push(`zone-${zone.id}`);
        if (zone.childZones?.length) {
          addZoneIds(zone.childZones);
        }
      }
    };
    addZoneIds(this.zones());
    return ids;
  });

  // Unassigned devices (not in any zone)
  unassignedDevices = computed(() => {
    const search = this.deviceSearch().toLowerCase();
    return this.devices()
      .filter(d => !d.zoneId)
      .filter(d => !search ||
        d.friendlyName.toLowerCase().includes(search) ||
        d.manufacturer?.toLowerCase().includes(search) ||
        d.deviceType?.toString().toLowerCase().includes(search)
      );
  });

  // Get devices for a specific zone
  getDevicesInZone(zoneId: number): Device[] {
    return this.devices().filter(d => d.zoneId === zoneId);
  }

  // Device type icons - deviceType can be string or enum number
  getDeviceIcon(deviceType?: string | number): string {
    // Handle enum numbers: Light=0, Switch=1, Sensor=2, Climate=3, Lock=4, Cover=5, Fan=6, Other=7
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

  ngAfterViewInit() {
    // Drop lists are connected via cdkDropListConnectedTo in template
  }

  async loadData() {
    this.loading.set(true);
    try {
      const [zones, devices] = await Promise.all([
        this.http.get<Zone[]>('/api/zones/tree').toPromise(),
        this.http.get<Device[]>('/api/devices').toPromise()
      ]);
      console.log('Zones tree:', JSON.stringify(zones, null, 2));
      this.zones.set(zones || []);
      this.devices.set(devices || []);
    } catch (error) {
      console.error('Error loading data:', error);
    } finally {
      this.loading.set(false);
    }
  }

  onDragStarted(device: Device) {
    this.draggingDevice.set(device);
  }

  onDragEnded() {
    this.draggingDevice.set(null);
    this.highlightedZoneId.set(null);
  }

  onDragEnterZone(zoneId: number | null) {
    this.highlightedZoneId.set(zoneId);
  }

  async onDeviceDropped(event: CdkDragDrop<Device[]>, targetZoneId: number | null) {
    const device = event.item.data as Device;

    if (device.zoneId === targetZoneId) {
      return; // No change
    }

    this.saving.set(true);
    try {
      if (targetZoneId === null) {
        // Remove from zone
        if (device.zoneId) {
          await this.http.delete(`/api/zones/${device.zoneId}/devices/${device.deviceId}`).toPromise();
        }
      } else {
        // Assign to zone
        await this.http.post(`/api/zones/${targetZoneId}/devices/${device.deviceId}`, {}).toPromise();
      }

      // Update local state
      this.devices.update(devices =>
        devices.map(d => d.deviceId === device.deviceId
          ? { ...d, zoneId: targetZoneId ?? undefined }
          : d
        )
      );
    } catch (error) {
      console.error('Error assigning device to zone:', error);
    } finally {
      this.saving.set(false);
      this.highlightedZoneId.set(null);
    }
  }

  // Quick assign via dropdown (fallback)
  async assignDeviceToZone(device: Device, zoneId: number | null) {
    if (device.zoneId === zoneId) return;

    this.saving.set(true);
    try {
      if (zoneId === null && device.zoneId) {
        await this.http.delete(`/api/zones/${device.zoneId}/devices/${device.deviceId}`).toPromise();
      } else if (zoneId !== null) {
        await this.http.post(`/api/zones/${zoneId}/devices/${device.deviceId}`, {}).toPromise();
      }

      this.devices.update(devices =>
        devices.map(d => d.deviceId === device.deviceId
          ? { ...d, zoneId: zoneId ?? undefined }
          : d
        )
      );
    } catch (error) {
      console.error('Error:', error);
    } finally {
      this.saving.set(false);
    }
  }

  // Flatten zones for dropdown
  getFlatZones(): { zone: Zone; depth: number }[] {
    const result: { zone: Zone; depth: number }[] = [];
    const flatten = (zones: Zone[], depth: number) => {
      for (const zone of zones) {
        result.push({ zone, depth });
        if (zone.childZones?.length) {
          flatten(zone.childZones, depth + 1);
        }
      }
    };
    flatten(this.zones(), 0);
    return result;
  }

  // Navigate to capability editor page for a zone
  openCapabilityEditor(zoneId: number) {
    this.router.navigate(['/zones', zoneId, 'capabilities']);
  }
}
