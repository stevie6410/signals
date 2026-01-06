import { Component, OnInit, OnDestroy, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule } from '@angular/router';
import { ZonesApiService, ZoneWithCapabilities, ZoneCapabilityAssignment, DevicesApiService } from '../../api/sdhome-client';
import { SignalRService } from '../../core/services/signalr.service';
import { forkJoin, of } from 'rxjs';
import { catchError, map } from 'rxjs/operators';

// Capability display configuration
// Sensor capabilities that we want to show on the dashboard
const SENSOR_CAPABILITIES = new Set([
  'temperature', 'humidity', 'motion', 'presence', 'contact', 'illuminance', 'co2', 'air_quality'
]);

const CAPABILITY_CONFIG: Record<string, {
  icon: string;
  label: string;
  unit?: string;
  property: string;
  format?: (v: any) => string;
}> = {
  temperature: { icon: '🌡️', label: 'Temp', unit: '°', property: 'temperature', format: (v) => `${Number(v).toFixed(1)}` },
  humidity: { icon: '💧', label: 'Humidity', unit: '%', property: 'humidity', format: (v) => `${Math.round(v)}` },
  motion: { icon: '🏃', label: 'Motion', property: 'occupancy', format: (v) => v ? 'Active' : 'Clear' },
  presence: { icon: '👤', label: 'Presence', property: 'presence', format: (v) => v ? 'Occupied' : 'Empty' },
  contact: { icon: '🚪', label: 'Door', property: 'contact', format: (v) => v ? 'Closed' : 'Open' },
  illuminance: { icon: '☀️', label: 'Light', unit: 'lx', property: 'illuminance', format: (v) => `${Math.round(v)}` },
  co2: { icon: '🫁', label: 'CO₂', unit: 'ppm', property: 'co2', format: (v) => `${Math.round(v)}` },
  air_quality: { icon: '🌬️', label: 'Air', property: 'air_quality' },
  lights: { icon: '💡', label: 'Lights', property: 'state', format: (v) => v === 'ON' || v === true ? 'On' : 'Off' },
  main_light: { icon: '💡', label: 'Light', property: 'state', format: (v) => v === 'ON' || v === true ? 'On' : 'Off' },
  accent_light: { icon: '✨', label: 'Accent', property: 'state', format: (v) => v === 'ON' || v === true ? 'On' : 'Off' },
  climate: { icon: '🏠', label: 'Climate', property: 'state' },
  heating: { icon: '🔥', label: 'Heat', property: 'state', format: (v) => v === 'ON' || v === true ? 'On' : 'Off' },
  cooling: { icon: '❄️', label: 'Cool', property: 'state', format: (v) => v === 'ON' || v === true ? 'On' : 'Off' },
  fan: { icon: '🌀', label: 'Fan', property: 'state', format: (v) => v === 'ON' || v === true ? 'On' : 'Off' },
  cover: { icon: '🪟', label: 'Blinds', property: 'position', format: (v) => `${v}%` },
  lock: { icon: '🔒', label: 'Lock', property: 'state', format: (v) => v === 'LOCKED' || v === true ? '🔒' : '🔓' },
  switch: { icon: '🔘', label: 'Switch', property: 'state', format: (v) => v === 'ON' || v === true ? 'On' : 'Off' },
  speaker: { icon: '🔊', label: 'Speaker', property: 'state' },
  display: { icon: '🖥️', label: 'Display', property: 'state' },
  tv: { icon: '📺', label: 'TV', property: 'state', format: (v) => v === 'ON' || v === true ? 'On' : 'Off' },
};

interface ZoneDisplayData {
  zone: ZoneWithCapabilities;
  temperature?: { value: number; formatted: string };
  humidity?: { value: number; formatted: string };
  motion?: boolean;
  presence?: boolean;
  contact?: boolean;
  illuminance?: { value: number; formatted: string };
  co2?: { value: number; formatted: string };
  lightsOn?: boolean;
  isOccupied: boolean;
  hasAlerts: boolean;
  capabilities: { key: string; value: any; formatted: string; config: typeof CAPABILITY_CONFIG[string] }[];
}

@Component({
  selector: 'app-home',
  standalone: true,
  imports: [CommonModule, RouterModule],
  templateUrl: './home.component.html',
  styleUrl: './home.component.scss'
})
export class HomeComponent implements OnInit, OnDestroy {
  private zonesApi = inject(ZonesApiService);
  private devicesApi = inject(DevicesApiService);
  private signalR = inject(SignalRService);

  zones = signal<ZoneWithCapabilities[]>([]);
  loading = signal(true);
  error = signal<string | null>(null);

  // Local device states that we populate on load and update via SignalR
  private localDeviceStates = signal<Map<string, { state: Record<string, any> }>>(new Map());

  // Merge local states with SignalR updates (SignalR takes precedence for live updates)
  deviceStates = computed(() => {
    const local = this.localDeviceStates();
    const signalRStates = this.signalR.deviceStateUpdates();

    // Start with local states, overlay SignalR updates
    const merged = new Map(local);
    signalRStates.forEach((value, key) => {
      merged.set(key, value);
    });
    return merged;
  });

  isConnected = this.signalR.isConnected;
  connectionState = this.signalR.connectionState;

  // Computed zone display data with real-time values
  zoneDisplayData = computed(() => {
    const states = this.deviceStates();
    const zonesData: ZoneDisplayData[] = [];

    for (const zone of this.zones()) {
      const data: ZoneDisplayData = {
        zone,
        isOccupied: false,
        hasAlerts: false,
        capabilities: [],
      };

      for (const assignment of zone.capabilityAssignments || []) {
        const deviceId = assignment.deviceId;
        const capability = assignment.capability;
        if (!deviceId || !capability) continue;

        const deviceState = states.get(deviceId);
        if (!deviceState) continue;

        const config = CAPABILITY_CONFIG[capability];
        if (!config) continue;

        const property = assignment.property || config.property;
        const rawValue = deviceState.state[property];
        if (rawValue === undefined) continue;

        const formatted = config.format ? config.format(rawValue) : String(rawValue);
        data.capabilities.push({ key: capability, value: rawValue, formatted, config });

        switch (capability) {
          case 'temperature':
            data.temperature = { value: Number(rawValue), formatted };
            break;
          case 'humidity':
            data.humidity = { value: Number(rawValue), formatted };
            break;
          case 'motion':
            data.motion = !!rawValue;
            if (rawValue) data.isOccupied = true;
            break;
          case 'presence':
            data.presence = !!rawValue;
            if (rawValue) data.isOccupied = true;
            break;
          case 'contact':
            data.contact = !!rawValue;
            if (!rawValue) data.hasAlerts = true;
            break;
          case 'illuminance':
            data.illuminance = { value: Number(rawValue), formatted };
            break;
          case 'co2':
            data.co2 = { value: Number(rawValue), formatted };
            if (Number(rawValue) > 1000) data.hasAlerts = true;
            break;
          case 'lights':
          case 'main_light':
            data.lightsOn = rawValue === 'ON' || rawValue === true;
            break;
        }
      }

      // Only include zones that have at least one sensor capability
      const hasSensorCapability = (zone.capabilityAssignments || []).some(a =>
        a.capability && SENSOR_CAPABILITIES.has(a.capability)
      );
      if (hasSensorCapability) {
        zonesData.push(data);
      }
    }

    return zonesData;
  });

  // Summary stats
  totalZones = computed(() => this.zones().length);
  occupiedZones = computed(() => this.zoneDisplayData().filter(z => z.isOccupied).length);
  alertZones = computed(() => this.zoneDisplayData().filter(z => z.hasAlerts).length);

  avgTemperature = computed(() => {
    const temps = this.zoneDisplayData().filter(z => z.temperature).map(z => z.temperature!.value);
    if (temps.length === 0) return null;
    return (temps.reduce((a, b) => a + b, 0) / temps.length).toFixed(1);
  });

  avgHumidity = computed(() => {
    const vals = this.zoneDisplayData().filter(z => z.humidity).map(z => z.humidity!.value);
    if (vals.length === 0) return null;
    return Math.round(vals.reduce((a, b) => a + b, 0) / vals.length);
  });

  lightsOnCount = computed(() => this.zoneDisplayData().filter(z => z.lightsOn).length);

  ngOnInit() {
    this.loadZones();
    // SignalR connection is managed by the service - no need to call connect() here
    // The service handles reconnection automatically
  }

  ngOnDestroy() {}

  async loadZones() {
    this.loading.set(true);
    this.error.set(null);

    try {
      const zones = await this.zonesApi.getZonesForDashboard().toPromise();
      this.zones.set(zones || []);

      // Collect unique device IDs from capability assignments
      const deviceIds = new Set<string>();
      for (const zone of zones || []) {
        for (const assignment of zone.capabilityAssignments || []) {
          if (assignment.deviceId) {
            deviceIds.add(assignment.deviceId);
          }
        }
      }

      // Fetch initial state for each device
      if (deviceIds.size > 0) {
        await this.loadDeviceStates(Array.from(deviceIds));
      }
    } catch (err) {
      console.error('Failed to load zones:', err);
      this.error.set('Failed to load zones');
    } finally {
      this.loading.set(false);
    }
  }

  private async loadDeviceStates(deviceIds: string[]) {
    // Fetch states in parallel
    const stateRequests = deviceIds.map(id =>
      this.devicesApi.getDeviceState(id).pipe(
        map(state => ({ deviceId: id, state })),
        catchError(err => {
          console.warn(`Failed to get state for device ${id}:`, err);
          return of(null);
        })
      )
    );

    try {
      const results = await forkJoin(stateRequests).toPromise();
      const statesMap = new Map<string, { state: Record<string, any> }>();

      for (const result of results || []) {
        if (result && result.state) {
          statesMap.set(result.deviceId, { state: result.state });
        }
      }

      this.localDeviceStates.set(statesMap);
      console.log(`Loaded initial states for ${statesMap.size} devices`);
    } catch (err) {
      console.error('Failed to load device states:', err);
    }
  }

  getTemperatureColor(temp: number): string {
    if (temp < 16) return 'freezing';
    if (temp < 19) return 'cold';
    if (temp <= 23) return 'comfortable';
    if (temp <= 26) return 'warm';
    return 'hot';
  }

  getHumidityColor(humidity: number): string {
    if (humidity < 30) return 'dry';
    if (humidity > 60) return 'humid';
    return 'comfortable';
  }

  getCo2Color(co2: number): string {
    if (co2 < 600) return 'excellent';
    if (co2 < 1000) return 'good';
    if (co2 < 1500) return 'moderate';
    return 'poor';
  }

  getLuxLevel(lux: number): string {
    if (lux < 50) return 'dark';
    if (lux < 200) return 'dim';
    if (lux < 500) return 'moderate';
    return 'bright';
  }

  trackZone(index: number, data: ZoneDisplayData): number {
    return data.zone.id || index;
  }
}
