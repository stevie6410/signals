import { Component, OnInit, OnDestroy, signal, computed, inject, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { Router, RouterModule } from '@angular/router';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { DevicesApiService, Device, DeviceType, Zone } from '../../api/sdhome-client';
import { SignalRService, DeviceSyncProgress, DevicePairingDevice } from '../../core/services/signalr.service';
import { PairingDialogComponent } from './pairing-dialog/pairing-dialog.component';

interface DevicesByZone {
  zone: Zone | null;
  zoneName: string;
  devices: Device[];
}

@Component({
  selector: 'app-devices',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule, PairingDialogComponent],
  templateUrl: './devices.component.html',
  styleUrl: './devices.component.scss'
})
export class DevicesComponent implements OnInit, OnDestroy {
  private apiService = inject(DevicesApiService);
  private signalRService = inject(SignalRService);
  private http = inject(HttpClient);
  private router = inject(Router);

  devices = signal<Device[]>([]);
  loading = signal(false);
  syncing = signal(false);
  saving = signal(false);
  renaming = signal(false);
  showEditDialog = false;
  selectedDevice: Device | null = null;
  originalDeviceName: string | null = null;
  newDeviceName: string = '';
  searchFilter = signal('');
  showTypeDropdown = false;
  typeFilter = signal<DeviceType | null>(null);

  // Sync modal state
  showSyncModal = signal(false);
  syncProgress = this.signalRService.deviceSyncProgress;

  // Pairing dialog state
  showPairingDialog = signal(false);

  // Computed sync status helpers
  syncStatusIcon = computed(() => {
    const progress = this.syncProgress();
    if (!progress) return '🔄';
    switch (progress.status) {
      case 'Started': return '🚀';
      case 'Connecting': return '🔌';
      case 'Subscribing': return '📡';
      case 'WaitingForDevices': return '⏳';
      case 'DeviceReceived': return '📥';
      case 'Processing': return '⚙️';
      case 'DeviceProcessed': return '✨';
      case 'Completed': return '✅';
      case 'Failed': return '❌';
      default: return '🔄';
    }
  });

  syncProgressPercent = computed(() => {
    const progress = this.syncProgress();
    if (!progress || progress.devicesTotal === 0) return 0;
    return Math.round((progress.devicesProcessed / progress.devicesTotal) * 100);
  });

  newDevicesCount = computed(() => {
    const progress = this.syncProgress();
    if (!progress?.discoveredDevices) return 0;
    return progress.discoveredDevices.filter(d => d.isNew && !d.isRemoved).length;
  });

  updatedDevicesCount = computed(() => {
    const progress = this.syncProgress();
    if (!progress?.discoveredDevices) return 0;
    return progress.discoveredDevices.filter(d => !d.isNew && !d.isRemoved).length;
  });

  removedDevicesCount = computed(() => {
    const progress = this.syncProgress();
    if (!progress?.discoveredDevices) return 0;
    return progress.discoveredDevices.filter(d => d.isRemoved).length;
  });

  deviceTypes = [
    { label: 'Light', value: DeviceType.Light },
    { label: 'Switch', value: DeviceType.Switch },
    { label: 'Sensor', value: DeviceType.Sensor },
    { label: 'Climate', value: DeviceType.Climate },
    { label: 'Lock', value: DeviceType.Lock },
    { label: 'Cover', value: DeviceType.Cover },
    { label: 'Fan', value: DeviceType.Fan },
    { label: 'Other', value: DeviceType.Other }
  ];

  // Filtered devices
  filteredDevices = computed(() => {
    const search = this.searchFilter().toLowerCase();
    const type = this.typeFilter();
    let result = this.devices();

    if (search) {
      result = result.filter(d =>
        d.friendlyName?.toLowerCase().includes(search) ||
        d.displayName?.toLowerCase().includes(search) ||
        d.manufacturer?.toLowerCase().includes(search) ||
        d.room?.toLowerCase().includes(search) ||
        d.zone?.name?.toLowerCase().includes(search) ||
        (d.deviceType?.toString() || '').toLowerCase().includes(search)
      );
    }

    if (type !== null) {
      result = result.filter(d => d.deviceType === type);
    }

    return result;
  });

  // Devices grouped by zone
  devicesByZone = computed(() => {
    const devices = this.filteredDevices();
    const groups = new Map<number | null, DevicesByZone>();

    for (const device of devices) {
      const zoneId = device.zoneId ?? null;

      if (!groups.has(zoneId)) {
        groups.set(zoneId, {
          zone: device.zone ?? null,
          zoneName: device.zone?.name || 'Unassigned',
          devices: []
        });
      }
      groups.get(zoneId)!.devices.push(device);
    }

    // Sort: zones with names first (alphabetically), then unassigned last
    const sorted = Array.from(groups.values()).sort((a, b) => {
      if (a.zone === null && b.zone !== null) return 1;
      if (a.zone !== null && b.zone === null) return -1;
      return a.zoneName.localeCompare(b.zoneName);
    });

    return sorted;
  });

  // Stats
  stats = computed(() => {
    const devices = this.devices();
    const online = devices.filter(d => d.isAvailable).length;
    const byType: Record<string, number> = {};
    const byRoom: Record<string, number> = {};

    devices.forEach(d => {
      if (d.deviceType !== undefined) {
        const typeStr = DeviceType[d.deviceType] || 'Unknown';
        byType[typeStr] = (byType[typeStr] || 0) + 1;
      }
      const room = d.room || 'Unassigned';
      byRoom[room] = (byRoom[room] || 0) + 1;
    });

    return {
      total: devices.length,
      online,
      offline: devices.length - online,
      byType,
      byRoom
    };
  });

  constructor() {
    // Auto-close sync modal when sync completes successfully
    effect(() => {
      const progress = this.syncProgress();
      if (progress?.status === 'Completed') {
        // Reload devices after successful sync
        this.loadDevices();
      }
    });
  }

  ngOnInit() {
    this.loadDevices();
  }

  ngOnDestroy() {
    // Clean up sync progress when leaving the page
    this.signalRService.clearSyncProgress();
  }

  async loadDevices() {
    this.loading.set(true);
    try {
      const devices = await this.apiService.getDevices().toPromise();
      this.devices.set(devices || []);
    } catch (error) {
      console.error('Error loading devices:', error);
    } finally {
      this.loading.set(false);
    }
  }

  async syncDevices() {
    // Clear any previous sync progress
    this.signalRService.clearSyncProgress();
    this.showSyncModal.set(true);
    this.syncing.set(true);

    try {
      // Call the realtime sync endpoint
      this.http.post<any>('/api/devices/sync/realtime', {}).subscribe({
        next: (response) => {
          console.log('Sync completed:', response);
          this.syncing.set(false);
        },
        error: (err) => {
          console.error('Sync error:', err);
          this.syncing.set(false);
        }
      });
    } catch (error) {
      console.error('Error syncing devices:', error);
      this.syncing.set(false);
    }
  }

  closeSyncModal() {
    this.showSyncModal.set(false);
    // Don't clear progress immediately so user can see final state
    setTimeout(() => {
      if (!this.showSyncModal()) {
        this.signalRService.clearSyncProgress();
      }
    }, 500);
  }

  editDevice(device: Device) {
    this.selectedDevice = Device.fromJS(JSON.parse(JSON.stringify(device)));
    this.originalDeviceName = device.friendlyName || device.deviceId || null;
    this.newDeviceName = '';
    this.showEditDialog = true;
  }

  closeDialog() {
    this.showEditDialog = false;
    this.selectedDevice = null;
    this.originalDeviceName = null;
    this.newDeviceName = '';
  }

  async saveDevice() {
    if (!this.selectedDevice) return;

    this.saving.set(true);
    try {
      // Just update local attributes (display name, room, type, etc.)
      await this.apiService.updateDevice(this.selectedDevice.deviceId!, this.selectedDevice).toPromise();

      this.showEditDialog = false;
      this.originalDeviceName = null;
      this.newDeviceName = '';
      await this.loadDevices();
    } catch (error) {
      console.error('Error saving device:', error);
    } finally {
      this.saving.set(false);
    }
  }

  async renameDevice() {
    if (!this.selectedDevice || !this.newDeviceName || this.newDeviceName === this.selectedDevice.deviceId) {
      return;
    }

    this.renaming.set(true);
    try {
      await this.http.post<Device>(
        `/api/devices/${encodeURIComponent(this.selectedDevice.deviceId!)}/rename`,
        { newName: this.newDeviceName }
      ).toPromise();

      console.log(`Device renamed from '${this.selectedDevice.deviceId}' to '${this.newDeviceName}'`);

      // Update the selected device with new name
      this.selectedDevice.deviceId = this.newDeviceName;
      this.selectedDevice.friendlyName = this.newDeviceName;
      this.originalDeviceName = this.newDeviceName;
      this.newDeviceName = '';

      await this.loadDevices();
    } catch (error) {
      console.error('Error renaming device:', error);
    } finally {
      this.renaming.set(false);
    }
  }

  selectType(type: DeviceType | null) {
    this.typeFilter.set(type);
    this.showTypeDropdown = false;
  }

  getSelectedTypeLabel(): string {
    const type = this.typeFilter();
    if (type === null) return 'All Types';
    return DeviceType[type] || 'Unknown';
  }

  getDeviceIcon(type?: DeviceType): string {
    switch (type) {
      case DeviceType.Light: return '💡';
      case DeviceType.Switch: return '🔌';
      case DeviceType.Sensor: return '📡';
      case DeviceType.Climate: return '🌡️';
      case DeviceType.Lock: return '🔒';
      case DeviceType.Cover: return '🪟';
      case DeviceType.Fan: return '🌀';
      default: return '📦';
    }
  }

  getDeviceTypeName(type?: DeviceType): string {
    if (type === undefined) return 'Unknown';
    return DeviceType[type] || 'Unknown';
  }

  getDeviceTypeClass(type?: DeviceType): string {
    switch (type) {
      case DeviceType.Light: return 'warning';
      case DeviceType.Switch: return 'info';
      case DeviceType.Sensor: return 'success';
      case DeviceType.Climate: return 'cyan';
      case DeviceType.Lock: return 'danger';
      case DeviceType.Cover: return 'magenta';
      default: return 'secondary';
    }
  }

  openPairingDialog() {
    this.showPairingDialog.set(true);
  }

  closePairingDialog() {
    this.showPairingDialog.set(false);
  }

  onDevicePaired(device: DevicePairingDevice) {
    console.log('Device paired:', device);
    // Reload devices after a device is paired
    setTimeout(() => this.loadDevices(), 1000);
  }

  trackDevice(index: number, device: Device): string {
    return device.deviceId ?? index.toString();
  }

  viewDevice(device: Device) {
    if (device.deviceId) {
      this.router.navigate(['/devices', device.deviceId]);
    }
  }

  getDisplayName(device: Device): string {
    return device.displayName || device.friendlyName || 'Unknown Device';
  }
}
