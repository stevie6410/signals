import { Injectable, signal, computed } from '@angular/core';
import * as signalR from '@microsoft/signalr';
import { SignalEvent, SensorReading, TriggerEvent } from '../../api/sdhome-client';

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'reconnecting';

export interface DeviceSyncDevice {
  deviceId: string;
  friendlyName: string;
  manufacturer?: string;
  model?: string;
  deviceType?: string;
  isNew: boolean;
  isRemoved: boolean;
}

export interface DeviceSyncProgress {
  syncId: string;
  status: 'Started' | 'Connecting' | 'Subscribing' | 'WaitingForDevices' | 'DeviceReceived' | 'Processing' | 'DeviceProcessed' | 'Completed' | 'Failed';
  message: string;
  devicesFound: number;
  devicesProcessed: number;
  devicesTotal: number;
  currentDevice?: DeviceSyncDevice;
  discoveredDevices: DeviceSyncDevice[];
  timestamp: string;
  error?: string;
}

export interface DevicePairingDevice {
  ieeeAddress: string;
  friendlyName?: string;
  manufacturer?: string;
  model?: string;
  deviceType?: string;
  status: 'Discovered' | 'Interviewing' | 'Configuring' | 'Ready' | 'Failed';
  discoveredAt: string;
}

export interface DevicePairingProgress {
  pairingId: string;
  status: 'Starting' | 'Active' | 'Interviewing' | 'DevicePaired' | 'CountdownTick' | 'Stopping' | 'Ended' | 'Failed';
  message: string;
  remainingSeconds: number;
  totalDuration: number;
  isActive: boolean;
  currentDevice?: DevicePairingDevice;
  discoveredDevices: DevicePairingDevice[];
  timestamp: string;
  error?: string;
}

export interface DeviceStateUpdate {
  deviceId: string;
  state: Record<string, any>;
  timestampUtc: string;
}

export type AutomationLogLevel = 'Debug' | 'Info' | 'Warning' | 'Success' | 'Error';
export type AutomationLogPhase =
  | 'TriggerReceived'
  | 'TriggerEvaluating'
  | 'TriggerMatched'
  | 'TriggerSkipped'
  | 'ConditionEvaluating'
  | 'ConditionPassed'
  | 'ConditionFailed'
  | 'CooldownActive'
  | 'ActionExecuting'
  | 'ActionCompleted'
  | 'ActionFailed'
  | 'ExecutionCompleted'
  | 'ExecutionFailed';

export interface AutomationLogEntry {
  automationId: string;
  automationName: string;
  level: AutomationLogLevel;
  phase: AutomationLogPhase;
  message: string;
  details?: Record<string, any>;
  timestampUtc: string;
  durationMs?: number;
}

export interface PipelineStage {
  name: string;
  durationMs: number;
  startOffsetMs: number;
  category: 'signal' | 'db' | 'broadcast' | 'automation' | 'mqtt' | 'webhook' | 'other';
  isSuccess: boolean;
}

export interface PipelineTimeline {
  id: string;
  deviceId: string;
  automationName?: string;
  timestampUtc: string;
  totalMs: number;
  stages: PipelineStage[];
}

@Injectable({
  providedIn: 'root'
})
export class SignalRService {
  private hubConnection: signalR.HubConnection | null = null;

  // Reactive state
  private _connectionState = signal<ConnectionState>('disconnected');
  private _latestSignal = signal<SignalEvent | null>(null);
  private _latestReading = signal<SensorReading | null>(null);
  private _latestTrigger = signal<TriggerEvent | null>(null);
  private _signalHistory = signal<SignalEvent[]>([]);
  private _readingHistory = signal<SensorReading[]>([]);
  private _triggerHistory = signal<TriggerEvent[]>([]);
  private _deviceSyncProgress = signal<DeviceSyncProgress | null>(null);
  private _devicePairingProgress = signal<DevicePairingProgress | null>(null);
  private _deviceStateUpdates = signal<Map<string, DeviceStateUpdate>>(new Map());
  private _automationLogs = signal<AutomationLogEntry[]>([]);
  private _automationLogsByAutomation = signal<Map<string, AutomationLogEntry[]>>(new Map());
  private _pipelineTimelines = signal<PipelineTimeline[]>([]);

  // Public readable signals
  readonly connectionState = this._connectionState.asReadonly();
  readonly latestSignal = this._latestSignal.asReadonly();
  readonly latestReading = this._latestReading.asReadonly();
  readonly latestTrigger = this._latestTrigger.asReadonly();
  readonly signalHistory = this._signalHistory.asReadonly();
  readonly readingHistory = this._readingHistory.asReadonly();
  readonly triggerHistory = this._triggerHistory.asReadonly();
  readonly deviceSyncProgress = this._deviceSyncProgress.asReadonly();
  readonly devicePairingProgress = this._devicePairingProgress.asReadonly();
  readonly deviceStateUpdates = this._deviceStateUpdates.asReadonly();
  readonly automationLogs = this._automationLogs.asReadonly();
  readonly automationLogsByAutomation = this._automationLogsByAutomation.asReadonly();
  readonly pipelineTimelines = this._pipelineTimelines.asReadonly();

  // Computed values
  readonly isConnected = computed(() => this._connectionState() === 'connected');
  readonly signalCount = computed(() => this._signalHistory().length);
  readonly readingCount = computed(() => this._readingHistory().length);
  readonly triggerCount = computed(() => this._triggerHistory().length);
  readonly isSyncing = computed(() => {
    const progress = this._deviceSyncProgress();
    return progress !== null && progress.status !== 'Completed' && progress.status !== 'Failed';
  });
  readonly isPairing = computed(() => {
    const progress = this._devicePairingProgress();
    return progress !== null && progress.isActive;
  });

  private maxHistorySize = 100;

  constructor() {
    this.initializeConnection();
  }

  private initializeConnection(): void {
    this.hubConnection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/signals')
      .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
      .configureLogging(signalR.LogLevel.Information)
      .build();

    this.setupEventHandlers();
    this.setupConnectionStateHandlers();
  }

  private setupEventHandlers(): void {
    if (!this.hubConnection) return;

    // Main signal event handler
    this.hubConnection.on('SignalReceived', (data: any) => {
      const signal = SignalEvent.fromJS(data);
      this._latestSignal.set(signal);
      this._signalHistory.update(history => {
        const updated = [signal, ...history];
        return updated.slice(0, this.maxHistorySize);
      });
    });

    // Sensor reading handler
    this.hubConnection.on('ReadingReceived', (data: any) => {
      const reading = SensorReading.fromJS(data);
      this._latestReading.set(reading);
      this._readingHistory.update(history => {
        const updated = [reading, ...history];
        return updated.slice(0, this.maxHistorySize);
      });
    });

    // Trigger event handler
    this.hubConnection.on('TriggerReceived', (data: any) => {
      const trigger = TriggerEvent.fromJS(data);
      this._latestTrigger.set(trigger);
      this._triggerHistory.update(history => {
        const updated = [trigger, ...history];
        return updated.slice(0, this.maxHistorySize);
      });
    });

    // Device-specific handlers (for when subscribed to a specific device)
    this.hubConnection.on('DeviceSignalReceived', (data: any) => {
      // Can be used for device-specific UI updates
      console.log('Device signal:', data);
    });

    this.hubConnection.on('DeviceReadingReceived', (data: any) => {
      console.log('Device reading:', data);
    });

    this.hubConnection.on('DeviceTriggerReceived', (data: any) => {
      console.log('Device trigger:', data);
    });

    // Device sync progress handler
    this.hubConnection.on('DeviceSyncProgress', (data: DeviceSyncProgress) => {
      console.log('Device sync progress:', data);
      this._deviceSyncProgress.set(data);
    });

    // Device pairing progress handler
    this.hubConnection.on('DevicePairingProgress', (data: DevicePairingProgress) => {
      console.log('Device pairing progress:', data);
      this._devicePairingProgress.set(data);
    });

    // Device state update handler - for instant UI updates
    this.hubConnection.on('DeviceStateUpdate', (data: DeviceStateUpdate) => {
      console.log('Device state update:', data.deviceId, data.state);
      this._deviceStateUpdates.update(map => {
        const newMap = new Map(map);
        newMap.set(data.deviceId, data);
        return newMap;
      });
    });

    // Automation log handler - for automation console monitoring
    this.hubConnection.on('AutomationLog', (data: AutomationLogEntry) => {
      console.log('Automation log:', data.automationName, data.phase, data.message);

      // Add to global logs
      this._automationLogs.update(logs => {
        const updated = [data, ...logs];
        return updated.slice(0, this.maxHistorySize);
      });

      // Add to automation-specific logs
      this._automationLogsByAutomation.update(map => {
        const newMap = new Map(map);
        const existing = newMap.get(data.automationId) || [];
        const updated = [data, ...existing].slice(0, this.maxHistorySize);
        newMap.set(data.automationId, updated);
        return newMap;
      });
    });

    // Pipeline timeline handler - for latency visualization
    this.hubConnection.on('PipelineTimeline', (data: PipelineTimeline) => {
      console.log('Pipeline timeline:', data.deviceId, `${data.totalMs}ms`, data.stages.length, 'stages');

      this._pipelineTimelines.update(timelines => {
        const updated = [data, ...timelines];
        return updated.slice(0, this.maxHistorySize);
      });
    });
  }

  private setupConnectionStateHandlers(): void {
    if (!this.hubConnection) return;

    this.hubConnection.onreconnecting(() => {
      this._connectionState.set('reconnecting');
      console.log('SignalR: Reconnecting...');
    });

    this.hubConnection.onreconnected(() => {
      this._connectionState.set('connected');
      console.log('SignalR: Reconnected');
    });

    this.hubConnection.onclose(() => {
      this._connectionState.set('disconnected');
      console.log('SignalR: Connection closed');
    });
  }

  async connect(): Promise<void> {
    if (!this.hubConnection) {
      this.initializeConnection();
    }

    // Only attempt to connect if in disconnected state
    if (this.hubConnection?.state !== signalR.HubConnectionState.Disconnected) {
      console.log('SignalR: Already connected or connecting, state:', this.hubConnection?.state);
      return;
    }

    try {
      this._connectionState.set('connecting');
      await this.hubConnection!.start();
      this._connectionState.set('connected');
      console.log('SignalR: Connected to signals hub');
    } catch (error) {
      this._connectionState.set('disconnected');
      console.error('SignalR: Connection failed', error);
      // Retry after delay
      setTimeout(() => this.connect(), 5000);
    }
  }

  async disconnect(): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      await this.hubConnection.stop();
      this._connectionState.set('disconnected');
    }
  }

  async subscribeToDevice(deviceId: string): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      await this.hubConnection.invoke('SubscribeToDevice', deviceId);
    }
  }

  async unsubscribeFromDevice(deviceId: string): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      await this.hubConnection.invoke('UnsubscribeFromDevice', deviceId);
    }
  }

  clearHistory(): void {
    this._signalHistory.set([]);
    this._readingHistory.set([]);
    this._triggerHistory.set([]);
    this._latestSignal.set(null);
    this._latestReading.set(null);
    this._latestTrigger.set(null);
  }

  clearSyncProgress(): void {
    this._deviceSyncProgress.set(null);
  }

  clearPairingProgress(): void {
    this._devicePairingProgress.set(null);
  }

  getDeviceState(deviceId: string): DeviceStateUpdate | undefined {
    return this._deviceStateUpdates().get(deviceId);
  }

  clearDeviceState(deviceId: string): void {
    this._deviceStateUpdates.update(map => {
      const newMap = new Map(map);
      newMap.delete(deviceId);
      return newMap;
    });
  }

  async subscribeToDeviceSync(syncId: string): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      await this.hubConnection.invoke('SubscribeToDeviceSync', syncId);
    }
  }

  async unsubscribeFromDeviceSync(syncId: string): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      await this.hubConnection.invoke('UnsubscribeFromDeviceSync', syncId);
    }
  }

  async subscribeToPairing(pairingId: string): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      await this.hubConnection.invoke('SubscribeToPairing', pairingId);
    }
  }

  async unsubscribeFromPairing(pairingId: string): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      await this.hubConnection.invoke('UnsubscribeFromPairing', pairingId);
    }
  }

  async subscribeToAutomation(automationId: string): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      await this.hubConnection.invoke('SubscribeToAutomation', automationId);
    }
  }

  async unsubscribeFromAutomation(automationId: string): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      await this.hubConnection.invoke('UnsubscribeFromAutomation', automationId);
    }
  }

  async subscribeToAllAutomations(): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      await this.hubConnection.invoke('SubscribeToAllAutomations');
    }
  }

  async unsubscribeFromAllAutomations(): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      await this.hubConnection.invoke('UnsubscribeFromAllAutomations');
    }
  }

  getAutomationLogs(automationId: string): AutomationLogEntry[] {
    return this._automationLogsByAutomation().get(automationId) || [];
  }

  clearAutomationLogs(automationId?: string): void {
    if (automationId) {
      this._automationLogsByAutomation.update(map => {
        const newMap = new Map(map);
        newMap.delete(automationId);
        return newMap;
      });
    } else {
      this._automationLogs.set([]);
      this._automationLogsByAutomation.set(new Map());
    }
  }

  async subscribeToPipelineTimelines(): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      await this.hubConnection.invoke('SubscribeToPipelineTimelines');
    }
  }

  async unsubscribeFromPipelineTimelines(): Promise<void> {
    if (this.hubConnection?.state === signalR.HubConnectionState.Connected) {
      await this.hubConnection.invoke('UnsubscribeFromPipelineTimelines');
    }
  }

  clearPipelineTimelines(): void {
    this._pipelineTimelines.set([]);
  }

  setMaxHistorySize(size: number): void {
    this.maxHistorySize = size;
  }
}
