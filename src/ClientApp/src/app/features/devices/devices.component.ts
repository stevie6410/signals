import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { CardModule } from 'primeng/card';
import { TableModule } from 'primeng/table';
import { ButtonModule } from 'primeng/button';
import { TagModule } from 'primeng/tag';
import { DialogModule } from 'primeng/dialog';
import { InputTextModule } from 'primeng/inputtext';
import { SelectModule } from 'primeng/select';
import { FormsModule } from '@angular/forms';
import { DevicesApiService, Device, DeviceType } from '../../api/sdhome-client';

@Component({
  selector: 'app-devices',
  standalone: true,
  imports: [
    CommonModule,
    RouterModule,
    CardModule,
    TableModule,
    ButtonModule,
    TagModule,
    DialogModule,
    InputTextModule,
    SelectModule,
    FormsModule
  ],
  template: `
    <div class="page-container">
      <div class="page-header">
        <h1>Devices</h1>
        <p-button
          label="Sync from Zigbee2MQTT"
          icon="pi pi-refresh"
          (onClick)="syncDevices()"
          [loading]="syncing()"
          severity="secondary" />
      </div>

      <p-card>
        <p-table
          [value]="devices()"
          [loading]="loading()"
          [paginator]="true"
          [rows]="10"
          [rowsPerPageOptions]="[10, 25, 50]"
          [globalFilterFields]="['friendlyName', 'manufacturer', 'room', 'deviceType']"
          styleClass="p-datatable-sm">

          <ng-template pTemplate="caption">
            <div class="table-header">
              <span class="p-input-icon-left">
                <i class="pi pi-search"></i>
                <input
                  pInputText
                  type="text"
                  (input)="onSearch($event)"
                  placeholder="Search devices..." />
              </span>
            </div>
          </ng-template>

          <ng-template pTemplate="header">
            <tr>
              <th pSortableColumn="friendlyName">
                Name <p-sortIcon field="friendlyName" />
              </th>
              <th pSortableColumn="manufacturer">Manufacturer</th>
              <th pSortableColumn="modelId">Model</th>
              <th pSortableColumn="deviceType">
                Type <p-sortIcon field="deviceType" />
              </th>
              <th pSortableColumn="room">
                Room <p-sortIcon field="room" />
              </th>
              <th>Status</th>
              <th>Actions</th>
            </tr>
          </ng-template>

          <ng-template pTemplate="body" let-device>
            <tr>
              <td>
                <div class="device-name">
                  <i class="pi" [ngClass]="getDeviceIcon(device.deviceType)"></i>
                  <span>{{ device.friendlyName }}</span>
                </div>
              </td>
              <td>{{ device.manufacturer || '-' }}</td>
              <td>{{ device.modelId || '-' }}</td>
              <td>
                <p-tag
                  [value]="device.deviceType || 'Other'"
                  [severity]="getDeviceTypeSeverity(device.deviceType)" />
              </td>
              <td>{{ device.room || 'Unassigned' }}</td>
              <td>
                <p-tag
                  [value]="device.isAvailable ? 'Online' : 'Offline'"
                  [severity]="device.isAvailable ? 'success' : 'danger'" />
              </td>
              <td>
                <p-button
                  icon="pi pi-pencil"
                  [text]="true"
                  [rounded]="true"
                  severity="secondary"
                  (onClick)="editDevice(device)" />
              </td>
            </tr>
          </ng-template>

          <ng-template pTemplate="emptymessage">
            <tr>
              <td colspan="7" class="text-center">
                <div class="empty-state">
                  <i class="pi pi-inbox" style="font-size: 3rem; color: var(--text-color-secondary);"></i>
                  <p>No devices found</p>
                  <p-button
                    label="Sync Devices"
                    icon="pi pi-refresh"
                    (onClick)="syncDevices()"
                    [outlined]="true" />
                </div>
              </td>
            </tr>
          </ng-template>
        </p-table>
      </p-card>

      <!-- Edit Device Dialog -->
      <p-dialog
        [(visible)]="showEditDialog"
        [modal]="true"
        [style]="{ width: '500px' }"
        header="Edit Device">

        <div class="dialog-content" *ngIf="selectedDevice">
          <div class="form-field">
            <label for="friendlyName">Friendly Name</label>
            <input
              id="friendlyName"
              type="text"
              pInputText
              [(ngModel)]="selectedDevice.friendlyName"
              class="w-full" />
          </div>

          <div class="form-field">
            <label for="deviceType">Device Type</label>
            <p-select
              id="deviceType"
              [(ngModel)]="selectedDevice.deviceType"
              [options]="deviceTypes"
              optionLabel="label"
              optionValue="value"
              placeholder="Select device type"
              class="w-full" />
          </div>

          <div class="form-field">
            <label for="room">Room</label>
            <input
              id="room"
              type="text"
              pInputText
              [(ngModel)]="selectedDevice.room"
              placeholder="e.g., Living Room, Bedroom"
              class="w-full" />
          </div>

          <div class="device-info">
            <h4>Device Information</h4>
            <div class="info-row">
              <span class="label">Manufacturer:</span>
              <span>{{ selectedDevice.manufacturer || '-' }}</span>
            </div>
            <div class="info-row">
              <span class="label">Model:</span>
              <span>{{ selectedDevice.modelId || '-' }}</span>
            </div>
            <div class="info-row">
              <span class="label">IEEE Address:</span>
              <span class="monospace">{{ selectedDevice.ieeeAddress || '-' }}</span>
            </div>
            <div class="info-row" *ngIf="selectedDevice.capabilities && selectedDevice.capabilities.length > 0">
              <span class="label">Capabilities:</span>
              <span>
                <p-tag
                  *ngFor="let cap of selectedDevice.capabilities"
                  [value]="cap"
                  severity="info"
                  [style]="{ 'margin-right': '0.5rem' }" />
              </span>
            </div>
          </div>
        </div>

        <ng-template pTemplate="footer">
          <p-button
            label="Cancel"
            [outlined]="true"
            (onClick)="showEditDialog = false" />
          <p-button
            label="Save"
            (onClick)="saveDevice()"
            [loading]="saving()" />
        </ng-template>
      </p-dialog>
    </div>
  `,
  styles: [`
    .page-container {
      max-width: 1400px;
      margin: 0 auto;
      padding: 2rem;
    }

    .page-header {
      display: flex;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 2rem;
    }

    .page-header h1 {
      font-size: 2.5rem;
      font-weight: 700;
      margin: 0;
      color: var(--text-color);
    }

    .table-header {
      display: flex;
      justify-content: flex-end;
      padding: 1rem 0;
    }

    .device-name {
      display: flex;
      align-items: center;
      gap: 0.75rem;
      font-weight: 600;
    }

    .device-name i {
      font-size: 1.25rem;
      color: var(--primary-color);
    }

    .empty-state {
      padding: 3rem;
      text-align: center;
    }

    .empty-state p {
      margin: 1rem 0;
      color: var(--text-color-secondary);
    }

    .dialog-content {
      display: flex;
      flex-direction: column;
      gap: 1.5rem;
      padding: 1rem 0;
    }

    .form-field {
      display: flex;
      flex-direction: column;
      gap: 0.5rem;
    }

    .form-field label {
      font-weight: 600;
      color: var(--text-color);
    }

    .w-full {
      width: 100%;
    }

    .device-info {
      margin-top: 1rem;
      padding: 1rem;
      background: var(--surface-ground);
      border-radius: 8px;
    }

    .device-info h4 {
      margin: 0 0 1rem 0;
      color: var(--text-color);
    }

    .info-row {
      display: flex;
      justify-content: space-between;
      padding: 0.5rem 0;
      border-bottom: 1px solid var(--surface-border);
    }

    .info-row:last-child {
      border-bottom: none;
    }

    .info-row .label {
      font-weight: 600;
      color: var(--text-color-secondary);
    }

    .monospace {
      font-family: 'Courier New', monospace;
      font-size: 0.875rem;
    }

    .text-center {
      text-align: center;
    }
  `]
})
export class DevicesComponent implements OnInit {
  devices = signal<Device[]>([]);
  loading = signal(false);
  syncing = signal(false);
  saving = signal(false);
  showEditDialog = false;
  selectedDevice: Device | null = null;

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

  constructor(private apiService: DevicesApiService) {
  }

  ngOnInit() {
    this.loadDevices();
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
    this.syncing.set(true);
    try {
      await this.apiService.syncDevices().toPromise();
      await this.loadDevices();
    } catch (error) {
      console.error('Error syncing devices:', error);
    } finally {
      this.syncing.set(false);
    }
  }

  editDevice(device: Device) {
    this.selectedDevice = Device.fromJS(JSON.parse(JSON.stringify(device)));
    this.showEditDialog = true;
  }

  async saveDevice() {
    if (!this.selectedDevice) return;

    this.saving.set(true);
    try {
      await this.apiService.updateDevice(this.selectedDevice.deviceId!, this.selectedDevice).toPromise();
      this.showEditDialog = false;
      await this.loadDevices();
    } catch (error) {
      console.error('Error saving device:', error);
    } finally {
      this.saving.set(false);
    }
  }

  onSearch(event: Event) {
    const input = event.target as HTMLInputElement;
    // Table filtering will be handled by PrimeNG's globalFilter
  }

  getDeviceIcon(type?: DeviceType): string {
    switch (type) {
      case DeviceType.Light: return 'pi-sun';
      case DeviceType.Switch: return 'pi-power-off';
      case DeviceType.Sensor: return 'pi-chart-line';
      case DeviceType.Climate: return 'pi-home';
      case DeviceType.Lock: return 'pi-lock';
      case DeviceType.Cover: return 'pi-window-maximize';
      case DeviceType.Fan: return 'pi-spin';
      default: return 'pi-box';
    }
  }

  getDeviceTypeSeverity(type?: DeviceType): 'success' | 'info' | 'warn' | 'danger' | 'secondary' | 'contrast' | undefined {
    switch (type) {
      case DeviceType.Light: return 'warn';
      case DeviceType.Switch: return 'info';
      case DeviceType.Sensor: return 'success';
      case DeviceType.Climate: return 'info';
      default: return 'secondary';
    }
  }
}
