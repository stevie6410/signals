import { Component, OnInit, OnDestroy, inject, signal, computed, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import {
  DevicesApiService,
  ReadingsApiService,
  SensorReading,
  Device,
  DeviceType,
  DeviceDefinition,
  DeviceCapability,
  ControlType,
  SetDeviceStateRequest,
} from '../../../api/sdhome-client';
import { SignalRService, DeviceStateUpdate } from '../../../core/services/signalr.service';
import { SensorChartComponent, ChartSeries } from '../../../shared/components/sensor-chart/sensor-chart.component';

@Component({
  selector: 'app-device-detail',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, SensorChartComponent],
  templateUrl: './device-detail.component.html',
  styleUrl: './device-detail.component.scss',
})
export class DeviceDetailComponent implements OnInit, OnDestroy {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private apiService = inject(DevicesApiService);
  private readingsService = inject(ReadingsApiService);
  private signalR = inject(SignalRService);

  // Expose ControlType enum for template
  ControlType = ControlType;

  // Expose Object for template
  Object = Object;

  // State
  deviceId = signal<string>('');
  device = signal<Device | null>(null);
  definition = signal<DeviceDefinition | null>(null);
  loading = signal(false);
  loadingDefinition = signal(false);
  refreshingState = signal(false);
  saving = signal(false);
  error = signal<string | null>(null);

  // Sensor readings history
  sensorReadings = signal<SensorReading[]>([]);
  loadingReadings = signal(false);
  selectedTimeRange = signal<'1h' | '6h' | '24h' | '7d'>('24h');

  // Edit state
  editingSettings = signal(false);
  editedDevice = signal<Partial<Device>>({});

  // State changes pending - with timestamps for expiry
  pendingState = signal<Record<string, { value: any; timestamp: number }>>({});

  // Cooldown to prevent rapid state changes
  private lastCommandTime = 0;
  private readonly COMMAND_COOLDOWN_MS = 100; // Reduced cooldown - updates are now instant
  private readonly PENDING_STATE_TTL_MS = 3000; // Reduced TTL - we get fast updates now
  private cleanupInterval: ReturnType<typeof setInterval> | null = null;
  private pendingCleanups: string[] = []; // Properties to clean up outside render cycle

  // Device types for dropdown
  deviceTypes = Object.values(DeviceType).filter((v) => typeof v === 'number') as DeviceType[];

  // Computed
  displayName = computed(() => {
    const def = this.definition();
    return def?.displayName || def?.friendlyName || this.deviceId();
  });

  constructor() {
    // Listen for real-time device state updates via SignalR
    effect(() => {
      const stateUpdates = this.signalR.deviceStateUpdates();
      const id = this.deviceId();
      if (!id) return;

      const update = stateUpdates.get(id);
      if (!update) return;

      // Update definition's current state from real-time update
      const def = this.definition();
      if (def?.currentState) {
        const newState = { ...def.currentState };
        let hasUpdates = false;

        for (const [key, value] of Object.entries(update.state)) {
          if (newState[key] !== value) {
            newState[key] = value;
            hasUpdates = true;
            // Schedule pending state cleanup for this property
            this.pendingCleanups.push(key);
          }
        }

        if (hasUpdates) {
          // Create a new DeviceDefinition with updated state
          const updatedDef = DeviceDefinition.fromJS({
            ...def.toJSON(),
            currentState: newState
          });
          this.definition.set(updatedDef);
        }
      }
    });
  }

  // Clear pending state for a property
  private clearPendingState(property: string) {
    this.pendingState.update(state => {
      if (property in state) {
        const { [property]: _, ...rest } = state;
        return rest;
      }
      return state;
    });
  }

  // Group capabilities by category
  groupedCapabilities = computed(() => {
    const def = this.definition();
    if (!def?.capabilities) return new Map<string, DeviceCapability[]>();

    const groups = new Map<string, DeviceCapability[]>();

    for (const cap of def.capabilities) {
      const category = cap.category || 'Other';
      if (!groups.has(category)) {
        groups.set(category, []);
      }
      groups.get(category)!.push(cap);
    }

    return groups;
  });

  // Capabilities that can be controlled (writable)
  controllableCapabilities = computed(() => {
    const def = this.definition();
    if (!def?.capabilities) return [];

    return def.capabilities.filter(cap =>
      cap.access?.canWrite &&
      cap.controlType !== ControlType.ReadOnly &&
      cap.controlType !== ControlType.Composite
    );
  });

  // Read-only capabilities (sensors/state)
  readOnlyCapabilities = computed(() => {
    const def = this.definition();
    if (!def?.capabilities) return [];

    return def.capabilities.filter(cap =>
      !cap.access?.canWrite ||
      cap.controlType === ControlType.ReadOnly
    );
  });

  // Numeric sensor capabilities (for charting)
  numericSensorCapabilities = computed(() => {
    const readOnly = this.readOnlyCapabilities();
    // Filter to numeric sensor types
    const sensorProps = ['temperature', 'humidity', 'pressure', 'illuminance', 'battery', 'voltage', 'power', 'energy', 'linkquality'];
    return readOnly.filter(cap =>
      sensorProps.includes(cap.property?.toLowerCase() ?? '') ||
      cap.type === 'numeric'
    );
  });

  // Check if device has sensor data worth charting
  hasSensorHistory = computed(() => {
    return this.numericSensorCapabilities().length > 0 && this.sensorReadings().length > 0;
  });

  // Convert sensor readings to individual charts (one chart per measurement)
  individualCharts = computed<Array<{ metric: string; series: ChartSeries[] }>>(() => {
    const readings = this.sensorReadings();
    const caps = this.numericSensorCapabilities();

    if (readings.length === 0) return [];

    // Group readings by metric
    const byMetric = new Map<string, SensorReading[]>();
    for (const r of readings) {
      if (!r.metric) continue;
      if (!byMetric.has(r.metric)) {
        byMetric.set(r.metric, []);
      }
      byMetric.get(r.metric)!.push(r);
    }

    // Create a separate chart for each metric
    const charts: Array<{ metric: string; series: ChartSeries[] }> = [];
    const colors = ['#00f5ff', '#f59e0b', '#10b981', '#8b5cf6', '#ef4444'];
    let colorIndex = 0;

    for (const [metric, metricReadings] of byMetric) {
      // Find matching capability for unit info
      const cap = caps.find(c => c.property?.toLowerCase() === metric.toLowerCase());

      const series: ChartSeries = {
        name: this.formatMetricName(metric),
        unit: cap?.unit || metricReadings[0]?.unit || '',
        color: colors[colorIndex % colors.length],
        data: metricReadings
          .filter(r => r.timestampUtc && r.value !== undefined && r.value !== null)
          .map(r => ({
            timestamp: r.timestampUtc!,
            value: r.value!,
          })),
      };

      charts.push({
        metric,
        series: [series], // Each chart has only one series
      });
      colorIndex++;
    }

    return charts;
  });

  // Quick actions (primary controls like on/off, brightness)
  quickActions = computed(() => {
    const controllable = this.controllableCapabilities();
    // Prioritize: state toggle, brightness, color_temp
    const priorityProps = ['state', 'brightness', 'color_temp', 'position'];
    return controllable
      .filter(cap => priorityProps.includes(cap.property ?? ''))
      .sort((a, b) => {
        const aIdx = priorityProps.indexOf(a.property ?? '');
        const bIdx = priorityProps.indexOf(b.property ?? '');
        return aIdx - bIdx;
      });
  });

  // Other controllable actions (settings, config)
  otherActions = computed(() => {
    const controllable = this.controllableCapabilities();
    const priorityProps = ['state', 'brightness', 'color_temp', 'position'];
    return controllable.filter(cap => !priorityProps.includes(cap.property ?? ''));
  });

  // Live signals for this device
  recentSignals = computed(() => {
    const id = this.deviceId();
    return this.signalR
      .signalHistory()
      .filter((s) => s.deviceId === id)
      .slice(0, 10);
  });

  ngOnInit() {
    this.route.params.subscribe((params) => {
      const id = params['deviceId'];
      if (id) {
        this.deviceId.set(id);
        this.loadDevice();
        this.loadDefinition();
        this.loadSensorReadings();
        // Subscribe to device-specific SignalR updates for faster response
        this.signalR.subscribeToDevice(id);
      }
    });

    // Start cleanup interval for pending states and scheduled cleanups
    this.cleanupInterval = setInterval(() => {
      this.processPendingCleanups();
      this.cleanupExpiredPendingStates();
    }, 500); // Run more frequently for snappier cleanup
  }

  ngOnDestroy() {
    // Unsubscribe from device-specific updates
    const id = this.deviceId();
    if (id) {
      this.signalR.unsubscribeFromDevice(id);
    }

    if (this.cleanupInterval) {
      clearInterval(this.cleanupInterval);
      this.cleanupInterval = null;
    }
  }

  // Process any pending cleanups scheduled from effects
  private processPendingCleanups() {
    if (this.pendingCleanups.length > 0) {
      const toClean = [...this.pendingCleanups];
      this.pendingCleanups = [];
      for (const prop of toClean) {
        this.clearPendingState(prop);
      }
    }
  }

  // Clean up expired pending states
  private cleanupExpiredPendingStates() {
    const now = Date.now();
    const pending = this.pendingState();
    const expired = Object.entries(pending)
      .filter(([_, entry]) => now - entry.timestamp >= this.PENDING_STATE_TTL_MS)
      .map(([prop]) => prop);

    if (expired.length > 0) {
      this.pendingState.update(state => {
        const newState = { ...state };
        for (const prop of expired) {
          delete newState[prop];
        }
        return newState;
      });
    }
  }

  async loadDevice() {
    this.loading.set(true);
    this.error.set(null);

    try {
      const device = await this.apiService.getDevice(this.deviceId()).toPromise();
      this.device.set(device ?? null);
    } catch (err: any) {
      console.error('Error loading device:', err);
      this.error.set(err.message || 'Failed to load device');
    } finally {
      this.loading.set(false);
    }
  }

  async loadDefinition() {
    this.loadingDefinition.set(true);

    try {
      const def = await this.apiService.getDeviceDefinition(this.deviceId()).toPromise();
      this.definition.set(def ?? null);

      // Definition now actively fetches state, but let's also do a separate
      // state fetch for maximum reliability
      this.refreshDeviceState();
    } catch (err: any) {
      console.error('Error loading definition:', err);
      // Don't set error - definition is optional
    } finally {
      this.loadingDefinition.set(false);
    }
  }

  /**
   * Actively request the current device state from Zigbee2MQTT.
   * This is more reliable than relying on cached/retained MQTT messages.
   */
  async refreshDeviceState() {
    const def = this.definition();
    if (!def) return;

    this.refreshingState.set(true);
    try {
      const response = await this.apiService.getDeviceState(this.deviceId()).toPromise();
      if (response && (response as any).state) {
        const newState = (response as any).state;

        // Merge the fresh state into the definition
        const updatedDef = DeviceDefinition.fromJS({
          ...def.toJSON(),
          currentState: { ...def.currentState, ...newState }
        });
        this.definition.set(updatedDef);
        console.log('Device state refreshed:', newState);
      }
    } catch (err: any) {
      console.warn('Could not refresh device state:', err.message);
      // Don't show error to user - this is a best-effort refresh
    } finally {
      this.refreshingState.set(false);
    }
  }

  async loadSensorReadings() {
    this.loadingReadings.set(true);

    try {
      const hours = this.getTimeRangeHours();
      const readings = await this.readingsService.getReadingsForDevice(
        this.deviceId(),
        500,  // take up to 500 readings
        hours
      ).toPromise();
      this.sensorReadings.set(readings ?? []);
    } catch (err: any) {
      console.error('Error loading sensor readings:', err);
      // Don't set error - readings are optional
    } finally {
      this.loadingReadings.set(false);
    }
  }

  private getTimeRangeHours(): number {
    switch (this.selectedTimeRange()) {
      case '1h': return 1;
      case '6h': return 6;
      case '24h': return 24;
      case '7d': return 168;
      default: return 24;
    }
  }

  setTimeRange(range: '1h' | '6h' | '24h' | '7d') {
    this.selectedTimeRange.set(range);
    this.loadSensorReadings();
  }

  private formatMetricName(metric: string): string {
    // Convert snake_case to Title Case
    return metric
      .replace(/_/g, ' ')
      .replace(/\b\w/g, c => c.toUpperCase());
  }

  // Get current value for a capability (pure read - no side effects)
  getValue(property: string): any {
    // Check pending state first (if not expired)
    const pending = this.pendingState();
    if (property in pending) {
      const entry = pending[property];
      // Only return pending value if not expired
      if (Date.now() - entry.timestamp < this.PENDING_STATE_TTL_MS) {
        return entry.value;
      }
      // Expired - don't clear here (would cause signal write during render)
      // Just fall through to return the actual state
    }
    // Return current state from definition
    return this.definition()?.currentState?.[property];
  }

  // Set value for a capability
  setValue(property: string, value: any) {
    this.pendingState.update((state) => ({
      ...state,
      [property]: { value, timestamp: Date.now() }
    }));
  }

  // Send state to device
  async sendState(property?: string) {
    // Check cooldown
    const now = Date.now();
    if (now - this.lastCommandTime < this.COMMAND_COOLDOWN_MS) {
      console.log('Command cooldown - skipping');
      return;
    }
    this.lastCommandTime = now;

    const pending = this.pendingState();
    const state = property
      ? { [property]: pending[property]?.value }
      : Object.fromEntries(
          Object.entries(pending).map(([k, v]) => [k, v.value])
        );

    if (Object.keys(state).length === 0) return;

    this.saving.set(true);

    try {
      const request = SetDeviceStateRequest.fromJS({ state });
      await this.apiService.setDeviceState(this.deviceId(), request).toPromise();
      // State will be updated via SignalR DeviceStateUpdate - no need to reload
    } catch (err: any) {
      console.error('Error sending state:', err);
      this.error.set(err.error?.error || 'Failed to send command');
      // Clear pending state on error so UI reverts
      if (property) {
        this.clearPendingState(property);
      } else {
        this.pendingState.set({});
      }
    } finally {
      this.saving.set(false);
    }
  }

  // Toggle a binary value
  toggleValue(capability: DeviceCapability) {
    const property = capability.property;
    if (!property) return;

    const current = this.getValue(property);
    const valueOn = capability.valueOn ?? 'ON';
    const valueOff = capability.valueOff ?? 'OFF';
    const newValue = current === valueOn ? valueOff : valueOn;

    this.setValue(property, newValue);
    this.sendState(property);
  }

  // Check if value is "on"
  isOn(capability: DeviceCapability): boolean {
    const property = capability.property;
    if (!property) return false;

    const value = this.getValue(property);
    const valueOn = capability.valueOn ?? 'ON';
    return value === valueOn || value === true || value === 1;
  }

  // Settings editing
  startEditSettings() {
    const device = this.device();
    if (device) {
      this.editedDevice.set({
        displayName: device.displayName,
        deviceType: device.deviceType,
        zoneId: device.zoneId,
      });
      this.editingSettings.set(true);
    }
  }

  cancelEditSettings() {
    this.editingSettings.set(false);
    this.editedDevice.set({});
  }

  async saveSettings() {
    const device = this.device();
    if (!device) return;

    this.saving.set(true);

    try {
      const updatedDevice = Device.fromJS({
        ...device.toJSON(),
        ...this.editedDevice(),
      });
      await this.apiService.updateDevice(device.deviceId!, updatedDevice).toPromise();
      await this.loadDevice();
      this.editingSettings.set(false);
    } catch (err: any) {
      console.error('Error saving settings:', err);
      this.error.set('Failed to save settings');
    } finally {
      this.saving.set(false);
    }
  }

  goBack() {
    this.router.navigate(['/devices']);
  }

  formatTimestamp(date: Date | string | undefined): string {
    if (!date) return '-';
    const d = new Date(date);
    return d.toLocaleTimeString('en-US', {
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false,
    });
  }

  getCategoryIcon(category: string): string {
    const icons: Record<string, string> = {
      light: '💡',
      switch: '🔘',
      sensor: '📊',
      climate: '🌡️',
      lock: '🔒',
      cover: '🪟',
      fan: '🌀',
      binary: '⚡',
      numeric: '🔢',
      enum: '📋',
    };
    return icons[category.toLowerCase()] || '⚙️';
  }

  getStateIcon(cap: DeviceCapability): string {
    const prop = cap.property?.toLowerCase() ?? '';
    const icons: Record<string, string> = {
      state: '💡',
      brightness: '🔆',
      color_temp: '🌡️',
      temperature: '🌡️',
      humidity: '💧',
      battery: '🔋',
      linkquality: '📶',
      occupancy: '🚶',
      contact: '🚪',
      illuminance: '☀️',
      pressure: '🌀',
      voltage: '⚡',
      power: '⚡',
      energy: '📊',
      action: '👆',
    };
    return icons[prop] || this.getCategoryIcon(cap.category || 'other');
  }

  formatStateValue(cap: DeviceCapability): string {
    const value = this.getValue(cap.property ?? '');
    if (value === null || value === undefined) return '-';

    // Format boolean/binary values
    if (cap.type === 'binary') {
      const valueOn = cap.valueOn ?? 'ON';
      return value === valueOn || value === true ? 'ON' : 'OFF';
    }

    // Format numeric values with precision
    if (typeof value === 'number') {
      if (Number.isInteger(value)) {
        return value.toString();
      }
      return value.toFixed(1);
    }

    return String(value);
  }

  formatPropertyName(prop: string): string {
    return prop
      .replace(/_/g, ' ')
      .replace(/\b\w/g, c => c.toUpperCase());
  }

  getExtraStateEntries(): Record<string, any> {
    const state = this.definition()?.currentState ?? {};
    const capProps = new Set(
      this.definition()?.capabilities?.map(c => c.property) ?? []
    );

    const extra: Record<string, any> = {};
    for (const [key, value] of Object.entries(state)) {
      if (!capProps.has(key)) {
        extra[key] = value;
      }
    }
    return extra;
  }

  getDeviceTypeLabel(type: DeviceType | undefined): string {
    if (type === undefined) return 'Unknown';
    return DeviceType[type] || 'Unknown';
  }

  // Template helper methods for edit form
  updateDisplayName(value: string) {
    this.editedDevice.update(d => ({ ...d, displayName: value }));
  }

  updateDeviceType(value: DeviceType) {
    this.editedDevice.update(d => ({ ...d, deviceType: value }));
  }

  // Get capability value with fallback (for template)
  getCapabilityValue(cap: DeviceCapability): any {
    return this.getValue(cap.property ?? '');
  }

  // Set capability value (for template)
  setCapabilityValue(cap: DeviceCapability, value: any) {
    if (cap.property) {
      this.setValue(cap.property, value);
    }
  }

  // Send capability state (for template)
  sendCapabilityState(cap: DeviceCapability) {
    if (cap.property) {
      this.sendState(cap.property);
    }
  }

  // Set and send in one (for selects)
  setAndSendCapability(cap: DeviceCapability, value: any) {
    if (cap.property) {
      this.setValue(cap.property, value);
      this.sendState(cap.property);
    }
  }

  refresh() {
    this.loadDevice();
    this.loadDefinition();
    this.loadSensorReadings();
  }
}
