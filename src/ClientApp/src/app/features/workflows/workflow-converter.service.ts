import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AutomationsApiService,
  CreateAutomationRequest,
  CreateTriggerRequest,
  CreateConditionRequest,
  CreateActionRequest,
  TriggerType,
  ConditionType,
  ActionType,
  ComparisonOperator,
  TriggerMode,
  ConditionMode,
  AutomationRule,
} from '../../api/sdhome-client';
import {
  WorkflowDefinition,
  WorkflowNode,
  WorkflowConnection,
  WorkflowNodeData,
} from './workflow-editor/workflow-editor.component';

export interface ZoneCapabilityAssignment {
  zoneId: number;
  capability: string;
  deviceId: string;
  property?: string;
}

@Injectable({
  providedIn: 'root',
})
export class WorkflowConverterService {
  private http = inject(HttpClient);
  private automationsApi = inject(AutomationsApiService);

  /**
   * Convert a visual workflow to a CreateAutomationRequest
   */
  workflowToAutomation(workflow: WorkflowDefinition): CreateAutomationRequest {
    const triggers: CreateTriggerRequest[] = [];
    const conditions: CreateConditionRequest[] = [];
    const actions: CreateActionRequest[] = [];

    // Process nodes by type
    for (const node of workflow.nodes) {
      const nodeType = node.type;
      const config = node.data.config;

      if (nodeType.includes('trigger')) {
        triggers.push(this.convertTriggerNode(node));
      } else if (nodeType.includes('condition')) {
        conditions.push(this.convertConditionNode(node));
      } else if (nodeType.includes('action')) {
        actions.push(this.convertActionNode(node));
      }
    }

    return new CreateAutomationRequest({
      name: workflow.name,
      description: workflow.description,
      isEnabled: true,
      triggerMode: triggers.length > 1 ? TriggerMode.Any : TriggerMode.All,
      conditionMode: ConditionMode.All,
      cooldownSeconds: 0,
      triggers,
      conditions,
      actions,
    });
  }

  private convertTriggerNode(node: WorkflowNode): CreateTriggerRequest {
    const config = node.data.config;
    const nodeType = node.type;

    // Zone-based trigger
    if (nodeType === 'zone-trigger') {
      return new CreateTriggerRequest({
        triggerType: TriggerType.DeviceState,
        // Note: The actual device ID would need to be resolved from the zone capability
        // For now, store the zone reference and capability
        deviceId: config['deviceId'] || '', // Will be resolved from zone capability
        property: config['property'] || this.getDefaultPropertyForCapability(config['capability']),
        operator: this.convertOperator(config['operator']),
        value: config['value'],
        sortOrder: 0,
      });
    }

    // Device-based trigger
    if (nodeType === 'device-trigger') {
      return new CreateTriggerRequest({
        triggerType: TriggerType.DeviceState,
        deviceId: config['deviceId'] || '',
        property: config['property'] || 'state',
        operator: this.convertOperator(config['operator']),
        value: config['value'],
        sortOrder: 0,
      });
    }

    // Time-based trigger
    if (nodeType === 'time-trigger') {
      return new CreateTriggerRequest({
        triggerType: TriggerType.Time,
        timeExpression: config['timeExpression'] || '',
        sortOrder: 0,
      });
    }

    // Default
    return new CreateTriggerRequest({
      triggerType: TriggerType.Manual,
      sortOrder: 0,
    });
  }

  private convertConditionNode(node: WorkflowNode): CreateConditionRequest {
    const config = node.data.config;
    const nodeType = node.type;

    // Zone-based condition
    if (nodeType === 'zone-condition') {
      return new CreateConditionRequest({
        conditionType: ConditionType.DeviceState,
        deviceId: config['deviceId'] || '',
        property: config['property'] || this.getDefaultPropertyForCapability(config['capability']),
        operator: this.convertOperator(config['operator']),
        value: config['value'],
        sortOrder: 0,
      });
    }

    // Device-based condition
    if (nodeType === 'device-condition') {
      return new CreateConditionRequest({
        conditionType: ConditionType.DeviceState,
        deviceId: config['deviceId'] || '',
        property: config['property'] || 'state',
        operator: this.convertOperator(config['operator']),
        value: config['value'],
        sortOrder: 0,
      });
    }

    // Time-based condition
    if (nodeType === 'time-condition') {
      return new CreateConditionRequest({
        conditionType: ConditionType.TimeRange,
        timeStart: config['timeStart'] || '',
        timeEnd: config['timeEnd'] || '',
        sortOrder: 0,
      });
    }

    // Default
    return new CreateConditionRequest({
      conditionType: ConditionType.DeviceState,
      sortOrder: 0,
    });
  }

  private convertActionNode(node: WorkflowNode): CreateActionRequest {
    const config = node.data.config;
    const nodeType = node.type;

    // Zone-based action
    if (nodeType === 'zone-action') {
      return new CreateActionRequest({
        actionType: ActionType.SetDeviceState,
        deviceId: config['deviceId'] || '',
        property: config['property'] || this.getDefaultPropertyForCapability(config['capability']),
        value: config['value'],
        sortOrder: 0,
      });
    }

    // Device-based action
    if (nodeType === 'device-action') {
      return new CreateActionRequest({
        actionType: ActionType.SetDeviceState,
        deviceId: config['deviceId'] || '',
        property: config['property'] || 'state',
        value: config['value'],
        sortOrder: 0,
      });
    }

    // Delay action
    if (nodeType === 'delay-action') {
      return new CreateActionRequest({
        actionType: ActionType.Delay,
        delaySeconds: parseInt(config['delay']) || 0,
        sortOrder: 0,
      });
    }

    // Webhook action
    if (nodeType === 'webhook-action') {
      return new CreateActionRequest({
        actionType: ActionType.Webhook,
        webhookUrl: config['webhookUrl'] || '',
        webhookMethod: config['webhookMethod'] || 'POST',
        webhookBody: config['webhookBody'] || '',
        sortOrder: 0,
      });
    }

    // Default
    return new CreateActionRequest({
      actionType: ActionType.SetDeviceState,
      sortOrder: 0,
    });
  }

  private convertOperator(operator: string): ComparisonOperator {
    const mapping: Record<string, ComparisonOperator> = {
      equals: ComparisonOperator.Equals,
      not_equals: ComparisonOperator.NotEquals,
      greater_than: ComparisonOperator.GreaterThan,
      less_than: ComparisonOperator.LessThan,
      changes_to: ComparisonOperator.ChangesTo,
      changes_from: ComparisonOperator.ChangesFrom,
      any_change: ComparisonOperator.AnyChange,
    };
    return mapping[operator] ?? ComparisonOperator.Equals;
  }

  private getDefaultPropertyForCapability(capability: string): string {
    const mapping: Record<string, string> = {
      motion: 'occupancy',
      presence: 'presence',
      temperature: 'temperature',
      humidity: 'humidity',
      contact: 'contact',
      illuminance: 'illuminance',
      lights: 'state',
      main_light: 'state',
      accent_light: 'state',
      climate: 'state',
      heating: 'state',
      cooling: 'state',
      fan: 'state',
      cover: 'position',
      lock: 'state',
      switch: 'state',
    };
    return mapping[capability] ?? 'state';
  }

  /**
   * Convert an existing AutomationRule back to a workflow for editing
   */
  automationToWorkflow(automation: AutomationRule): WorkflowDefinition {
    const nodes: WorkflowNode[] = [];
    const connections: WorkflowConnection[] = [];

    let xOffset = 100;
    const yTriggers = 100;
    const yConditions = 250;
    const yActions = 400;

    // Convert triggers
    for (let i = 0; i < (automation.triggers?.length || 0); i++) {
      const trigger = automation.triggers![i];
      const nodeId = `trigger-${i}`;

      nodes.push({
        id: nodeId,
        type: trigger.deviceId ? 'device-trigger' : 'time-trigger',
        position: { x: xOffset + i * 220, y: yTriggers },
        data: {
          nodeType: trigger.deviceId ? 'device-trigger' : 'time-trigger',
          label: trigger.deviceId ? 'Device Trigger' : 'Time Trigger',
          config: {
            deviceId: trigger.deviceId,
            property: trigger.property,
            operator: this.reverseOperator(trigger.operator),
            value: trigger.value,
            timeExpression: trigger.timeExpression,
          },
        },
      });
    }

    // Convert conditions
    for (let i = 0; i < (automation.conditions?.length || 0); i++) {
      const condition = automation.conditions![i];
      const nodeId = `condition-${i}`;

      nodes.push({
        id: nodeId,
        type: condition.conditionType === ConditionType.TimeRange ? 'time-condition' : 'device-condition',
        position: { x: xOffset + i * 220, y: yConditions },
        data: {
          nodeType: condition.conditionType === ConditionType.TimeRange ? 'time-condition' : 'device-condition',
          label: condition.conditionType === ConditionType.TimeRange ? 'Time Condition' : 'Device Condition',
          config: {
            deviceId: condition.deviceId,
            property: condition.property,
            operator: this.reverseOperator(condition.operator),
            value: condition.value,
            timeStart: condition.timeStart,
            timeEnd: condition.timeEnd,
          },
        },
      });
    }

    // Convert actions
    for (let i = 0; i < (automation.actions?.length || 0); i++) {
      const action = automation.actions![i];
      const nodeId = `action-${i}`;

      let nodeType = 'device-action';
      let label = 'Device Action';

      if (action.actionType === ActionType.Delay) {
        nodeType = 'delay-action';
        label = 'Delay';
      } else if (action.actionType === ActionType.Webhook) {
        nodeType = 'webhook-action';
        label = 'Webhook';
      }

      nodes.push({
        id: nodeId,
        type: nodeType,
        position: { x: xOffset + i * 220, y: yActions },
        data: {
          nodeType,
          label,
          config: {
            deviceId: action.deviceId,
            property: action.property,
            value: action.value,
            delay: action.delaySeconds,
            webhookUrl: action.webhookUrl,
            webhookMethod: action.webhookMethod,
          },
        },
      });
    }

    // Create connections (simple linear flow for now)
    // Connect triggers to first condition or first action
    const firstTrigger = nodes.find((n) => n.type.includes('trigger'));
    const firstCondition = nodes.find((n) => n.type.includes('condition'));
    const firstAction = nodes.find((n) => n.type.includes('action'));

    if (firstTrigger && firstCondition) {
      connections.push({
        fromNode: firstTrigger.id,
        fromOutput: 'output_1',
        toNode: firstCondition.id,
        toInput: 'input_1',
      });
    }

    if (firstCondition && firstAction) {
      connections.push({
        fromNode: firstCondition.id,
        fromOutput: 'output_1',
        toNode: firstAction.id,
        toInput: 'input_1',
      });
    } else if (firstTrigger && firstAction && !firstCondition) {
      connections.push({
        fromNode: firstTrigger.id,
        fromOutput: 'output_1',
        toNode: firstAction.id,
        toInput: 'input_1',
      });
    }

    return {
      name: automation.name || 'Imported Automation',
      description: automation.description,
      nodes,
      connections,
    };
  }

  private reverseOperator(operator: ComparisonOperator | undefined): string {
    if (!operator) return 'equals';

    const mapping: Record<number, string> = {
      [ComparisonOperator.Equals]: 'equals',
      [ComparisonOperator.NotEquals]: 'not_equals',
      [ComparisonOperator.GreaterThan]: 'greater_than',
      [ComparisonOperator.LessThan]: 'less_than',
      [ComparisonOperator.ChangesTo]: 'changes_to',
      [ComparisonOperator.ChangesFrom]: 'changes_from',
      [ComparisonOperator.AnyChange]: 'any_change',
    };
    return mapping[operator] ?? 'equals';
  }

  /**
   * Resolve zone capability references to actual device IDs
   */
  async resolveZoneCapabilities(
    workflow: WorkflowDefinition
  ): Promise<WorkflowDefinition> {
    try {
      // Fetch all zone capability assignments
      const assignments = await this.http
        .get<ZoneCapabilityAssignment[]>('/api/zones/capabilities/all')
        .toPromise();

      if (!assignments || assignments.length === 0) return workflow;

      // Create a lookup map
      const capabilityMap = new Map<string, ZoneCapabilityAssignment>();
      for (const assignment of assignments) {
        const key = `${assignment.zoneId}-${assignment.capability}`;
        capabilityMap.set(key, assignment);
      }

      // Update nodes with resolved device IDs
      for (const node of workflow.nodes) {
        const config = node.data.config;
        const zoneId = config['zoneId'];
        const capability = config['capability'];

        if (zoneId && capability) {
          const key = `${zoneId}-${capability}`;
          const assignment = capabilityMap.get(key);

          if (assignment) {
            config['deviceId'] = assignment.deviceId;
            if (assignment.property) {
              config['property'] = assignment.property;
            }
          }
        }
      }
    } catch (error) {
      console.warn('Could not resolve zone capabilities, using device IDs as-is:', error);
    }

    return workflow;
  }

  /**
   * Save workflow as an automation
   */
  async saveWorkflowAsAutomation(workflow: WorkflowDefinition): Promise<AutomationRule> {
    // First resolve any zone capability references
    const resolvedWorkflow = await this.resolveZoneCapabilities(workflow);

    // Convert to automation request
    const request = this.workflowToAutomation(resolvedWorkflow);

    // Create the automation
    return this.automationsApi.createAutomation(request).toPromise() as Promise<AutomationRule>;
  }
}
