import {
  Component,
  OnInit,
  OnDestroy,
  AfterViewInit,
  signal,
  computed,
  inject,
  ElementRef,
  ViewChild,
  Input,
  Output,
  EventEmitter,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { HttpClient } from '@angular/common/http';
import { Router, ActivatedRoute } from '@angular/router';
import Drawflow from 'drawflow';
import { WorkflowConverterService } from '../workflow-converter.service';

// Node data interfaces
export interface WorkflowNodeData {
  nodeType: string;
  label: string;
  config: Record<string, any>;
}

export interface ZoneTriggerConfig {
  zoneId?: number;
  zoneName?: string;
  capability?: string;
  operator?: string;
  value?: string;
}

export interface DeviceTriggerConfig {
  deviceId?: string;
  deviceName?: string;
  property?: string;
  operator?: string;
  value?: string;
}

export interface ConditionConfig {
  conditionType?: string;
  zoneId?: number;
  zoneName?: string;
  deviceId?: string;
  deviceName?: string;
  capability?: string;
  property?: string;
  operator?: string;
  value?: string;
  value2?: string;
}

export interface ActionConfig {
  actionType?: string;
  zoneId?: number;
  zoneName?: string;
  deviceId?: string;
  deviceName?: string;
  capability?: string;
  property?: string;
  value?: string;
  delay?: number;
}

export interface Zone {
  id: number;
  name: string;
  icon?: string;
  color?: string;
  parentZoneId?: number;
  childZones?: Zone[];
}

export interface Device {
  deviceId: string;
  friendlyName: string;
  displayName?: string;
  effectiveDisplayName?: string;
  deviceType?: string | number;
  zoneId?: number;
}

export interface CapabilityType {
  id: string;
  label: string;
  icon: string;
}

// Workflow export format
export interface WorkflowDefinition {
  name: string;
  description?: string;
  nodes: WorkflowNode[];
  connections: WorkflowConnection[];
}

export interface WorkflowNode {
  id: string;
  type: string;
  position: { x: number; y: number };
  data: WorkflowNodeData;
}

export interface WorkflowConnection {
  fromNode: string;
  fromOutput: string;
  toNode: string;
  toInput: string;
}

@Component({
  selector: 'app-workflow-editor',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './workflow-editor.component.html',
  styleUrl: './workflow-editor.component.scss',
})
export class WorkflowEditorComponent implements OnInit, OnDestroy, AfterViewInit {
  private http = inject(HttpClient);
  private router = inject(Router);
  private route = inject(ActivatedRoute);
  private workflowConverter = inject(WorkflowConverterService);

  @ViewChild('drawflowContainer', { static: true }) drawflowContainer!: ElementRef;

  @Input() workflowId?: string;
  @Output() save = new EventEmitter<WorkflowDefinition>();
  @Output() close = new EventEmitter<void>();

  private editor: Drawflow | null = null;

  // Data
  zones = signal<Zone[]>([]);
  devices = signal<Device[]>([]);
  capabilityTypes = signal<CapabilityType[]>([]);

  loading = signal(true);
  saving = signal(false);

  // Workflow metadata
  workflowName = signal('New Automation');
  workflowDescription = signal('');

  // Selected node for editing
  selectedNodeId = signal<string | null>(null);
  selectedNodeData = signal<WorkflowNodeData | null>(null);

  // Node type definitions
  nodeTypes = [
    { id: 'zone-trigger', category: 'triggers', label: 'Zone Trigger', icon: '🏠', color: '#10b981' },
    { id: 'device-trigger', category: 'triggers', label: 'Device Trigger', icon: '📡', color: '#06b6d4' },
    { id: 'time-trigger', category: 'triggers', label: 'Time Trigger', icon: '⏰', color: '#8b5cf6' },
    { id: 'zone-condition', category: 'conditions', label: 'Zone Condition', icon: '❓', color: '#f59e0b' },
    { id: 'device-condition', category: 'conditions', label: 'Device Condition', icon: '🔍', color: '#eab308' },
    { id: 'time-condition', category: 'conditions', label: 'Time Condition', icon: '🕐', color: '#f97316' },
    { id: 'zone-action', category: 'actions', label: 'Zone Action', icon: '⚡', color: '#ef4444' },
    { id: 'device-action', category: 'actions', label: 'Device Action', icon: '🎯', color: '#ec4899' },
    { id: 'delay-action', category: 'actions', label: 'Delay', icon: '⏳', color: '#a855f7' },
    { id: 'webhook-action', category: 'actions', label: 'Webhook', icon: '🌐', color: '#3b82f6' },
  ];

  triggerNodes = computed(() => this.nodeTypes.filter(n => n.category === 'triggers'));
  conditionNodes = computed(() => this.nodeTypes.filter(n => n.category === 'conditions'));
  actionNodes = computed(() => this.nodeTypes.filter(n => n.category === 'actions'));

  // Operator options
  operators = [
    { id: 'equals', label: '= Equals' },
    { id: 'not_equals', label: '≠ Not Equals' },
    { id: 'greater_than', label: '> Greater Than' },
    { id: 'less_than', label: '< Less Than' },
    { id: 'changes_to', label: '→ Changes To' },
    { id: 'changes_from', label: '← Changes From' },
    { id: 'any_change', label: '∿ Any Change' },
  ];

  ngOnInit() {
    this.loadData();
  }

  ngAfterViewInit() {
    this.initDrawflow();
  }

  ngOnDestroy() {
    if (this.editor) {
      // Clean up event listeners
    }
  }

  async loadData() {
    this.loading.set(true);
    try {
      // Load zones and devices
      const [zones, devices] = await Promise.all([
        this.http.get<Zone[]>('/api/zones/tree').toPromise().catch(() => []),
        this.http.get<Device[]>('/api/devices').toPromise().catch(() => []),
      ]);

      this.zones.set(zones || []);
      this.devices.set(devices || []);

      // Try to load capability types, with fallback defaults
      try {
        const capTypes = await this.http.get<{ capabilities: string[]; labels: Record<string, string>; icons: Record<string, string> }>('/api/zones/capabilities/types').toPromise();

        if (capTypes?.capabilities) {
          this.capabilityTypes.set(
            capTypes.capabilities.map(cap => ({
              id: cap,
              label: capTypes.labels?.[cap] || cap,
              icon: capTypes.icons?.[cap] || '❓',
            }))
          );
        } else {
          this.setDefaultCapabilityTypes();
        }
      } catch {
        this.setDefaultCapabilityTypes();
      }
    } catch (error) {
      console.error('Error loading data:', error);
      this.setDefaultCapabilityTypes();
    } finally {
      this.loading.set(false);
    }
  }

  private setDefaultCapabilityTypes() {
    // Fallback capability types if API fails
    this.capabilityTypes.set([
      { id: 'motion', label: 'Motion', icon: '🚶' },
      { id: 'presence', label: 'Presence', icon: '👤' },
      { id: 'temperature', label: 'Temperature', icon: '🌡️' },
      { id: 'humidity', label: 'Humidity', icon: '💧' },
      { id: 'illuminance', label: 'Illuminance', icon: '☀️' },
      { id: 'contact', label: 'Contact', icon: '🚪' },
      { id: 'lights', label: 'Lights', icon: '💡' },
      { id: 'climate', label: 'Climate', icon: '❄️' },
      { id: 'media', label: 'Media', icon: '📺' },
      { id: 'switch', label: 'Switch', icon: '🔌' },
    ]);
  }

  private initDrawflow() {
    const container = this.drawflowContainer.nativeElement;

    this.editor = new Drawflow(container);
    this.editor.reroute = true;
    this.editor.reroute_fix_curvature = true;
    this.editor.force_first_input = false;

    // Start the editor
    this.editor.start();

    // Register node click events
    this.editor.on('nodeSelected', (nodeId: number) => {
      this.onNodeSelected(nodeId.toString());
    });

    this.editor.on('nodeUnselected', () => {
      this.selectedNodeId.set(null);
      this.selectedNodeData.set(null);
    });

    this.editor.on('nodeRemoved', (nodeId: number) => {
      if (this.selectedNodeId() === nodeId.toString()) {
        this.selectedNodeId.set(null);
        this.selectedNodeData.set(null);
      }
    });

    // Handle zoom with mouse wheel
    container.addEventListener('wheel', (e: WheelEvent) => {
      if (e.ctrlKey) {
        e.preventDefault();
        const delta = e.deltaY > 0 ? -0.1 : 0.1;
        this.editor!.zoom_in();
      }
    });
  }

  onNodeSelected(nodeId: string) {
    this.selectedNodeId.set(nodeId);

    if (this.editor) {
      const nodeData = this.editor.getNodeFromId(nodeId);
      if (nodeData?.data) {
        this.selectedNodeData.set(nodeData.data as WorkflowNodeData);
      }
    }
  }

  // Add a node to the canvas from the palette
  addNode(nodeType: string) {
    if (!this.editor) return;

    const nodeInfo = this.nodeTypes.find(n => n.id === nodeType);
    if (!nodeInfo) return;

    const data: WorkflowNodeData = {
      nodeType: nodeType,
      label: nodeInfo.label,
      config: {},
    };

    // Determine inputs and outputs based on node type
    let inputs = 1;
    let outputs = 1;

    if (nodeInfo.category === 'triggers') {
      inputs = 0;
      outputs = 1;
    } else if (nodeInfo.category === 'actions') {
      inputs = 1;
      outputs = 0;
    }

    // Create the HTML for the node
    const html = this.createNodeHtml(nodeInfo, data);

    // Add node to the center of the canvas viewport
    const rect = this.drawflowContainer.nativeElement.getBoundingClientRect();
    const x = rect.width / 2 - 100;
    const y = rect.height / 2 - 50;

    this.editor.addNode(
      nodeType,
      inputs,
      outputs,
      x,
      y,
      nodeType,
      data,
      html
    );
  }

  private createNodeHtml(
    nodeInfo: { id: string; label: string; icon: string; color: string },
    data: WorkflowNodeData
  ): string {
    return `
      <div class="workflow-node" style="--node-color: ${nodeInfo.color}">
        <div class="node-header">
          <span class="node-icon">${nodeInfo.icon}</span>
          <span class="node-label">${nodeInfo.label}</span>
        </div>
        <div class="node-body">
          <span class="node-config-summary">${this.getConfigSummary(data)}</span>
        </div>
      </div>
    `;
  }

  private getConfigSummary(data: WorkflowNodeData): string {
    const config = data.config;

    if (data.nodeType.includes('zone')) {
      if (config['zoneName']) {
        return `${config['zoneName']} → ${config['capability'] || 'any'}`;
      }
      return 'Click to configure';
    }

    if (data.nodeType.includes('device')) {
      if (config['deviceName']) {
        return `${config['deviceName']}`;
      }
      return 'Click to configure';
    }

    if (data.nodeType === 'delay-action') {
      return config['delay'] ? `Wait ${config['delay']}s` : 'Click to configure';
    }

    return 'Click to configure';
  }

  // Update the selected node's data
  updateNodeConfig(key: string, value: any) {
    if (!this.selectedNodeId() || !this.editor) return;

    const nodeId = this.selectedNodeId()!;
    const currentData = this.selectedNodeData();

    if (currentData) {
      currentData.config[key] = value;

      // Update the node in Drawflow
      this.editor.updateNodeDataFromId(nodeId, currentData);

      // Update the node's HTML to reflect changes
      const nodeInfo = this.nodeTypes.find(n => n.id === currentData.nodeType);
      if (nodeInfo) {
        const html = this.createNodeHtml(nodeInfo, currentData);
        // Re-render the node
        const nodeElement = document.querySelector(`#node-${nodeId} .drawflow_content_node`);
        if (nodeElement) {
          nodeElement.innerHTML = html;
        }

        // Update the html in drawflow data
        const drawflowData = (this.editor.drawflow as any).drawflow;
        if (drawflowData?.['Home']?.data?.[nodeId]) {
          drawflowData['Home'].data[nodeId].html = html;
        }
      }

      // Trigger change detection
      this.selectedNodeData.set({ ...currentData });
    }
  }

  // Delete selected node
  deleteSelectedNode() {
    if (!this.selectedNodeId() || !this.editor) return;
    this.editor.removeNodeId(`node-${this.selectedNodeId()}`);
    this.selectedNodeId.set(null);
    this.selectedNodeData.set(null);
  }

  // Clear the entire canvas
  clearCanvas() {
    if (this.editor && confirm('Clear all nodes? This cannot be undone.')) {
      this.editor.clear();
      this.selectedNodeId.set(null);
      this.selectedNodeData.set(null);
    }
  }

  // Export the workflow
  exportWorkflow(): WorkflowDefinition {
    if (!this.editor) {
      return { name: this.workflowName(), nodes: [], connections: [] };
    }

    const drawflowData = this.editor.export() as any;
    const homeData = drawflowData?.drawflow?.['Home']?.data || {};

    const nodes: WorkflowNode[] = [];
    const connections: WorkflowConnection[] = [];

    // Convert Drawflow nodes to our format
    for (const [nodeId, node] of Object.entries(homeData) as any) {
      nodes.push({
        id: nodeId,
        type: node.name,
        position: { x: node.pos_x, y: node.pos_y },
        data: node.data,
      });

      // Extract connections
      for (const [outputName, outputs] of Object.entries(node.outputs || {}) as any) {
        for (const conn of outputs.connections || []) {
          connections.push({
            fromNode: nodeId,
            fromOutput: outputName,
            toNode: conn.node,
            toInput: conn.output,
          });
        }
      }
    }

    return {
      name: this.workflowName(),
      description: this.workflowDescription(),
      nodes,
      connections,
    };
  }

  // Import a workflow
  importWorkflow(workflow: WorkflowDefinition) {
    if (!this.editor) return;

    this.editor.clear();
    this.workflowName.set(workflow.name);
    this.workflowDescription.set(workflow.description || '');

    // Create a map of old IDs to new IDs
    const idMap: Record<string, string> = {};

    // Add nodes
    for (const node of workflow.nodes) {
      const nodeInfo = this.nodeTypes.find(n => n.id === node.type);
      if (!nodeInfo) continue;

      let inputs = 1;
      let outputs = 1;
      if (nodeInfo.category === 'triggers') {
        inputs = 0;
      } else if (nodeInfo.category === 'actions') {
        outputs = 0;
      }

      const html = this.createNodeHtml(nodeInfo, node.data);

      const newId = this.editor.addNode(
        node.type,
        inputs,
        outputs,
        node.position.x,
        node.position.y,
        node.type,
        node.data,
        html
      );

      idMap[node.id] = newId.toString();
    }

    // Add connections
    for (const conn of workflow.connections) {
      const fromId = idMap[conn.fromNode];
      const toId = idMap[conn.toNode];
      if (fromId && toId) {
        this.editor.addConnection(
          fromId,
          toId,
          conn.fromOutput,
          conn.toInput
        );
      }
    }
  }

  // Save the workflow
  async saveWorkflow() {
    const workflow = this.exportWorkflow();

    // Validate the workflow has at least a trigger and an action
    const triggers = workflow.nodes.filter(n => n.type.includes('trigger'));
    const actions = workflow.nodes.filter(n => n.type.includes('action'));

    if (triggers.length === 0) {
      alert('Please add at least one trigger node');
      return;
    }

    if (actions.length === 0) {
      alert('Please add at least one action node');
      return;
    }

    this.saving.set(true);
    try {
      const automation = await this.workflowConverter.saveWorkflowAsAutomation(workflow);
      if (automation) {
        alert(`Automation "${automation.name}" created successfully!`);
        this.router.navigate(['/automations']);
      }
    } catch (error) {
      console.error('Error saving workflow:', error);
      alert('Failed to save automation. Please check the console for details.');
    } finally {
      this.saving.set(false);
    }
  }

  // Zoom controls
  zoomIn() {
    this.editor?.zoom_in();
  }

  zoomOut() {
    this.editor?.zoom_out();
  }

  zoomReset() {
    this.editor?.zoom_reset();
  }

  // Helper to flatten zones for dropdown
  getFlatZones(): Zone[] {
    const result: Zone[] = [];
    const flatten = (zones: Zone[]) => {
      for (const zone of zones) {
        result.push(zone);
        if (zone.childZones?.length) {
          flatten(zone.childZones);
        }
      }
    };
    flatten(this.zones());
    return result;
  }

  // Helper method to get zone name by ID
  getZoneName(zoneId: any): string {
    const zone = this.getFlatZones().find(z => z.id === +zoneId);
    return zone?.name || '';
  }

  // Helper method to get device name by ID
  getDeviceName(deviceId: string): string {
    const device = this.devices().find(d => d.deviceId === deviceId);
    return device?.friendlyName || '';
  }

  // Handle zone selection change
  onZoneSelected(zoneId: any) {
    this.updateNodeConfig('zoneId', zoneId);
    this.updateNodeConfig('zoneName', this.getZoneName(zoneId));
  }

  // Handle device selection change
  onDeviceSelected(deviceId: string) {
    this.updateNodeConfig('deviceId', deviceId);
    this.updateNodeConfig('deviceName', this.getDeviceName(deviceId));
  }

  closeEditor() {
    this.router.navigate(['/automations']);
  }
}
