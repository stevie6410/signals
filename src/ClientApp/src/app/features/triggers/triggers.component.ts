import { Component, OnInit, inject, signal, computed } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { TriggersApiService, TriggerEvent, CustomTriggersApiService, CustomTriggerSummary, CustomTriggerRule, CreateCustomTriggerRequest, UpdateCustomTriggerRequest, CustomTriggerType, ThresholdOperator } from '../../api/sdhome-client';
import { SignalRService } from '../../core/services/signalr.service';
import { CapabilityMappingService, TranslatedState } from '../../core/services/capability-mapping.service';
import { DevicesApiService, Device } from '../../api/sdhome-client';

interface FilterOption {
  label: string;
  value: string;
}

@Component({
  selector: 'app-triggers',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './triggers.component.html',
  styleUrl: './triggers.component.scss'
})
export class TriggersComponent implements OnInit {
  private triggersService = inject(TriggersApiService);
  private customTriggersService = inject(CustomTriggersApiService);
  private devicesService = inject(DevicesApiService);
  private signalrService = inject(SignalRService);
  private capabilityMapping = inject(CapabilityMappingService);

  // Tab state
  activeTab = signal<'events' | 'custom'>('events');

  // Trigger Events State
  triggers = signal<TriggerEvent[]>([]);
  loading = signal(false);
  searchFilter = signal('');
  typeFilter = signal<string | null>(null);
  showTypeDropdown = false;

  // Custom Triggers State
  customTriggers = signal<CustomTriggerSummary[]>([]);
  customTriggersLoading = signal(false);
  devices = signal<Device[]>([]);
  showCreateModal = signal(false);
  editingTrigger = signal<CustomTriggerRule | null>(null);
  
  // Form state
  form = signal({
    name: '',
    description: '',
    enabled: true,
    triggerType: CustomTriggerType.SensorThreshold,
    deviceId: '',
    metric: '',
    operator: ThresholdOperator.LessThan,
    threshold: 0,
    threshold2: null as number | null,
    cooldownSeconds: 300
  });

  // Live triggers from SignalR
  liveTriggers = this.signalrService.triggerHistory;
  isConnected = this.signalrService.isConnected;

  // Combined triggers
  allTriggers = computed(() => {
    const live = this.liveTriggers();
    const historical = this.triggers();
    const merged = [...live, ...historical];
    const seen = new Set<string>();
    return merged.filter(t => {
      if (!t.id || seen.has(t.id)) return false;
      seen.add(t.id);
      return true;
    }).slice(0, 200);
  });

  // Filtered triggers
  filteredTriggers = computed(() => {
    let data = this.allTriggers();
    const search = this.searchFilter().toLowerCase();
    const type = this.typeFilter();

    if (search) {
      data = data.filter(t =>
        t.deviceId?.toLowerCase().includes(search) ||
        t.capability?.toLowerCase().includes(search) ||
        t.triggerType?.toLowerCase().includes(search)
      );
    }

    if (type) {
      data = data.filter(t => t.triggerType === type);
    }

    return data;
  });

  // Unique trigger types for filter
  uniqueTypes = computed<FilterOption[]>(() => {
    const types = new Set(this.allTriggers().map(t => t.triggerType).filter(Boolean));
    return [
      { label: 'All Types', value: '' },
      ...Array.from(types).map(t => ({ label: t!, value: t! }))
    ];
  });

  // Stats
  stats = computed(() => {
    const data = this.allTriggers();
    const byType: Record<string, number> = {};
    const byDevice: Record<string, number> = {};

    data.forEach(t => {
      if (t.triggerType) {
        byType[t.triggerType] = (byType[t.triggerType] || 0) + 1;
      }
      if (t.deviceId) {
        byDevice[t.deviceId] = (byDevice[t.deviceId] || 0) + 1;
      }
    });

    return { byType, byDevice, total: data.length };
  });

  ngOnInit() {
    this.capabilityMapping.ensureLoaded();
    this.loadTriggers();
    this.loadDevices();
  }

  loadTriggers() {
    this.loading.set(true);
    this.triggersService.getRecentTriggers(100).subscribe({
      next: (data) => {
        this.triggers.set(data);
        this.loading.set(false);
      },
      error: (err) => {
        console.error('Error loading triggers:', err);
        this.loading.set(false);
      }
    });
  }

  refresh() {
    this.loadTriggers();
  }

  selectType(value: string) {
    this.typeFilter.set(value || null);
    this.showTypeDropdown = false;
  }

  getSelectedTypeLabel(): string {
    const type = this.typeFilter();
    if (!type) return 'All Types';
    return type;
  }

  getTriggerTypeClass(type: string | undefined): string {
    if (!type) return 'secondary';
    const t = type.toLowerCase();
    if (t.includes('motion') || t.includes('occupancy')) return 'warning';
    if (t.includes('contact') || t.includes('door') || t.includes('window')) return 'info';
    if (t.includes('button') || t.includes('action') || t.includes('click')) return 'magenta';
    if (t.includes('vibration') || t.includes('tilt')) return 'danger';
    return 'secondary';
  }

  formatTimestamp(date: Date | string | undefined): string {
    if (!date) return '-';
    const d = new Date(date);
    return d.toLocaleString('en-US', {
      month: 'short',
      day: 'numeric',
      hour: '2-digit',
      minute: '2-digit',
      second: '2-digit',
      hour12: false
    });
  }

  getTypeIcon(type: string | undefined): string {
    if (!type) return '🔔';
    // Use capability mapping if available
    const display = this.capabilityMapping.getCapabilityDisplay(type);
    return display.icon;
  }

  getTypeDisplayName(type: string | undefined): string {
    if (!type) return '-';
    const display = this.capabilityMapping.getCapabilityDisplay(type);
    return display.displayName;
  }

  /**
   * Translate a trigger value using capability mappings.
   * For button triggers, shows the action type (single, double, hold, etc.)
   */
  translateValue(trigger: TriggerEvent): TranslatedState {
    // For button triggers, display the action type from triggerSubType
    if (trigger.triggerType === 'button' && trigger.triggerSubType) {
      return this.formatButtonAction(trigger.triggerSubType);
    }

    const capability = trigger.capability || trigger.triggerType || '';
    return this.capabilityMapping.translate(capability, trigger.value);
  }

  /**
   * Format button action for display (e.g., "single" -> "Single Press")
   */
  private formatButtonAction(action: string): TranslatedState {
    const actionMap: Record<string, { label: string; color: string; icon: string }> = {
      // Basic press types
      'single': { label: 'Single Press', color: '#10b981', icon: '👆' },
      'double': { label: 'Double Press', color: '#3b82f6', icon: '✌️' },
      'triple': { label: 'Triple Press', color: '#f59e0b', icon: '🤟' },
      'quadruple': { label: 'Quadruple Press', color: '#ec4899', icon: '🖐️' },
      'press': { label: 'Press', color: '#10b981', icon: '👆' },
      'long_press': { label: 'Long Press', color: '#f59e0b', icon: '✊' },

      // Hold actions
      'hold': { label: 'Hold', color: '#f59e0b', icon: '✊' },
      'release': { label: 'Release', color: '#6b7280', icon: '🖐️' },
      'hold_release': { label: 'Hold Release', color: '#6b7280', icon: '🖐️' },

      // Dimmer-style compound actions (button_action_modifier)
      'press_release': { label: 'Press Release', color: '#6b7280', icon: '👆' },

      // Brightness/dimmer actions
      'brightness_move_up': { label: 'Brightness Up', color: '#10b981', icon: '🔆' },
      'brightness_move_down': { label: 'Brightness Down', color: '#3b82f6', icon: '🔅' },
      'brightness_stop': { label: 'Brightness Stop', color: '#6b7280', icon: '⏹️' },
    };

    const lowerAction = action.toLowerCase();
    const mapped = actionMap[lowerAction];
    if (mapped) {
      return {
        rawValue: action,
        friendlyName: mapped.label,
        color: mapped.color,
        icon: mapped.icon,
        isActive: true
      };
    }

    // Fallback: format the action nicely
    // Convert underscores to spaces and title case
    const label = action
      .replace(/_/g, ' ')
      .replace(/\b\w/g, c => c.toUpperCase());

    return { rawValue: action, friendlyName: label, isActive: true };
  }

  /**
   * Get display name for capability, handling multi-button sources
   */
  getCapabilityDisplayName(trigger: TriggerEvent): string {
    const capability = trigger.capability || '';

    // Check if this is a multi-button capability (e.g., "button_1", "button_2")
    const buttonMatch = capability.match(/^button[_\s]?(\d+)$/i);
    if (buttonMatch) {
      return `Button ${buttonMatch[1]}`;
    }

    // Check for named button sources from dimmer remotes (e.g., "up", "down", "on", "off")
    const namedButtons: Record<string, string> = {
      'up': 'Up Button',
      'down': 'Down Button',
      'on': 'On Button',
      'off': 'Off Button',
      'left': 'Left Button',
      'right': 'Right Button',
      'top': 'Top Button',
      'bottom': 'Bottom Button',
      'middle': 'Middle Button',
      'center': 'Center Button',
    };

    const lowerCap = capability.toLowerCase();
    if (namedButtons[lowerCap]) {
      return namedButtons[lowerCap];
    }

    const display = this.capabilityMapping.getCapabilityDisplay(capability);
    return display.displayName;
  }

  trackTrigger(index: number, trigger: TriggerEvent): string {
    return trigger.id ?? index.toString();
  }

  // ===== Custom Triggers Methods =====

  switchTab(tab: 'events' | 'custom') {
    this.activeTab.set(tab);
    if (tab === 'custom' && this.customTriggers().length === 0) {
      this.loadCustomTriggers();
      this.loadDevices();
    }
  }

  loadCustomTriggers() {
    this.customTriggersLoading.set(true);
    this.customTriggersService.getCustomTriggers().subscribe({
      next: (data) => {
        this.customTriggers.set(data);
        this.customTriggersLoading.set(false);
      },
      error: (err) => {
        console.error('Error loading custom triggers:', err);
        this.customTriggersLoading.set(false);
      }
    });
  }

  loadDevices() {
    this.devicesService.getDevices().subscribe({
      next: (data) => this.devices.set(data),
      error: (err) => console.error('Error loading devices:', err)
    });
  }

  openCreateModal() {
    this.editingTrigger.set(null);
    this.resetForm();
    this.showCreateModal.set(true);
  }

  openEditModal(trigger: CustomTriggerSummary) {
    if (!trigger.id) return;
    this.customTriggersService.getCustomTrigger(trigger.id).subscribe({
      next: (data) => {
        this.editingTrigger.set(data);
        this.form.set({
          name: data.name || '',
          description: data.description || '',
          enabled: data.enabled ?? true,
          triggerType: data.triggerType!,
          deviceId: data.deviceId || '',
          metric: data.metric || '',
          operator: data.operator!,
          threshold: data.threshold ?? 0,
          threshold2: data.threshold2 ?? null,
          cooldownSeconds: data.cooldownSeconds ?? 300
        });
        this.showCreateModal.set(true);
      },
      error: (err) => console.error('Error loading trigger:', err)
    });
  }

  closeModal() {
    this.showCreateModal.set(false);
    this.editingTrigger.set(null);
    this.resetForm();
  }

  resetForm() {
    this.form.set({
      name: '',
      description: '',
      enabled: true,
      triggerType: CustomTriggerType.SensorThreshold,
      deviceId: '',
      metric: '',
      operator: ThresholdOperator.LessThan,
      threshold: 0,
      threshold2: null,
      cooldownSeconds: 300
    });
  }

  async saveTrigger() {
    const f = this.form();
    const request = new CreateCustomTriggerRequest({
      name: f.name,
      description: f.description || undefined,
      enabled: f.enabled,
      triggerType: f.triggerType,
      deviceId: f.deviceId,
      metric: f.metric,
      operator: f.operator,
      threshold: f.threshold,
      threshold2: f.threshold2 ?? undefined,
      cooldownSeconds: f.cooldownSeconds ?? undefined
    });

    const editing = this.editingTrigger();
    const observable = editing
      ? this.customTriggersService.updateCustomTrigger(editing.id!, new UpdateCustomTriggerRequest(request as any))
      : this.customTriggersService.createCustomTrigger(request);

    observable.subscribe({
      next: () => {
        this.loadCustomTriggers();
        this.closeModal();
      },
      error: (err) => {
        console.error('Error saving custom trigger:', err);
        alert('Error saving custom trigger: ' + (err.message || 'Unknown error'));
      }
    });
  }

  async toggleCustomTrigger(trigger: CustomTriggerSummary) {
    if (!trigger.id) return;
    this.customTriggersService.toggleCustomTrigger(trigger.id, !trigger.enabled).subscribe({
      next: () => this.loadCustomTriggers(),
      error: (err) => console.error('Error toggling trigger:', err)
    });
  }

  async deleteCustomTrigger(trigger: CustomTriggerSummary) {
    if (!trigger.id) return;
    if (!confirm(`Delete custom trigger "${trigger.name}"?`)) return;

    this.customTriggersService.deleteCustomTrigger(trigger.id).subscribe({
      next: () => this.loadCustomTriggers(),
      error: (err) => console.error('Error deleting trigger:', err)
    });
  }

  getOperatorSymbol(op: string | undefined): string {
    if (!op) return '?';
    const operators: Record<string, string> = {
      'GreaterThan': '>',
      'GreaterThanOrEqual': '≥',
      'LessThan': '<',
      'LessThanOrEqual': '≤',
      'Equals': '=',
      'NotEquals': '≠',
      'Between': 'between'
    };
    return operators[op] || op;
  }

  getMetricOptions(): string[] {
    return ['temperature', 'humidity', 'battery', 'linkquality', 'pressure', 'co2', 'voc', 'pm25'];
  }

  getTriggerTypeOptions() {
    return [
      { value: CustomTriggerType.SensorThreshold, label: 'Sensor Threshold' },
      { value: CustomTriggerType.SignalQuality, label: 'Signal Quality' }
    ];
  }

  getOperatorOptions() {
    return [
      { value: ThresholdOperator.LessThan, label: '<  Less Than' },
      { value: ThresholdOperator.LessThanOrEqual, label: '≤  Less Than or Equal' },
      { value: ThresholdOperator.GreaterThan, label: '>  Greater Than' },
      { value: ThresholdOperator.GreaterThanOrEqual, label: '≥  Greater Than or Equal' },
      { value: ThresholdOperator.Equals, label: '=  Equals' },
      { value: ThresholdOperator.NotEquals, label: '≠  Not Equals' },
      { value: ThresholdOperator.Between, label: 'Between' }
    ];
  }

  // Form field update methods
  updateFormName(value: string) {
    this.form.update(f => ({ ...f, name: value }));
  }

  updateFormDescription(value: string) {
    this.form.update(f => ({ ...f, description: value }));
  }

  updateFormDeviceId(value: string) {
    this.form.update(f => ({ ...f, deviceId: value }));
  }

  updateFormMetric(value: string) {
    this.form.update(f => ({ ...f, metric: value }));
  }

  updateFormOperator(value: string) {
    this.form.update(f => ({ ...f, operator: value as unknown as ThresholdOperator }));
  }

  updateFormThreshold(value: string) {
    this.form.update(f => ({ ...f, threshold: +value }));
  }

  updateFormThreshold2(value: string) {
    this.form.update(f => ({ ...f, threshold2: +value }));
  }

  updateFormCooldownSeconds(value: string) {
    this.form.update(f => ({ ...f, cooldownSeconds: value ? +value : 300 }));
  }

  updateFormEnabled(value: boolean) {
    this.form.update(f => ({ ...f, enabled: value }));
  }

  CustomTriggerType = CustomTriggerType;
  ThresholdOperator = ThresholdOperator;
}

