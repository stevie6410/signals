import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterModule, ActivatedRoute, Router } from '@angular/router';
import { FormsModule } from '@angular/forms';
import {
  AutomationsApiService,
  DevicesApiService,
  AutomationRule,
  CreateAutomationRequest,
  UpdateAutomationRequest,
  CreateTriggerRequest,
  CreateConditionRequest,
  CreateActionRequest,
  TriggerType,
  ConditionType,
  ActionType,
  ComparisonOperator,
  TriggerMode,
  ConditionMode,
  Device,
  DeviceDefinition,
  DeviceCapability,
  ControlType,
} from '../../../api/sdhome-client';
import { AutomationConsoleComponent } from '../automation-console/automation-console.component';
import { PipelineTimelineComponent } from '../pipeline-timeline/pipeline-timeline.component';

interface CachedDeviceDefinition {
  definition: DeviceDefinition;
  capabilities: DeviceCapability[];
  propertyOptions: PropertyOption[];
}

interface PropertyOption {
  property: string;
  name: string;
  type: string;
  values?: string[];
  valueMin?: number;
  valueMax?: number;
  valueStep?: number;
  valueOn?: any;
  valueOff?: any;
  unit?: string;
  controlType?: ControlType;
  canWrite: boolean;
  canRead: boolean;
}

@Component({
  selector: 'app-automation-editor',
  standalone: true,
  imports: [CommonModule, RouterModule, FormsModule, AutomationConsoleComponent, PipelineTimelineComponent],
  templateUrl: './automation-editor.component.html',
  styleUrl: './automation-editor.component.scss',
})
export class AutomationEditorComponent implements OnInit {
  private route = inject(ActivatedRoute);
  private router = inject(Router);
  private automationsService = inject(AutomationsApiService);
  private devicesService = inject(DevicesApiService);

  // Enums for templates
  TriggerType = TriggerType;
  ConditionType = ConditionType;
  ActionType = ActionType;
  ComparisonOperator = ComparisonOperator;
  TriggerMode = TriggerMode;
  ConditionMode = ConditionMode;

  // State
  automationId = signal<string | null>(null);
  isNew = computed(() => !this.automationId());
  loading = signal(true);
  saving = signal(false);
  devices = signal<Device[]>([]);

  // Device definitions cache - keyed by deviceId
  deviceDefinitions = signal<Map<string, CachedDeviceDefinition>>(new Map());
  loadingDefinitions = signal<Set<string>>(new Set());

  // Form state
  name = signal('');
  description = signal('');
  icon = signal('⚡');
  color = signal('#00f5ff');
  isEnabled = signal(true);
  triggerMode = signal<TriggerMode>(TriggerMode.Any);
  conditionMode = signal<ConditionMode>(ConditionMode.All);
  cooldownSeconds = signal(0);

  triggers = signal<TriggerFormItem[]>([]);
  conditions = signal<ConditionFormItem[]>([]);
  actions = signal<ActionFormItem[]>([]);

  // Console visibility
  showConsole = signal(true);
  showPipelineTimeline = signal(false);

  // Icon options
  iconOptions = ['⚡', '💡', '🌡️', '🔔', '🏠', '🌙', '☀️', '🎬', '🔒', '🚪', '💨', '🎵'];

  // Color options
  colorOptions = [
    '#00f5ff', // Cyan
    '#8b5cf6', // Purple
    '#10b981', // Green
    '#f59e0b', // Amber
    '#ef4444', // Red
    '#3b82f6', // Blue
    '#ec4899', // Pink
    '#6366f1', // Indigo
  ];

  ngOnInit() {
    this.loadDevices();

    this.route.params.subscribe((params) => {
      const id = params['id'];
      if (id && id !== 'new') {
        this.automationId.set(id);
        this.loadAutomation(id);
      } else {
        this.loading.set(false);
        // Add default trigger and action
        this.addTrigger();
        this.addAction();
      }
    });
  }

  async loadDevices() {
    try {
      const data = await this.devicesService.getDevices().toPromise();
      this.devices.set(data || []);
    } catch (error) {
      console.error('Error loading devices:', error);
    }
  }

  // Load device definition with capabilities
  loadDeviceDefinition(deviceId: string): void {
    if (!deviceId) return;

    // Check cache first
    const cached = this.deviceDefinitions().get(deviceId);
    if (cached) return;

    // Check if already loading
    if (this.loadingDefinitions().has(deviceId)) return;

    // Use setTimeout to defer signal updates outside of template rendering
    setTimeout(() => {
      this.loadDeviceDefinitionAsync(deviceId);
    }, 0);
  }

  // Async method to actually load the definition
  private async loadDeviceDefinitionAsync(deviceId: string): Promise<void> {
    // Double-check cache (might have loaded while waiting)
    if (this.deviceDefinitions().get(deviceId)) return;
    if (this.loadingDefinitions().has(deviceId)) return;

    // Mark as loading
    this.loadingDefinitions.update((set) => new Set(set).add(deviceId));

    try {
      const definition = await this.devicesService.getDeviceDefinition(deviceId).toPromise();
      if (!definition) return;

      const capabilities = definition.capabilities || [];
      const propertyOptions = this.extractPropertyOptions(capabilities);

      const cached: CachedDeviceDefinition = {
        definition,
        capabilities,
        propertyOptions,
      };

      // Update cache
      this.deviceDefinitions.update((map) => {
        const newMap = new Map(map);
        newMap.set(deviceId, cached);
        return newMap;
      });
    } catch (error) {
      console.error(`Error loading device definition for ${deviceId}:`, error);
    } finally {
      this.loadingDefinitions.update((set) => {
        const newSet = new Set(set);
        newSet.delete(deviceId);
        return newSet;
      });
    }
  }

  // Extract all properties from capabilities (including nested)
  private extractPropertyOptions(capabilities: DeviceCapability[], prefix = ''): PropertyOption[] {
    const options: PropertyOption[] = [];

    for (const cap of capabilities) {
      if (cap.property) {
        options.push({
          property: cap.property,
          name: cap.name || cap.property,
          type: cap.type || 'unknown',
          values: cap.values,
          valueMin: cap.valueMin,
          valueMax: cap.valueMax,
          valueStep: cap.valueStep,
          valueOn: cap.valueOn,
          valueOff: cap.valueOff,
          unit: cap.unit,
          controlType: cap.controlType,
          canWrite: cap.access?.canWrite ?? false,
          canRead: cap.access?.canRead ?? true,
        });
      }

      // Recurse into nested features
      if (cap.features && cap.features.length > 0) {
        options.push(...this.extractPropertyOptions(cap.features, cap.property ? `${cap.property}.` : ''));
      }
    }

    return options;
  }

  // Get properties for a device (triggers - things we can watch)
  getTriggerProperties(deviceId: string): PropertyOption[] {
    const cached = this.deviceDefinitions().get(deviceId);
    if (!cached) {
      // Trigger load if not cached
      this.loadDeviceDefinition(deviceId);
      return [];
    }
    // For triggers, we want properties that are published (readable)
    return cached.propertyOptions.filter((p) => p.canRead);
  }

  // Get properties for a device (actions - things we can set)
  getActionProperties(deviceId: string): PropertyOption[] {
    const cached = this.deviceDefinitions().get(deviceId);
    if (!cached) {
      // Trigger load if not cached
      this.loadDeviceDefinition(deviceId);
      return [];
    }
    // For actions, we want writable properties
    return cached.propertyOptions.filter((p) => p.canWrite);
  }

  // Get values for a property
  getPropertyValues(deviceId: string, property: string): string[] {
    const props = this.deviceDefinitions().get(deviceId)?.propertyOptions || [];
    const prop = props.find((p) => p.property === property);
    return prop?.values || [];
  }

  // Get property info
  getPropertyInfo(deviceId: string, property: string): PropertyOption | undefined {
    const props = this.deviceDefinitions().get(deviceId)?.propertyOptions || [];
    return props.find((p) => p.property === property);
  }

  // Check if a property has enum values (like button actions)
  hasEnumValues(deviceId: string, property: string): boolean {
    const prop = this.getPropertyInfo(deviceId, property);
    return !!prop?.values && prop.values.length > 0;
  }

  // Check if a property is binary (on/off)
  isBinaryProperty(deviceId: string, property: string): boolean {
    const prop = this.getPropertyInfo(deviceId, property);
    return prop?.type === 'binary' || (prop?.valueOn !== undefined && prop?.valueOff !== undefined);
  }

  // Check if a property is numeric (slider)
  isNumericProperty(deviceId: string, property: string): boolean {
    const prop = this.getPropertyInfo(deviceId, property);
    return prop?.type === 'numeric' && (prop?.valueMin !== undefined || prop?.valueMax !== undefined);
  }

  // Handle device selection change - load its definition
  onDeviceSelected(deviceId: string) {
    if (deviceId) {
      this.loadDeviceDefinition(deviceId);
    }
  }

  // Get suggested operators based on property type
  getSuggestedOperators(deviceId: string, property: string): ComparisonOperator[] {
    const prop = this.getPropertyInfo(deviceId, property);
    if (!prop) return [ComparisonOperator.Equals, ComparisonOperator.ChangesTo, ComparisonOperator.AnyChange];

    if (prop.type === 'binary') {
      return [ComparisonOperator.ChangesTo, ComparisonOperator.ChangesFrom, ComparisonOperator.Equals];
    }

    if (prop.type === 'enum') {
      return [ComparisonOperator.Equals, ComparisonOperator.NotEquals, ComparisonOperator.ChangesTo];
    }

    if (prop.type === 'numeric') {
      return [
        ComparisonOperator.Equals,
        ComparisonOperator.GreaterThan,
        ComparisonOperator.LessThan,
        ComparisonOperator.GreaterThanOrEqual,
        ComparisonOperator.LessThanOrEqual,
        ComparisonOperator.Between,
        ComparisonOperator.ChangesTo,
        ComparisonOperator.AnyChange,
      ];
    }

    // Default for text/action types
    return [ComparisonOperator.Equals, ComparisonOperator.ChangesTo, ComparisonOperator.AnyChange];
  }

  async loadAutomation(id: string) {
    this.loading.set(true);
    try {
      const automation = await this.automationsService.getAutomation(id).toPromise();
      if (automation) {
        // Preload device definitions for all devices used in this automation
        await this.preloadDeviceDefinitions(automation);
        this.populateForm(automation);
      }
    } catch (error) {
      console.error('Error loading automation:', error);
      this.router.navigate(['/automations']);
    } finally {
      this.loading.set(false);
    }
  }

  // Preload device definitions for all devices in an automation
  private async preloadDeviceDefinitions(automation: AutomationRule): Promise<void> {
    const deviceIds = new Set<string>();

    // Collect device IDs from triggers, conditions, and actions
    automation.triggers?.forEach(t => { if (t.deviceId) deviceIds.add(t.deviceId); });
    automation.conditions?.forEach(c => { if (c.deviceId) deviceIds.add(c.deviceId); });
    automation.actions?.forEach(a => { if (a.deviceId) deviceIds.add(a.deviceId); });

    // Load all device definitions in parallel
    const loadPromises = Array.from(deviceIds).map(async (deviceId) => {
      try {
        const definition = await this.devicesService.getDeviceDefinition(deviceId).toPromise();
        if (definition) {
          const capabilities = definition.capabilities || [];
          const propertyOptions = this.extractPropertyOptions(capabilities);
          const cached: CachedDeviceDefinition = { definition, capabilities, propertyOptions };

          this.deviceDefinitions.update((map) => {
            const newMap = new Map(map);
            newMap.set(deviceId, cached);
            return newMap;
          });
        }
      } catch (error) {
        console.error(`Error preloading device definition for ${deviceId}:`, error);
      }
    });

    await Promise.all(loadPromises);
  }

  populateForm(automation: AutomationRule) {
    this.name.set(automation.name || '');
    this.description.set(automation.description || '');
    this.icon.set(automation.icon || '⚡');
    this.color.set(automation.color || '#00f5ff');
    this.isEnabled.set(automation.isEnabled ?? true);
    this.triggerMode.set(automation.triggerMode ?? TriggerMode.Any);
    this.conditionMode.set(automation.conditionMode ?? ConditionMode.All);
    this.cooldownSeconds.set(automation.cooldownSeconds ?? 0);

    // Map triggers
    this.triggers.set(
      (automation.triggers || []).map((t, i) => ({
        id: crypto.randomUUID(),
        triggerType: t.triggerType ?? TriggerType.DeviceState,
        deviceId: t.deviceId || '',
        property: t.property || '',
        operator: t.operator ?? ComparisonOperator.Equals,
        value: t.value?.toString() || '',
        timeExpression: t.timeExpression || '',
        sunEvent: t.sunEvent || '',
        offsetMinutes: t.offsetMinutes ?? 0,
        sortOrder: t.sortOrder ?? i,
      }))
    );

    // Map conditions
    this.conditions.set(
      (automation.conditions || []).map((c, i) => ({
        id: crypto.randomUUID(),
        conditionType: c.conditionType ?? ConditionType.DeviceState,
        deviceId: c.deviceId || '',
        property: c.property || '',
        operator: c.operator ?? ComparisonOperator.Equals,
        value: c.value?.toString() || '',
        value2: c.value2?.toString() || '',
        timeStart: c.timeStart || '',
        timeEnd: c.timeEnd || '',
        daysOfWeek: (c.daysOfWeek || []).map(d => d as number),
        sortOrder: c.sortOrder ?? i,
      }))
    );

    // Map actions
    this.actions.set(
      (automation.actions || []).map((a, i) => ({
        id: crypto.randomUUID(),
        actionType: a.actionType ?? ActionType.SetDeviceState,
        deviceId: a.deviceId || '',
        property: a.property || '',
        value: a.value?.toString() || '',
        delaySeconds: a.delaySeconds ?? 0,
        webhookUrl: a.webhookUrl || '',
        webhookMethod: a.webhookMethod || 'POST',
        webhookBody: a.webhookBody || '',
        notificationTitle: a.notificationTitle || '',
        notificationMessage: a.notificationMessage || '',
        sceneId: a.sceneId || undefined,
        sortOrder: a.sortOrder ?? i,
      }))
    );
  }

  // Trigger management
  addTrigger() {
    this.triggers.update((list) => [
      ...list,
      {
        id: crypto.randomUUID(),
        triggerType: TriggerType.DeviceState,
        deviceId: '',
        property: '',
        operator: ComparisonOperator.ChangesTo,
        value: '',
        timeExpression: '',
        sunEvent: '',
        offsetMinutes: 0,
        sortOrder: list.length,
      },
    ]);
  }

  removeTrigger(id: string) {
    this.triggers.update((list) => list.filter((t) => t.id !== id));
  }

  // Condition management
  addCondition() {
    this.conditions.update((list) => [
      ...list,
      {
        id: crypto.randomUUID(),
        conditionType: ConditionType.DeviceState,
        deviceId: '',
        property: '',
        operator: ComparisonOperator.Equals,
        value: '',
        value2: '',
        timeStart: '',
        timeEnd: '',
        daysOfWeek: [],
        sortOrder: list.length,
      },
    ]);
  }

  removeCondition(id: string) {
    this.conditions.update((list) => list.filter((c) => c.id !== id));
  }

  toggleDay(condition: ConditionFormItem, day: number) {
    const idx = condition.daysOfWeek.indexOf(day);
    if (idx === -1) {
      condition.daysOfWeek.push(day);
    } else {
      condition.daysOfWeek.splice(idx, 1);
    }
  }

  // Action management
  addAction() {
    this.actions.update((list) => [
      ...list,
      {
        id: crypto.randomUUID(),
        actionType: ActionType.SetDeviceState,
        deviceId: '',
        property: '',
        value: '',
        delaySeconds: 0,
        webhookUrl: '',
        webhookMethod: 'POST',
        webhookBody: '',
        notificationTitle: '',
        notificationMessage: '',
        sceneId: undefined,
        sortOrder: list.length,
      },
    ]);
  }

  removeAction(id: string) {
    this.actions.update((list) => list.filter((a) => a.id !== id));
  }

  // Form helpers
  getDeviceLabel(deviceId: string): string {
    const device = this.devices().find((d) => d.deviceId === deviceId);
    return device?.effectiveDisplayName || device?.friendlyName || deviceId;
  }

  getTriggerTypeLabel(type: TriggerType): string {
    const labels: Record<number, string> = {
      [TriggerType.DeviceState as number]: '📡 Device State',
      [TriggerType.Time]: '⏰ Time',
      [TriggerType.Sunrise]: '🌅 Sunrise',
      [TriggerType.Sunset]: '🌇 Sunset',
      [TriggerType.SensorThreshold]: '📊 Sensor Threshold',
      [TriggerType.Manual]: '👆 Manual',
    };
    return labels[type as number] || type.toString();
  }

  getConditionTypeLabel(type: ConditionType): string {
    const labels: Record<number, string> = {
      [ConditionType.DeviceState]: '📡 Device State',
      [ConditionType.TimeRange]: '⏰ Time Range',
      [ConditionType.DayOfWeek]: '📅 Day of Week',
      [ConditionType.SunPosition]: '☀️ Sun Position',
      [ConditionType.And]: '➕ AND',
      [ConditionType.Or]: '➗ OR',
    };
    return labels[type] || type.toString();
  }

  getActionTypeLabel(type: ActionType): string {
    const labels: Record<number, string> = {
      [ActionType.SetDeviceState]: '📡 Set Device State',
      [ActionType.ToggleDevice]: '🔄 Toggle Device',
      [ActionType.Delay]: '⏱️ Delay',
      [ActionType.Webhook]: '🌐 Webhook',
      [ActionType.Notification]: '🔔 Notification',
      [ActionType.ActivateScene]: '🎬 Activate Scene',
      [ActionType.RunAutomation]: '⚡ Run Automation',
    };
    return labels[type] || type.toString();
  }

  getOperatorLabel(op: ComparisonOperator): string {
    const labels: Record<number, string> = {
      [ComparisonOperator.Equals]: '= Equals',
      [ComparisonOperator.NotEquals]: '≠ Not Equals',
      [ComparisonOperator.GreaterThan]: '> Greater Than',
      [ComparisonOperator.GreaterThanOrEqual]: '≥ Greater or Equal',
      [ComparisonOperator.LessThan]: '< Less Than',
      [ComparisonOperator.LessThanOrEqual]: '≤ Less or Equal',
      [ComparisonOperator.Between]: '↔ Between',
      [ComparisonOperator.Contains]: '∈ Contains',
      [ComparisonOperator.StartsWith]: '⊃ Starts With',
      [ComparisonOperator.EndsWith]: '⊂ Ends With',
      [ComparisonOperator.ChangesTo]: '→ Changes To',
      [ComparisonOperator.ChangesFrom]: '← Changes From',
      [ComparisonOperator.AnyChange]: '↕ Any Change',
    };
    return labels[op] || op.toString();
  }

  // Save
  async save() {
    if (!this.name()) {
      alert('Please enter a name for the automation');
      return;
    }

    if (this.triggers().length === 0) {
      alert('Please add at least one trigger');
      return;
    }

    if (this.actions().length === 0) {
      alert('Please add at least one action');
      return;
    }

    this.saving.set(true);

    try {
      const triggerRequests: CreateTriggerRequest[] = this.triggers().map((t, i) =>
        CreateTriggerRequest.fromJS({
          triggerType: t.triggerType,
          deviceId: t.deviceId || undefined,
          property: t.property || undefined,
          operator: t.operator,
          value: t.value || undefined,
          timeExpression: t.timeExpression || undefined,
          sunEvent: t.sunEvent || undefined,
          offsetMinutes: t.offsetMinutes || undefined,
          sortOrder: i,
        })
      );

      const conditionRequests: CreateConditionRequest[] = this.conditions().map((c, i) =>
        CreateConditionRequest.fromJS({
          conditionType: c.conditionType,
          deviceId: c.deviceId || undefined,
          property: c.property || undefined,
          operator: c.operator,
          value: c.value || undefined,
          value2: c.value2 || undefined,
          timeStart: c.timeStart || undefined,
          timeEnd: c.timeEnd || undefined,
          daysOfWeek: c.daysOfWeek.length > 0 ? c.daysOfWeek as DayOfWeek[] : undefined,
          sortOrder: i,
        })
      );

      const actionRequests: CreateActionRequest[] = this.actions().map((a, i) =>
        CreateActionRequest.fromJS({
          actionType: a.actionType,
          deviceId: a.deviceId || undefined,
          property: a.property || undefined,
          value: a.value || undefined,
          delaySeconds: a.delaySeconds || undefined,
          webhookUrl: a.webhookUrl || undefined,
          webhookMethod: a.webhookMethod || undefined,
          webhookBody: a.webhookBody || undefined,
          notificationTitle: a.notificationTitle || undefined,
          notificationMessage: a.notificationMessage || undefined,
          sceneId: a.sceneId || undefined,
          sortOrder: i,
        })
      );

      if (this.isNew()) {
        const request = CreateAutomationRequest.fromJS({
          name: this.name(),
          description: this.description() || undefined,
          icon: this.icon(),
          color: this.color(),
          isEnabled: this.isEnabled(),
          triggerMode: this.triggerMode(),
          conditionMode: this.conditionMode(),
          cooldownSeconds: this.cooldownSeconds(),
          triggers: triggerRequests,
          conditions: conditionRequests,
          actions: actionRequests,
        });

        await this.automationsService.createAutomation(request).toPromise();
      } else {
        const request = UpdateAutomationRequest.fromJS({
          name: this.name(),
          description: this.description() || undefined,
          icon: this.icon(),
          color: this.color(),
          isEnabled: this.isEnabled(),
          triggerMode: this.triggerMode(),
          conditionMode: this.conditionMode(),
          cooldownSeconds: this.cooldownSeconds(),
          triggers: triggerRequests,
          conditions: conditionRequests,
          actions: actionRequests,
        });

        await this.automationsService
          .updateAutomation(this.automationId()!, request)
          .toPromise();
      }

      this.router.navigate(['/automations']);
    } catch (error) {
      console.error('Error saving automation:', error);
      alert('Failed to save automation');
    } finally {
      this.saving.set(false);
    }
  }

  cancel() {
    this.router.navigate(['/automations']);
  }

  copyToJson() {
    const automationData = {
      name: this.name(),
      description: this.description() || null,
      icon: this.icon(),
      color: this.color(),
      isEnabled: this.isEnabled(),
      triggerMode: TriggerMode[this.triggerMode()],
      conditionMode: ConditionMode[this.conditionMode()],
      cooldownSeconds: this.cooldownSeconds(),
      triggers: this.triggers().map((t, i) => ({
        triggerType: TriggerType[t.triggerType],
        deviceId: t.deviceId || null,
        property: t.property || null,
        operator: t.operator ? ComparisonOperator[t.operator] : null,
        value: t.value || null,
        timeExpression: t.timeExpression || null,
        sunEvent: t.sunEvent || null,
        offsetMinutes: t.offsetMinutes || null,
        sortOrder: i,
      })),
      conditions: this.conditions().map((c, i) => ({
        conditionType: ConditionType[c.conditionType],
        deviceId: c.deviceId || null,
        property: c.property || null,
        operator: c.operator ? ComparisonOperator[c.operator] : null,
        value: c.value || null,
        value2: c.value2 || null,
        timeStart: c.timeStart || null,
        timeEnd: c.timeEnd || null,
        daysOfWeek: c.daysOfWeek.length > 0 ? c.daysOfWeek : null,
        sortOrder: i,
      })),
      actions: this.actions().map((a, i) => ({
        actionType: ActionType[a.actionType],
        deviceId: a.deviceId || null,
        property: a.property || null,
        value: a.value || null,
        delaySeconds: a.delaySeconds || null,
        webhookUrl: a.webhookUrl || null,
        webhookMethod: a.webhookMethod || null,
        webhookBody: a.webhookBody || null,
        notificationTitle: a.notificationTitle || null,
        notificationMessage: a.notificationMessage || null,
        sceneId: a.sceneId || null,
        sortOrder: i,
      })),
    };

    const json = JSON.stringify(automationData, null, 2);
    navigator.clipboard.writeText(json).then(() => {
      alert('Automation JSON copied to clipboard!');
    }).catch((err) => {
      console.error('Failed to copy:', err);
      // Fallback: show in console
      console.log('Automation JSON:', json);
      alert('Failed to copy to clipboard. Check console for JSON.');
    });
  }
}

// Form item interfaces
interface TriggerFormItem {
  id: string;
  triggerType: TriggerType;
  deviceId: string;
  property: string;
  operator: ComparisonOperator;
  value: string;
  timeExpression: string;
  sunEvent: string;
  offsetMinutes: number;
  sortOrder: number;
}

interface ConditionFormItem {
  id: string;
  conditionType: ConditionType;
  deviceId: string;
  property: string;
  operator: ComparisonOperator;
  value: string;
  value2: string;
  timeStart: string;
  timeEnd: string;
  daysOfWeek: number[];
  sortOrder: number;
}

interface ActionFormItem {
  id: string;
  actionType: ActionType;
  deviceId: string;
  property: string;
  value: string;
  delaySeconds: number;
  webhookUrl: string;
  webhookMethod: string;
  webhookBody: string;
  notificationTitle: string;
  notificationMessage: string;
  sceneId: string | undefined;
  sortOrder: number;
}

type DayOfWeek = 0 | 1 | 2 | 3 | 4 | 5 | 6;
